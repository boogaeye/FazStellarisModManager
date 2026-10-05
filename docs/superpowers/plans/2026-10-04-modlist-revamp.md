# Mod List Revamp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Mods page becomes one list. Each row shows the mod's icon, a drag grip, an editable position number and a Workshop link. Mods are added through an "+ Add mods" picker. The left Installed panel is gone.

**Architecture:** The logic lives in Core and is tested:
- `ModListEditor`: pure list edits.
- `ModThumbnail`: finds a mod's picture in its folder or zip.
- `ModIconCache`: a disk-cached PNG data URI per mod. Shrinking is injected.

The app adds:
- a WPF `ImageShrinker`;
- two Razor components, `ModIcon` and `AddModsDialog`;
- a rewritten `Pages/Mods.razor`.

**Tech Stack:** .NET 10, Blazor in BlazorWebView (WPF), xUnit, System.IO.Compression, System.Windows.Media.Imaging.

**Spec:** `docs/superpowers/specs/2026-10-04-modlist-revamp-design.md`

**Conventions (all tasks):**
- **Working copy:** work ONLY in the worktree `C:\Users\SCP Fazbear\source\repos\FazStellarisModmanager-modlist`, branch `feature/modlist-revamp`. Never touch `C:\Users\SCP Fazbear\source\repos\FazStellarisModmanager`, because another session works there.
- **Build and test** with `--artifacts-path`, because the running app locks the normal bin folders. Never kill FazStellarisModmanager.exe:
  `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path "$SCRATCH/art"` (and `dotnet build FazStellarisModmanager -c Release --artifacts-path "$SCRATCH/art"` for the app).
- **C# string escapes:** don't put backslash escapes in C# string literals, because the tooling mangles them. Use `(char)92` for a backslash and `(char)10` for a newline.
- **Razor:** inside `@if`/`@foreach` blocks, don't use `<text>` elements with attributes.

---

### Task 1: ModListEditor

**Files:**
- Create: `FazStellarisModmanager.Core/Lists/ModListEditor.cs`
- Test: `FazStellarisModmanager.Tests/ModListEditorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;

namespace FazStellarisModmanager.Tests;

public class ModListEditorTests
{
    static List<string> L(params string[] items) => items.ToList();

    [Theory]
    [InlineData(1, 3, "a,c,b,d", 2)]   // down: lands at position 3
    [InlineData(3, 1, "d,a,b,c", 0)]   // up to the top
    [InlineData(0, 99, "b,c,d,a", 3)]  // clamped to the end
    [InlineData(2, 0, "c,a,b,d", 0)]   // clamped to the start
    [InlineData(2, 3, "a,b,c,d", 2)]   // same place
    public void MoveTo_moves_to_one_based_position(int from, int position, string expected, int newIndex)
    {
        var list = L("a", "b", "c", "d");
        Assert.Equal(newIndex, ModListEditor.MoveTo(list, from, position));
        Assert.Equal(expected, string.Join(",", list));
    }

    [Fact]
    public void MoveTo_ignores_bad_index()
    {
        var list = L("a", "b");
        Assert.Equal(-1, ModListEditor.MoveTo(list, 5, 1));
        Assert.Equal("a,b", string.Join(",", list));
    }

    [Theory]
    [InlineData(1, 3, "a,c,d,b,e", true)]  // moving down lands after the target
    [InlineData(3, 1, "a,d,b,c,e", true)]  // moving up lands before the target
    [InlineData(2, 2, "a,b,c,d,e", false)]
    [InlineData(0, 9, "a,b,c,d,e", false)]
    public void Move_takes_the_targets_place(int from, int to, string expected, bool moved)
    {
        var list = L("a", "b", "c", "d", "e");
        Assert.Equal(moved, ModListEditor.Move(list, from, to));
        Assert.Equal(expected, string.Join(",", list));
    }

    static InstalledMod Mod(string key, string name) =>
        new(key, name, "mod/" + key + ".mod", null, null, null, "", ModSource.Local, []);

    [Fact]
    public void AddRange_appends_new_mods_in_order_and_skips_duplicates()
    {
        var list = new List<ModListEntry> { new("mod:a", "A", "mod/a.mod", null) };
        var added = ModListEditor.AddRange(list, [Mod("MOD:A", "A again"), Mod("mod:c", "C"), Mod("mod:b", "B"), Mod("mod:c", "C twice")]);
        Assert.Equal(2, added);
        Assert.Equal(["mod:a", "mod:c", "mod:b"], list.Select(e => e.Key));
        Assert.Equal("mod/c.mod", list[1].DescriptorRel);
    }
}
```

- [ ] **Step 2: Run them and confirm they fail.** Run `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path "$SCRATCH/art" --filter ModListEditorTests`. Expected: a compile error, because ModListEditor does not exist yet.

- [ ] **Step 3: Implement**

```csharp
using FazStellarisModmanager.Core.Library;

namespace FazStellarisModmanager.Core.Lists;

/// <summary>Edits on an ordered (load order) mod list.</summary>
public static class ModListEditor
{
    /// <summary>Moves the item at <paramref name="from"/> to the 1-based <paramref name="position"/> (clamped to the list). Returns its new index, or -1 when <paramref name="from"/> is out of range.</summary>
    public static int MoveTo<T>(List<T> list, int from, int position)
    {
        if (from < 0 || from >= list.Count) return -1;
        var to = Math.Clamp(position, 1, list.Count) - 1;
        Move(list, from, to);
        return to;
    }

    /// <summary>Drag and drop: the item at <paramref name="from"/> takes the place of the item at <paramref name="to"/>, landing after it when moving down and before it when moving up. Returns false (no change) for bad or equal indexes.</summary>
    public static bool Move<T>(List<T> list, int from, int to)
    {
        if (from < 0 || from >= list.Count || to < 0 || to >= list.Count || from == to) return false;
        var item = list[from];
        list.RemoveAt(from);
        list.Insert(to, item);
        return true;
    }

    /// <summary>Appends mods whose key (case-insensitive) is not in the list yet, in the given order. Returns how many were added.</summary>
    public static int AddRange(List<ModListEntry> list, IEnumerable<InstalledMod> mods)
    {
        var keys = new HashSet<string>(list.Select(e => e.Key), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var m in mods)
        {
            if (!keys.Add(m.Key)) continue;
            list.Add(new ModListEntry(m.Key, m.Name, m.DescriptorRel, m.RemoteId));
            added++;
        }
        return added;
    }
}
```

- [ ] **Step 4: Run the tests again.** Expected: they PASS.
- [ ] **Step 5: Commit.** Run `git add -A && git commit -m "feat: ModListEditor - move to position, drag move, add range"`.

---

### Task 2: Picture field and ModThumbnail

**Files:**
- Modify: `FazStellarisModmanager.Core/Library/InstalledMod.cs`. Add `string? Picture = null` as the last parameter, with doc comment "descriptor picture=, relative to the content".
- Modify: `FazStellarisModmanager.Core/Library/ModLibrary.cs` (in `Scan`, the `new InstalledMod(...)` call). Pass `d.Picture` after `d.Tags`. `ModDescriptor.Picture` already exists; check the property name and use it.
- Create: `FazStellarisModmanager.Core/Library/ModThumbnail.cs`
- Test: `FazStellarisModmanager.Tests/ModThumbnailTests.cs`, plus one assertion in `ModLibraryTests` that `picture="thumbnail.png"` ends up in `Picture`. Follow that file's existing descriptor-writing style.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO.Compression;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ModThumbnailTests
{
    static InstalledMod Mod(string content, string? picture = null) =>
        new("mod:x", "X", "mod/x.mod", null, null, null, content, ModSource.Local, [], picture);

    [Fact]
    public void Picture_field_wins()
    {
        using var t = new TempDir();
        t.Write("m/thumbnail.png", "default");
        var cover = t.Write("m/gfx/cover.jpg", "cover");
        var s = ModThumbnail.Find(Mod(t.Mkdir("m"), "gfx/cover.jpg"));
        Assert.Equal(Path.GetFullPath(cover), s!.FilePath, ignoreCase: true);
        Assert.Null(s.ZipEntry);
    }

    [Fact]
    public void Falls_back_to_thumbnail_png_any_case()
    {
        using var t = new TempDir();
        t.Write("m/Thumbnail.PNG", "x");
        var s = ModThumbnail.Find(Mod(t.Mkdir("m"), "missing.png"));
        Assert.NotNull(s);
        Assert.Equal("x", File.ReadAllText(s.FilePath));
    }

    [Fact]
    public void Finds_jpg()
    {
        using var t = new TempDir();
        t.Write("m/thumbnail.jpg", "j");
        Assert.EndsWith("thumbnail.jpg", ModThumbnail.Find(Mod(t.Mkdir("m")))!.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsafe_picture_is_ignored()
    {
        using var t = new TempDir();
        t.Write("secret.png", "s");
        t.Mkdir("m");
        Assert.Null(ModThumbnail.Find(Mod(Path.Combine(t.Path, "m"), "../secret.png")));
        Assert.Null(ModThumbnail.Find(Mod(Path.Combine(t.Path, "m"), Path.Combine(t.Path, "secret.png"))));
    }

    [Fact]
    public void Empty_file_and_nothing_found_give_null()
    {
        using var t = new TempDir();
        t.Write("m/thumbnail.png", "");
        Assert.Null(ModThumbnail.Find(Mod(Path.Combine(t.Path, "m"))));
        Assert.Null(ModThumbnail.Find(Mod(Path.Combine(t.Path, "nope"))));
        Assert.Null(ModThumbnail.Find(Mod("")));
    }

    static string Zip(TempDir t, string rel, params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(t.Path, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open());
            w.Write(content);
        }
        return path;
    }

    [Fact]
    public void Finds_root_entry_in_zip_and_reads_it()
    {
        using var t = new TempDir();
        var zip = Zip(t, "w/mod.zip", ("common/a.txt", "a"), ("Thumbnail.png", "zipped"));
        var s = ModThumbnail.Find(Mod(zip))!;
        Assert.Equal(zip, s.FilePath, ignoreCase: true);
        Assert.Equal("Thumbnail.png", s.ZipEntry);
        Assert.Equal("zipped", System.Text.Encoding.UTF8.GetString(ModThumbnail.Read(s)));
        Assert.Contains("Thumbnail.png", s.Id);
    }

    [Fact]
    public void Finds_thumbnail_beside_zip()
    {
        using var t = new TempDir();
        var zip = Zip(t, "w/mod.zip", ("common/a.txt", "a"));
        t.Write("w/thumbnail.png", "beside");
        var s = ModThumbnail.Find(Mod(zip))!;
        Assert.Null(s.ZipEntry);
        Assert.Equal("beside", System.Text.Encoding.UTF8.GetString(ModThumbnail.Read(s)));
    }

    [Fact]
    public void Stamp_changes_when_file_changes()
    {
        using var t = new TempDir();
        var f = t.Write("m/thumbnail.png", "one");
        var s = ModThumbnail.Find(Mod(Path.Combine(t.Path, "m")))!;
        var before = ModThumbnail.Stamp(s);
        File.WriteAllText(f, "two!");
        Assert.NotEqual(before, ModThumbnail.Stamp(s));
    }
}
```

- [ ] **Step 2: Run them and confirm they fail.** Use `--filter ModThumbnailTests`. Expected: a compile error.

- [ ] **Step 3: Implement**

```csharp
using System.IO.Compression;

namespace FazStellarisModmanager.Core.Library;

/// <summary>A mod picture: a file, or an entry (<see cref="ZipEntry"/>) inside the zip at <see cref="FilePath"/>.</summary>
public sealed record ThumbnailSource(string FilePath, string? ZipEntry)
{
    public string Id => ZipEntry is null ? FilePath : FilePath + "|" + ZipEntry;
}

/// <summary>Finds a mod's picture: the descriptor's picture= (if a safe relative path), else thumbnail.png/.jpg/.jpeg, in the content folder or zip (then beside the zip).</summary>
public static class ModThumbnail
{
    public const long MaxBytes = 20L * 1024 * 1024;
    static readonly string[] DefaultNames = ["thumbnail.png", "thumbnail.jpg", "thumbnail.jpeg"];

    /// <summary>The mod's picture, or null when there is none (or it is empty, too big or unreadable).</summary>
    public static ThumbnailSource? Find(InstalledMod mod)
    {
        try
        {
            var content = mod.ContentPath;
            if (content.Length == 0) return null;
            var names = Names(mod.Picture);
            if (Directory.Exists(content)) return InFolder(content, names);
            if (File.Exists(content) && content.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return InZip(content, names) ?? InFolder(Path.GetDirectoryName(content)!, names);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>"length:lastWriteTicks" of the file (the zip for zip entries); changes when the picture may have changed.</summary>
    public static string Stamp(ThumbnailSource source)
    {
        var info = new FileInfo(source.FilePath);
        return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
    }

    public static byte[] Read(ThumbnailSource source)
    {
        if (source.ZipEntry is null) return File.ReadAllBytes(source.FilePath);
        using var archive = ZipFile.OpenRead(source.FilePath);
        var entry = archive.GetEntry(source.ZipEntry) ?? throw new FileNotFoundException("Picture not found in the archive.", source.ZipEntry);
        if (entry.Length > MaxBytes) throw new InvalidDataException("Picture too large.");
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    static List<string> Names(string? picture)
    {
        var names = new List<string>();
        if (IsSafe(picture)) names.Add(picture!.Replace((char)92, '/'));
        foreach (var n in DefaultNames)
            if (!names.Contains(n, StringComparer.OrdinalIgnoreCase)) names.Add(n);
        return names;
    }

    static bool IsSafe(string? p) =>
        !string.IsNullOrWhiteSpace(p) && !Path.IsPathRooted(p) && !p.Contains(':') && !p.Replace((char)92, '/').Split('/').Contains("..");

    // Windows file names are case-insensitive, so a plain lookup matches Thumbnail.PNG too.
    static ThumbnailSource? InFolder(string folder, List<string> names)
    {
        foreach (var n in names)
        {
            var info = new FileInfo(Path.Combine(folder, n));
            if (info.Exists && info.Length > 0 && info.Length <= MaxBytes) return new ThumbnailSource(info.FullName, null);
        }
        return null;
    }

    static ThumbnailSource? InZip(string zip, List<string> names)
    {
        using var archive = ZipFile.OpenRead(zip);
        foreach (var n in names)
        {
            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace((char)92, '/'), n, StringComparison.OrdinalIgnoreCase));
            if (entry is { Length: > 0 and <= MaxBytes }) return new ThumbnailSource(zip, entry.FullName);
        }
        return null;
    }
}
```

- [ ] **Step 4: Run the full test suite.** Expected: everything PASSES. That is the earlier 438 tests plus the new ones.
- [ ] **Step 5: Commit.** Run `git commit -am` (or `add -A`) with the message `"feat: mod pictures - InstalledMod.Picture and ModThumbnail"`.

---

### Task 3: ModIconCache

**Files:**
- Create: `FazStellarisModmanager.Core/Library/ModIconCache.cs`
- Test: `FazStellarisModmanager.Tests/ModIconCacheTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ModIconCacheTests
{
    static InstalledMod Mod(string content) =>
        new("mod:x", "X", "mod/x.mod", null, null, null, content, ModSource.Local, []);

    [Fact]
    public async Task Returns_data_uri_and_reuses_disk_cache()
    {
        using var t = new TempDir();
        t.Write("m/thumbnail.png", "big picture");
        var mod = Mod(Path.Combine(t.Path, "m"));
        var calls = 0;
        byte[] Shrink(byte[] b) { calls++; return [1, 2, 3]; }

        var first = new ModIconCache(Path.Combine(t.Path, "cache"), Shrink);
        Assert.Null(first.TryPeek(mod));
        var uri = await first.GetAsync(mod);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String([1, 2, 3]), uri);
        Assert.Equal(uri, first.TryPeek(mod));
        Assert.Single(Directory.GetFiles(Path.Combine(t.Path, "cache"), "*.png"));

        var second = new ModIconCache(Path.Combine(t.Path, "cache"), Shrink);
        Assert.Equal(uri, await second.GetAsync(mod));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Changed_picture_makes_new_cache_entry()
    {
        using var t = new TempDir();
        var f = t.Write("m/thumbnail.png", "one");
        var mod = Mod(Path.Combine(t.Path, "m"));
        var cache = Path.Combine(t.Path, "cache");
        await new ModIconCache(cache, b => b).GetAsync(mod);
        File.WriteAllText(f, "two!");
        File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(1));
        var uri = await new ModIconCache(cache, b => b).GetAsync(mod);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String("two!"u8.ToArray()), uri);
        Assert.Equal(2, Directory.GetFiles(cache, "*.png").Length);
    }

    [Fact]
    public async Task Failures_give_null()
    {
        using var t = new TempDir();
        t.Write("m/thumbnail.png", "x");
        var cache = Path.Combine(t.Path, "cache");
        Assert.Null(await new ModIconCache(cache, _ => null).GetAsync(Mod(Path.Combine(t.Path, "m"))));
        Assert.Null(await new ModIconCache(cache, _ => throw new InvalidOperationException()).GetAsync(Mod(Path.Combine(t.Path, "m"))));
        Assert.Null(await new ModIconCache(cache, b => b).GetAsync(Mod(Path.Combine(t.Path, "none"))));
    }
}
```

- [ ] **Step 2: Run them and confirm they fail.** Use `--filter ModIconCacheTests`.

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace FazStellarisModmanager.Core.Library;

/// <summary>Small PNG icons for mods as data URIs. Each picture is shrunk once (disk cache keyed by picture and stamp) and remembered for the app run. Never throws; failures give null.</summary>
public sealed class ModIconCache(string directory, Func<byte[], byte[]?> shrinkToPng)
{
    readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _memo = new(StringComparer.OrdinalIgnoreCase);

    static string MemoKey(InstalledMod mod) => mod.DescriptorRel + "|" + mod.ContentPath;

    /// <summary>The icon if it has already been loaded, else null. Does no IO (safe while rendering).</summary>
    public string? TryPeek(InstalledMod mod) =>
        _memo.TryGetValue(MemoKey(mod), out var lazy) && lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully ? lazy.Value.Result : null;

    /// <summary>Loads the icon on the thread pool (once per mod per run).</summary>
    public Task<string?> GetAsync(InstalledMod mod) =>
        _memo.GetOrAdd(MemoKey(mod), _ => new Lazy<Task<string?>>(() => Task.Run(() => Load(mod)))).Value;

    /// <summary>Finds, shrinks (or reads from the disk cache) and encodes the icon. Does IO.</summary>
    public string? Load(InstalledMod mod)
    {
        try
        {
            var source = ModThumbnail.Find(mod);
            if (source is null) return null;
            var id = source.Id + "|" + ModThumbnail.Stamp(source);
            var path = Path.Combine(directory, Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(id))) + ".png");
            byte[]? png;
            if (File.Exists(path))
            {
                png = File.ReadAllBytes(path);
            }
            else
            {
                png = shrinkToPng(ModThumbnail.Read(source));
                if (png is null || png.Length == 0) return null;
                Write(path, png);
            }
            return "data:image/png;base64," + Convert.ToBase64String(png);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    // Atomic: other runs never see half a file.
    static void Write(string path, byte[] png)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, png);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
```

- [ ] **Step 4: Run the full test suite.** Expected: everything PASSES.
- [ ] **Step 5: Commit.** Use the message `"feat: ModIconCache - disk-cached mod icons as data URIs"`.

---

### Task 4: App wiring and ImageShrinker

**Files:**
- Modify: `FazStellarisModmanager.Core/AppPaths.cs`. Add `public string ModIcons => Path.Combine(Root, "mod-icons");` after `Icons`.
- Create: `FazStellarisModmanager/Services/ImageShrinker.cs`
- Modify: `FazStellarisModmanager/AppServices.cs`. Add `services.AddSingleton(new ModIconCache(paths.ModIcons, ImageShrinker.ToSmallPng));` after `services.AddSingleton(paths);`, plus the usings `FazStellarisModmanager.Core.Library` and `FazStellarisModmanager.Services`.
- Modify: `FazStellarisModmanager/_Imports.razor`. Add `@using FazStellarisModmanager.Services` only if a Razor file needs it. ModIconCache is already covered by `Core.Library`.

- [ ] **Step 1: Write ImageShrinker**

```csharp
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FazStellarisModmanager.Services;

/// <summary>Turns a picture (png/jpg/bmp/gif) into a PNG whose longer side is at most <see cref="MaxSize"/> px. Thread-safe (no shared WPF objects).</summary>
public static class ImageShrinker
{
    public const int MaxSize = 64;

    /// <summary>The small PNG, or null when the bytes can't be decoded.</summary>
    public static byte[]? ToSmallPng(byte[] image)
    {
        try
        {
            using var input = new MemoryStream(image);
            var frame = BitmapDecoder.Create(input, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            var scale = Math.Min(1.0, (double)MaxSize / Math.Max(frame.PixelWidth, frame.PixelHeight));
            BitmapSource result = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(result));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 2: Add `AppPaths.ModIcons` and the AppServices registration** as listed above.
- [ ] **Step 3: Build the app and run the tests.** Run `dotnet build FazStellarisModmanager -c Release --artifacts-path "$SCRATCH/art"`, then the full test run. Expected: 0 errors and all tests passing.
- [ ] **Step 4: Commit.** Use the message `"feat: register ModIconCache with a WPF image shrinker"`.

---

### Task 5: ModIcon and AddModsDialog components and their CSS

**Files:**
- Create: `FazStellarisModmanager/Components/ModIcon.razor`
- Create: `FazStellarisModmanager/Components/AddModsDialog.razor`
- Modify: `FazStellarisModmanager/wwwroot/css/site.css`. Append the rules below at the end.

- [ ] **Step 1: ModIcon.razor**

```razor
@if (Uri is not null)
{
    <img class="micon" src="@Uri" alt="" style="width:@(Size)px;height:@(Size)px" />
}
else
{
    <span class="micon ph" style="width:@(Size)px;height:@(Size)px;font-size:@(Size / 2)px">@Letter</span>
}

@code {
    /// <summary>Icon data URI; null shows the first letter of <see cref="Name"/>.</summary>
    [Parameter] public string? Uri { get; set; }
    [Parameter] public string Name { get; set; } = "";
    [Parameter] public int Size { get; set; } = 40;

    string Letter => Name.Trim() is { Length: > 0 } n ? char.ToUpperInvariant(n[0]).ToString() : "?";
}
```

- [ ] **Step 2: AddModsDialog.razor**

```razor
<div class="modal-backdrop" @onclick="Close">
    <div class="modal addmods-modal" @onclick:stopPropagation @onkeydown="Key">
        <div class="modal-head">
            <h2>Add mods to "@ListName"</h2>
            <button class="small" title="Close" @onclick="Close">✕</button>
        </div>
        <div class="addmods-filter">
            <input class="search" placeholder="Search installed mods…" @ref="searchBox" @bind="search" @bind:event="oninput" />
            <select @bind="source">
                <option value="all">All</option>
                <option value="workshop">Workshop</option>
                <option value="local">Local</option>
            </select>
        </div>
        <div class="addlist">
            @{ var matches = Matches().ToList(); }
            @foreach (var m in matches.Take(MaxShown))
            {
                <label class="pick">
                    <input type="checkbox" checked="@picked.Contains(m)" @onchange="e => Toggle(m, e)" />
                    <ModIcon Uri="@Icons.TryPeek(m)" Name="@m.Name" Size="32" />
                    <span class="pname" title="@m.DescriptorRel">@m.Name</span>
                    <span class="badge @(m.Source == ModSource.Workshop ? "workshop" : "local")">@(m.Source == ModSource.Workshop ? "Workshop" : "Local")</span>
                </label>
            }
            @if (matches.Count > MaxShown)
            {
                <p class="muted">Showing @MaxShown of @matches.Count — search to narrow it down.</p>
            }
            @if (matches.Count == 0)
            {
                <p class="muted">@(Candidates().Any() ? "No installed mods match." : "Every installed mod is already in this list.")</p>
            }
        </div>
        <div class="modal-actions">
            <span class="muted">Added at the end; drag them into place afterwards.</span>
            <button class="primary" disabled="@(picked.Count == 0)" @onclick="AddPicked">Add @picked.Count @(picked.Count == 1 ? "mod" : "mods")</button>
        </div>
    </div>
</div>

@code {
    const int MaxShown = 300;

    [Parameter, EditorRequired] public IReadOnlyList<InstalledMod> Library { get; set; } = [];
    /// <summary>Keys already in the list (case-insensitive set); these are not offered.</summary>
    [Parameter, EditorRequired] public IReadOnlySet<string> InList { get; set; } = new HashSet<string>();
    [Parameter, EditorRequired] public ModIconCache Icons { get; set; } = default!;
    [Parameter] public string ListName { get; set; } = "";
    [Parameter] public EventCallback<IReadOnlyList<InstalledMod>> OnAdd { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    ElementReference searchBox;
    string search = "";
    string source = "all";
    readonly HashSet<InstalledMod> picked = [];

    IEnumerable<InstalledMod> Candidates() =>
        Library.Where(m => !InList.Contains(m.Key)).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase);

    IEnumerable<InstalledMod> Matches() =>
        Candidates().Where(m => (source == "all" || (source == "workshop") == (m.Source == ModSource.Workshop))
                                && (search.Length == 0 || m.Name.Contains(search, StringComparison.OrdinalIgnoreCase)));

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) await searchBox.FocusAsync();
    }

    void Toggle(InstalledMod m, ChangeEventArgs e)
    {
        if (e.Value is true) picked.Add(m); else picked.Remove(m);
    }

    Task AddPicked() =>
        OnAdd.InvokeAsync(picked.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList());

    Task Close() => OnClose.InvokeAsync();

    Task Key(KeyboardEventArgs e) => e.Key == "Escape" ? Close() : Task.CompletedTask;
}
```

- [ ] **Step 3: Append the CSS for these components and the new list (Task 6 uses the list rules)**

```css
/* Mod list revamp */
.mods-page { display: flex; flex-direction: column; gap: .5rem; height: calc(100% - 2.5rem); }
.mods-toolbar { display: flex; gap: .4rem; align-items: center; flex-wrap: wrap; }
.mods-toolbar .spacer { flex: 1; }
.mods-toolbar .list-filter { flex: 1; min-width: 12rem; }
.modlist { list-style: none; margin: 0; padding: .3rem; overflow: auto; flex: 1; min-height: 0; background: var(--panel); border: 1px solid var(--border); border-radius: 8px; }
.mrow { display: flex; gap: .55rem; align-items: center; padding: .3rem .5rem; margin-bottom: 3px; border: 1px solid transparent; border-radius: 6px; background: var(--panel-2); }
.mrow:hover { border-color: var(--border); }
.mrow.dragging { opacity: .5; border: 1px dashed var(--accent); }
.mrow.drop-above { box-shadow: inset 0 3px 0 var(--accent); }
.mrow.drop-below { box-shadow: inset 0 -3px 0 var(--accent); }
.mrow.missing .mname { color: var(--danger); }
.mrow .grip { color: var(--muted); cursor: grab; letter-spacing: -2px; user-select: none; }
.mrow input.pos { width: 3.4rem; text-align: center; padding: .2rem .3rem; }
.mrow .mname { flex: 1; min-width: 0; }
.mrow .mname div:first-child { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.mrow .mname .sub { font-size: .72rem; color: var(--muted); }
.micon { border-radius: 5px; object-fit: cover; flex: none; }
.micon.ph { display: inline-flex; align-items: center; justify-content: center; background: var(--border); color: var(--muted); font-weight: 700; }
.modal.addmods-modal { width: min(720px, 94vw); max-height: 86vh; }
.modal-head { display: flex; align-items: center; justify-content: space-between; gap: .5rem; }
.addmods-filter { display: flex; gap: .4rem; }
.addmods-filter .search { flex: 1; }
.addlist { overflow: auto; flex: 1; min-height: 8rem; border: 1px solid var(--border); border-radius: 6px; padding: .25rem; }
.addlist .pick { display: flex; gap: .55rem; align-items: center; padding: .25rem .4rem; border-radius: 5px; cursor: pointer; }
.addlist .pick:hover { background: var(--panel-2); }
.addlist .pname { flex: 1; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
```

Remove the old `.mods-page { display: grid; … }` rule (line ~39) so the new one is the only one. Also remove the rules only the old page used: `.mod-list li.in-list .name` and `.mod-list.ordered li`. Leave other `.mod-list` rules alone if anything else uses them; check with `grep -rn "mod-list" FazStellarisModmanager --include=*.razor`.

- [ ] **Step 4: Build the app.** Expected: 0 errors. AddModsDialog is not used yet.
- [ ] **Step 5: Commit.** Use the message `"feat: ModIcon and AddModsDialog components"`.

---

### Task 6: Rewrite the Mods page

**Files:**
- Modify (rewrite the markup and parts of `@code`): `FazStellarisModmanager/Pages/Mods.razor`

Keep these unchanged: `SetList`, `ImportCurrent`, `Save`, `DeleteList`, `LoadSelected`, `Apply`, `ApplyAndLaunch`, `RunAsync`, `Run`, `ErrorSuffix`, `Build`, and the fields `current`, `disabledDlcs`, `listName`, `savedLists`, `status`, `error`, `busy`, `loaded`, `confirmEmpty` and `selectedList`.

Remove `FilteredLibrary`, `Add`, `Move(index, delta)` and the old `Drop`.

- [ ] **Step 1: Replace the markup above `@code` with this:**

```razor
@page "/"
@implements IDisposable
@inject ModManagerService Manager
@inject ModIconCache Icons

<div class="mods-page">
    <div class="mods-toolbar">
        <input class="list-name" @bind="listName" placeholder="List name" />
        <select @bind="selectedList" @bind:after="LoadSelected" disabled="@busy">
            <option value="">Load list…</option>
            @foreach (var l in savedLists)
            {
                <option value="@l.Name">@l.Name</option>
            }
        </select>
        <button @onclick="Save" disabled="@busy">Save list</button>
        <button @onclick="ImportCurrent" disabled="@busy">Import current</button>
        <button class="danger" @onclick="DeleteList" disabled="@(busy || !savedLists.Any(l => string.Equals(l.Name, listName.Trim(), StringComparison.OrdinalIgnoreCase)))">Delete list</button>
        <button @onclick="Refresh" disabled="@busy" title="Scan the mod folder again">Rescan mods</button>
        <span class="spacer"></span>
        <button class="primary" @onclick="Apply" disabled="@(busy || !loaded)">Apply</button>
        <button class="primary" @onclick="ApplyAndLaunch" disabled="@(busy || !loaded)">Apply &amp; Launch</button>
    </div>
    <div class="mods-toolbar">
        <button class="primary" @onclick="() => adding = true" disabled="@(busy || !loaded)">+ Add mods</button>
        <input class="list-filter" placeholder="Filter this list…" @bind="filter" @bind:event="oninput" />
        <span class="muted">@current.Count @(current.Count == 1 ? "mod" : "mods")</span>
    </div>

    @if (status is not null)
    {
        <p class="status @(error ? "error" : "")">@status</p>
    }
    @if (Manager.LibraryErrors.Count > 0)
    {
        <details class="library-errors">
            <summary>@Manager.LibraryErrors.Count descriptors could not be read</summary>
            <ul>
                @foreach (var err in Manager.LibraryErrors)
                {
                    <li>@err</li>
                }
            </ul>
        </details>
    }

    <ol class="modlist">
        @for (int i = 0; i < current.Count; i++)
        {
            var index = i;
            var e = current[i];
            if (filter.Length > 0 && !e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var mod = byKey.GetValueOrDefault(e.Key);
            var installed = Manager.IsInstalled(e);
            var isWorkshop = mod?.Source == ModSource.Workshop || (mod is null && ModKeys.WorkshopId(e.Key) is not null);
            var workshopId = ModMatcher.WorkshopIdOf(e.Key, mod?.RemoteId ?? e.RemoteId);
            <li class="mrow @RowClass(index) @(installed ? "" : "missing")" draggable="true"
                @ondragstart="() => { dragIndex = index; overIndex = null; }"
                @ondragenter="() => overIndex = index"
                @ondragend="() => { dragIndex = null; overIndex = null; }"
                @ondragover:preventDefault @ondrop:preventDefault @ondrop="() => Drop(index)">
                <span class="grip" title="Drag to reorder">⋮⋮</span>
                <input @key="posVersion" class="pos" type="number" min="1" max="@current.Count" value="@(index + 1)"
                       title="Type a position and press Enter" @onchange="ev => SetPosition(index, ev.Value)" />
                <ModIcon Uri="@(mod is null ? null : Icons.TryPeek(mod))" Name="@e.Name" />
                <div class="mname">
                    <div title="@e.Name">@e.Name</div>
                    <div class="sub">@e.DescriptorRel</div>
                </div>
                <span class="badge @(isWorkshop ? "workshop" : "local")">@(isWorkshop ? "Workshop" : "Local")</span>
                @if (!isWorkshop && workshopId is not null)
                {
                    <span class="badge local" title="This local mod is a copy of a Steam Workshop item">local copy of Workshop @workshopId</span>
                }
                @if (!installed)
                {
                    <span class="badge missing">not installed</span>
                }
                @if (workshopId is not null)
                {
                    <a class="button small" href="steam://url/CommunityFilePage/@workshopId" target="_blank" title="Open on the Steam Workshop">Workshop ↗</a>
                }
                <button class="small danger" title="Remove from list" @onclick="() => Remove(index)">✕</button>
            </li>
        }
    </ol>
</div>

@if (adding)
{
    <AddModsDialog Library="Manager.Library" InList="InListKeys()" Icons="Icons" ListName="@listName"
                   OnAdd="AddMods" OnClose="() => adding = false" />
}
```

Notes:
- `ModKeys.WorkshopId(key)` returns `ulong?`: the id for `ugc:` keys, otherwise null. `ModMatcher.WorkshopIdOf` returns `string?`.
- Don't put `@key` on the rows, because a list can contain the same key twice.
- Add `a.button.small { padding: .1rem .45rem; font-size: .85rem; }` to site.css if the existing `button.small` rule doesn't cover anchors.

- [ ] **Step 2: Update `@code`.** Add and replace the members below. Keep the existing ones listed above.

```csharp
    string filter = "";
    int? dragIndex;
    int? overIndex;
    int posVersion;
    bool adding;
    Dictionary<string, InstalledMod> byKey = new(StringComparer.OrdinalIgnoreCase);
    CancellationTokenSource? iconRun;

    protected override async Task OnInitializedAsync()
    {
        status = "Scanning mods…";
        error = false;
        busy = true;
        await RunAsync(async () =>
        {
            await Manager.RefreshLibraryAsync();
            savedLists = Manager.Lists.LoadAll();
            SetList(Manager.ImportCurrent("Current"));
            loaded = true;
            LibraryChanged();
            return $"Loaded {current.Count} enabled mods from dlc_load.json." + ErrorSuffix();
        });
    }

    async Task Refresh()
    {
        status = "Scanning mods…";
        error = false;
        busy = true;
        await RunAsync(async () =>
        {
            await Manager.RefreshLibraryAsync();
            LibraryChanged();
            return $"Found {Manager.Library.Count} installed mods." + ErrorSuffix();
        });
    }

    // Rebuilds the key lookup and loads icons in the background: mods in the list first, 3 at a time.
    void LibraryChanged()
    {
        byKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (var m in Manager.Library) byKey.TryAdd(m.Key, m);

        iconRun?.Cancel();
        var run = iconRun = new CancellationTokenSource();
        var inList = InListKeys();
        var mods = Manager.Library.OrderBy(m => inList.Contains(m.Key) ? 0 : 1).ToList();
        _ = LoadIconsAsync(mods, run.Token);
    }

    async Task LoadIconsAsync(List<InstalledMod> mods, CancellationToken ct)
    {
        var done = 0;
        try
        {
            await Parallel.ForEachAsync(mods, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct }, async (m, _) =>
            {
                await Icons.GetAsync(m);
                if (Interlocked.Increment(ref done) % 12 == 0 && !ct.IsCancellationRequested) await InvokeAsync(StateHasChanged);
            });
            if (!ct.IsCancellationRequested) await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { }
    }

    HashSet<string> InListKeys() => new(current.Select(e => e.Key), StringComparer.OrdinalIgnoreCase);

    string RowClass(int index)
    {
        if (dragIndex == index) return "dragging";
        if (dragIndex is not int from || overIndex != index) return "";
        return from < index ? "drop-below" : "drop-above";
    }

    void Drop(int target)
    {
        var from = dragIndex;
        dragIndex = null;
        overIndex = null;
        if (from is int f && ModListEditor.Move(current, f, target))
        {
            confirmEmpty = false;
            posVersion++;
        }
    }

    void SetPosition(int index, object? value)
    {
        posVersion++;   // recreate the number boxes so a rejected value doesn't stay typed in
        if (!int.TryParse(value?.ToString(), out var position))
        {
            (status, error) = ("Type a position number.", true);
            return;
        }
        var to = ModListEditor.MoveTo(current, index, position);
        if (to < 0) return;
        confirmEmpty = false;
        (status, error) = ($"Moved {current[to].Name} to position {to + 1}.", false);
    }

    void Remove(int index)
    {
        if (index < 0 || index >= current.Count) return;
        confirmEmpty = false;
        current.RemoveAt(index);
        posVersion++;
    }

    void AddMods(IReadOnlyList<InstalledMod> mods)
    {
        adding = false;
        var added = ModListEditor.AddRange(current, mods);
        if (added > 0) confirmEmpty = false;
        (status, error) = ($"Added {added} {(added == 1 ? "mod" : "mods")}.", false);
    }

    public void Dispose()
    {
        iconRun?.Cancel();
        iconRun?.Dispose();
    }
```

- [ ] **Step 3: Build the app and run the full test suite.** Expected: 0 errors and all tests passing.
- [ ] **Step 4: Run it and check by hand.** Run the built exe from `$SCRATCH/art/bin/FazStellarisModmanager/release/` and confirm:
  - icons appear (placeholders until they load);
  - dragging a row shows the blue line and drops in the right place;
  - typing `1` in a row's number box and pressing Enter moves the row to the top;
  - "Workshop ↗" opens Steam;
  - "+ Add mods" opens the picker, search and the source filter work, ticking 2 mods and pressing Add appends them, and Esc and a backdrop click close it;
  - Rescan, Save, Load, Apply and Launch still work.
- [ ] **Step 5: Commit.** Use the message `"feat: Mods page revamp - icons, drag/number ordering, Workshop links, Add mods picker"`.

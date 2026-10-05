# Mod File Conflicts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show "⚠ NN% overwritten" on mods in the list that have at least 75% of their content files overridden by later mods. Clicking it opens details grouped by the winning mod.

**Architecture:**
- Core: `ModFiles` lists a mod's content paths, `ModFileCache` memoizes them, and `ConflictAnalyzer` is a pure analysis. All three are tested.
- Mods.razor: a debounced background check whose results are stored per entry, plus a badge and a details panel. The file cache is registered as a singleton.

**Tech Stack:** .NET 10, Blazor in BlazorWebView, xUnit, System.IO.Compression.

**Spec:** `docs/superpowers/specs/2026-10-04-mod-conflicts-design.md`

**Conventions:**
- Work in `C:\Users\SCP Fazbear\source\repos\FazStellarisModmanager`, branch `feature/mod-conflicts`.
- Build and test with `-c Release --artifacts-path <scratchpad>/art`. Never kill FazStellarisModmanager.exe.
- Don't use backslash escapes in C# string literals. Use `(char)92` for a backslash and `(char)10` for a newline.

---

### Task 1: Core — ModFiles, ModFileCache, ConflictAnalyzer

**Files:**
- Create: `FazStellarisModmanager.Core/Conflicts/ModFiles.cs`
- Create: `FazStellarisModmanager.Core/Conflicts/ModFileCache.cs`
- Create: `FazStellarisModmanager.Core/Conflicts/ConflictAnalyzer.cs`
- Test: `FazStellarisModmanager.Tests/ModFilesTests.cs`
- Test: `FazStellarisModmanager.Tests/ConflictAnalyzerTests.cs`

- [ ] **Step 1: Write the failing tests**

`ModFilesTests.cs`:
```csharp
using System.IO.Compression;
using FazStellarisModmanager.Core.Conflicts;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ModFilesTests
{
    [Fact]
    public void Folder_lists_files_in_subfolders_only()
    {
        using var t = new TempDir();
        t.Write("m/descriptor.mod", "x");
        t.Write("m/thumbnail.png", "x");
        t.Write("m/common/buildings/a.txt", "x");
        t.Write("m/interface/main.gui", "x");
        Assert.Equal(["common/buildings/a.txt", "interface/main.gui"], ModFiles.List(Path.Combine(t.Path, "m")));
    }

    [Fact]
    public void Zip_lists_entries_normalized_and_deduplicated()
    {
        using var t = new TempDir();
        var zip = Path.Combine(t.Path, "mod.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            z.CreateEntry("descriptor.mod");
            z.CreateEntry("common/");
            z.CreateEntry("common" + (char)92 + "Techs.txt");
            z.CreateEntry("common/techs.txt");
            z.CreateEntry("gfx/a.dds");
        }
        Assert.Equal(["common/Techs.txt", "gfx/a.dds"], ModFiles.List(zip));
    }

    [Fact]
    public void Missing_or_empty_path_gives_empty()
    {
        using var t = new TempDir();
        Assert.Empty(ModFiles.List(Path.Combine(t.Path, "nope")));
        Assert.Empty(ModFiles.List(""));
    }

    [Fact]
    public void Cache_remembers_until_cleared()
    {
        using var t = new TempDir();
        t.Write("m/common/a.txt", "x");
        var cache = new ModFileCache();
        var dir = Path.Combine(t.Path, "m");
        Assert.Single(cache.Get(dir));
        t.Write("m/common/b.txt", "x");
        Assert.Single(cache.Get(dir));
        cache.Clear();
        Assert.Equal(2, cache.Get(dir).Count);
    }

    [Fact]
    public void Cache_does_not_remember_errors()
    {
        using var t = new TempDir();
        var zip = t.Write("bad.zip", "not a zip");
        var cache = new ModFileCache();
        Assert.Empty(cache.Get(zip));
        File.Delete(zip);
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) z.CreateEntry("common/a.txt");
        Assert.Single(cache.Get(zip));
    }
}
```

`ConflictAnalyzerTests.cs`:
```csharp
using FazStellarisModmanager.Core.Conflicts;

namespace FazStellarisModmanager.Tests;

public class ConflictAnalyzerTests
{
    static IReadOnlyCollection<string> F(params string[] files) => files;

    [Fact]
    public void Last_mod_wins_and_groups_are_by_winner()
    {
        var r = ConflictAnalyzer.Analyze([
            F("a/1.txt", "a/2.txt", "a/3.txt", "a/4.txt"),
            F("a/1.txt", "a/2.txt"),
            F("A/2.TXT", "a/3.txt"),
        ]);
        Assert.Equal(4, r[0].Total);
        Assert.Equal(3, r[0].Overwritten);
        Assert.Equal(75, r[0].Percent);
        Assert.True(r[0].IsHeavy);
        Assert.Equal([1, 2], r[0].Groups.Select(g => g.WinnerIndex));
        Assert.Equal(["a/1.txt"], r[0].Groups[0].Files);
        Assert.Equal(["a/2.txt", "a/3.txt"], r[0].Groups[1].Files);

        Assert.Equal(1, r[1].Overwritten);
        Assert.Equal(50, r[1].Percent);
        Assert.False(r[1].IsHeavy);
        Assert.Equal(0, r[2].Overwritten);
        Assert.Empty(r[2].Groups);
    }

    [Fact]
    public void Threshold_is_75_percent_rounded_down()
    {
        var mine = Enumerable.Range(0, 100).Select(i => $"c/{i}.txt").ToArray();
        var r = ConflictAnalyzer.Analyze([F(mine), F(mine.Take(74).ToArray())]);
        Assert.Equal(74, r[0].Percent);
        Assert.False(r[0].IsHeavy);
    }

    [Fact]
    public void Empty_mod_and_duplicate_files_in_a_mod()
    {
        var r = ConflictAnalyzer.Analyze([F(), F("x/a.txt", "X/A.txt"), F("x/a.txt")]);
        Assert.Equal(0, r[0].Total);
        Assert.False(r[0].IsHeavy);
        Assert.Equal(0, r[0].Percent);
        Assert.Equal(1, r[1].Total);
        Assert.Equal(100, r[1].Percent);
    }

    [Fact]
    public void Earlier_copy_of_same_mod_is_fully_overwritten()
    {
        var r = ConflictAnalyzer.Analyze([F("a/1.txt", "a/2.txt"), F("a/1.txt", "a/2.txt")]);
        Assert.Equal(100, r[0].Percent);
        Assert.Equal(0, r[1].Percent);
    }
}
```

- [ ] **Step 2: Run the tests.** Use `--filter "ModFilesTests|ConflictAnalyzerTests"`. They should fail to compile.

- [ ] **Step 3: Implement**

`ModFiles.cs`:
```csharp
using System.IO.Compression;

namespace FazStellarisModmanager.Core.Conflicts;

/// <summary>A mod's content files: paths (forward slashes) inside subfolders of its folder or zip. Root files (descriptor, thumbnail, readme) are not content.</summary>
public static class ModFiles
{
    /// <summary>Content paths sorted case-insensitively without case-insensitive duplicates (first spelling kept); empty when the path is neither a folder nor a .zip. Throws on IO and zip errors.</summary>
    public static IReadOnlyList<string> List(string contentPath)
    {
        IEnumerable<string> raw;
        if (contentPath.Length > 0 && Directory.Exists(contentPath))
        {
            var root = Path.GetFullPath(contentPath);
            raw = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f));
        }
        else if (contentPath.Length > 0 && File.Exists(contentPath) && contentPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(contentPath);
            raw = zip.Entries.Select(e => e.FullName).ToList();
        }
        else
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var r in raw)
        {
            var p = r.Replace((char)92, '/').TrimStart('/');
            if (p.Length == 0 || p.EndsWith('/') || !p.Contains('/')) continue;
            if (seen.Add(p)) result.Add(p);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }
}
```

`ModFileCache.cs`:
```csharp
using System.Collections.Concurrent;

namespace FazStellarisModmanager.Core.Conflicts;

/// <summary>Remembers <see cref="ModFiles.List"/> per content path for the app run. Unreadable content gives an empty list that is not remembered. Thread-safe.</summary>
public sealed class ModFileCache
{
    readonly ConcurrentDictionary<string, IReadOnlyList<string>> _lists = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Get(string contentPath)
    {
        if (_lists.TryGetValue(contentPath, out var cached)) return cached;
        try
        {
            var list = ModFiles.List(contentPath);
            _lists[contentPath] = list;
            return list;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>Forgets every list (after a rescan, when mods may have changed on disk).</summary>
    public void Clear() => _lists.Clear();
}
```

`ConflictAnalyzer.cs`:
```csharp
namespace FazStellarisModmanager.Core.Conflicts;

/// <summary>Files of one mod overwritten by the mod at <see cref="WinnerIndex"/> (the last mod in load order with the same path).</summary>
public sealed record ConflictGroup(int WinnerIndex, IReadOnlyList<string> Files);

/// <summary>How much of one mod's content later mods overwrite.</summary>
public sealed record ModConflicts(int Total, int Overwritten, IReadOnlyList<ConflictGroup> Groups)
{
    /// <summary>Overwritten share in whole percent, rounded down (0 for a mod without content).</summary>
    public int Percent => Total == 0 ? 0 : (int)(Overwritten * 100L / Total);

    public bool IsHeavy => Total > 0 && Percent >= ConflictAnalyzer.HeavyPercent;
}

/// <summary>File-level overrides in a load order: a later mod's file replaces an earlier mod's file at the same path (case-insensitive).</summary>
public static class ConflictAnalyzer
{
    public const int HeavyPercent = 75;

    /// <summary>One result per mod, in the same order as <paramref name="filesInLoadOrder"/>.</summary>
    public static IReadOnlyList<ModConflicts> Analyze(IReadOnlyList<IReadOnlyCollection<string>> filesInLoadOrder)
    {
        var sets = filesInLoadOrder.Select(files =>
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return files.Where(seen.Add).ToList();
        }).ToList();

        var last = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sets.Count; i++)
            foreach (var f in sets[i]) last[f] = i;

        var result = new List<ModConflicts>(sets.Count);
        for (var i = 0; i < sets.Count; i++)
        {
            var index = i;
            var groups = sets[i]
                .Where(f => last[f] > index)
                .GroupBy(f => last[f])
                .OrderBy(g => g.Key)
                .Select(g => new ConflictGroup(g.Key, g.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()))
                .ToList();
            result.Add(new ModConflicts(sets[i].Count, groups.Sum(g => g.Files.Count), groups));
        }
        return result;
    }
}
```

- [ ] **Step 4: Run the tests, then the full suite.** Both should PASS.
- [ ] **Step 5: Commit.** Use the message `"feat: file-level mod conflict analysis (ModFiles, ModFileCache, ConflictAnalyzer)"`.

---

### Task 2: Mods page — background check, badge, details panel

**Files:**
- Modify: `FazStellarisModmanager/AppServices.cs`. After the ModIconCache registration, add `services.AddSingleton(new ModFileCache());` and `using FazStellarisModmanager.Core.Conflicts;`.
- Modify: `FazStellarisModmanager/_Imports.razor`. Add `@using FazStellarisModmanager.Core.Conflicts`.
- Modify: `FazStellarisModmanager/Pages/Mods.razor`
- Modify: `FazStellarisModmanager/wwwroot/css/site.css`. Append the rules at the end of this task.

- [ ] **Step 1: Wire the page into the background check**

At the top of Mods.razor, add `@inject ModFileCache FileCache`.

In `@code`, add:

```csharp
    // File-conflict results for the list as it was when last analyzed (by entry reference); shown until a newer run finishes.
    Dictionary<ModListEntry, ModConflicts> conflicts = new(ReferenceEqualityComparer.Instance);
    List<ModListEntry> analyzed = [];
    readonly HashSet<ModListEntry> openConflicts = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<(ModListEntry, int)> expandedGroups = [];
    CancellationTokenSource? conflictRun;

    // Re-checks file conflicts half a second after the last list change, off the UI thread.
    void ScheduleConflicts()
    {
        conflictRun?.Cancel();
        conflictRun?.Dispose();
        var run = conflictRun = new CancellationTokenSource();
        var snapshot = current.ToList();
        var mods = snapshot.Select(e => byKey.GetValueOrDefault(e.Key)).ToList();
        _ = CheckConflictsAsync(snapshot, mods, run.Token);
    }

    async Task CheckConflictsAsync(List<ModListEntry> snapshot, List<InstalledMod?> mods, CancellationToken ct)
    {
        try
        {
            await Task.Delay(500, ct);
            var results = await Task.Run(() =>
            {
                var files = mods.Select(m => m is null ? (IReadOnlyCollection<string>)[] : FileCache.Get(m.ContentPath)).ToList();
                ct.ThrowIfCancellationRequested();
                return ConflictAnalyzer.Analyze(files);
            }, ct);
            await InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested) return;
                analyzed = snapshot;
                conflicts = new(ReferenceEqualityComparer.Instance);
                for (var i = 0; i < snapshot.Count; i++) conflicts.TryAdd(snapshot[i], results[i]);
                StateHasChanged();
            });
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { }
    }

    ModConflicts? ConflictsOf(ModListEntry e) => conflicts.GetValueOrDefault(e);

    void ToggleConflicts(ModListEntry e)
    {
        if (!openConflicts.Remove(e)) openConflicts.Add(e);
    }
```

Then call `ScheduleConflicts()` from each of these places:
- at the end of `SetList(...)`;
- at the end of `LibraryChanged()`;
- in `MoveRow` after a successful move;
- in `SetPosition` after a successful `MoveTo` (when `to >= 0`);
- in `Remove` after `RemoveAt`;
- in `AddMods` when `added > 0`.

In `Refresh()`, call `FileCache.Clear();` next to `Icons.Refresh();`.

In `DisposeAsync`, add `conflictRun?.Cancel(); conflictRun?.Dispose();` next to the icon-run cancel.

- [ ] **Step 2: Add the badge and panel markup.** In the row, put the badge after the "not installed" `@if` block and before the Workshop link:

```razor
                @if (ConflictsOf(e) is { IsHeavy: true } heavy)
                {
                    <button class="warnbadge" title="@heavy.Percent% of this mod's files are overwritten by mods later in the list"
                            @onclick="() => ToggleConflicts(e)">⚠ @heavy.Percent% overwritten</button>
                }
```

After the row's closing `</li>`, still inside the for loop, add the panel:

```razor
            @if (openConflicts.Contains(e) && ConflictsOf(e) is { IsHeavy: true } c)
            {
                <li class="conflict-panel">
                    <h4>@c.Overwritten of @c.Total files are overwritten by mods later in the list</h4>
                    @foreach (var g in c.Groups)
                    {
                        var winner = g.WinnerIndex < analyzed.Count ? analyzed[g.WinnerIndex] : null;
                        var winnerPos = winner is null ? -1 : IndexOf(winner);
                        var all = expandedGroups.Contains((e, g.WinnerIndex));
                        <div class="cgroup">by #@(winnerPos >= 0 ? (winnerPos + 1).ToString() : "?") @(winner?.Name ?? "a removed mod") (@g.Files.Count @(g.Files.Count == 1 ? "file" : "files"))</div>
                        <ul>
                            @foreach (var f in all ? g.Files : g.Files.Take(10))
                            {
                                <li>@f</li>
                            }
                        </ul>
                        @if (!all && g.Files.Count > 10)
                        {
                            var key = (e, g.WinnerIndex);
                            <button class="link" @onclick="() => expandedGroups.Add(key)">… @(g.Files.Count - 10) more</button>
                        }
                    }
                    <p class="muted">Usually means this mod has little effect here. Move it below those mods, or remove it.</p>
                </li>
            }
```

(Inside the for loop, the `@if` comes after the `</li>` of the row. The filter's `continue` already skips both the row and its panel.)

- [ ] **Step 3: Append the CSS**

```css
/* File conflicts */
button.warnbadge { color: #f2c94c; background: #3a2e12; border: 1px solid #6b5520; border-radius: 5px; padding: .05rem .45rem; font-size: .78rem; white-space: nowrap; }
button.warnbadge:hover:not(:disabled) { border-color: #f2c94c; }
.conflict-panel { list-style: none; background: var(--panel); border: 1px solid #3a4a5c; border-radius: 8px; padding: .6rem .8rem; margin: -1px 0 6px 2.5rem; cursor: default; user-select: text; }
.conflict-panel h4 { margin: 0 0 .35rem; font-size: .85rem; }
.conflict-panel .cgroup { margin: .45rem 0 .15rem; color: #9fd2ff; font-size: .82rem; }
.conflict-panel ul { margin: 0; padding-left: 1.2rem; color: var(--muted); font-family: Consolas, monospace; font-size: .74rem; }
.conflict-panel p { margin: .5rem 0 0; font-size: .78rem; }
```

- [ ] **Step 4: Build the app and run the full test suite.** Expected: 0 errors and all tests passing.
- [ ] **Step 5: Commit.** Use the message `"feat: Mods page warns about mods mostly overwritten by later mods"`.

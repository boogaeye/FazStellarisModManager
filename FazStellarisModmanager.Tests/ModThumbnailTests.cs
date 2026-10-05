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
        var s = ModThumbnail.Find(Mod(zip) with { Source = ModSource.Workshop })!;
        Assert.Null(s.ZipEntry);
        Assert.Equal("beside", System.Text.Encoding.UTF8.GetString(ModThumbnail.Read(s)));
    }

    [Fact]
    public void Local_zip_ignores_thumbnail_beside_it()
    {
        using var t = new TempDir();
        var zip = Zip(t, "w/mod.zip", ("common/a.txt", "a"));
        t.Write("w/thumbnail.png", "stray");
        Assert.Null(ModThumbnail.Find(Mod(zip)));
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

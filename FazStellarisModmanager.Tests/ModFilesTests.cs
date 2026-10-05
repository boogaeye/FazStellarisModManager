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
    public void Icons_are_not_content()
    {
        using var t = new TempDir();
        t.Write("m/gfx/interface/icons/resources/sr_a.dds", "x");
        t.Write("m/GFX/Interface/Icons/b.dds", "x");
        t.Write("m/gfx/models/portraits/x.dds", "x");
        t.Write("m/common/a.txt", "x");
        Assert.Equal(["common/a.txt", "gfx/models/portraits/x.dds"], ModFiles.List(Path.Combine(t.Path, "m")));
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

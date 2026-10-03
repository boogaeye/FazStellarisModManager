using FazStellarisModmanager.Core.Hashing;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class HashingTests
{
    const string ManifestText = """
        # Stellaris checksum manifest
        directory = {
        	name = "common"
        	sub_directories = yes
        	file_extension = ".txt"
        }
        directory = {
        	name = "events"
        	sub_directories = no
        	file_extension = txt
        }
        """;

    [Fact]
    public void Parses_manifest()
    {
        var rules = Manifest.Parse(ManifestText);

        Assert.Equal(new[]
        {
            new ManifestEntry("common", true, ".txt"),
            new ManifestEntry("events", false, ".txt"),
        }, rules);
    }

    [Fact]
    public void Collects_matching_files_and_later_roots_override()
    {
        using var tmp = new TempDir();
        tmp.Write("game/common/a.txt", "1");
        tmp.Write("game/common/deep/b.txt", "2");
        tmp.Write("game/common/c.dds", "skip");
        tmp.Write("game/events/e.txt", "3");
        tmp.Write("game/events/deep/skip.txt", "not recursive");
        tmp.Write("mod/common/A.txt", "override, different case");

        var files = FileCollector.Collect(Manifest.Parse(ManifestText),
            [Path.Combine(tmp.Path, "game"), Path.Combine(tmp.Path, "mod")]);

        Assert.Equal(new[] { "common/a.txt", "common/deep/b.txt", "events/e.txt" }, files.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(tmp.Path, "mod", "common", "A.txt"), files["common/a.txt"]);
    }

    [Fact]
    public void HashCache_computes_md5_and_reuses_it_until_file_changes()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("f.txt", "abc");
        var cache = HashCache.InMemory();

        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", cache.GetMd5(file, out var size));
        Assert.Equal(3, size);
        cache.GetMd5(file, out _);
        Assert.Equal(1, cache.Misses);

        File.WriteAllText(file, "abcd");
        Assert.Equal("e2fc714c4727ee9395f324cd2e7f331f", cache.GetMd5(file, out _));
        Assert.Equal(2, cache.Misses);
    }

    [Fact]
    public void HashCache_persists()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("f.txt", "abc");
        var cachePath = Path.Combine(tmp.Path, "data", "hashcache.json");
        var cache = HashCache.Load(cachePath);
        cache.GetMd5(file, out _);
        cache.Save();

        var reloaded = HashCache.Load(cachePath);
        reloaded.GetMd5(file, out _);

        Assert.Equal(0, reloaded.Misses);
    }

    [Fact]
    public void Corrupt_cache_file_starts_empty()
    {
        using var tmp = new TempDir();
        var cachePath = tmp.Write("hashcache.json", "{not json");

        Assert.Equal(0, HashCache.Load(cachePath).Count);
    }
}

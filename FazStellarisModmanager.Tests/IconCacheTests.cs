using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class IconCacheTests
{
    const string IconRel = "gfx/interface/icons/technologies/tech_a.dds";

    static ContentSource Source(TempDir tmp, string folder, byte[]? dds)
    {
        var root = tmp.Mkdir(folder);
        if (dds is not null)
        {
            var path = Path.Combine(root, IconRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, dds);
        }
        return ContentSource.FromPath(folder, root);
    }

    [Fact]
    public void Makes_a_png_data_uri_and_caches_it_on_disk()
    {
        using var tmp = new TempDir();
        using var s = Source(tmp, "base", DdsBuilder.Bgra32(1, 1, (255, 0, 0, 255)));
        var cacheDir = Path.Combine(tmp.Path, "cache");
        var cache = new IconCache(cacheDir);

        var uri = cache.DataUri([s], "tech_a");

        Assert.StartsWith("data:image/png;base64,", uri);
        Assert.Single(Directory.GetFiles(cacheDir, "*.png"));
        Assert.Equal(uri, new IconCache(cacheDir).DataUri([s], "tech_a"));
        Assert.Null(cache.DataUri([s], "tech_missing"));
    }

    [Fact]
    public void Oversized_icons_are_downscaled_to_64()
    {
        using var tmp = new TempDir();
        var pixels = new (byte R, byte G, byte B, byte A)[128 * 128];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (10, 200, 30, 255);
        using var s = Source(tmp, "base", DdsBuilder.Bgra32(128, 128, pixels));
        var uri = new IconCache(Path.Combine(tmp.Path, "cache")).DataUri([s], "tech_a");

        var png = Convert.FromBase64String(uri!["data:image/png;base64,".Length..]);
        static int BE(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        Assert.Equal((64, 64), (BE(png, 16), BE(png, 20)));
    }

    [Fact]
    public void The_last_source_with_the_icon_wins()
    {
        using var tmp = new TempDir();
        using var good = Source(tmp, "good", DdsBuilder.Bgra32(1, 1, (1, 2, 3, 255)));
        using var bad = Source(tmp, "bad", [1, 2, 3]);
        using var none = Source(tmp, "none", null);
        var cache = new IconCache(Path.Combine(tmp.Path, "cache"));

        Assert.Null(cache.DataUri([good, bad], "tech_a"));
        Assert.NotNull(cache.DataUri([bad, good, none], "tech_a"));
    }

    [Fact]
    public void Icon_keys_that_could_escape_the_icon_folder_are_rejected()
    {
        using var tmp = new TempDir();
        using var s = Source(tmp, "base", DdsBuilder.Bgra32(1, 1, (255, 0, 0, 255)));
        var cache = new IconCache(Path.Combine(tmp.Path, "cache"));

        Assert.Null(cache.DataUri([s], "../evil"));
        Assert.Null(cache.DataUri([s], "a/b"));
        Assert.Null(cache.DataUri([s], "a" + (char)92 + "b"));
        Assert.Null(cache.DataUri([s], "c:x"));
    }
}

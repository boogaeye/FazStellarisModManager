using System.Buffers.Binary;
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

    static int PngWidth(string dataUri) =>
        BinaryPrimitives.ReadInt32BigEndian(Convert.FromBase64String(dataUri["data:image/png;base64,".Length..]).AsSpan(16));

    static byte[] Png(string dataUri) => Convert.FromBase64String(dataUri["data:image/png;base64,".Length..]);

    /// <summary>First pixel (RGBA) of a single-IDAT, unfiltered-or-not PNG; the tests only need the first row's first pixel.</summary>
    static (byte R, byte G, byte B, byte A) FirstPixel(string dataUri)
    {
        var png = Png(dataUri);
        var len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33));
        using var z = new System.IO.Compression.ZLibStream(new MemoryStream(png, 33 + 8, len), System.IO.Compression.CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var b = raw.ToArray();
        return (b[1], b[2], b[3], b[4]); // b[0] is the row's filter byte
    }

    [Fact]
    public void A_one_pixel_wide_sheet_is_shown_uncropped()
    {
        using var tmp = new TempDir();
        var root = tmp.Mkdir("g");
        Directory.CreateDirectory(Path.Combine(root, "gfx"));
        File.WriteAllBytes(Path.Combine(root, "gfx", "thin.dds"), DdsBuilder.Bgra32(1, 1, (255, 0, 0, 255)));
        using var s = ContentSource.FromPath("g", root);

        var uri = new IconCache(Path.Combine(tmp.Path, "cache")).DataUri([s], new IconRef("gfx/thin.dds", 2, 2));

        Assert.Equal(1, PngWidth(uri!));
    }

    [Fact]
    public void Crops_sprite_sheet_frames()
    {
        using var tmp = new TempDir();
        var root = tmp.Mkdir("g");
        Directory.CreateDirectory(Path.Combine(root, "gfx"));
        File.WriteAllBytes(Path.Combine(root, "gfx", "sheet.dds"),
            DdsBuilder.Bgra32(4, 1, (255, 0, 0, 255), (255, 0, 0, 255), (0, 0, 255, 255), (0, 0, 255, 255)));
        using var s = ContentSource.FromPath("g", root);
        var cache = new IconCache(Path.Combine(tmp.Path, "cache"));

        var first = cache.DataUri([s], new IconRef("gfx/sheet.dds", 1, 2));
        var second = cache.DataUri([s], new IconRef("gfx/sheet.dds", 2, 2));

        Assert.Equal(2, PngWidth(first!));
        Assert.Equal(2, PngWidth(second!));
        Assert.NotEqual(first, second);
        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), FirstPixel(first!));
        Assert.Equal(((byte)0, (byte)0, (byte)255, (byte)255), FirstPixel(second!));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(tmp.Path, "cache"), "*.png").Length);
    }

    [Theory]
    [InlineData("../evil.dds")]
    [InlineData("gfx/x.png")]
    [InlineData("C:/x.dds")]
    public void Rejects_unsafe_or_non_dds_paths(string path)
    {
        using var tmp = new TempDir();
        using var s = ContentSource.FromPath("g", tmp.Mkdir("g"));

        Assert.Null(new IconCache(Path.Combine(tmp.Path, "cache")).DataUri([s], new IconRef(path)));
    }
}

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

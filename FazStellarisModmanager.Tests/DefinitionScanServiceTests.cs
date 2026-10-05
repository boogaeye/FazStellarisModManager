using System.IO.Compression;
using FazStellarisModmanager.Core.Conflicts;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class DefinitionScanServiceTests
{
    static InstalledMod Mod(string name, string content) =>
        new("mod:" + name, name, "mod/" + name + ".mod", null, null, null, content, ModSource.Local, []);

    [Fact]
    public async Task Scans_folder_and_zip_mods()
    {
        using var t = new TempDir();
        t.Write("a/common/buildings/x.txt", "b1 = { } b2 = { }");
        t.Write("a/events/e.txt", "namespace = a country_event = { id = a.1 }");
        t.Write("a/common/on_actions/o.txt", "on_game_start = { }");
        var zip = Path.Combine(t.Path, "b.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var w = new StreamWriter(z.CreateEntry("common/buildings/y.txt").Open())) w.Write("b1 = { }");

        var service = new DefinitionScanService();
        var scan = await service.ScanAsync([Mod("a", Path.Combine(t.Path, "a")), Mod("b", zip), null], null, CancellationToken.None);

        Assert.Empty(scan.Errors);
        Assert.Equal(["common/buildings/x.txt", "events/e.txt"], scan.Mods[0].Files.Select(f => f.Path).Order());
        Assert.Equal(["b1", "b2"], scan.Mods[0].Files.Single(f => f.Path.EndsWith("x.txt")).Names);
        Assert.Equal(["a.1"], scan.Mods[0].Files.Single(f => f.Path.StartsWith("events")).Names);
        Assert.Equal(["b1"], Assert.Single(scan.Mods[1].Files).Names);
        Assert.Empty(scan.Mods[2].Files);
    }

    [Fact]
    public async Task Reuses_names_until_the_file_changes()
    {
        using var t = new TempDir();
        var f = t.Write("a/common/buildings/x.txt", "b1 = { }");
        var mod = Mod("a", Path.Combine(t.Path, "a"));
        var service = new DefinitionScanService();
        var first = await service.ScanAsync([mod], null, CancellationToken.None);
        var again = await service.ScanAsync([mod], null, CancellationToken.None);
        Assert.Same(first.Mods[0].Files[0].Names, again.Mods[0].Files[0].Names);

        File.WriteAllText(f, "b1 = { } b9 = { }");
        File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(1));
        var changed = await service.ScanAsync([mod], null, CancellationToken.None);
        Assert.Equal(["b1", "b9"], changed.Mods[0].Files[0].Names);
    }

    [Fact]
    public async Task Unreadable_mod_is_an_error_line()
    {
        using var t = new TempDir();
        var service = new DefinitionScanService();
        var scan = await service.ScanAsync([Mod("gone", Path.Combine(t.Path, "missing"))], null, CancellationToken.None);
        Assert.Empty(scan.Mods[0].Files);
        Assert.Contains("gone", Assert.Single(scan.Errors));
    }

    [Fact]
    public async Task Reports_progress_per_mod()
    {
        using var t = new TempDir();
        t.Mkdir("a");
        var seen = new System.Collections.Concurrent.ConcurrentBag<int>();
        await new DefinitionScanService().ScanAsync([Mod("a", Path.Combine(t.Path, "a")), null], n => seen.Add(n), CancellationToken.None);
        Assert.Equal([1, 2], seen.Order());
    }
}

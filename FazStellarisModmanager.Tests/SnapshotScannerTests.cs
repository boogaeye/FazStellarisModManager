using System.Collections.Concurrent;
using FazStellarisModmanager.Core.Hashing;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Snapshots;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class SnapshotScannerTests
{
    sealed class BagProgress : IProgress<string>
    {
        public ConcurrentBag<string> Messages { get; } = new();
        public void Report(string value) => Messages.Add(value);
    }

    [Fact]
    public async Task Scans_base_dlc_and_enabled_mods_in_load_order()
    {
        using var fake = new FakeInstall();
        ModLibrary.EnsureWorkshopDescriptors(fake.UserDir, fake.WorkshopDir);
        fake.Write("user/dlc_load.json",
            "{\"disabled_dlcs\":[],\"enabled_mods\":[\"mod/ugc_111.mod\",\"mod/local.mod\",\"mod/ugc_111.mod\",\"mod/gone.mod\"]}");

        var snap = await SnapshotScanner.ScanAsync(fake.UserDir, fake.GameDir, "PC1", HashCache.InMemory());

        Assert.Equal("PC1", snap.Machine);
        Assert.Equal("Pegasus v4.4.6", snap.GameVersion);
        Assert.Equal(new[] { "common/a.txt", "common/sub/b.txt" }, snap.Base.Files.Select(f => f.Path));

        var dlc = Assert.Single(snap.Dlcs);
        Assert.Equal("dlc:dlc001", dlc.Key);
        Assert.Equal("Test DLC", dlc.Name);
        Assert.StartsWith("crc32:", Assert.Single(dlc.Files).Md5);

        Assert.Equal(new[] { ("ugc:111", 1), ("local:local.mod", 2), ("local:gone.mod", 3) },
            snap.Mods.Select(m => (m.Key, m.LoadOrder)));
        Assert.Equal(new[] { "common/x.txt", "descriptor.mod" }, snap.Mods[0].Files.Select(f => f.Path));
        Assert.Equal(new[] { "common/y.txt" }, snap.Mods[1].Files.Select(f => f.Path));
        Assert.Empty(snap.Mods[2].Files);
    }

    [Fact]
    public async Task Disabled_dlc_is_skipped()
    {
        using var fake = new FakeInstall();
        fake.Write("user/dlc_load.json", "{\"disabled_dlcs\":[\"dlc/dlc001_test/dlc001.dlc\"],\"enabled_mods\":[]}");

        var snap = await SnapshotScanner.ScanAsync(fake.UserDir, fake.GameDir, "PC1", HashCache.InMemory());

        Assert.Empty(snap.Dlcs);
    }

    [Fact]
    public async Task Second_scan_hits_cache()
    {
        using var fake = new FakeInstall();
        var cache = HashCache.InMemory();

        await SnapshotScanner.ScanAsync(fake.UserDir, fake.GameDir, "PC1", cache);
        var missesAfterFirst = cache.Misses;
        await SnapshotScanner.ScanAsync(fake.UserDir, fake.GameDir, "PC1", cache);

        Assert.True(missesAfterFirst > 0);
        Assert.Equal(missesAfterFirst, cache.Misses);
    }

    [Fact]
    public async Task Locked_mod_file_is_skipped_and_reported()
    {
        using var fake = new FakeInstall();
        var locked = Path.Combine(fake.UserDir, "mod", "local", "common", "y.txt");
        var progress = new BagProgress();

        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var snap = await SnapshotScanner.ScanAsync(fake.UserDir, fake.GameDir, "PC1", HashCache.InMemory(), progress);

            var mod = Assert.Single(snap.Mods);
            Assert.Empty(mod.Files);
        }

        Assert.Contains(progress.Messages, m => m.Contains("[unreadable]"));
    }
}

using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ModManagerServiceTests
{
    static ModManagerService Create(FakeInstall fake)
    {
        var paths = new AppPaths(fake.DataDir);
        SettingsStore.Save(paths.Settings, new AppSettings(UserDir: fake.UserDir));
        return new ModManagerService(paths, _ => fake.GameDir);
    }

    [Fact]
    public void Resolve_uses_settings_and_derives_workshop_dir()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);

        var p = svc.Resolve();

        Assert.Equal(fake.UserDir, p.UserDir);
        Assert.Equal(fake.GameDir, p.GameDir);
        Assert.Equal(fake.WorkshopDir, p.WorkshopDir);
    }

    [Fact]
    public void RefreshLibrary_creates_workshop_descriptors_and_scans()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);

        var lib = svc.RefreshLibrary();

        Assert.Equal(new[] { "local:local.mod", "ugc:111" }, lib.Select(m => m.Key));
        Assert.True(File.Exists(Path.Combine(fake.UserDir, "mod", "ugc_111.mod")));
    }

    [Fact]
    public void RefreshLibrary_reports_unreadable_descriptors_and_keeps_the_rest()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);
        var locked = fake.Write("user/mod/locked.mod", "name=\"Locked\"\npath=\"mod/locked\"\n");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var lib = svc.RefreshLibrary();

        Assert.Single(svc.LibraryErrors);
        Assert.Equal(new[] { "local:local.mod", "ugc:111" }, lib.Select(m => m.Key));
    }

    [Fact]
    public void ImportCurrent_reads_dlc_load_with_library_names()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);
        svc.RefreshLibrary();

        var list = svc.ImportCurrent("Current");

        var entry = Assert.Single(list.Mods);
        Assert.Equal(("local:local.mod", "Local Mod"), (entry.Key, entry.Name));
        Assert.True(svc.IsInstalled(entry));
        Assert.False(svc.IsInstalled(new ModListEntry("ugc:9", "Nine", "mod/ugc_9.mod", "9")));
    }

    [Fact]
    public void Apply_writes_dlc_load_and_backs_up()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);
        svc.RefreshLibrary();
        var list = new ModList("L", [new("ugc:111", "Workshop One", "mod/ugc_111.mod", "111"), new("local:local.mod", "Local Mod", "mod/local.mod", null)], []);

        var backup = svc.Apply(list);

        Assert.Equal(new[] { "mod/ugc_111.mod", "mod/local.mod" }, DlcLoadFile.Read(fake.UserDir).EnabledMods);
        Assert.NotNull(backup);
        Assert.StartsWith(Path.Combine(fake.DataDir, "backups"), backup);
    }

    [Fact]
    public void UpdateSettings_persists()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);

        svc.UpdateSettings(svc.Settings with { PlayerName = "Faz" });

        Assert.Equal("Faz", new ModManagerService(new AppPaths(fake.DataDir), _ => fake.GameDir).Settings.PlayerName);
    }

    [Fact]
    public void Launch_without_game_dir_throws_helpful_error()
    {
        using var fake = new FakeInstall();
        var svc = new ModManagerService(new AppPaths(fake.DataDir), _ => null);

        var ex = Assert.Throws<InvalidOperationException>(svc.Launch);

        Assert.Contains("Settings", ex.Message);
    }

    [Fact]
    public async Task RefreshLibraryAsync_returns_the_same_keys()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);

        var lib = await svc.RefreshLibraryAsync();

        Assert.Equal(new[] { "local:local.mod", "ugc:111" }, lib.Select(m => m.Key));
    }

    [Fact]
    public void ImportCurrent_throws_InvalidDataException_on_corrupt_dlc_load()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);
        svc.RefreshLibrary();
        fake.Write("user/dlc_load.json", "{not json");

        Assert.Throws<InvalidDataException>(() => svc.ImportCurrent("Current"));
    }
}

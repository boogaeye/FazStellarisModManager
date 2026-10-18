using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Addons;
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ModManagerServiceTests
{
    static ModManagerService Create(FakeInstall fake)
    {
        var paths = new AppPaths(fake.DataDir);
        SettingsStore.Save(paths.Settings, new AppSettings(UserDir: fake.UserDir));
        return new ModManagerService(paths, _ => fake.GameDir) { IsLauncherRunning = () => false };
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

        var result = svc.Apply(list);

        Assert.Equal(new[] { "mod/ugc_111.mod", "mod/local.mod" }, DlcLoadFile.Read(fake.UserDir).EnabledMods);
        Assert.NotNull(result.BackupPath);
        Assert.StartsWith(Path.Combine(fake.DataDir, "backups"), result.BackupPath);
        Assert.False(result.LauncherPlaysetSet);
        Assert.Null(result.LauncherWarning);
    }

    [Fact]
    public void Apply_refuses_while_the_launcher_runs_and_writes_nothing()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);
        svc.IsLauncherRunning = () => true;
        var before = File.ReadAllText(Path.Combine(fake.UserDir, "dlc_load.json"));

        var ex = Assert.Throws<InvalidOperationException>(() => svc.Apply(new ModList("L", [new("local:local.mod", "Local Mod", "mod/local.mod", null)], [])));

        Assert.Contains("Paradox launcher", ex.Message);
        Assert.Equal(before, File.ReadAllText(Path.Combine(fake.UserDir, "dlc_load.json")));
    }

    [Fact]
    public void Apply_sets_the_launcher_playset_when_the_launcher_database_exists()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);
        var db = Path.Combine(fake.UserDir, LauncherPlaysetFile.FileName);
        LauncherDb.Create(db);
        LauncherDb.AddMod(db, "m1", "mod/local.mod");

        var result = svc.Apply(new ModList("L", [new("local:local.mod", "Local Mod", "mod/local.mod", null)], []));

        Assert.True(result.LauncherPlaysetSet);
        Assert.Null(result.LauncherWarning);
        Assert.Equal("m1", LauncherDb.Query(db, "select modId from playsets_mods").Single()[0]);
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

    // A second descriptor named "Local Mod", so mod/local.mod and mod/copy.mod clash.
    static ModManagerService WithDupe(FakeInstall fake)
    {
        fake.Write("user/mod/copy.mod", "name=" + '"' + "Local Mod" + '"' + (char)10 + "path=" + '"' + "mod/local" + '"' + (char)10);
        var svc = Create(fake);
        svc.RefreshLibrary();
        return svc;
    }

    static ModList LocalList() => new("L", [new("local:local.mod", "Local Mod", "mod/local.mod", null)], []);

    [Fact]
    public void DupesIn_returns_groups_touching_the_list()
    {
        using var fake = new FakeInstall();
        var svc = WithDupe(fake);

        var group = Assert.Single(svc.DupesIn(LocalList()));

        Assert.Equal(new[] { "mod/copy.mod", "mod/local.mod" }, group.Mods.Select(m => m.DescriptorRel));
    }

    [Fact]
    public void DupesIn_ignores_groups_with_no_member_in_the_list()
    {
        using var fake = new FakeInstall();
        var svc = WithDupe(fake);

        Assert.Empty(svc.DupesIn(new ModList("L", [new("ugc:111", "Workshop One", "mod/ugc_111.mod", "111")], [])));
    }

    [Fact]
    public void ResolveDupesWithDefaults_keeps_the_listed_copy_and_rescans()
    {
        using var fake = new FakeInstall();
        var svc = WithDupe(fake);
        var list = LocalList();

        var result = svc.ResolveDupesWithDefaults(list);

        var rename = Assert.Single(result.Renamed);
        Assert.Equal(("mod/copy.mod", "Local Mod (duplicate: copy)"), (rename.DescriptorRel, rename.NewName));
        Assert.Null(result.LauncherWarning);
        Assert.Contains("Local Mod (duplicate: copy)", File.ReadAllText(Path.Combine(fake.UserDir, "mod", "copy.mod")));
        Assert.Contains("Local Mod", File.ReadAllText(Path.Combine(fake.UserDir, "mod", "local.mod")));
        Assert.DoesNotContain("duplicate", File.ReadAllText(Path.Combine(fake.UserDir, "mod", "local.mod")));
        Assert.Equal("Local Mod (duplicate: copy)", svc.Library.Single(m => m.DescriptorRel == "mod/copy.mod").Name);
        Assert.Equal("Local Mod", svc.Library.Single(m => m.DescriptorRel == "mod/local.mod").Name);
        Assert.Empty(svc.DupesIn(list));
    }

    [Fact]
    public void ResolveDupesWithDefaults_does_nothing_without_dupes()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);
        svc.RefreshLibrary();

        var result = svc.ResolveDupesWithDefaults(LocalList());

        Assert.Empty(result.Renamed);
        Assert.Null(result.BackupDir);
        Assert.Null(result.LauncherWarning);
    }

    [Fact]
    public void ResolveDupes_refuses_while_the_launcher_runs_and_renames_nothing()
    {
        using var fake = new FakeInstall();
        var svc = WithDupe(fake);
        var group = Assert.Single(svc.DupesIn(LocalList()));
        var copy = Path.Combine(fake.UserDir, "mod", "copy.mod");
        var before = File.ReadAllBytes(copy);
        svc.IsLauncherRunning = () => true;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            svc.ResolveDupes([new DupeChoice(group, group.Mods.Single(m => m.DescriptorRel == "mod/local.mod"))]));

        Assert.Contains("Paradox launcher", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(copy));
    }

    // Faz Addons: nanites (rare, off the market in the base game) goes on the market.
    static AddonSettings NanitesOnMarket(bool enabled = true) => new(enabled, new MarketplaceSettings(
        Resources: new Dictionary<string, ResourceOverride> { ["nanites"] = new(OnMarket: true) }));

    static ModManagerService WithAddons(FakeInstall fake, AddonSettings? settings)
    {
        fake.AddMarket();
        var svc = Create(fake);
        svc.EffectiveAddons = () => settings;
        svc.RefreshLibrary();
        return svc;
    }

    static string AddonFile(FakeInstall fake, string rel) => Path.Combine(fake.UserDir, "mod", "faz_addons", rel);

    [Fact]
    public void Apply_with_addons_on_builds_Faz_Addons_and_loads_it_last()
    {
        using var fake = new FakeInstall();
        var svc = WithAddons(fake, NanitesOnMarket());
        var db = Path.Combine(fake.UserDir, LauncherPlaysetFile.FileName);
        LauncherDb.Create(db);
        LauncherDb.AddMod(db, "m1", "mod/local.mod");
        var list = new ModList("L", [new("ugc:111", "Workshop One", "mod/ugc_111.mod", "111"), new("local:local.mod", "Local Mod", "mod/local.mod", null)], []);

        var result = svc.Apply(list);

        Assert.True(result.AddonsApplied);
        Assert.Null(result.AddonWarning);
        Assert.Null(result.LauncherWarning);
        Assert.Equal(new[] { "mod/ugc_111.mod", "mod/local.mod", "mod/faz_addons.mod" }, DlcLoadFile.Read(fake.UserDir).EnabledMods);
        Assert.Equal(new object?[] { "mod/ugc_111.mod", "mod/local.mod", "mod/faz_addons.mod" },
            LauncherDb.Query(db, "select m.gameRegistryId from playsets_mods p join mods m on m.id = p.modId order by p.position").Select(r => r[0]));
        var resources = File.ReadAllText(AddonFile(fake, FakeInstall.MarketFile));
        Assert.Contains("nanites = {" + (char)10 + (char)9 + "market_amount = 5" + (char)10 + (char)9 + "market_price = 100", resources);
        var descriptor = File.ReadAllText(Path.Combine(fake.UserDir, "mod", "faz_addons.mod"));
        Assert.Contains("name=" + (char)34 + "Faz Addons" + (char)34, descriptor);
        Assert.Contains("supported_version=" + (char)34 + "v4.5.*" + (char)34, descriptor);
        Assert.Contains("path=" + (char)34 + "mod/faz_addons" + (char)34, descriptor);
    }

    [Fact]
    public void Apply_builds_Faz_Addons_from_the_lists_mods()
    {
        using var fake = new FakeInstall();
        var svc = WithAddons(fake, NanitesOnMarket());
        // The local mod ships its own copy of the file, which replaces the base game's.
        fake.Write("user/mod/local/" + FakeInstall.MarketFile, "nanites = { category = rare market_price = 40 }" + (char)10 + "local_res = { category = basic }" + (char)10);

        svc.Apply(LocalList());

        Assert.Equal("nanites = { market_amount = 5 category = rare market_price = 40 }" + (char)10 + "local_res = { category = basic }" + (char)10,
            File.ReadAllText(AddonFile(fake, FakeInstall.MarketFile)).Replace((char)10 + "" + (char)9, " "));
    }

    [Fact]
    public void Apply_without_effective_addon_settings_does_not_load_Faz_Addons()
    {
        using var fake = new FakeInstall();
        var svc = WithAddons(fake, null);

        var result = svc.Apply(LocalList());

        Assert.False(result.AddonsApplied);
        Assert.Null(result.AddonWarning);
        Assert.Equal(new[] { "mod/local.mod" }, DlcLoadFile.Read(fake.UserDir).EnabledMods);
        Assert.False(Directory.Exists(Path.Combine(fake.UserDir, "mod", "faz_addons")));
    }

    [Fact]
    public void Apply_with_addons_off_or_unchanged_does_not_load_Faz_Addons()
    {
        foreach (var settings in new[]
        {
            NanitesOnMarket(enabled: false),
            new AddonSettings(Enabled: true),
            new AddonSettings(Enabled: true, new MarketplaceSettings(Enabled: false, MaxBaseAmounts: 1000)),
            // Equal to the game's values: nothing changes.
            new AddonSettings(Enabled: true, new MarketplaceSettings(Resources: new Dictionary<string, ResourceOverride> { ["energy"] = new(OnMarket: true, MarketAmount: 100) })),
        })
        {
            using var fake = new FakeInstall();
            var svc = WithAddons(fake, settings);

            var result = svc.Apply(LocalList());

            Assert.False(result.AddonsApplied);
            Assert.Null(result.AddonWarning);
            Assert.Equal(new[] { "mod/local.mod" }, DlcLoadFile.Read(fake.UserDir).EnabledMods);
        }
    }

    [Fact]
    public void Apply_never_loads_Faz_Addons_from_the_list_itself()
    {
        using var fake = new FakeInstall();
        var svc = WithAddons(fake, null);
        var list = new ModList("L", [new("local:faz_addons.mod", "Faz Addons", "mod/faz_addons.mod", null), new("local:local.mod", "Local Mod", "mod/local.mod", null),
            new("local:faz_addons.mod", "Faz Addons", "mod" + (char)92 + "FAZ_ADDONS.mod", null)], []);

        svc.Apply(list);

        Assert.Equal(new[] { "mod/local.mod" }, DlcLoadFile.Read(fake.UserDir).EnabledMods);
    }

    [Fact]
    public void Apply_without_a_game_folder_warns_and_applies_the_list_without_Faz_Addons()
    {
        using var fake = new FakeInstall();
        fake.AddMarket();
        var paths = new AppPaths(fake.DataDir);
        SettingsStore.Save(paths.Settings, new AppSettings(UserDir: fake.UserDir));
        var svc = new ModManagerService(paths, _ => null) { IsLauncherRunning = () => false, EffectiveAddons = () => NanitesOnMarket() };

        var result = svc.Apply(LocalList());

        Assert.False(result.AddonsApplied);
        Assert.Contains("Faz Addons", result.AddonWarning);
        Assert.Contains("Settings", result.AddonWarning);
        Assert.Equal(new[] { "mod/local.mod" }, DlcLoadFile.Read(fake.UserDir).EnabledMods);
    }

    [Fact]
    public void Apply_warns_and_skips_Faz_Addons_when_its_files_cannot_be_written()
    {
        using var fake = new FakeInstall();
        var svc = WithAddons(fake, NanitesOnMarket());
        var locked = fake.Write("user/mod/faz_addons/README.txt", "old");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = svc.Apply(LocalList());

        Assert.False(result.AddonsApplied);
        Assert.Contains("Faz Addons", result.AddonWarning);
        Assert.Equal(new[] { "mod/local.mod" }, DlcLoadFile.Read(fake.UserDir).EnabledMods);
    }

    [Fact]
    public void The_library_and_ImportCurrent_leave_out_Faz_Addons()
    {
        using var fake = new FakeInstall();
        var svc = WithAddons(fake, NanitesOnMarket());
        svc.Apply(LocalList());

        var lib = svc.RefreshLibrary();
        var imported = svc.ImportCurrent("Current");

        Assert.True(File.Exists(Path.Combine(fake.UserDir, "mod", "faz_addons.mod")));
        Assert.Equal(new[] { "local:local.mod", "ugc:111" }, lib.Select(m => m.Key));
        Assert.Equal(new[] { "mod/local.mod" }, imported.Mods.Select(m => m.DescriptorRel));
    }

    [Fact]
    public void Launch_is_refused_while_a_reason_blocks_it()
    {
        using var fake = new FakeInstall();
        var manager = new ModManagerService(new AppPaths(fake.DataDir), _ => fake.GameDir) { LaunchBlockedReason = () => "busy downloading" };

        Assert.Equal("busy downloading", Assert.Throws<InvalidOperationException>(manager.Launch).Message);
    }
}

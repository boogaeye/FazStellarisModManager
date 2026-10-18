using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Addons;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class AddonServiceTests
{
    static readonly string Lf = MarketFixture.Lf;

    static (FakeInstall Fake, ModManagerService Manager) Create(Func<string?, string?>? findGameDir = null, AddonSettings? saved = null)
    {
        var fake = new FakeInstall();
        fake.AddMarket();
        fake.Write("lib/steamapps/common/Stellaris/localisation/english/r_l_english.yml", "l_english:" + Lf + " energy: " + (char)34 + "Energy Credits" + (char)34 + Lf);
        var icon = Path.Combine(fake.GameDir, "gfx", "interface", "icons", "resources", "energy.dds");
        Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
        File.WriteAllBytes(icon, DdsBuilder.Bgra32(1, 1, (9, 9, 9, 255)));
        var paths = new AppPaths(fake.DataDir);
        SettingsStore.Save(paths.Settings, new AppSettings(UserDir: fake.UserDir));
        if (saved is not null) AddonSettingsStore.Save(paths.Addons, saved);
        var manager = new ModManagerService(paths, findGameDir ?? (_ => fake.GameDir)) { IsLauncherRunning = () => false };
        manager.RefreshLibrary();
        return (fake, manager);
    }

    static ModList LocalList() => new("L", [new("local:local.mod", "Local Mod", "mod/local.mod", null)], []);

    static AddonSettings Sample() => new(Enabled: true, new MarketplaceSettings(MaxBaseAmounts: 1000,
        Resources: new Dictionary<string, ResourceOverride> { ["nanites"] = new(OnMarket: true) }));

    [Fact]
    public void Settings_are_loaded_from_the_app_data_folder()
    {
        var (fake, manager) = Create(saved: Sample());
        using var _cleanup = fake;

        Assert.Equal(Sample(), new AddonService(manager).Settings);
    }

    [Fact]
    public void Settings_default_to_addons_off()
    {
        var (fake, manager) = Create();
        using var _cleanup = fake;

        Assert.Equal(new AddonSettings(), new AddonService(manager).Settings);
    }

    [Fact]
    public void Save_persists_and_raises_Changed()
    {
        var (fake, manager) = Create();
        using var _cleanup = fake;
        var addons = new AddonService(manager);
        var changed = 0;
        addons.Changed += () => changed++;

        addons.Save(Sample());

        Assert.Equal(Sample(), addons.Settings);
        Assert.Equal(Sample(), AddonSettingsStore.Load(manager.Paths.Addons));
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Save_drops_overrides_equal_to_the_game_once_the_catalog_is_built()
    {
        var (fake, manager) = Create();
        using var _cleanup = fake;
        var addons = new AddonService(manager);
        var unchanged = new AddonSettings(Enabled: true, new MarketplaceSettings(Resources: new Dictionary<string, ResourceOverride>
        {
            ["energy"] = new(OnMarket: true, MarketAmount: 100, BaseStorage: 30000),
            ["nanites"] = new(Tradable: false),
        }));

        addons.Save(unchanged);
        Assert.Equal(unchanged, addons.Settings); // no catalog yet: kept as given
        await addons.BuildCatalogAsync(LocalList());
        addons.Save(unchanged);

        var resources = addons.Settings.Marketplace!.Resources!;
        Assert.Equal(new ResourceOverride(BaseStorage: 30000), Assert.Single(resources).Value);
        Assert.Equal(addons.Settings, AddonSettingsStore.Load(manager.Paths.Addons));
    }

    [Fact]
    public async Task Builds_the_catalog_with_names_and_icons()
    {
        var (fake, manager) = Create();
        using var _cleanup = fake;
        var addons = new AddonService(manager);
        var changed = 0;
        addons.Changed += () => changed++;

        var catalog = await addons.BuildCatalogAsync(LocalList());

        Assert.Same(catalog, addons.Catalog);
        Assert.Equal(new[] { ("energy", "Energy Credits"), ("nanites", "nanites") }, catalog.Resources.Select(r => (r.Key, r.Name)));
        Assert.StartsWith("data:image/png;base64,", catalog.Find("energy")!.IconUri);
        Assert.Null(catalog.Find("nanites")!.IconUri);
        Assert.False(addons.IsBuilding);
        Assert.Null(addons.Progress);
        Assert.True(changed > 0);
    }

    [Fact]
    public async Task The_catalog_is_cached_per_list_until_invalidated()
    {
        var (fake, manager) = Create();
        using var _cleanup = fake;
        var addons = new AddonService(manager);
        var first = await addons.BuildCatalogAsync(LocalList());
        fake.Write("user/mod/local/common/strategic_resources/zz_local.txt", "local_res = { category = basic }" + Lf);

        var again = await addons.BuildCatalogAsync(LocalList());
        var vanilla = await addons.BuildCatalogAsync(new ModList("Vanilla", [], []));
        addons.InvalidateCatalog();
        var rebuilt = await addons.BuildCatalogAsync(LocalList());

        Assert.Same(first, again);
        Assert.Null(first.Find("local_res"));
        Assert.Equal(new[] { "energy", "nanites" }, vanilla.Resources.Select(r => r.Key));
        Assert.NotNull(rebuilt.Find("local_res"));
        Assert.Same(rebuilt, addons.Catalog);
    }

    [Fact]
    public async Task Concurrent_builds_for_the_same_list_share_one_catalog()
    {
        var (fake, manager) = Create();
        using var _cleanup = fake;
        var addons = new AddonService(manager);

        var both = await Task.WhenAll(addons.BuildCatalogAsync(LocalList()), addons.BuildCatalogAsync(LocalList()));

        Assert.Same(both[0], both[1]);
    }

    [Fact]
    public async Task Without_a_game_folder_the_build_fails_and_keeps_no_catalog()
    {
        var (fake, manager) = Create(findGameDir: _ => null);
        using var _cleanup = fake;
        var addons = new AddonService(manager);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => addons.BuildCatalogAsync(LocalList()));

        Assert.Contains("Settings", ex.Message);
        Assert.Null(addons.Catalog);
        Assert.False(addons.IsBuilding);
        Assert.Null(addons.Progress);
    }

    [Fact]
    public async Task Preview_builds_the_files_Apply_would_write_for_the_catalogs_list()
    {
        var (fake, manager) = Create();
        using var _cleanup = fake;
        var addons = new AddonService(manager);

        Assert.Null(addons.Preview(Sample()));
        await addons.BuildCatalogAsync(LocalList());
        var output = addons.Preview(Sample())!;

        Assert.True(output.HasChanges);
        Assert.Contains("market_amount = 5", System.Text.Encoding.UTF8.GetString(output.Get(FakeInstall.MarketFile)!));
        Assert.Contains("supported_version=" + (char)34 + "v4.5.*" + (char)34, System.Text.Encoding.UTF8.GetString(output.Get(AddonBuilder.DescriptorPath)!));
    }

    [Fact]
    public async Task Outside_a_session_the_effective_settings_are_this_PCs_own()
    {
        var (fake, manager) = Create(saved: Sample());
        using var _cleanup = fake;
        var addons = new AddonService(manager);
        await using var session = new SessionService(manager);

        Assert.Equal(Sample(), addons.EffectiveFor(session));
    }
}

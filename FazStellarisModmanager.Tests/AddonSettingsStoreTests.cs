using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Addons;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class AddonSettingsStoreTests
{
    static AddonSettings Sample() => new(Enabled: true, new MarketplaceSettings(
        MaxBaseAmounts: 1000, TradeFeeBase: 0.2, MinFluctuation: -50,
        Resources: new Dictionary<string, ResourceOverride>
        {
            ["nuclei_soup"] = new(OnMarket: true, MarketAmount: 10, MarketPrice: 120),
            ["minerals"] = new(BaseStorage: 30000, AiTrades: false, Tradable: false),
        }));

    [Fact]
    public void Addons_path_is_under_root() =>
        Assert.Equal(@"C:\data\addons.json", new AppPaths(@"C:\data").Addons);

    [Fact]
    public void Missing_file_gives_the_defaults_with_addons_off()
    {
        using var tmp = new TempDir();

        var s = AddonSettingsStore.Load(Path.Combine(tmp.Path, "addons.json"));

        Assert.Equal(new AddonSettings(), s);
        Assert.False(s.Enabled);
    }

    [Fact]
    public void Settings_round_trip()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, "sub", "addons.json");

        AddonSettingsStore.Save(file, Sample());
        var back = AddonSettingsStore.Load(file);

        Assert.Equal(Sample(), back);
        Assert.Equal(1000, back.Marketplace!.MaxBaseAmounts);
        Assert.Equal(30000, back.Marketplace.Resources!["minerals"].BaseStorage);
        Assert.Null(back.Marketplace.Resources["minerals"].OnMarket);
        Assert.True(back.Marketplace.Enabled);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("[1, 2]")]
    public void Corrupt_files_give_the_defaults(string json)
    {
        using var tmp = new TempDir();
        var file = tmp.Write("addons.json", json);

        Assert.Equal(new AddonSettings(), AddonSettingsStore.Load(file));
    }

    [Fact]
    public void Null_resource_entries_are_dropped_on_load()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("addons.json", """{ "Enabled": true, "Marketplace": { "Resources": { "energy": null, "food": { "Tradable": true } } } }""");

        var s = AddonSettingsStore.Load(file);

        Assert.True(s.Enabled);
        Assert.Equal(["food"], s.Marketplace!.Resources!.Keys);
    }

    [Fact]
    public void Settings_compare_by_value_including_the_resource_overrides()
    {
        var reordered = new AddonSettings(Enabled: true, new MarketplaceSettings(
            MaxBaseAmounts: 1000, TradeFeeBase: 0.2, MinFluctuation: -50,
            Resources: new Dictionary<string, ResourceOverride>
            {
                ["minerals"] = new(BaseStorage: 30000, AiTrades: false, Tradable: false),
                ["nuclei_soup"] = new(OnMarket: true, MarketAmount: 10, MarketPrice: 120),
            }));

        Assert.Equal(Sample(), reordered);
        Assert.Equal(Sample().GetHashCode(), reordered.GetHashCode());
        Assert.NotEqual(Sample(), reordered with { Marketplace = reordered.Marketplace! with { Resources = null } });
        Assert.Equal(new MarketplaceSettings(Resources: new Dictionary<string, ResourceOverride>()), new MarketplaceSettings());
    }

    static MarketFixture Game() => new MarketFixture()
        .Base("common/strategic_resources/00_strategic_resources.txt", """
            energy = { tradable = yes category = basic market_amount = 100 market_price = 100 max = 25000 }
            influence = { category = other max = 1000 fixed_max_amount = yes }
            nuclei_soup = { tradable = yes category = strategic }
            """)
        .Base("common/defines/00_defines.txt", """
            NEconomy = {
            	MARKET_TRADE_FEE_BASE = 0.3
            	MARKET_TRADE_FEE_MIN = 0.05
            	MARKET_MIN_FLUCTUATION_FROM_BASE_PRICE = -80
            	MARKET_MAX_FLUCTUATION_FROM_BASE_PRICE = 400
            	MARKET_MAX_BASE_AMOUNTS = 200
            }
            NAI = { AI_ALLOWED_TO_BUY = { "energy" } AI_ALLOWED_TO_SELL = { "energy" } }
            """);

    [Fact]
    public void Normalize_drops_overrides_equal_to_the_game_values()
    {
        using var game = Game();
        var catalog = game.Catalog();
        var s = new AddonSettings(Enabled: true, new MarketplaceSettings(
            MaxBaseAmounts: 200, TradeFeeBase: 0.30000001, TradeFeeMin: 0.1, MinFluctuation: -80, MaxFluctuation: 500,
            Resources: new Dictionary<string, ResourceOverride>
            {
                ["energy"] = new(OnMarket: true, MarketAmount: 100, MarketPrice: 150, Tradable: true, AiTrades: true, BaseStorage: 25000),
                ["influence"] = new(BaseStorage: 5000),
                ["nuclei_soup"] = new(OnMarket: false, MarketAmount: 7, BaseStorage: 100, AiTrades: false),
                ["mystery"] = new(Tradable: true),
            }));

        var n = s.Normalize(catalog);

        var m = n.Marketplace!;
        Assert.True(n.Enabled);
        Assert.Null(m.MaxBaseAmounts);
        Assert.Null(m.TradeFeeBase);
        Assert.Equal(0.1, m.TradeFeeMin);
        Assert.Null(m.MinFluctuation);
        Assert.Equal(500, m.MaxFluctuation);
        Assert.Equal(["energy", "mystery"], m.Resources!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new ResourceOverride(MarketPrice: 150), m.Resources["energy"]);
        Assert.Equal(new ResourceOverride(Tradable: true), m.Resources["mystery"]);
        Assert.Equal(4, m.ChangeCount);
    }

    [Fact]
    public void Normalize_keeps_real_changes()
    {
        using var game = Game();
        var catalog = game.Catalog();
        var s = new AddonSettings(Enabled: true, new MarketplaceSettings(
            Resources: new Dictionary<string, ResourceOverride>
            {
                ["energy"] = new(OnMarket: false, MarketPrice: 150, AiTrades: false, BaseStorage: 30000),
                ["nuclei_soup"] = new(OnMarket: true, MarketAmount: 10, MarketPrice: 100, AiTrades: true, Tradable: false),
            }));

        var m = s.Normalize(catalog).Marketplace!;

        // Off the market, a price has no effect.
        Assert.Equal(new ResourceOverride(OnMarket: false, AiTrades: false, BaseStorage: 30000), m.Resources!["energy"]);
        Assert.Equal(new ResourceOverride(OnMarket: true, MarketAmount: 10, MarketPrice: 100, AiTrades: true, Tradable: false), m.Resources["nuclei_soup"]);
        Assert.Equal(8, m.ChangeCount);
    }

    [Fact]
    public void Normalize_with_nothing_left_has_no_resources()
    {
        using var game = Game();
        var s = new AddonSettings(Enabled: true, new MarketplaceSettings(Enabled: false,
            Resources: new Dictionary<string, ResourceOverride> { ["energy"] = new(Tradable: true), ["food"] = new() }));

        var m = s.Normalize(game.Catalog()).Marketplace!;

        Assert.False(m.Enabled);
        Assert.Null(m.Resources);
        Assert.Equal(0, m.ChangeCount);
        Assert.Equal(new AddonSettings(), new AddonSettings().Normalize(game.Catalog()));
    }
}

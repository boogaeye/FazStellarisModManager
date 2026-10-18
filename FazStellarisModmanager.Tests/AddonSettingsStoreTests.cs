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

    static CustomTech PatronageTech() => new("tech_faz_patronage", "Patronage", "More subjects.", "tech_galactic_administration",
        Ethics: ["ethic_authoritarian"], Prerequisites: ["tech_galactic_administration"],
        Modifiers: [new TechModifier("divided_patrongage_max_subjects", 1), new TechModifier("country_unity_produces_mult", 0.05)]);

    static AddonSettings WithTechs() => new(Enabled: true, Technologies: new TechnologiesSettings(Techs:
    [
        PatronageTech(),
        new CustomTech("tech_faz_rare", "Rare", "", "tech_rare", Area: "physics", Category: "particles", Tier: 2, Repeatable: false, Rare: true,
            Cost: 1200, CostPerLevel: 0, Weight: 3),
    ]));

    [Fact]
    public void Custom_techs_round_trip()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, "addons.json");

        AddonSettingsStore.Save(file, WithTechs());
        var back = AddonSettingsStore.Load(file);

        Assert.Equal(WithTechs(), back);
        var techs = back.Technologies!.Techs!;
        Assert.True(back.Technologies.Enabled);
        Assert.Equal(["ethic_authoritarian"], techs[0].Ethics!);
        Assert.Equal(0.05, techs[0].Modifiers![1].Value);
        Assert.Equal(("physics", 2, false, true, 1200), (techs[1].Area, techs[1].Tier, techs[1].Repeatable, techs[1].Rare, techs[1].Cost));
        Assert.Null(techs[1].Ethics);
        Assert.Null(back.Marketplace);
    }

    [Fact]
    public void Missing_tech_fields_take_the_defaults()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("addons.json", """{ "Technologies": { "Techs": [ { "Key": "tech_faz_x", "Name": "X", "Description": "", "Icon": "tech_x" } ] } }""");

        var s = AddonSettingsStore.Load(file);

        Assert.True(s.Technologies!.Enabled);
        Assert.Equal(new CustomTech("tech_faz_x", "X", "", "tech_x"), Assert.Single(s.Technologies.Techs!));
        Assert.Equal(("society", "statecraft", 5, true, 50000, 5000, 15), (s.Technologies.Techs![0].Area, s.Technologies.Techs[0].Category,
            s.Technologies.Techs[0].Tier, s.Technologies.Techs[0].Repeatable, s.Technologies.Techs[0].Cost, s.Technologies.Techs[0].CostPerLevel,
            s.Technologies.Techs[0].Weight));
    }

    [Fact]
    public void Null_techs_and_null_list_items_are_dropped_on_load()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("addons.json", """
            { "Enabled": true, "Technologies": { "Techs": [ null, {
                "Key": "tech_faz_x", "Name": null, "Description": null, "Icon": "tech_x", "Area": null, "Category": null,
                "Ethics": [ null, "ethic_militarist" ], "Prerequisites": [ null ], "Modifiers": [ null, { "Key": null, "Value": 2 }, { "Key": "a", "Value": 1 } ] }, null ] } }
            """);

        var tech = Assert.Single(AddonSettingsStore.Load(file).Technologies!.Techs!);

        Assert.Equal(("tech_faz_x", "", "", "", ""), (tech.Key, tech.Name, tech.Description, tech.Area, tech.Category));
        Assert.Equal(["ethic_militarist"], tech.Ethics!);
        Assert.Null(tech.Prerequisites);
        Assert.Equal([new TechModifier("", 2), new TechModifier("a", 1)], tech.Modifiers!);
    }

    [Fact]
    public void An_empty_tech_list_loads_as_none()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("addons.json", """{ "Technologies": { "Enabled": false, "Techs": [ null ] } }""");

        var t = AddonSettingsStore.Load(file).Technologies!;

        Assert.False(t.Enabled);
        Assert.Null(t.Techs);
    }

    [Fact]
    public void Techs_compare_by_value_including_their_lists()
    {
        var copy = WithTechs() with
        {
            Technologies = new TechnologiesSettings(Techs: WithTechs().Technologies!.Techs!.Select(t => t with
            {
                Ethics = t.Ethics?.ToList(), Prerequisites = t.Prerequisites?.ToArray(), Modifiers = t.Modifiers?.ToList(),
            }).ToList()),
        };
        var tech = PatronageTech();

        Assert.Equal(WithTechs(), copy);
        Assert.Equal(WithTechs().GetHashCode(), copy.GetHashCode());
        Assert.Equal(tech.GetHashCode(), (tech with { Ethics = ["ethic_authoritarian"] }).GetHashCode());
        Assert.NotEqual(tech, tech with { Modifiers = [.. tech.Modifiers!.Reverse()] });
        Assert.NotEqual(tech, tech with { Modifiers = [new TechModifier("divided_patrongage_max_subjects", 2), tech.Modifiers![1]] });
        Assert.NotEqual(tech, tech with { Prerequisites = null });
        Assert.NotEqual(tech, tech with { Cost = 1 });
        Assert.Equal(tech with { Ethics = [] }, tech with { Ethics = null });
        Assert.Equal(new TechnologiesSettings(Techs: []), new TechnologiesSettings());
        Assert.NotEqual(WithTechs(), WithTechs() with { Technologies = WithTechs().Technologies! with { Enabled = false } });
    }

    [Fact]
    public void Normalize_keeps_the_techs()
    {
        using var game = Game();

        var n = WithTechs().Normalize(game.Catalog());

        Assert.Equal(WithTechs(), n);
        Assert.Equal(WithTechs().Technologies, (WithTechs() with { Marketplace = new MarketplaceSettings(MaxBaseAmounts: 200) }).Normalize(game.Catalog()).Technologies);
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

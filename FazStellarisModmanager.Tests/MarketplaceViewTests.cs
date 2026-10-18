using FazStellarisModmanager.Core.Addons;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class MarketplaceViewTests
{
    const string Resources = """
        energy = { tradable = yes category = basic market_amount = 100 market_price = 100 max = 25000 }
        influence = { category = abstract max = 1000 fixed_max_amount = yes }
        alloys = { tradable = yes category = advanced market_amount = 25 market_price = 100 max = 15000 }
        volatile_motes = { tradable = yes category = strategic market_amount = 10 market_price = 200 max = 10000 }
        nuclei_soup = { tradable = no category = strategic market_price = 7 }
        sr_dark_matter = { tradable = yes category = rare market_amount = 5 market_price = 400 }
        sr_mystery = { tradable = yes }
        ai_unobtainium = { category = rare visibility_prerequisite = { always = no } }
        """;

    const string Defines = """
        NEconomy = {
        	MARKET_MAX_BASE_AMOUNTS = 200
        	MARKET_TRADE_FEE_BASE = 0.3
        	MARKET_TRADE_FEE_MIN = 0.05
        	MARKET_MIN_FLUCTUATION_FROM_BASE_PRICE = -80
        	MARKET_MAX_FLUCTUATION_FROM_BASE_PRICE = 400
        }
        NAI = {
        	AI_ALLOWED_TO_BUY = { "energy" "alloys" "volatile_motes" }
        	AI_ALLOWED_TO_SELL = { "energy" "alloys" }
        }
        """;

    static MarketFixture Fixture() => new MarketFixture()
        .Base("common/strategic_resources/00_strategic_resources.txt", Resources)
        .Base("common/defines/00_defines.txt", Defines);

    static MarketplaceSettings Market(params (string Key, ResourceOverride Override)[] resources) =>
        new(Resources: resources.ToDictionary(r => r.Key, r => r.Override, StringComparer.OrdinalIgnoreCase));

    static ResourceRow Row(IReadOnlyList<ResourceRow> rows, string key) => rows.Single(r => r.Key == key);

    [Fact]
    public void Rows_show_the_game_values_when_nothing_is_changed()
    {
        using var f = Fixture();
        var c = f.Catalog();

        var rows = MarketplaceView.Rows(c, null);

        Assert.Equal(c.Resources.Select(r => r.Key), rows.Select(r => r.Key));
        var energy = Row(rows, "energy");
        Assert.True(energy.OnMarket);
        Assert.Equal((100, 100), (energy.Bundle, energy.Price));
        Assert.True(energy.Tradable);
        Assert.True(energy.AiTrades);
        Assert.Equal(25000, energy.BaseStorage);
        Assert.False(energy.Changed);
        Assert.False(energy.Added);
        Assert.False(energy.HasOverride);

        var soup = Row(rows, "nuclei_soup");
        Assert.False(soup.OnMarket);
        Assert.Null(soup.Bundle);
        Assert.Equal(7, soup.Price);
        Assert.Null(soup.BaseStorage);

        // The AI buys volatile motes but doesn't sell them, so it doesn't trade them.
        Assert.False(Row(rows, "volatile_motes").AiTrades);
        Assert.Equal(1000, Row(rows, "influence").BaseStorage);
    }

    [Fact]
    public void A_resource_put_on_the_market_takes_the_category_bundle_and_its_own_price()
    {
        using var f = Fixture();
        var c = f.Catalog();

        var rows = MarketplaceView.Rows(c, Market(("nuclei_soup", new ResourceOverride(OnMarket: true)), ("sr_mystery", new ResourceOverride(OnMarket: true))));

        var soup = Row(rows, "nuclei_soup");
        Assert.True(soup.OnMarket);
        Assert.True(soup.Added);
        Assert.Equal((10, 7), (soup.Bundle, soup.Price)); // strategic bundle; its own positive price
        Assert.True(soup.MarketChanged);
        Assert.True(soup.BundleChanged);
        Assert.False(soup.PriceChanged);
        Assert.True(soup.Changed);
        Assert.True(soup.HasOverride);

        var mystery = Row(rows, "sr_mystery");
        Assert.Equal((10, MarketCatalog.SuggestedPrice), (mystery.Bundle, mystery.Price));
        Assert.True(mystery.PriceChanged);
    }

    [Fact]
    public void Overrides_replace_the_game_values_and_are_marked_changed()
    {
        using var f = Fixture();
        var c = f.Catalog();

        var rows = MarketplaceView.Rows(c, Market(
            ("energy", new ResourceOverride(MarketAmount: 50, MarketPrice: 120, Tradable: false, AiTrades: false, BaseStorage: 30000)),
            ("alloys", new ResourceOverride(OnMarket: false, MarketAmount: 99)),
            ("volatile_motes", new ResourceOverride(AiTrades: true))));

        var energy = Row(rows, "energy");
        Assert.Equal((50, 120, false, false, 30000), (energy.Bundle, energy.Price, energy.Tradable, energy.AiTrades, energy.BaseStorage));
        Assert.True(energy.BundleChanged && energy.PriceChanged && energy.TradableChanged && energy.AiTradesChanged && energy.BaseStorageChanged);
        Assert.False(energy.MarketChanged);

        // Off the market: the bundle override changes nothing and the game's bundle is shown.
        var alloys = Row(rows, "alloys");
        Assert.False(alloys.OnMarket);
        Assert.Equal(25, alloys.Bundle);
        Assert.True(alloys.MarketChanged);
        Assert.False(alloys.BundleChanged);
        Assert.False(alloys.Added);

        var motes = Row(rows, "volatile_motes");
        Assert.True(motes.AiTrades);
        Assert.True(motes.AiTradesChanged);
    }

    [Fact]
    public void Overrides_equal_to_the_game_or_not_allowed_are_not_changes()
    {
        using var f = Fixture();
        var c = f.Catalog();

        var rows = MarketplaceView.Rows(c, Market(
            ("energy", new ResourceOverride(OnMarket: true, MarketAmount: 100, Tradable: true, AiTrades: true, BaseStorage: 25000)),
            ("influence", new ResourceOverride(BaseStorage: 5000)),
            ("sr_dark_matter", new ResourceOverride(BaseStorage: 5000))));

        Assert.False(Row(rows, "energy").Changed);
        Assert.True(Row(rows, "energy").HasOverride); // still saved until normalized: the reset button can remove it
        Assert.Equal(1000, Row(rows, "influence").BaseStorage); // fixed_max_amount
        Assert.False(Row(rows, "influence").Changed);
        Assert.Null(Row(rows, "sr_dark_matter").BaseStorage); // no max
        Assert.False(Row(rows, "sr_dark_matter").Changed);
    }

    [Fact]
    public void Max_per_trade_is_the_bundle_times_the_max_base_amounts_while_on_the_market()
    {
        using var f = Fixture();
        var c = f.Catalog();
        var rows = MarketplaceView.Rows(c, null);

        Assert.Equal(20000L, Row(rows, "energy").MaxPerTrade(200));
        Assert.Equal(5L * int.MaxValue, Row(rows, "sr_dark_matter").MaxPerTrade(int.MaxValue)); // no overflow
        Assert.Null(Row(rows, "nuclei_soup").MaxPerTrade(200));
        Assert.Null(Row(rows, "energy").MaxPerTrade(null));
    }

    [Fact]
    public void Effective_market_rules_fall_back_to_the_game()
    {
        using var f = Fixture();
        var c = f.Catalog();

        Assert.Equal(200, MarketplaceView.MaxBaseAmounts(c, null));
        Assert.Equal(1000, MarketplaceView.MaxBaseAmounts(c, new MarketplaceSettings(MaxBaseAmounts: 1000)));
        Assert.Equal(200, MarketplaceView.MaxBaseAmounts(c, new MarketplaceSettings(MaxBaseAmounts: 0)));
        Assert.Null(MarketplaceView.MaxBaseAmounts(MarketCatalog.Empty, null));
    }

    [Fact]
    public void The_trade_preview_uses_the_bundles_of_energy_alloys_and_volatile_motes()
    {
        using var f = Fixture();
        var c = f.Catalog();

        var preview = MarketplaceView.TradePreview(MarketplaceView.Rows(c, Market(("alloys", new ResourceOverride(MarketAmount: 30)))), 1000);

        Assert.Equal([("Basic", 100_000L), ("Alloys", 30_000L), ("Strategic", 10_000L)], preview);
        Assert.Empty(MarketplaceView.TradePreview(MarketplaceView.Rows(MarketCatalog.Empty, null), 1000));
        var offMarket = MarketplaceView.Rows(c, Market(("alloys", new ResourceOverride(OnMarket: false))));
        Assert.Equal(["Basic", "Strategic"], MarketplaceView.TradePreview(offMarket, 200).Select(p => p.Label));
    }

    [Fact]
    public void Groups_follow_the_category_with_unknown_ones_under_other_and_hidden_ones_last()
    {
        using var f = Fixture();
        var c = f.Catalog();
        var rows = MarketplaceView.Rows(c, null);

        var groups = MarketplaceView.Group(rows, ResourceFilter.All, showHidden: true, search: null);

        Assert.Equal(["Basic", "Advanced", "Strategic", "Rare", "Other", MarketplaceView.HiddenGroup], groups.Select(g => g.Name));
        Assert.Equal(["volatile_motes", "nuclei_soup"], groups.Single(g => g.Name == "Strategic").Rows.Select(r => r.Key));
        Assert.Equal(["influence", "sr_mystery"], groups.Single(g => g.Name == "Other").Rows.Select(r => r.Key));
        Assert.Equal(["ai_unobtainium"], groups.Single(g => g.Name == MarketplaceView.HiddenGroup).Rows.Select(r => r.Key));
        Assert.Equal("Basic", MarketplaceView.GroupOf(c.Find("energy")!));
        Assert.Equal("Advanced", MarketplaceView.GroupOf(c.Find("alloys")! with { Category = "ADVANCED" }));
        Assert.Equal("Other", MarketplaceView.GroupOf(c.Find("energy")! with { Category = null }));
    }

    [Fact]
    public void Filters_hide_hidden_resources_unless_asked_and_search_name_and_key()
    {
        using var f = Fixture();
        var c = f.Catalog();
        var rows = MarketplaceView.Rows(c, Market(("energy", new ResourceOverride(MarketPrice: 150)), ("ai_unobtainium", new ResourceOverride(Tradable: true))));

        Assert.Equal(7, MarketplaceView.Count(rows, ResourceFilter.All, showHidden: false));
        Assert.Equal(8, MarketplaceView.Count(rows, ResourceFilter.All, showHidden: true));
        Assert.Equal(4, MarketplaceView.Count(rows, ResourceFilter.OnMarket, showHidden: false));
        Assert.Equal(3, MarketplaceView.Count(rows, ResourceFilter.OffMarket, showHidden: false));
        Assert.Equal(1, MarketplaceView.Count(rows, ResourceFilter.Changed, showHidden: false));
        Assert.Equal(2, MarketplaceView.Count(rows, ResourceFilter.Changed, showHidden: true));

        Assert.Equal(["energy"], Keys(MarketplaceView.Group(rows, ResourceFilter.Changed, false, null)));
        Assert.Equal(["sr_dark_matter"], Keys(MarketplaceView.Group(rows, ResourceFilter.All, false, " DARK ")));
        Assert.Equal(["sr_dark_matter", "sr_mystery"], Keys(MarketplaceView.Group(rows, ResourceFilter.All, false, "sr_")));
        Assert.Empty(Keys(MarketplaceView.Group(rows, ResourceFilter.All, false, "unobtainium")));
        Assert.Equal(["ai_unobtainium"], Keys(MarketplaceView.Group(rows, ResourceFilter.All, true, "unobtainium")));
        Assert.DoesNotContain(MarketplaceView.Group(rows, ResourceFilter.OnMarket, false, null), g => g.Rows.Count == 0);

        static List<string> Keys(IReadOnlyList<ResourceGroup> groups) => groups.SelectMany(g => g.Rows).Select(r => r.Key).ToList();
    }

    [Fact]
    public void Editing_a_resource_adds_updates_and_removes_its_override()
    {
        var settings = new AddonSettings(Enabled: true);

        var on = MarketplaceView.EditResource(settings, "nuclei_soup", o => o with { OnMarket = true });
        var priced = MarketplaceView.EditResource(on, "NUCLEI_SOUP", o => o with { MarketPrice = 90 });
        var other = MarketplaceView.EditResource(priced, "energy", o => o with { Tradable = false });
        var reset = MarketplaceView.ResetResource(other, "nuclei_soup");
        var empty = MarketplaceView.EditResource(reset, "energy", o => o with { Tradable = null });

        Assert.Null(settings.Marketplace);
        Assert.True(on.Enabled);
        Assert.Equal(new ResourceOverride(OnMarket: true, MarketPrice: 90), priced.Marketplace!.Resources!["nuclei_soup"]);
        Assert.Single(priced.Marketplace.Resources);
        Assert.Equal(2, other.Marketplace!.Resources!.Count);
        Assert.Equal(["energy"], reset.Marketplace!.Resources!.Keys);
        Assert.Null(empty.Marketplace!.Resources);
        Assert.Equal(1, on.Marketplace!.ChangeCount);
    }

    [Fact]
    public void Editing_the_market_keeps_the_resources()
    {
        var settings = MarketplaceView.EditResource(new AddonSettings(), "energy", o => o with { MarketAmount = 50 });

        var edited = MarketplaceView.EditMarket(settings, m => m with { MaxBaseAmounts = 1000, Enabled = false });

        Assert.Equal(1000, edited.Marketplace!.MaxBaseAmounts);
        Assert.False(edited.Marketplace.Enabled);
        Assert.Equal(50, edited.Marketplace.Resources!["energy"].MarketAmount);
        Assert.Equal(new MarketplaceSettings(MaxBaseAmounts: 5), MarketplaceView.EditMarket(new AddonSettings(), m => m with { MaxBaseAmounts = 5 }).Marketplace);
    }

    [Theory]
    [InlineData(0.3, "30")]
    [InlineData(0.05, "5")]
    [InlineData(0.125, "12.5")]
    [InlineData(0.33333, "33.33")]
    [InlineData(null, "")]
    public void Percent_shows_a_fraction_in_percent(double? fraction, string expected) =>
        Assert.Equal(expected, MarketplaceView.Percent(fraction));

    [Theory]
    [InlineData("30", 0.3)]
    [InlineData("12.5", 0.125)]
    [InlineData(" 5 ", 0.05)]
    [InlineData("33.333333", 0.3333)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    [InlineData("-1", null)]
    [InlineData("101", null)]
    public void Fractions_are_read_from_percent_between_0_and_100(string text, double? expected) =>
        Assert.Equal(expected, MarketplaceView.FractionFromPercent(text));
}

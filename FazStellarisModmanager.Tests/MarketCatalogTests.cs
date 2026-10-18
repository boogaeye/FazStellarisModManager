using FazStellarisModmanager.Core.Addons;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class MarketCatalogTests
{
    const string Main = "common/strategic_resources/00_strategic_resources.txt";
    const string Other = "common/strategic_resources/01_other.txt";
    const string ModFile = "common/strategic_resources/zz_moda_resources.txt";

    const string BaseMain = """
        # energy = { market_amount = 1 } in a comment is ignored
        energy = {
        	tradable = yes
        	category = basic
        	market_amount = 100
        	market_price = 100
        	max = 25000
        }
        influence = {
        	category = other
        	max = 1000
        	fixed_max_amount = yes
        }
        nanites = {
        	tradable = no
        	category = rare
        }
        """;

    // Mod A's copy of the base file: replaces it whole, so nanites is gone.
    const string ModMain = """
        energy = {
        	tradable = yes
        	category = basic
        	market_amount = 200
        	market_price = 100
        	max = 25000
        }
        influence = {
        	category = other
        	max = 1000
        	fixed_max_amount = yes
        }
        consumer_goods = {
        	tradable = yes
        	category = "advanced"
        	market_amount = 50
        	market_price = 100
        	max = 15000
        	tradable_in_market = { NOT = { has_valid_civic = civic_x } }
        }
        ai_unobtainium = {
        	category = other
        	visibility_prerequisite = { always = no }
        }
        sr_zro = {
        	category = rare
        	visibility_prerequisite = { always = yes }
        	market_amount = 5
        	market_price = 100
        }
        """;

    static MarketFixture Fixture() => new MarketFixture()
        .Base(Main, BaseMain)
        .Base(Other, "alloys = { tradable = yes category = advanced market_amount = 25 market_price = 100 max = 15000 }")
        .Mod("Mod A", Main, ModMain)
        .Mod("Mod A", ModFile, """
            @soup = 7
            nuclei_soup = {
            	tradable = yes
            	category = strategic
            	market_price = @soup
            }
            alloys = { tradable = yes category = advanced market_amount = 30 market_price = 100 max = 15000 }
            """);

    [Fact]
    public void A_later_source_with_the_same_file_name_replaces_the_whole_file()
    {
        using var f = Fixture();

        var c = f.Catalog();

        Assert.Equal(["energy", "influence", "consumer_goods", "ai_unobtainium", "sr_zro", "nuclei_soup", "alloys"], c.Resources.Select(r => r.Key));
        Assert.Null(c.Find("nanites"));
        Assert.Equal(200, c.Find("energy")!.MarketAmount);
        Assert.Equal("Mod A", c.Find("energy")!.SourceName);
        Assert.Equal(Main, c.Find("energy")!.FileName);
        Assert.Equal([Main, Other, ModFile], c.Files.Select(x => x.Path));
        Assert.Equal(["Mod A", "Base game", "Mod A"], c.Files.Select(x => x.SourceName));
        Assert.Empty(f.Warnings);
    }

    [Fact]
    public void Reads_market_storage_and_visibility_fields()
    {
        using var f = Fixture();

        var c = f.Catalog();

        var energy = c.Find("ENERGY")!;
        Assert.True(energy.OnMarket);
        Assert.True(energy.Tradable);
        Assert.Equal("basic", energy.Category);
        Assert.Equal((100, 25000), (energy.MarketPrice, energy.Max));
        Assert.False(energy.FixedMax);
        Assert.False(energy.Hidden);
        Assert.False(energy.Gated);

        var influence = c.Find("influence")!;
        Assert.False(influence.OnMarket);
        Assert.False(influence.Tradable);
        Assert.Equal((-1, -1), (influence.MarketAmount, influence.MarketPrice));
        Assert.Equal(1000, influence.Max);
        Assert.True(influence.FixedMax);

        var goods = c.Find("consumer_goods")!;
        Assert.Equal("advanced", goods.Category);
        Assert.True(goods.Gated);
        Assert.True(goods.OnMarket);

        Assert.True(c.Find("ai_unobtainium")!.Hidden);
        Assert.False(c.Find("sr_zro")!.Hidden);
        Assert.True(c.Find("sr_zro")!.OnMarket);
        Assert.Null(c.Find("sr_zro")!.Max);
    }

    [Fact]
    public void A_market_price_without_a_bundle_is_off_market_and_variables_resolve()
    {
        using var f = Fixture();

        var soup = f.Catalog().Find("nuclei_soup")!;

        Assert.Equal(7, soup.MarketPrice);
        Assert.Equal(-1, soup.MarketAmount);
        Assert.False(soup.OnMarket);
        Assert.Null(soup.Max);
        Assert.Equal("strategic", soup.Category);
    }

    [Fact]
    public void Global_scripted_variables_resolve()
    {
        using var f = new MarketFixture()
            .Base("common/scripted_variables/00_vars.txt", "@goods_bundle = 40")
            .Base(Main, "goods = { category = advanced market_amount = @goods_bundle market_price = 90 max = @missing }");

        var goods = f.Catalog().Find("goods")!;

        Assert.Equal(40, goods.MarketAmount);
        Assert.True(goods.OnMarket);
        Assert.Null(goods.Max);
    }

    [Fact]
    public void A_key_in_several_files_keeps_the_last_definition_and_lists_every_file()
    {
        using var f = Fixture();

        var alloys = f.Catalog().Find("alloys")!;

        Assert.Equal(30, alloys.MarketAmount);
        Assert.Equal("Mod A", alloys.SourceName);
        Assert.Equal(ModFile, alloys.FileName);
        Assert.Equal([Other, ModFile], alloys.Files);
    }

    [Fact]
    public void Names_come_from_localisation_with_the_key_as_fallback()
    {
        using var f = Fixture();
        var loc = new Localisation();
        loc.AddText("""
            l_english:
             energy:0 "§YEnergy Credits§!"
            """);

        var c = f.Catalog(loc);

        Assert.Equal("Energy Credits", c.Find("energy")!.Name);
        Assert.Equal("nuclei_soup", c.Find("nuclei_soup")!.Name);
        Assert.Equal("influence", f.Catalog().Find("influence")!.Name);
    }

    [Fact]
    public void Winning_files_keep_their_original_bytes()
    {
        using var f = Fixture();

        var c = f.Catalog();

        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(ModMain), c.Files[0].Bytes);
    }

    [Fact]
    public void Defines_are_read_last_wins_including_the_ai_lists()
    {
        using var f = new MarketFixture()
            .Base("common/defines/00_defines.txt", """
                NEconomy = {
                	MARKET_TRADE_FEE_BASE				= 0.3	# Base percentage
                	MARKET_TRADE_FEE_MIN				= 0.05
                	MARKET_MIN_FLUCTUATION_FROM_BASE_PRICE	= -80
                	MARKET_MAX_FLUCTUATION_FROM_BASE_PRICE	= 400
                	MARKET_MAX_BASE_AMOUNTS				= 200
                }
                NAI = {
                	AI_ALLOWED_TO_BUY = { "energy" "minerals" }
                	AI_ALLOWED_TO_SELL = { "energy" "minerals" "food" }
                }
                """)
            .Mod("Mod A", "common/defines/zz_moda_defines.txt", """
                NEconomy = { MARKET_MAX_BASE_AMOUNTS = 500 }
                NAI = { AI_ALLOWED_TO_SELL = { "energy" "alloys" } }
                """);

        var c = f.Catalog();

        Assert.Equal(500, c.MaxBaseAmounts);
        Assert.Equal(0.3, c.TradeFeeBase);
        Assert.Equal(0.05, c.TradeFeeMin);
        Assert.Equal(-80, c.MinFluctuation);
        Assert.Equal(400, c.MaxFluctuation);
        Assert.Equal(["energy", "minerals"], c.AiAllowedToBuy);
        Assert.Equal(["energy", "alloys"], c.AiAllowedToSell);
        Assert.True(c.AiTrades("energy"));
        Assert.False(c.AiTrades("minerals"));
        Assert.True(c.AiBuys("MINERALS"));
        Assert.False(c.AiSells("minerals"));
    }

    [Fact]
    public void Defines_files_load_in_file_name_order_and_a_same_named_file_replaces_the_earlier_one()
    {
        // As the game does (and the Conflicts page reports): files at the same path replace earlier ones, then every
        // remaining file loads in file-name order, whichever mod it comes from.
        using var f = new MarketFixture()
            .Base("common/defines/00_defines.txt", "NEconomy = { MARKET_TRADE_FEE_MIN = 0.05 MARKET_TRADE_FEE_BASE = 0.3 }")
            .Base("common/defines/01_more.txt", "NEconomy = { MARKET_MAX_BASE_AMOUNTS = 200 }")
            .Mod("Mod A", "common/defines/00_aaa.txt", "NEconomy = { MARKET_TRADE_FEE_MIN = 0.2 }")
            .Mod("Mod B", "common/defines/01_more.txt", "NEconomy = { MARKET_MIN_FLUCTUATION_FROM_BASE_PRICE = -50 }");

        var c = f.Catalog();

        Assert.Equal(0.05, c.TradeFeeMin);
        Assert.Equal(0.3, c.TradeFeeBase);
        Assert.Null(c.MaxBaseAmounts);
        Assert.Equal(-50, c.MinFluctuation);
        Assert.Null(c.MaxFluctuation);
        Assert.Empty(c.AiAllowedToBuy);
    }

    [Fact]
    public void No_strategic_resources_gives_an_empty_catalog()
    {
        using var f = new MarketFixture();

        var c = f.Catalog();

        Assert.Empty(c.Resources);
        Assert.Empty(c.Files);
        Assert.Null(c.MaxBaseAmounts);
    }

    [Theory]
    [InlineData("basic", 100)]
    [InlineData("Advanced", 25)]
    [InlineData("strategic", 10)]
    [InlineData("rare", 5)]
    [InlineData("other", 10)]
    [InlineData(null, 10)]
    public void Suggested_bundle_follows_the_category(string? category, int bundle) =>
        Assert.Equal(bundle, MarketCatalog.SuggestedBundle(category));

    [Fact]
    public void Icons_load_from_the_resource_sprite_else_the_resource_icon_file()
    {
        using var f = new MarketFixture()
            .Base(Main, "energy = { category = basic } minerals = { category = basic } food = { category = basic }")
            .Base("interface/resources.gfx", """
                spriteTypes = {
                	spriteType = { name = "GFX_resource_energy" texturefile = "gfx/interface/icons/res/energy_big.dds" }
                }
                """)
            .Bytes("Base game", "gfx/interface/icons/res/energy_big.dds", DdsBuilder.Bgra32(1, 1, (255, 0, 0, 255)))
            .Bytes("Base game", "gfx/interface/icons/resources/minerals.dds", DdsBuilder.Bgra32(1, 1, (0, 255, 0, 255)));
        var catalog = f.Catalog();
        var sprites = SpriteIndex.Build(f.Sources, new List<string>());

        var withIcons = catalog.LoadIcons(f.Sources, sprites, new IconCache(Path.Combine(f.TempPath, "cache")));

        Assert.StartsWith("data:image/png;base64,", withIcons.Find("energy")!.IconUri);
        Assert.StartsWith("data:image/png;base64,", withIcons.Find("minerals")!.IconUri);
        Assert.Null(withIcons.Find("food")!.IconUri);
        Assert.NotEqual(withIcons.Find("energy")!.IconUri, withIcons.Find("minerals")!.IconUri);
        Assert.Null(catalog.Find("energy")!.IconUri);
        Assert.Equal(catalog.Files, withIcons.Files);
    }
}

using System.Globalization;
using System.Text;
using FazStellarisModmanager.Core.Addons;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class AddonBuilderTests
{
    const string ResFile = "common/strategic_resources/00_strategic_resources.txt";
    const string OtherFile = "common/strategic_resources/01_other.txt";
    const string DefinesFile = "common/defines/zzzzz_faz_addons.txt";

    static readonly string Lf = MarketFixture.Lf;
    static readonly string Crlf = MarketFixture.Crlf;

    static readonly string Original = MarketFixture.LfText("""
        ### Basic Resources

        energy = {
        	tradable = yes
        	category = basic
        	market_amount = 100
        	market_price = 100 # base price
        	max = 25000
        	ai_weight = { weight = 1.5 }
        }

        minerals = {
        	tradable = yes
        	category = basic
        	market_amount = 100
        	market_price = 100
        	max = 15000
        	dynamic_capacity = { base = 0 modifier = { max = 5 } }
        }

        influence = {
        	category = other
        	max = 1000
        	fixed_max_amount = yes
        }

        nuclei_soup = {
        	category = strategic
        	tradable = no
        	deficit_modifier = "soup_deficit" # } is not a brace here
        }

        rare_thing = {
        	category = rare
        	market_amount = 8
        }
        """) + Lf;

    const string Defines = """
        NEconomy = {
        	MARKET_TRADE_FEE_BASE = 0.3
        	MARKET_TRADE_FEE_MIN = 0.05
        	MARKET_MIN_FLUCTUATION_FROM_BASE_PRICE = -80
        	MARKET_MAX_FLUCTUATION_FROM_BASE_PRICE = 400
        	MARKET_MAX_BASE_AMOUNTS = 200
        }
        NAI = {
        	AI_ALLOWED_TO_BUY = { "energy" "minerals" "food" }
        	AI_ALLOWED_TO_SELL = { "energy" "minerals" }
        }
        """;

    static MarketFixture Game(byte[]? main = null) => new MarketFixture()
        .Bytes("Base game", ResFile, main ?? Encoding.UTF8.GetBytes(Original))
        .Base(OtherFile, "alloys = { tradable = yes category = advanced market_amount = 25 market_price = 100 max = 15000 }" + Lf)
        .Base("common/defines/00_defines.txt", Defines);

    static AddonSettings On(params (string Key, ResourceOverride Override)[] resources) =>
        new(Enabled: true, new MarketplaceSettings(Resources: resources.ToDictionary(r => r.Key, r => r.Override)));

    static AddonOutput Build(MarketFixture game, AddonSettings settings, string? version = "Cepheus v4.5.2") =>
        AddonBuilder.Build(game.Catalog(), settings, version);

    static string Text(AddonOutput output, string path) => Encoding.UTF8.GetString(output.Get(path)!);

    static string Edited(params (string From, string To)[] edits) =>
        edits.Aggregate(Original, (text, e) =>
        {
            Assert.Equal(1, CountOf(text, e.From));
            return text.Replace(e.From, e.To);
        });

    static int CountOf(string text, string part) => (text.Length - text.Replace(part, "").Length) / part.Length;

    static string Lines(params string[] lines) => string.Join(Lf, lines);

    [Fact]
    public void Replaces_an_existing_value_and_leaves_the_rest_of_the_file_byte_identical()
    {
        using var game = Game();

        var output = Build(game, On(("energy", new ResourceOverride(MarketPrice: 150))));

        Assert.True(output.HasChanges);
        Assert.Equal(["README.txt", ResFile, "descriptor.mod"], output.Files.Select(f => f.Path));
        Assert.Equal(Edited(("market_price = 100 # base price", "market_price = 150 # base price")), Text(output, ResFile));
    }

    [Fact]
    public void Putting_a_resource_on_the_market_inserts_the_category_bundle_and_the_default_price()
    {
        using var game = Game();

        var output = Build(game, On(("nuclei_soup", new ResourceOverride(OnMarket: true))));

        var tab = ((char)9).ToString();
        Assert.Equal(Edited(("nuclei_soup = {", Lines("nuclei_soup = {", tab + "market_amount = 10", tab + "market_price = 100"))),
            Text(output, ResFile));
    }

    [Fact]
    public void Putting_a_resource_on_the_market_keeps_an_existing_bundle_and_uses_overrides()
    {
        using var game = Game();

        var kept = Build(game, On(("rare_thing", new ResourceOverride(OnMarket: true))));
        var overridden = Build(game, On(("nuclei_soup", new ResourceOverride(OnMarket: true, MarketAmount: 40, MarketPrice: 250))));

        var tab = ((char)9).ToString();
        Assert.Equal(Edited(("rare_thing = {", Lines("rare_thing = {", tab + "market_price = 100"))), Text(kept, ResFile));
        Assert.Equal(Edited(("nuclei_soup = {", Lines("nuclei_soup = {", tab + "market_amount = 40", tab + "market_price = 250"))),
            Text(overridden, ResFile));
    }

    [Fact]
    public void Taking_a_resource_off_the_market_sets_the_bundle_to_minus_one()
    {
        using var game = Game();

        var output = Build(game, On(("minerals", new ResourceOverride(OnMarket: false, MarketPrice: 300))));

        var minerals = Original.IndexOf("minerals = {", StringComparison.Ordinal);
        var expected = Original[..minerals] + Original[minerals..].ReplaceFirst("market_amount = 100", "market_amount = -1");
        Assert.Equal(expected, Text(output, ResFile));
    }

    [Fact]
    public void Sets_tradable_and_base_storage()
    {
        using var game = Game();

        var output = Build(game, On(
            ("energy", new ResourceOverride(Tradable: false)),
            ("minerals", new ResourceOverride(BaseStorage: 30000))));

        var energy = Original.IndexOf("energy = {", StringComparison.Ordinal);
        var minerals = Original.IndexOf("minerals = {", StringComparison.Ordinal);
        var expected = Original[..energy] + Original[energy..minerals].ReplaceFirst("tradable = yes", "tradable = no")
            + Original[minerals..].ReplaceFirst("max = 15000", "max = 30000");
        Assert.Equal(expected, Text(output, ResFile));
        Assert.Contains("modifier = { max = 5 }", Text(output, ResFile));
    }

    [Fact]
    public void Base_storage_is_ignored_without_max_or_with_a_fixed_max()
    {
        using var game = Game();

        var output = Build(game, On(
            ("influence", new ResourceOverride(BaseStorage: 5000)),
            ("nuclei_soup", new ResourceOverride(BaseStorage: 100))));

        Assert.False(output.HasChanges);
        Assert.Equal(["README.txt", "descriptor.mod"], output.Files.Select(f => f.Path));
    }

    [Fact]
    public void Files_without_a_changed_resource_are_not_output()
    {
        using var game = Game();

        var energyOnly = Build(game, On(("energy", new ResourceOverride(MarketPrice: 150))));
        var alloysOnly = Build(game, On(("alloys", new ResourceOverride(MarketAmount: 50))));

        Assert.Null(energyOnly.Get(OtherFile));
        Assert.Null(alloysOnly.Get(ResFile));
        Assert.Equal("alloys = { tradable = yes category = advanced market_amount = 50 market_price = 100 max = 15000 }" + Lf, Text(alloysOnly, OtherFile));
    }

    [Fact]
    public void A_resource_in_several_winning_files_is_edited_in_each()
    {
        using var game = Game();
        game.Mod("Mod A", "common/strategic_resources/zz_more.txt", "alloys = { category = advanced tradable = yes }");

        var output = Build(game, On(("alloys", new ResourceOverride(Tradable: false))));

        Assert.Contains("tradable = no", Text(output, OtherFile));
        Assert.Equal("alloys = { category = advanced tradable = no }", Text(output, "common/strategic_resources/zz_more.txt"));
    }

    [Fact]
    public void Keeps_crlf_the_bom_and_bytes_that_are_not_utf8()
    {
        var tab = ((char)9).ToString();
        string Crlfs(params string[] lines) => string.Join(Crlf, lines) + Crlf;
        var before = Crlfs("# Café resources", "energy = {", tab + "market_amount = 100", tab + "market_price = 100", "}", "soup = {", tab + "category = rare", "}");
        var after = Crlfs("# Café resources", "energy = {", tab + "market_amount = 100", tab + "market_price = 120", "}",
            "soup = {", tab + "market_amount = 5", tab + "market_price = 100", tab + "category = rare", "}");
        byte[] Bom(string text) => [0xEF, 0xBB, 0xBF, .. Encoding.Latin1.GetBytes(text)];
        using var game = Game(Bom(before));

        var output = Build(game, On(("energy", new ResourceOverride(MarketPrice: 120)), ("soup", new ResourceOverride(OnMarket: true))));

        Assert.Equal(Bom(after), output.Get(ResFile));
    }

    [Fact]
    public void Keeps_utf8_text_without_a_bom()
    {
        var before = Encoding.UTF8.GetBytes("# Café ✓" + Lf + "energy = { market_amount = 100 market_price = 100 } # naïve" + Lf);
        using var game = Game(before);

        var output = Build(game, On(("energy", new ResourceOverride(MarketAmount: 60))));

        Assert.Equal(Encoding.UTF8.GetBytes("# Café ✓" + Lf + "energy = { market_amount = 60 market_price = 100 } # naïve" + Lf), output.Get(ResFile));
    }

    [Fact]
    public void The_defines_file_has_only_changed_keys_in_invariant_culture()
    {
        using var game = Game();
        var settings = new AddonSettings(Enabled: true, new MarketplaceSettings(MaxBaseAmounts: 1000, TradeFeeBase: 0.3, TradeFeeMin: 0.123456, MaxFluctuation: 250));
        var culture = CultureInfo.CurrentCulture;
        AddonOutput output;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            output = Build(game, settings);
        }
        finally { CultureInfo.CurrentCulture = culture; }

        Assert.Equal(["README.txt", DefinesFile, "descriptor.mod"], output.Files.Select(f => f.Path));
        Assert.Equal(MarketFixture.LfText("""
            NEconomy = {
            	MARKET_MAX_BASE_AMOUNTS = 1000
            	MARKET_TRADE_FEE_MIN = 0.1235
            	MARKET_MAX_FLUCTUATION_FROM_BASE_PRICE = 250
            }
            """) + Lf, Text(output, DefinesFile));
    }

    [Fact]
    public void Ai_lists_keep_their_order_drop_removals_and_append_additions_in_ordinal_order()
    {
        using var game = Game();

        var output = Build(game, On(
            ("nuclei_soup", new ResourceOverride(AiTrades: true)),
            ("minerals", new ResourceOverride(AiTrades: false)),
            ("energy", new ResourceOverride(AiTrades: true)),
            ("alloys", new ResourceOverride(AiTrades: true))));

        Assert.Equal(MarketFixture.LfText("""
            NAI = {
            	AI_ALLOWED_TO_BUY = { "energy" "food" "alloys" "nuclei_soup" }
            	AI_ALLOWED_TO_SELL = { "energy" "alloys" "nuclei_soup" }
            }
            """) + Lf, Text(output, DefinesFile));
        Assert.Null(output.Get(ResFile));
    }

    [Fact]
    public void Off_disabled_or_unchanged_settings_give_only_the_descriptor_and_an_empty_readme()
    {
        using var game = Game();
        var change = new ResourceOverride(MarketPrice: 150);
        AddonSettings[] cases =
        [
            new(),
            On(("energy", change)) with { Enabled = false },
            new(Enabled: true, new MarketplaceSettings(Enabled: false, MaxBaseAmounts: 1000)),
            new(Enabled: true),
            new(Enabled: true, new MarketplaceSettings(MaxBaseAmounts: 200, TradeFeeBase: 0.3)),
            On(("energy", new ResourceOverride(MarketPrice: 100, Tradable: true)), ("mystery", new ResourceOverride(Tradable: true))),
        ];

        foreach (var settings in cases)
        {
            var output = Build(game, settings);

            Assert.False(output.HasChanges);
            Assert.Equal(["README.txt", "descriptor.mod"], output.Files.Select(f => f.Path));
            Assert.Empty(output.Get("README.txt")!);
        }
    }

    [Fact]
    public void The_descriptor_names_the_mod_and_supports_the_games_minor_version()
    {
        using var game = Game();

        var versioned = Build(game, new AddonSettings(), "Cepheus v4.5.2");
        var unknown = Build(game, new AddonSettings(), "unknown");
        var none = Build(game, new AddonSettings(), null);

        Assert.Equal(MarketFixture.LfText("""
            version="1"
            tags={
            	"Utilities"
            }
            name="Faz Addons"
            supported_version="v4.5.*"
            """) + Lf, Text(versioned, "descriptor.mod"));
        Assert.Contains("supported_version=" + (char)34 + "*" + (char)34, Text(unknown, "descriptor.mod"));
        Assert.Equal(Text(unknown, "descriptor.mod"), Text(none, "descriptor.mod"));
    }

    [Fact]
    public void A_build_with_changes_has_a_readme()
    {
        using var game = Game();

        var output = Build(game, On(("energy", new ResourceOverride(MarketPrice: 150))));

        Assert.Contains("Faz Addons", Text(output, "README.txt"));
    }

    [Fact]
    public void Builds_are_deterministic_whatever_the_dictionary_order()
    {
        using var game = Game();
        (string, ResourceOverride)[] overrides =
        [
            ("nuclei_soup", new ResourceOverride(OnMarket: true, AiTrades: true)),
            ("minerals", new ResourceOverride(BaseStorage: 30000, AiTrades: false)),
            ("alloys", new ResourceOverride(Tradable: false, AiTrades: true)),
            ("energy", new ResourceOverride(MarketAmount: 50)),
        ];
        var forward = On(overrides) with { Marketplace = On(overrides).Marketplace! with { MaxBaseAmounts = 1000, TradeFeeMin = 0.1 } };
        var backwards = Enumerable.Reverse(overrides).ToArray();
        var reversed = On(backwards) with { Marketplace = On(backwards).Marketplace! with { TradeFeeMin = 0.1, MaxBaseAmounts = 1000 } };

        var a = Build(game, forward);
        var b = Build(game, forward);
        var c = AddonBuilder.Build(game.Catalog(), reversed, "Cepheus v4.5.2");

        Assert.Equal(5, a.Files.Count);
        foreach (var other in new[] { b, c })
        {
            Assert.Equal(a.Files.Select(f => f.Path), other.Files.Select(f => f.Path));
            for (var i = 0; i < a.Files.Count; i++) Assert.Equal(a.Files[i].Bytes, other.Files[i].Bytes);
        }
    }
}

static class StringTestExtensions
{
    public static string ReplaceFirst(this string text, string from, string to)
    {
        var i = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(i >= 0, $"'{from}' not found");
        return text[..i] + to + text[(i + from.Length)..];
    }
}

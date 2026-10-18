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
        AddonBuilder.Build(game.Catalog(), Techs, settings, version);

    // The game's techs as Apply reads them (file names only): a tech to require, two categories, ethics (authoritarian with its fanatic
    // variant, materialist without one, gestalt) and two tech icons.
    static readonly TechAddonCatalog Techs = TechGame();

    static TechAddonCatalog TechGame()
    {
        var dds = DdsBuilder.Bgra32(1, 1, (1, 2, 3, 255));
        using var f = new MarketFixture()
            .Base("common/technology/00_soc_tech.txt", "tech_galactic_administration = { area = society tier = 3 category = { statecraft } }" + Lf)
            .Base("common/technology/category/00_category.txt", "statecraft = { }" + Lf + "particles = { }" + Lf)
            .Base("common/ethics/00_ethics.txt", "ethic_authoritarian = { }" + Lf + "ethic_fanatic_authoritarian = { }" + Lf
                + "ethic_materialist = { }" + Lf + "ethic_gestalt_consciousness = { }" + Lf)
            .Bytes("Base game", "gfx/interface/icons/technologies/repeatable_diplomacy_tech.dds", dds)
            .Bytes("Base game", "gfx/interface/icons/technologies/tech_lasers_1.dds", dds);
        return f.TechCatalog();
    }

    [Fact]
    public void The_build_is_the_same_with_or_without_names_and_this_pcs_modifiers_log()
    {
        // Session players build from the host's settings; a PC without modifiers.log (or other names) must still build the same bytes.
        var dds = DdsBuilder.Bgra32(1, 1, (1, 2, 3, 255));
        using var f = new MarketFixture()
            .Base("common/technology/00_soc_tech.txt", "tech_galactic_administration = { area = society tier = 3 category = { statecraft } }" + Lf)
            .Base("common/technology/category/00_category.txt", "statecraft = { }" + Lf)
            .Base("common/ethics/00_ethics.txt", "ethic_authoritarian = { }" + Lf + "ethic_fanatic_authoritarian = { }" + Lf)
            .Base("localisation/english/names_l_english.yml", "l_english:" + Lf + " mod_divided_patrongage_max_subjects:0 " + (char)34 + "Exempt" + (char)34 + Lf)
            .Bytes("Base game", "gfx/interface/icons/technologies/repeatable_diplomacy_tech.dds", dds);
        var bare = f.TechCatalog();
        var loc = FazStellarisModmanager.Core.Technology.Localisation.Load(f.Sources, f.Warnings);
        var full = f.TechCatalog(loc, "Printing Modifier Definitions:" + Lf + "- divided_patrongage_max_subjects, Category: Federations" + Lf);
        var unknown = Patronage with { Key = "tech_faz_unknown_modifier", Name = "Unknown", Modifiers = [new TechModifier("made_up_modifier", 0)] };
        var broken = Patronage with { Key = "tech_faz_broken", Name = "Broken", Icon = "nope" };
        var settings = WithTechs(Patronage, unknown, broken);

        var a = AddonBuilder.Build(MarketCatalog.Empty, bare, settings, "Cepheus v4.5.2");
        var b = AddonBuilder.Build(MarketCatalog.Empty, full, settings, "Cepheus v4.5.2");

        Assert.NotEqual(bare.ModifiersFromLog, full.ModifiersFromLog);
        Assert.Equal(a.Files.Select(x => x.Path), b.Files.Select(x => x.Path));
        for (var i = 0; i < a.Files.Count; i++) Assert.Equal(a.Files[i].Bytes, b.Files[i].Bytes);
        Assert.Contains("tech_faz_unknown_modifier", Encoding.UTF8.GetString(a.Get(AddonBuilder.TechPath)!)); // warnings never drop a tech
        Assert.DoesNotContain("tech_faz_broken", Encoding.UTF8.GetString(a.Get(AddonBuilder.TechPath)!));
    }

    // The mockup's example: a repeatable, Authoritarian-only tech giving +1 subject exempt from Divided Patronage per level.
    static readonly CustomTech Patronage = new("tech_faz_imperial_patronage", "Imperial Patronage Doctrine",
        "Our administrators learn to keep more subjects content under one banner.", "repeatable_diplomacy_tech",
        Area: "society", Category: "statecraft", Tier: 5, Repeatable: true, Cost: 50000, CostPerLevel: 5000, Weight: 15,
        Ethics: ["ethic_authoritarian"], Prerequisites: ["tech_galactic_administration"],
        Modifiers: [new TechModifier("divided_patrongage_max_subjects", 1)]);

    static readonly string PatronageText = MarketFixture.LfText("""
        tech_faz_imperial_patronage = {
        	area = society
        	tier = 5
        	category = { statecraft }
        	cost = 50000
        	cost_per_level = 5000
        	levels = -1
        	icon = repeatable_diplomacy_tech
        	weight = 15

        	potential = {
        		OR = {
        			has_ethic = ethic_authoritarian
        			has_ethic = ethic_fanatic_authoritarian
        		}
        	}
        	prerequisites = { "tech_galactic_administration" }

        	weight_modifier = { factor = 0.5 }
        	ai_weight = { factor = 1 }
        	weight_groups = { repeatable }
        	mod_weight_if_group_picked = { repeatable = 0.01 }

        	modifier = {
        		divided_patrongage_max_subjects = 1
        	}
        }
        """) + Lf;

    // A one-off rare tech without ethics or prerequisites; its cost per level doesn't count.
    static readonly CustomTech Lasers = new("tech_faz_ancient_lasers", "Ancient Lasers", "Found in a derelict.", "tech_lasers_1",
        Area: "physics", Category: "particles", Tier: 2, Repeatable: false, Rare: true, Cost: 3000, CostPerLevel: 700, Weight: 50,
        Modifiers: [new TechModifier("ship_weapon_damage", 0.05), new TechModifier("ship_upkeep_mult", -0.25)]);

    static readonly string LasersText = MarketFixture.LfText("""
        tech_faz_ancient_lasers = {
        	area = physics
        	tier = 2
        	category = { particles }
        	cost = 3000
        	icon = tech_lasers_1
        	is_rare = yes
        	weight = 50

        	ai_weight = { factor = 1 }

        	modifier = {
        		ship_weapon_damage = 0.05
        		ship_upkeep_mult = -0.25
        	}
        }
        """) + Lf;

    static AddonSettings WithTechs(params CustomTech[] techs) => new(Enabled: true, Technologies: new TechnologiesSettings(Techs: techs));

    static string Utf8WithBom(AddonOutput output, string path)
    {
        var bytes = output.Get(path)!;
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        return Encoding.UTF8.GetString(bytes[3..]);
    }

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
    public void By_default_resources_reach_the_galactic_market_once_a_community_member_has_some_in_stock()
    {
        using var game = Game();

        var output = Build(game, On(("rare_thing", new ResourceOverride(OnMarket: true)), ("nuclei_soup", new ResourceOverride(OnMarket: true))));

        Assert.Equal(MarketFixture.LfText("""
            namespace = faz_addons

            # Generated by Faz Stellaris Mod Manager. The game only puts its own resources on the Galactic Market (when it is founded,
            # then monthly), so this does the same for the resources Faz Addons put on the market: like the game, once a Galactic
            # Community member has some in stock.
            event = {
            	id = faz_addons.1
            	hide_window = yes
            	is_triggered_only = yes

            	trigger = {
            		has_global_flag = galactic_market_founded
            		OR = {
            			NOT = { is_on_market = nuclei_soup }
            			NOT = { is_on_market = rare_thing }
            		}
            	}

            	immediate = {
            		if = {
            			limit = {
            				NOT = { is_on_market = nuclei_soup }
            				any_playable_country = {
            					is_galactic_community_member = yes
            					resource_stockpile_compare = { resource = nuclei_soup value >= 1 }
            				}
            			}
            			enable_on_market = nuclei_soup
            		}
            		if = {
            			limit = {
            				NOT = { is_on_market = rare_thing }
            				any_playable_country = {
            					is_galactic_community_member = yes
            					resource_stockpile_compare = { resource = rare_thing value >= 1 }
            				}
            			}
            			enable_on_market = rare_thing
            		}
            	}
            }
            """) + Lf, Text(output, AddonBuilder.MarketEventsPath));
        Assert.Equal(MarketFixture.LfText("""
            on_monthly_pulse = {
            	events = {
            		faz_addons.1
            	}
            }
            """) + Lf, Text(output, AddonBuilder.MarketOnActionsPath));
    }

    [Fact]
    public void Without_the_stock_rule_resources_reach_the_galactic_market_right_away()
    {
        using var game = Game();
        var settings = On(("rare_thing", new ResourceOverride(OnMarket: true)), ("nuclei_soup", new ResourceOverride(OnMarket: true)));
        settings = settings with { Marketplace = settings.Marketplace! with { GalacticMarketNeedsStock = false } };

        var output = Build(game, settings);

        Assert.Equal(MarketFixture.LfText("""
            namespace = faz_addons

            # Generated by Faz Stellaris Mod Manager. The game only puts its own resources on the Galactic Market (when it is founded,
            # then monthly), so this does the same for the resources Faz Addons put on the market: right away.
            event = {
            	id = faz_addons.1
            	hide_window = yes
            	is_triggered_only = yes

            	trigger = {
            		has_global_flag = galactic_market_founded
            		OR = {
            			NOT = { is_on_market = nuclei_soup }
            			NOT = { is_on_market = rare_thing }
            		}
            	}

            	immediate = {
            		if = { limit = { NOT = { is_on_market = nuclei_soup } } enable_on_market = nuclei_soup }
            		if = { limit = { NOT = { is_on_market = rare_thing } } enable_on_market = rare_thing }
            	}
            }
            """) + Lf, Text(output, AddonBuilder.MarketEventsPath));
    }

    [Fact]
    public void No_galactic_market_event_without_resources_put_on_the_market()
    {
        using var game = Game();

        var output = Build(game, On(("energy", new ResourceOverride(MarketPrice: 150)), ("alloys", new ResourceOverride(OnMarket: false))));

        Assert.Null(output.Get(AddonBuilder.MarketEventsPath));
        Assert.Null(output.Get(AddonBuilder.MarketOnActionsPath));
    }

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
            WithTechs(Patronage) with { Enabled = false },
            WithTechs(Patronage) with { Technologies = new TechnologiesSettings(Enabled: false, Techs: [Patronage]) },
            WithTechs(),
            new(Enabled: true, Technologies: new TechnologiesSettings()),
            WithTechs(Patronage with { Icon = "missing_icon" }),
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
        var c = AddonBuilder.Build(game.Catalog(), Techs, reversed, "Cepheus v4.5.2");

        Assert.Equal(7, a.Files.Count); // README, descriptor, defines, on_actions, events and the two resource files
        foreach (var other in new[] { b, c })
        {
            Assert.Equal(a.Files.Select(f => f.Path), other.Files.Select(f => f.Path));
            for (var i = 0; i < a.Files.Count; i++) Assert.Equal(a.Files[i].Bytes, other.Files[i].Bytes);
        }
    }

    [Fact]
    public void A_repeatable_ethic_locked_tech_is_written_with_the_fanatic_variant()
    {
        using var game = Game();

        var output = Build(game, WithTechs(Patronage));

        Assert.True(output.HasChanges);
        Assert.Equal(["README.txt", AddonBuilder.TechPath, "descriptor.mod", AddonBuilder.TechLocalisationPath], output.Files.Select(f => f.Path));
        Assert.Equal(PatronageText, Text(output, AddonBuilder.TechPath));
        Assert.False(output.Get(AddonBuilder.TechPath)!.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Contains("Faz Addons", Text(output, "README.txt"));
    }

    [Fact]
    public void A_one_off_rare_tech_has_no_repeatable_lines_and_no_potential()
    {
        using var game = Game();

        var output = Build(game, WithTechs(Lasers));

        Assert.Equal(LasersText, Text(output, AddonBuilder.TechPath));
    }

    [Fact]
    public void Techs_are_written_in_settings_order_separated_by_a_blank_line()
    {
        using var game = Game();

        var output = Build(game, WithTechs(Lasers, Patronage));

        Assert.Equal(LasersText + Lf + PatronageText, Text(output, AddonBuilder.TechPath));
        Assert.Equal(MarketFixture.LfText("""
            l_english:
             tech_faz_ancient_lasers:0 "Ancient Lasers"
             tech_faz_ancient_lasers_desc:0 "Found in a derelict."
             tech_faz_imperial_patronage:0 "Imperial Patronage Doctrine"
             tech_faz_imperial_patronage_desc:0 "Our administrators learn to keep more subjects content under one banner."
            """) + Lf, Utf8WithBom(output, AddonBuilder.TechLocalisationPath));
    }

    [Fact]
    public void Localisation_starts_with_a_bom_and_the_language_and_cleans_line_breaks_and_quotes()
    {
        using var game = Game();
        var q = ((char)34).ToString();
        var tech = Lasers with { Name = "The " + q + "Old" + q + " Lasers", Description = "First line" + Crlf + "second line" + Lf + "third" + (char)13 + "end" };

        var output = Build(game, WithTechs(tech));

        Assert.Equal("l_english:" + Lf
            + " tech_faz_ancient_lasers:0 " + q + "The 'Old' Lasers" + q + Lf
            + " tech_faz_ancient_lasers_desc:0 " + q + "First line second line third end" + q + Lf,
            Utf8WithBom(output, AddonBuilder.TechLocalisationPath));
    }

    [Fact]
    public void Ethics_without_a_fanatic_variant_are_listed_alone()
    {
        using var game = Game();

        var output = Build(game, WithTechs(Lasers with { Ethics = ["ethic_materialist", "ethic_gestalt_consciousness"] }));

        Assert.Contains(MarketFixture.LfText("""
            	weight = 50

            	potential = {
            		OR = {
            			has_ethic = ethic_materialist
            			has_ethic = ethic_gestalt_consciousness
            		}
            	}

            	ai_weight = { factor = 1 }
            """), Text(output, AddonBuilder.TechPath));
    }

    [Fact]
    public void Prerequisites_without_ethics_and_a_tech_without_effects()
    {
        using var game = Game();

        var output = Build(game, WithTechs(Lasers with { Prerequisites = ["tech_galactic_administration", "tech_faz_imperial_patronage"], Modifiers = null }, Patronage));

        Assert.StartsWith(MarketFixture.LfText("""
            tech_faz_ancient_lasers = {
            	area = physics
            	tier = 2
            	category = { particles }
            	cost = 3000
            	icon = tech_lasers_1
            	is_rare = yes
            	weight = 50

            	prerequisites = { "tech_galactic_administration" "tech_faz_imperial_patronage" }

            	ai_weight = { factor = 1 }
            }

            tech_faz_imperial_patronage = {
            """), Text(output, AddonBuilder.TechPath));
    }

    [Fact]
    public void Modifier_values_are_invariant_with_up_to_4_decimals_and_never_minus_zero()
    {
        using var game = Game();
        var tech = Lasers with { Modifiers = [new("a_mult", 1.23456), new("b_add", -0.00001), new("c_add", 1500), new("d_mult", -0.1)] };
        var culture = CultureInfo.CurrentCulture;
        AddonOutput output;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            output = Build(game, WithTechs(tech));
        }
        finally { CultureInfo.CurrentCulture = culture; }

        Assert.Contains(MarketFixture.LfText("""
            	modifier = {
            		a_mult = 1.2346
            		b_add = 0
            		c_add = 1500
            		d_mult = -0.1
            	}
            """), Text(output, AddonBuilder.TechPath));
    }

    [Fact]
    public void Techs_with_errors_are_left_out_and_the_rest_are_built()
    {
        using var game = Game();
        var broken = Lasers with { Key = "tech_faz_broken", Category = "nonsense" };
        // Requires the broken tech, so it can't be built either.
        var dependent = Lasers with { Key = "tech_faz_dependent", Prerequisites = ["tech_faz_broken"] };

        var output = Build(game, WithTechs(broken, Patronage, dependent));

        Assert.Equal(PatronageText, Text(output, AddonBuilder.TechPath));
        Assert.DoesNotContain("tech_faz_broken", Utf8WithBom(output, AddonBuilder.TechLocalisationPath));
        Assert.DoesNotContain("tech_faz_dependent", Utf8WithBom(output, AddonBuilder.TechLocalisationPath));
    }

    [Fact]
    public void Market_changes_and_techs_are_built_together()
    {
        using var game = Game();
        var settings = On(("energy", new ResourceOverride(MarketPrice: 150))) with { Technologies = new TechnologiesSettings(Techs: [Patronage]) };

        var output = Build(game, settings);

        Assert.True(output.HasChanges);
        Assert.Equal(["README.txt", ResFile, AddonBuilder.TechPath, "descriptor.mod", AddonBuilder.TechLocalisationPath], output.Files.Select(f => f.Path));
    }

    [Fact]
    public void Tech_builds_are_deterministic()
    {
        using var game = Game();
        var settings = WithTechs(Patronage, Lasers with { Ethics = ["ethic_authoritarian", "ethic_materialist"] });

        var a = Build(game, settings);
        var b = AddonBuilder.Build(game.Catalog(), TechGame(), settings with { Technologies = settings.Technologies! with { Techs = settings.Technologies.Techs!.ToList() } }, "Cepheus v4.5.2");

        Assert.Equal(a.Files.Select(f => f.Path), b.Files.Select(f => f.Path));
        for (var i = 0; i < a.Files.Count; i++) Assert.Equal(a.Files[i].Bytes, b.Files[i].Bytes);
    }

    [Fact]
    public void TechText_is_one_techs_part_of_the_files()
    {
        var (technology, localisation) = AddonBuilder.TechText(Patronage, Techs);

        Assert.Equal(PatronageText, technology);
        Assert.Equal("l_english:" + Lf
            + " tech_faz_imperial_patronage:0 " + (char)34 + "Imperial Patronage Doctrine" + (char)34 + Lf
            + " tech_faz_imperial_patronage_desc:0 " + (char)34 + "Our administrators learn to keep more subjects content under one banner." + (char)34 + Lf,
            localisation);
    }

    [Fact]
    public void TechText_writes_a_tech_even_when_it_has_errors()
    {
        var (technology, _) = AddonBuilder.TechText(Patronage with { Ethics = ["ethic_unknown"], Icon = "" }, Techs);

        Assert.Contains(MarketFixture.LfText("""
            		OR = {
            			has_ethic = ethic_unknown
            		}
            """), technology);
        Assert.Contains(Lf + (char)9 + "icon = " + Lf, technology);
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

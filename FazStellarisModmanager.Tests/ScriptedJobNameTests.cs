using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ScriptedJobNameTests
{
    const string PhysicistSwap = """
        defined_text = {
        	name = GetPhysicistSwapPluralWithIcon
        	text = {
        		trigger = {
        			OR = {
        				AND = { NOT = { is_scope_type = country } exists = owner owner = { is_gestalt = yes } }
        				AND = { is_scope_type = country is_gestalt = yes }
        			}
        		}
        		localization_key = job_calculator_physicist_swap_plural_with_icon
        	}
        	text = {
        		trigger = { is_gestalt = no }
        		localization_key = job_physicist_swap_plural_with_icon
        	}
        	default = job_physicist_swap_plural_with_icon
        }
        """;

    static Localisation Loc(string scriptedLoc = PhysicistSwap)
    {
        var loc = new Localisation();
        var nbsp = (char)0xA0;
        loc.AddText($"""
            l_english:
             mod_planet_physicists_produces_mult: "Resources Produced from $physicist_type_plural_with_icon$"
             physicist_type_plural_with_icon: "[GetPhysicistSwapPluralWithIcon]"
             job_physicist_swap_plural_with_icon: "[physicist.GetIcon]{nbsp} [physicist.GetNamePlural]"
             job_calculator_physicist_swap_plural_with_icon: "[calculator_physicist.GetIcon] [calculator_physicist.GetNamePlural]"
             job_physicist: "Physicist"
             job_physicist_plural: "Physicists"
             job_calculator_physicist_plural: "Calculators"
             civic_imperial_cult: "Imperial Cult"
             greeting: "Hello [Root.GetName] and [Unknown] from [fleet.GetName][fleet.GetIcon]"
             single: "[physicist.GetName]"
            """);
        var index = new ScriptedLoc();
        index.AddText(scriptedLoc);
        loc.Scripted = index;
        return loc;
    }

    [Fact]
    public void Scripted_functions_show_their_default_and_job_commands_become_job_names()
    {
        var loc = Loc();

        Assert.Equal("Resources Produced from Physicists", loc.Get("mod_planet_physicists_produces_mult"));
        Assert.Equal("Physicist", loc.Get("single"));
        Assert.Equal("Hello [Root.GetName] and [Unknown] from [fleet.GetName][fleet.GetIcon]", loc.Get("greeting"));
        Assert.Null(loc.FirstIconTag("greeting"));
        Assert.Equal("job_physicist", loc.FirstIconTag("mod_planet_physicists_produces_mult"));
    }

    [Fact]
    public void Without_scripted_loc_the_function_is_left_as_written()
    {
        var loc = Loc();
        loc.Scripted = ScriptedLoc.Empty;

        Assert.Equal("Resources Produced from [GetPhysicistSwapPluralWithIcon]", loc.Get("mod_planet_physicists_produces_mult"));
        Assert.Empty(loc.Variants("mod_planet_physicists_produces_mult"));
    }

    [Fact]
    public void Variants_list_each_branch_with_its_name_icon_and_condition()
    {
        var variants = Loc().Variants("mod_planet_physicists_produces_mult");

        Assert.Equal(2, variants.Count);
        Assert.Equal(("Resources Produced from Calculators", "job_calculator_physicist", "Gestalt", false),
            (variants[0].Text, variants[0].IconTag, variants[0].Condition, variants[0].IsDefault));
        Assert.Equal(("Resources Produced from Physicists", "job_physicist", "not Gestalt", true),
            (variants[1].Text, variants[1].IconTag, variants[1].Condition, variants[1].IsDefault));
        Assert.Contains("is_gestalt = yes", variants[0].ConditionScript);
    }

    [Fact]
    public void A_default_that_matches_no_branch_becomes_an_otherwise_variant_and_single_options_have_no_variants()
    {
        var loc = Loc("""
            defined_text = {
            	name = GetPhysicistSwapPluralWithIcon
            	text = { trigger = { has_civic = civic_imperial_cult } localization_key = job_calculator_physicist_swap_plural_with_icon }
            	default = job_physicist_swap_plural_with_icon
            }
            defined_text = { name = GetOnly text = { trigger = { is_gestalt = yes } localization_key = job_physicist } }
            """);
        loc.AddText("l_english:\n only: \"[GetOnly]\"\n");

        var variants = loc.Variants("mod_planet_physicists_produces_mult");

        Assert.Equal(["Civic: Imperial Cult", "otherwise"], variants.Select(v => v.Condition));
        Assert.True(variants[1].IsDefault);
        Assert.Null(variants[1].ConditionScript);
        Assert.Empty(loc.Variants("only"));
        Assert.Equal("Physicist", loc.Get("only"));
    }

    [Theory]
    [InlineData("is_spiritualist = yes is_worker_coop_empire = no", "Spiritualist, not Worker co-op")]
    [InlineData("NOT = { OR = { is_megacorp = yes is_hive_empire = yes } }", "not Megacorp, not Hive mind")]
    [InlineData("OR = { is_machine_empire = yes has_civic = civic_imperial_cult }", "Machine empire or Civic: Imperial Cult")]
    [InlineData("NAND = { is_gestalt = yes is_megacorp = yes }", "not Gestalt or not Megacorp")]
    [InlineData("is_scope_valid = yes exists = owner", "always")]
    [InlineData("always = no", "never")]
    [InlineData("NOT = { AND = { is_gestalt = yes is_megacorp = yes } }", "not Gestalt or not Megacorp")]
    [InlineData("NOT = { owner = { is_gestalt = yes is_megacorp = yes } }", "not Gestalt or not Megacorp")]
    [InlineData("NOT = { owner = { is_gestalt = yes } }", "not Gestalt")]
    [InlineData("num_pops > 10", "num_pops > 10")]
    public void Trigger_summaries_read_naturally(string trigger, string expected)
    {
        var loc = Loc();

        Assert.Equal(expected, TriggerSummary.Describe(ParadoxScriptParser.Parse(trigger), loc.Get));
    }

    [Fact]
    public void Build_reads_scripted_loc_with_overrides_and_skips_translations()
    {
        using var tmp = new TempDir();
        tmp.Write("g/common/scripted_loc/00_scripted_loc.txt", "defined_text = { name = GetA default = key_base }\ndefined_text = { name = GetB default = key_b }");
        tmp.Write("g/common/scripted_loc/00_scripted_loc_fr.txt", "defined_text = { name = GetB default = key_french }");
        tmp.Write("g/common/scripted_loc/scripted_loc_deloc.txt", "defined_text = { name = GetC default = key_german }");
        tmp.Write("m/common/scripted_loc/zz_mod.txt", "defined_text = { name = GetA default = key_mod }");
        tmp.Write("m/common/scripted_loc/zz_tradeloc.txt", "defined_text = { name = GetTrade default = key_trade }");
        using var g = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        using var m = ContentSource.FromPath("Mod", Path.Combine(tmp.Path, "m"));
        var warnings = new List<string>();

        var index = ScriptedLoc.Build([g, m], warnings);

        Assert.Empty(warnings);
        Assert.Equal("key_mod", index.Find("geta")!.DefaultKey);
        Assert.Equal("key_b", index.Find("GetB")!.DefaultKey);
        Assert.Null(index.Find("GetC"));
        Assert.Equal("key_trade", index.Find("GetTrade")!.DefaultKey);
    }

    [Fact]
    public void Stat_bonuses_carry_their_variants()
    {
        var bonus = TechDetailsBuilder.Bonus("planet_physicists_produces_mult", "0.1", Loc(),
            new Dictionary<string, string>(), new Dictionary<string, string>());

        Assert.Equal("Resources Produced from Physicists", bonus.Name);
        Assert.Equal("job_physicist", bonus.IconTag);
        Assert.Equal(["Resources Produced from Calculators", "Resources Produced from Physicists"], bonus.Variants.Select(v => v.Name));
    }

    [Fact]
    public void Bonus_icons_fall_back_to_unscoped_modifier_files_resource_sprites_and_job_files()
    {
        using var tmp = new TempDir();
        tmp.Write("g/interface/r.gfx", "spriteTypes = { spriteType = { name = \"GFX_resource_unity\" texturefile = \"gfx/interface/icons/resources/unity.dds\" } }");
        using var g = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        var sprites = SpriteIndex.Build([g], new List<string>());
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gfx/interface/icons/modifiers/mod_influence_produces_mult.dds",
            "gfx/interface/icons/resources/unity.dds",
            "gfx/interface/icons/jobs/job_artisan_drone.dds",
        };
        StatBonus B(string key, string? tag = null) => new(key, "0.1", key, "+10%", false, tag);

        Assert.Equal("gfx/interface/icons/modifiers/mod_influence_produces_mult.dds",
            IconResolver.ForBonus(B("country_influence_produces_mult"), files.Contains, sprites)?.Path);
        Assert.Equal("gfx/interface/icons/resources/unity.dds",
            IconResolver.ForBonus(B("country_unity_produces_mult"), files.Contains, sprites)?.Path);
        Assert.Equal("gfx/interface/icons/jobs/job_artisan_drone.dds",
            IconResolver.ForBonus(B("planet_artisans_produces_mult", "job_artisan_drone"), files.Contains, sprites)?.Path);
        Assert.Null(IconResolver.ForBonus(B("planet_artisans_produces_mult", "job_missing"), files.Contains, sprites));
    }
}

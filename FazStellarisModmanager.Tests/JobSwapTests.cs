using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class JobSwapTests
{
    const string Jobs = """
        bureaucrat = {
        	category = specialist
        	swappable_data = {
        		default = { building_icon = building_bureaucratic_1 }
        		swap_type = { trigger = { bureaucrat_is_priest = yes } name = priest icon = priest weight = 5 }
        		swap_type = { trigger = { has_building = building_imperial_office } name = imperial_official icon = manager }
        		swap_type = { trigger = { always = yes } icon = nameless }
        	}
        }
        farmer = { category = worker }
        """;

    static Localisation Loc(string jobs = Jobs, string? scriptedLoc = null)
    {
        var loc = new Localisation();
        loc.AddText("""
            l_english:
             mod_planet_bureaucrats_produces_mult: "Resources from [bureaucrat.GetIcon] [bureaucrat.GetNamePlural]"
             mod_planet_bureaucrats_upkeep_mult: "Upkeep for $bureaucrat_type$"
             bureaucrat_type: "[GetBureaucratSwap]"
             job_bureaucrat: "Bureaucrat"
             job_bureaucrat_plural: "Bureaucrats"
             job_coordinator_plural: "Coordinators"
             job_coordinator_swap: "[coordinator.GetNamePlural]"
             job_bureaucrat_swap: "[bureaucrat.GetNamePlural]"
             job_priest_plural: "Priests"
             job_imperial_official_plural: "Imperial Officials"
             civic_imperial_cult: "Imperial Cult"
             building_imperial_office: "Imperial Office"
            """);
        var parsed = ParadoxScriptParser.Parse(jobs).Entries.Where(e => e.Value is PdxBlock)
            .ToDictionary(e => e.Key, e => (PdxBlock)e.Value, StringComparer.OrdinalIgnoreCase);
        loc.Jobs = JobSwaps.From(parsed);
        loc.ScriptedTriggers = ParadoxScriptParser.Parse("""
            bureaucrat_is_priest = { exists = owner owner = { is_megacorp = no } bureaucrat_is_spiritualist = yes }
            bureaucrat_is_spiritualist = { owner = { has_civic = civic_imperial_cult } }
            is_gestalt = { has_ethic = ethic_gestalt_consciousness }
            """).Entries.ToDictionary(e => e.Key, e => (PdxBlock)e.Value, StringComparer.OrdinalIgnoreCase);
        if (scriptedLoc is not null)
        {
            var index = new ScriptedLoc();
            index.AddText(scriptedLoc);
            loc.Scripted = index;
        }
        return loc;
    }

    [Fact]
    public void Swaps_are_read_from_swappable_data_and_need_a_name()
    {
        var swaps = Loc().Jobs.For("Bureaucrat");

        Assert.Equal(["priest", "imperial_official"], swaps.Select(s => s.Name));
        Assert.Equal(["priest", "manager"], swaps.Select(s => s.Icon));
        Assert.Empty(Loc().Jobs.For("farmer"));
        Assert.Empty(Loc().Jobs.For("unknown"));
    }

    [Fact]
    public void A_job_name_gets_one_variant_per_swap_with_scripted_triggers_worded()
    {
        var variants = Loc().Variants("mod_planet_bureaucrats_produces_mult");

        Assert.Equal(
            [
                ("Resources from Bureaucrats", "job_bureaucrat", "otherwise", true),
                ("Resources from Priests", "job_priest", "not Megacorp, Civic: Imperial Cult", false),
                ("Resources from Imperial Officials", "job_manager", "Building: Imperial Office", false),
            ],
            variants.Select(v => (v.Text, v.IconTag, v.Condition, v.IsDefault)));
        Assert.Contains("bureaucrat_is_priest = yes", variants[1].ConditionScript);
    }

    [Fact]
    public void Scripted_function_branches_are_expanded_by_the_swaps_of_the_job_they_name()
    {
        var loc = Loc(scriptedLoc: """
            defined_text = {
            	name = GetBureaucratSwap
            	text = { trigger = { is_gestalt = yes } localization_key = job_coordinator_swap }
            	text = { trigger = { is_gestalt = no } localization_key = job_bureaucrat_swap }
            	default = job_bureaucrat_swap
            }
            """);

        var variants = loc.Variants("mod_planet_bureaucrats_upkeep_mult");

        Assert.Equal(
            [
                ("Upkeep for Coordinators", "Gestalt"),
                ("Upkeep for Bureaucrats", "not Gestalt, otherwise"),
                ("Upkeep for Priests", "not Gestalt, not Megacorp, Civic: Imperial Cult"),
                ("Upkeep for Imperial Officials", "not Gestalt, Building: Imperial Office"),
            ],
            variants.Select(v => (v.Text, v.Condition)));
        Assert.True(variants[1].IsDefault);
        Assert.Contains("# and", variants[2].ConditionScript);
    }

    [Fact]
    public void Swaps_that_give_the_same_text_are_merged()
    {
        var loc = Loc("""
            bureaucrat = { swappable_data = {
            	swap_type = { trigger = { has_civic = civic_imperial_cult } name = priest }
            	swap_type = { trigger = { has_building = building_imperial_office } name = priest }
            } }
            """);

        var variants = loc.Variants("mod_planet_bureaucrats_produces_mult");

        Assert.Equal(2, variants.Count);
        Assert.Equal("Civic: Imperial Cult or Building: Imperial Office", variants[1].Condition);
    }

    [Theory]
    [InlineData("bureaucrat_is_spiritualist = yes", "Civic: Imperial Cult")]
    [InlineData("bureaucrat_is_priest = no", "Megacorp or not Civic: Imperial Cult")]
    [InlineData("is_gestalt = yes", "Gestalt")]
    [InlineData("unknown_trigger = yes", "Unknown trigger")]
    public void Scripted_triggers_are_worded_by_their_contents(string trigger, string expected)
    {
        var loc = Loc();

        Assert.Equal(expected, TriggerSummary.Describe(ParadoxScriptParser.Parse(trigger), loc.Get,
            n => loc.ScriptedTriggers.TryGetValue(n, out var b) ? b : null));
    }

    [Fact]
    public void Common_definitions_follow_file_and_object_overrides()
    {
        using var tmp = new TempDir();
        tmp.Write("g/common/pop_jobs/00_jobs.txt", "a = { v = base }\nb = { v = base }");
        tmp.Write("g/common/pop_jobs/01_more.txt", "c = { v = base }");
        tmp.Write("m/common/pop_jobs/00_jobs.txt", "a = { v = replaced }");
        tmp.Write("m/common/pop_jobs/zz_mod.txt", "c = { v = mod }\n@x = 1");
        using var g = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        using var m = ContentSource.FromPath("Mod", Path.Combine(tmp.Path, "m"));
        var warnings = new List<string>();

        var defs = CommonDefinitions.Load([g, m], "common/pop_jobs", warnings);

        Assert.Empty(warnings);
        Assert.Equal(["a", "c"], defs.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("replaced", defs["A"].GetString("v"));
        Assert.Equal("mod", defs["c"].GetString("v"));
    }
}

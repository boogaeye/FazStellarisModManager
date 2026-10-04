using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Tests;

public class GrantFinderTests
{
    static GrantFinder Finder(ScriptLibrary? library = null) =>
        new(library ?? new ScriptLibrary(), b => TriggerSummary.Describe(b, _ => null));

    static PdxBlock P(string text) => ParadoxScriptParser.Parse(text);

    [Fact]
    public void Finds_the_three_effects_in_their_forms()
    {
        var found = Finder().Find(P("""
            give_technology = { tech = tech_a message = no }
            add_research_option = tech_b
            add_research_option = { tech = tech_c }
            add_tech_progress = { tech = tech_d progress = 0.25 }
            add_tech_progress = { tech = tech_e progress = @var }
            """));

        Assert.Equal(
            [("tech_a", GrantKind.Gives, (double?)null), ("tech_b", GrantKind.ResearchOption, null), ("tech_c", GrantKind.ResearchOption, null),
             ("tech_d", GrantKind.Progress, 0.25), ("tech_e", GrantKind.Progress, null)],
            found.Select(f => (f.Tech, f.Kind, f.Progress)));
        Assert.All(found, f => Assert.Equal((null, null), (f.Condition, f.Via)));
    }

    [Fact]
    public void Conditions_come_from_if_else_and_random_lists_and_trigger_blocks_are_skipped()
    {
        var found = Finder().Find(P("""
            owner = {
                if = { limit = { has_civic = civic_x } give_technology = { tech = tech_a } }
                else_if = { limit = { is_gestalt = yes } add_research_option = tech_b }
                else = {
                    random_list = {
                        50 = { add_tech_progress = { tech = tech_c progress = 0.5 } modifier = { factor = 2 give_technology = { tech = tech_never } } }
                    }
                }
                IF = { limit = { always = yes } add_research_option = tech_d }
            }
            limit = { give_technology = { tech = tech_in_trigger } }
            ai_chance = { factor = 1 }
            """));

        Assert.Equal(
            [("tech_a", "Civic: civic_x"), ("tech_b", "otherwise, Gestalt"), ("tech_c", "otherwise; by chance"), ("tech_d", (string?)null)],
            found.Select(f => (f.Tech, f.Condition)));
    }

    [Fact]
    public void Tooltips_and_the_effect_block_of_create_country_are_skipped()
    {
        var found = Finder().Find(P("""
            tooltip = { give_technology = { tech = tech_tooltip } }
            create_country = {
                name = random
                effect = { give_technology = { tech = tech_new_country } }
            }
            owner = { effect = { give_technology = { tech = tech_a } } }
            """));

        Assert.Equal(["tech_a"], found.Select(f => f.Tech));
    }

    [Fact]
    public void Switch_cases_become_conditions()
    {
        var found = Finder().Find(P("""
            switch = {
                trigger = has_origin
                origin_a = { give_technology = { tech = tech_a } }
                default = { add_research_option = tech_b }
            }
            inverted_switch = {
                trigger = has_civic
                civic_x = { add_tech_progress = { tech = tech_c progress = 0.5 } }
            }
            """));

        Assert.Equal(
            [("tech_a", "has_origin = origin_a"), ("tech_b", "otherwise"), ("tech_c", "not has_civic = civic_x")],
            found.Select(f => (f.Tech, f.Condition)));
    }

    [Fact]
    public void Locked_random_lists_are_by_chance()
    {
        var found = Finder().Find(P("locked_random_list = { 10 = { give_technology = { tech = tech_a } } }"));

        Assert.Equal([("tech_a", "by chance")], found.Select(f => (f.Tech, f.Condition)));
    }

    [Fact]
    public void Scripted_effects_are_followed_with_parameters_and_cycles_stop()
    {
        var library = new ScriptLibrary();
        library.AddEffects("""
            grant_it = { give_technology = { tech = $TECH$ } }
            reward = { if = { limit = { always = yes } grant_it = { TECH = tech_a } } }
            default_tech = { add_research_option = $TECH|tech_d$ }
            loop_a = { loop_b = yes }
            loop_b = { loop_a = yes give_technology = { tech = tech_loop } }
            off = { give_technology = { tech = tech_off } }
            """);

        var found = Finder(library).Find(P("reward = yes default_tech = yes loop_a = yes off = no"));

        Assert.Equal(
            [("tech_a", GrantKind.Gives, "reward"), ("tech_d", GrantKind.ResearchOption, "default_tech"), ("tech_loop", GrantKind.Gives, "loop_a")],
            found.Select(f => (f.Tech, f.Kind, f.Via)));
    }

    [Fact]
    public void Inline_scripts_are_expanded_with_parameters()
    {
        var library = new ScriptLibrary();
        library.AddInlineScript("events/grant", "give_technology = { tech = $TECH$ }");

        var found = Finder(library).Find(P("""
            inline_script = { script = events/grant TECH = tech_a }
            inline_script = "events/grant"
            inline_script = missing/path
            """));

        Assert.Equal(["tech_a", "$TECH$"], found.Select(f => f.Tech));
        Assert.All(found, f => Assert.Null(f.Via));
    }

    [Fact]
    public void Substitution_uses_values_then_defaults_and_keeps_unknown_parameters()
    {
        var parameters = new Dictionary<string, string> { ["X"] = "1" };

        Assert.Equal("a = 1 b = def c = $Z$", ScriptLibrary.Substitute("a = $X$ b = $Y|def$ c = $Z$", parameters));
        var block = ScriptLibrary.Substitute(P("key_$X$ = { v = $X$ }"), parameters);
        Assert.Equal("1", block.GetBlock("key_1")!.GetString("v"));
    }
}

using FazStellarisModmanager.Core.Conditions;
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Saves;

namespace FazStellarisModmanager.Tests;

public class ConditionEvaluatorTests
{
    static HashSet<string> Set(params string[] items) => new(items, StringComparer.OrdinalIgnoreCase);

    static EmpireFacts Facts(bool isPlayer = true, string? origin = "origin_default", string? authority = "auth_democratic", string type = "default") =>
        new(Set("tech_a"), Set("my_flag"), Set("global_one"), Set("ap_x"), Set("tr_y"), Set("civic_z"), Set("ethic_militarist"),
            origin, authority, type, Set("Utopia"), isPlayer);

    static ConditionNode Eval(string script, EmpireFacts? facts = null, string? scripted = null)
    {
        var defs = scripted is null ? [] : ParadoxScriptParser.Parse(scripted).Entries
            .Where(e => e.Value is PdxBlock).ToDictionary(e => e.Key, e => (PdxBlock)e.Value, StringComparer.OrdinalIgnoreCase);
        return new ConditionEvaluator(n => defs.GetValueOrDefault(n)).Evaluate(ParadoxScriptParser.Parse(script), facts ?? Facts());
    }

    static Truth R(string script, EmpireFacts? facts = null, string? scripted = null) => Eval(script, facts, scripted).Result;

    [Fact]
    public void Empty_block_is_true() => Assert.Equal(Truth.True, R(""));

    [Fact]
    public void Root_is_and_of_entries()
    {
        Assert.Equal(Truth.True, R("has_technology = tech_a has_country_flag = my_flag"));
        Assert.Equal(Truth.False, R("has_technology = tech_a has_country_flag = nope"));
        Assert.Equal(Truth.False, R("has_technology = tech_a has_country_flag = nope num_owned_planets > 5"));
        Assert.Equal(Truth.Unknown, R("has_technology = tech_a num_owned_planets > 5"));
    }

    [Fact]
    public void Or_is_true_despite_unknown_sibling()
    {
        Assert.Equal(Truth.True, R("OR = { num_owned_planets > 5 has_technology = tech_a }"));
        Assert.Equal(Truth.Unknown, R("OR = { num_owned_planets > 5 has_technology = nope }"));
        Assert.Equal(Truth.False, R("OR = { has_technology = nope has_country_flag = nope }"));
    }

    [Fact]
    public void Not_is_nor_of_its_entries()
    {
        Assert.Equal(Truth.False, R("NOT = { has_technology = tech_a }"));
        Assert.Equal(Truth.True, R("NOT = { has_technology = nope }"));
        Assert.Equal(Truth.Unknown, R("NOT = { num_owned_planets > 5 }"));
        // NOT with two entries: neither may hold.
        Assert.Equal(Truth.False, R("NOT = { has_technology = nope has_country_flag = my_flag }"));
        Assert.Equal(Truth.True, R("NOT = { has_technology = nope has_country_flag = nope }"));
        Assert.Equal(Truth.False, R("NOT = { num_owned_planets > 5 has_technology = tech_a }"));
    }

    [Fact]
    public void Nor_nand_and_hidden_trigger()
    {
        Assert.Equal(Truth.False, R("NOR = { has_technology = tech_a has_country_flag = nope }"));
        Assert.Equal(Truth.True, R("NAND = { has_technology = tech_a has_country_flag = nope }"));
        Assert.Equal(Truth.False, R("NAND = { has_technology = tech_a has_country_flag = my_flag }"));
        Assert.Equal(Truth.Unknown, R("NAND = { has_technology = tech_a num_owned_planets > 5 }"));
        Assert.Equal(Truth.False, R("hidden_trigger = { has_technology = tech_a has_country_flag = nope }"));
        Assert.Equal(Truth.True, R("AND = { has_technology = tech_a has_country_flag = my_flag }"));
    }

    [Fact]
    public void Block_nodes_have_children_and_plain_text()
    {
        var n = Eval("OR = { has_technology = tech_a has_country_flag = nope }");
        var or = Assert.Single(n.Children);
        Assert.Equal("OR", or.Text);
        Assert.Equal(["has_technology = tech_a", "has_country_flag = nope"], or.Children.Select(c => c.Text));
        Assert.Equal([Truth.True, Truth.False], or.Children.Select(c => c.Result));
        Assert.Empty(or.Children[0].Children);
    }

    [Fact]
    public void Leaf_triggers()
    {
        Assert.Equal(Truth.True, R("always = yes"));
        Assert.Equal(Truth.False, R("always = no"));
        Assert.Equal(Truth.True, R("has_global_flag = global_one"));
        Assert.Equal(Truth.False, R("has_global_flag = other"));
        Assert.Equal(Truth.True, R("has_ascension_perk = ap_x"));
        Assert.Equal(Truth.True, R("has_tradition = tr_y"));
        Assert.Equal(Truth.True, R("has_ethic = ethic_militarist"));
        Assert.Equal(Truth.False, R("has_ethic = ethic_fanatic_militarist"));
        Assert.Equal(Truth.True, R("has_civic = civic_z"));
        Assert.Equal(Truth.True, R("has_valid_civic = civic_z"));
        Assert.Equal(Truth.False, R("has_civic = civic_q"));
        Assert.Equal(Truth.True, R("has_origin = origin_default"));
        Assert.Equal(Truth.False, R("has_origin = origin_other"));
        Assert.Equal(Truth.True, R("has_authority = auth_democratic"));
        Assert.Equal(Truth.True, R("is_country_type = default"));
        Assert.Equal(Truth.False, R("is_country_type = fallen_empire"));
        Assert.Equal(Truth.True, R("has_technology = \"tech_a\""));
        Assert.Equal(Truth.True, R("has_technology = TECH_A"));
    }

    [Fact]
    public void Boolean_triggers_and_no_negation()
    {
        Assert.Equal(Truth.True, R("is_regular_empire = yes"));
        Assert.Equal(Truth.False, R("is_regular_empire = no"));
        Assert.Equal(Truth.False, R("is_fallen_empire = yes"));
        Assert.Equal(Truth.True, R("is_fallen_empire = no"));
        Assert.Equal(Truth.True, R("is_fallen_empire = yes", Facts(type: "awakened_fallen_empire")));
        Assert.Equal(Truth.True, R("is_fallen_empire = yes", Facts(type: "fallen_empire")));
        Assert.Equal(Truth.False, R("is_regular_empire = yes", Facts(type: "fallen_empire")));
        Assert.Equal(Truth.False, R("is_machine_empire = yes"));
        Assert.Equal(Truth.True, R("is_machine_empire = yes", Facts(authority: "auth_machine_intelligence")));
        Assert.Equal(Truth.True, R("is_hive_empire = yes", Facts(authority: "auth_hive_mind")));
        Assert.Equal(Truth.True, R("is_gestalt = yes", Facts(authority: "auth_hive_mind")));
        Assert.Equal(Truth.True, R("is_gestalt = yes", Facts(authority: "auth_machine_intelligence")));
        Assert.Equal(Truth.True, R("is_gestalt = no"));
        Assert.Equal(Truth.False, R("is_ai = yes"));
        Assert.Equal(Truth.True, R("is_ai = no"));
        Assert.Equal(Truth.True, R("is_ai = yes", Facts(isPlayer: false)));
    }

    [Fact]
    public void Host_has_dlc()
    {
        Assert.Equal(Truth.True, R("host_has_dlc = \"Utopia\""));
        Assert.Equal(Truth.False, R("host_has_dlc = \"Apocalypse\""));
    }

    [Fact]
    public void Operators_and_unknown_things_are_unknown_and_keep_their_operator()
    {
        var n = Eval("num_owned_planets > 5");
        Assert.Equal(Truth.Unknown, n.Result);
        Assert.Equal("num_owned_planets > 5", n.Children.Single().Text);
        Assert.Equal(Truth.Unknown, R("some_unknown_trigger = yes"));
        Assert.Equal(Truth.Unknown, R("has_technology != tech_a"));
        Assert.Equal(Truth.Unknown, R("has_technology = yes"));
        Assert.Equal(Truth.Unknown, R("has_global_flag = @some_variable"));
        Assert.Equal(Truth.Unknown, R("has_country_flag = event_target:x"));
    }

    [Fact]
    public void Scope_blocks_are_unknown_with_unevaluated_children()
    {
        var n = Eval("any_owned_planet = { has_technology = tech_a is_capital = yes }");
        var scope = n.Children.Single();
        Assert.Equal("any_owned_planet", scope.Text);
        Assert.Equal(Truth.Unknown, scope.Result);
        Assert.All(scope.Children, c => Assert.Equal(Truth.Unknown, c.Result));
        Assert.Equal(2, scope.Children.Count);
        Assert.Equal(Truth.Unknown, R("owner_species = { has_trait = trait_x }"));
    }

    [Fact]
    public void Custom_tooltip_is_and_of_its_triggers()
    {
        Assert.Equal(Truth.True, R("custom_tooltip = { fail_text = a success_text = b text = c has_technology = tech_a }"));
        Assert.Equal(Truth.False, R("custom_tooltip = { fail_text = a has_technology = nope }"));
        Assert.Equal(Truth.Unknown, R("custom_tooltip = { fail_text = a text = b }"));
        var n = Eval("custom_tooltip = { fail_text = a has_technology = tech_a }").Children.Single();
        Assert.Equal(["has_technology = tech_a"], n.Children.Select(c => c.Text));
    }

    [Fact]
    public void Scripted_triggers_expand_to_children()
    {
        const string defs = """
            my_check = { has_technology = tech_a has_country_flag = my_flag }
            my_failing = { has_technology = nope }
            my_param = { has_technology = $TECH$ }
            loop_a = { loop_b = yes }
            loop_b = { loop_a = yes }
            """;
        var n = Eval("my_check = yes", scripted: defs).Children.Single();
        Assert.Equal("my_check = yes", n.Text);
        Assert.Equal(Truth.True, n.Result);
        Assert.Equal(["has_technology = tech_a", "has_country_flag = my_flag"], n.Children.Select(c => c.Text));
        Assert.Equal(Truth.False, R("my_check = no", scripted: defs));
        Assert.Equal(Truth.False, R("my_failing = yes", scripted: defs));
        Assert.Equal(Truth.True, R("my_failing = no", scripted: defs));
        Assert.Equal(Truth.Unknown, R("my_param = yes", scripted: defs));
        Assert.Equal(Truth.Unknown, R("my_param = no", scripted: defs));
        Assert.Equal(Truth.Unknown, R("loop_a = yes", scripted: defs));
    }

    [Fact]
    public void Scripted_trigger_with_unknown_inside_is_unknown_unless_decided()
    {
        const string defs = """
            mixed = { num_owned_planets > 5 has_technology = nope }
            mixed_or = { OR = { num_owned_planets > 5 has_technology = tech_a } }
            """;
        Assert.Equal(Truth.False, R("mixed = yes", scripted: defs));
        Assert.Equal(Truth.True, R("mixed_or = yes", scripted: defs));
    }

    [Fact]
    public void Facts_from_save_country()
    {
        var holdings = new CountryHoldings(["civic_z"], "origin_o", "auth_a", ["tr_y"], ["ap_x"], [], [], [], []);
        var c = new SaveCountry(0, "default", null, new Dictionary<string, string>(), null, null, 1, 0, 0, 0, 0, 0, 0, 0, null, [], ["tech_a"],
            holdings, null, ["f1"], ["ethic_e"]);
        var s = new GameSnapshot("n", "d", "v", "p", DateTime.UtcNow, [new SavePlayer("H", 0)], [c], GlobalFlags: ["g1"], Dlcs: ["Utopia"]);
        var f = EmpireFacts.From(c, s, isPlayer: true);
        Assert.Contains("TECH_A", f.Techs);
        Assert.Contains("f1", f.Flags);
        Assert.Contains("g1", f.GlobalFlags);
        Assert.Contains("ap_x", f.Perks);
        Assert.Contains("tr_y", f.Traditions);
        Assert.Contains("civic_z", f.Civics);
        Assert.Contains("ethic_e", f.Ethics);
        Assert.Equal("origin_o", f.Origin);
        Assert.Equal("auth_a", f.Authority);
        Assert.Equal("default", f.CountryType);
        Assert.Contains("Utopia", f.Dlcs);
        Assert.True(f.IsPlayer);
        var bare = EmpireFacts.From(c with { Holdings = null, Flags = null, Ethics = null }, s with { GlobalFlags = null, Dlcs = null }, false);
        Assert.Empty(bare.Flags);
        Assert.Null(bare.Origin);
        Assert.False(bare.IsPlayer);
    }

    [Fact]
    public void Year_triggers_compare_against_the_save_date_and_galaxy_setup()
    {
        // 2387.09.17 with mid game at 100 and end game at 200 years: 187 passed, 87 into mid game, 13 before end game.
        var f = Facts() with { YearsPassed = EmpireFacts.YearsSinceStart("2387.09.17"), MidGameStart = 100, EndGameStart = 200 };
        Assert.Equal(187, f.YearsPassed);
        Assert.Equal(Truth.True, R("years_passed >= 187", f));
        Assert.Equal(Truth.False, R("years_passed >= 200", f));
        Assert.Equal(Truth.True, R("mid_game_years_passed > 80", f));
        Assert.Equal(Truth.False, R("mid_game_years_passed >= 100", f));
        Assert.Equal(Truth.False, R("end_game_years_passed >= 50", f));
        Assert.Equal(Truth.True, R("end_game_years_passed < 0", f));
        Assert.Equal(Truth.False, R("OR = { end_game_years_passed >= 50 mid_game_years_passed >= 100 years_passed >= 200 }", f));
    }

    [Fact]
    public void Year_triggers_are_unknown_without_data()
    {
        Assert.Equal(Truth.Unknown, R("years_passed > 5"));
        Assert.Equal(Truth.Unknown, R("mid_game_years_passed > 5", Facts() with { YearsPassed = 10 }));
        Assert.Equal(Truth.Unknown, R("years_passed > @x", Facts() with { YearsPassed = 10 }));
        Assert.Null(EmpireFacts.YearsSinceStart(null));
    }

    [Fact]
    public void Year_conditions_say_how_long_until_they_are_true()
    {
        var f = Facts() with { YearsPassed = 187, MidGameStart = 100, EndGameStart = 200 };
        string? Note(string script) => Eval(script, f).Children[0].Note;
        Assert.Equal("now -13 · true in 63 years (2450)", Note("end_game_years_passed >= 50"));
        Assert.Equal("now 87 · true in 13 years (2400)", Note("mid_game_years_passed >= 100"));
        Assert.Equal("now 187 · true in 14 years (2401)", Note("years_passed > 200"));
        Assert.Equal("now 187 · true in 1 year (2388)", Note("years_passed = 188"));
        Assert.Equal("now 187", Note("years_passed >= 100"));
        Assert.Equal("now 187 · no longer possible", Note("years_passed < 100"));
        Assert.Equal("now 187 · no longer possible", Note("years_passed = 150"));
        Assert.Null(Eval("years_passed > 5").Children[0].Note);
    }
}

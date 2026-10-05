using FazStellarisModmanager.Core.Conditions;
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Events;

namespace FazStellarisModmanager.Tests;

public class WeightedChanceTests
{
    static WeightInput Branch(double? weight, params ModifierResult[] modifiers) => new(weight, modifiers);

    [Fact]
    public void Chance_is_weight_times_applying_factors_over_the_total()
    {
        var chances = WeightedChance.Chances([
            Branch(10, new ModifierResult(3, null, Truth.True), new ModifierResult(0, null, Truth.False)),
            Branch(20, new ModifierResult(0.5, null, Truth.True)),
            Branch(50),
        ])!;

        // 30 : 10 : 50
        Assert.Equal([30.0 / 90, 10.0 / 90, 50.0 / 90], chances);
    }

    [Fact]
    public void Add_modifiers_apply_in_order_with_factors()
    {
        var chances = WeightedChance.Chances([
            Branch(10, new ModifierResult(null, 5, Truth.True), new ModifierResult(2, null, Truth.True)),
            Branch(10),
        ])!;

        Assert.Equal([0.75, 0.25], chances);
    }

    [Fact]
    public void Any_undecided_modifier_or_unknown_weight_gives_no_chances()
    {
        Assert.Null(WeightedChance.Chances([Branch(10), Branch(10, new ModifierResult(2, null, Truth.Unknown))]));
        Assert.Null(WeightedChance.Chances([Branch(10), Branch(null)]));
        Assert.Null(WeightedChance.Chances([Branch(10, new ModifierResult(null, null, Truth.True))]));
    }

    [Fact]
    public void All_zero_weights_give_zero_chances()
    {
        Assert.Equal([0.0, 0.0], WeightedChance.Chances([Branch(10, new ModifierResult(0, null, Truth.True)), Branch(0)]));
    }

    [Fact]
    public void A_pick_is_evaluated_against_an_empire()
    {
        static PdxBlock P(string s) => ParadoxScriptParser.Parse(s);
        var pick = new WeightedPick([
            new WeightBranch("10", 10, [new WeightModifier(0, null, P("has_country_flag = no_a"), "flag"), new WeightModifier(4, null, P(""), "always")], null),
            new WeightBranch("20", 20, [new WeightModifier(2, null, P("has_technology = tech_x"), "tech")], null),
        ], 0);
        var evaluator = new ConditionEvaluator(_ => null);
        static HashSet<string> Set(params string[] items) => new(items, StringComparer.OrdinalIgnoreCase);
        var facts = new EmpireFacts(Set("tech_x"), Set(), Set(), Set(), Set(), Set(), Set(), null, null, "default", Set(), true);

        var nodes = WeightedChance.Evaluate(pick, evaluator, facts);
        Assert.Equal([[Truth.False, Truth.True], [Truth.True]], nodes.Select(b => b.Select(n => n.Result).ToList()).ToList());
        Assert.Equal([0.5, 0.5], WeightedChance.Chances(pick, evaluator, facts));

        var unknown = pick with { Branches = [pick.Branches[0], new WeightBranch("20", 20, [new WeightModifier(2, null, P("owner = { is_ai = no }"), "x")], null)] };
        Assert.Null(WeightedChance.Chances(unknown, evaluator, facts));
    }
}

using FazStellarisModmanager.Core.Conditions;

namespace FazStellarisModmanager.Core.Events;

/// <summary>A weight modifier with whether it applies: factor multiplies, add adds (both may be set; factor first).</summary>
public sealed record ModifierResult(double? Factor, double? Add, Truth Applies);

/// <summary>A branch of a weighted pick: base weight (null when not a number) and its modifiers in order.</summary>
public sealed record WeightInput(double? Weight, IReadOnlyList<ModifierResult> Modifiers);

/// <summary>Chances of the branches of a random_list or random_events for one empire: weight × applying factors / total.</summary>
public static class WeightedChance
{
    /// <summary>
    /// Each branch's chance (0–1), in order; all 0 when every weight ends at 0. Null when a weight is not a number, a modifier is
    /// undecided (Unknown), or an applying modifier has neither a numeric factor nor add.
    /// </summary>
    public static IReadOnlyList<double>? Chances(IReadOnlyList<WeightInput> branches)
    {
        var weights = new double[branches.Count];
        for (var i = 0; i < branches.Count; i++)
        {
            if (branches[i].Weight is not { } weight) return null;
            foreach (var m in branches[i].Modifiers)
            {
                if (m.Applies == Truth.Unknown) return null;
                if (m.Applies == Truth.False) continue;
                if (m.Factor is null && m.Add is null) return null;
                if (m.Factor is { } factor) weight *= factor;
                if (m.Add is { } add) weight += add;
            }
            weights[i] = Math.Max(0, weight);
        }
        var total = weights.Sum();
        return weights.Select(w => total > 0 ? w / total : 0).ToList();
    }

    /// <summary>Each branch's modifiers evaluated against the empire (one node per modifier), for marking.</summary>
    public static IReadOnlyList<IReadOnlyList<ConditionNode>> Evaluate(WeightedPick pick, ConditionEvaluator evaluator, EmpireFacts facts) =>
        pick.Branches.Select(b => (IReadOnlyList<ConditionNode>)b.Modifiers.Select(m => evaluator.Evaluate(m.Trigger, facts)).ToList()).ToList();

    /// <summary>The chances from modifiers already evaluated by <see cref="Evaluate"/>.</summary>
    public static IReadOnlyList<double>? Chances(WeightedPick pick, IReadOnlyList<IReadOnlyList<ConditionNode>> evaluated) =>
        Chances(pick.Branches.Select((b, i) => new WeightInput(b.Weight,
            b.Modifiers.Select((m, j) => new ModifierResult(m.Factor, m.Add, evaluated[i][j].Result)).ToList())).ToList());

    /// <summary>The chances of a pick for an empire; null unless every modifier of every branch is decided.</summary>
    public static IReadOnlyList<double>? Chances(WeightedPick pick, ConditionEvaluator evaluator, EmpireFacts facts) =>
        Chances(pick, Evaluate(pick, evaluator, facts));
}

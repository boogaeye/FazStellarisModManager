using System.Globalization;
using FazStellarisModmanager.Core.Conditions;
using FazStellarisModmanager.Core.Events;

namespace FazStellarisModmanager.Components;

/// <summary>Short texts shared by the Events tab components.</summary>
public static class EventText
{
    public static string Percent(double chance) => (chance * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    /// <summary>" · 25% for you" when every modifier is decided, else " · base 20%" from the base weights, else "".</summary>
    public static string PickSummary(WeightedPick pick, EventGraph graph, EmpireFacts? facts)
    {
        if (facts is not null && WeightedChance.Chances(pick, graph.Conditions, facts) is { } chances)
            return " · " + Percent(chances[pick.Index]) + " for you";
        return pick.BaseChance is { } b ? " · base " + Percent(b) : "";
    }

    public static string Shorten(string? text)
    {
        text ??= "conditions";
        return text.Length > 110 ? text[..110] + "…" : text;
    }
}

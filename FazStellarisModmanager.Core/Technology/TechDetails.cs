using System.Globalization;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>One entry of a tech's modifier block, e.g. army_damage_mult = 0.05 shown as "+5% Army Damage".</summary>
public sealed record StatBonus(string Key, string RawValue, string Name, string Display, bool IsNegative);

/// <summary>A prereqfor_desc line: Kind is the child block name (ship, custom, …).</summary>
public sealed record CustomUnlock(string Kind, string Title, string? Description);

public sealed record TechSwap(string Name, string? TriggerScript, bool InheritsEffects);

public sealed record TechDetails(
    IReadOnlyList<StatBonus> Bonuses,
    IReadOnlyList<CustomUnlock> CustomUnlocks,
    IReadOnlyList<string> FeatureFlags,
    string? Gateway,
    IReadOnlyList<TechSwap> Swaps,
    string? Weight,
    string? WeightModifierScript,
    string? AiWeightScript,
    string? PotentialScript,
    string RawScript);

public static class TechDetailsBuilder
{
    static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled);

    public static TechDetails Build(string key, PdxBlock block, Localisation loc,
        IReadOnlyDictionary<string, string> locals, IReadOnlyDictionary<string, string> globals)
    {
        // "# name" annotations: only for identifier-like values with a short localised name.
        string? Annotate(string v) =>
            Identifier.IsMatch(v) && v is not ("yes" or "no") && loc.Get(v) is { Length: > 0 and <= 60 } name ? name : null;

        string? Script(string name) => block.GetBlock(name) is { } b ? PdxScriptPrinter.Print(b, Annotate) : null;

        var bonuses = (block.GetBlock("modifier")?.Entries ?? [])
            .Where(e => e.Value is string)
            .Select(e => Bonus(e.Key, (string)e.Value, loc))
            .ToList();

        var custom = new List<CustomUnlock>();
        if (block.GetBlock("prereqfor_desc") is { } descs)
            foreach (var e in descs.Entries)
                if (e.Value is PdxBlock b)
                {
                    var title = b.GetString("title");
                    var desc = b.GetString("desc");
                    custom.Add(new CustomUnlock(e.Key, title is null ? e.Key : loc.Get(title) ?? title, desc is null ? null : loc.Get(desc) ?? desc));
                }

        var swaps = block.Entries
            .Where(e => e.Key.Equals("technology_swap", StringComparison.OrdinalIgnoreCase) && e.Value is PdxBlock)
            .Select(e => (PdxBlock)e.Value)
            .Select(b => new TechSwap(
                b.GetString("name") ?? "?",
                b.GetBlock("trigger") is { } t ? PdxScriptPrinter.Print(t, Annotate) : null,
                string.Equals(b.GetString("inherit_effects"), "yes", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new TechDetails(
            bonuses,
            custom,
            block.GetBlock("feature_flags")?.StringItems.ToList() ?? [],
            block.GetString("gateway"),
            swaps,
            block.GetString("weight") is { } w ? ScriptedVariables.Resolve(w, locals, globals) : null,
            Script("weight_modifier"),
            Script("ai_weight"),
            Script("potential"),
            PdxScriptPrinter.PrintNamed(key, block, Annotate));
    }

    /// <summary>_mult values show as signed percentages, _add values as signed numbers, anything else as written. Name from mod_KEY localisation.</summary>
    public static StatBonus Bonus(string key, string raw, Localisation loc)
    {
        var name = loc.Get("mod_" + key) ?? key;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return new StatBonus(key, raw, name, raw, false);
        var sign = v < 0 ? "-" : "+";
        var display = key.EndsWith("_mult", StringComparison.OrdinalIgnoreCase) ? sign + Number(Math.Abs(v) * 100) + "%"
            : key.EndsWith("_add", StringComparison.OrdinalIgnoreCase) ? sign + Number(Math.Abs(v))
            : raw;
        return new StatBonus(key, raw, name, display, v < 0);
    }

    static string Number(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
}

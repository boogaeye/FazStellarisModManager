using System.Globalization;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>One entry of a tech's modifier block, e.g. army_damage_mult = 0.05 shown as "+5% Army Damage".</summary>
public sealed record StatBonus(string Key, string RawValue, string Name, string Display, bool IsNegative, string? IconTag = null,
    IReadOnlyList<BonusVariant>? NameVariants = null)
{
    /// <summary>Empire-dependent names (e.g. Physicists / Calculators); empty when the name never changes.</summary>
    public IReadOnlyList<BonusVariant> Variants => NameVariants ?? [];
}

/// <summary>One empire-dependent name of a stat bonus and the condition (readable and as script) that selects it.</summary>
public sealed record BonusVariant(string Name, string? IconTag, string Condition, string? ConditionScript, bool IsDefault);

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
    string RawScript)
{
    public static TechDetails Empty { get; } = new([], [], [], null, [], null, null, null, null, "");
}

public static class TechDetailsBuilder
{
    static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled);

    static readonly HashSet<string> NotBonuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "custom_tooltip", "custom_tooltip_with_params", "show_only_custom_tooltip", "description",
    };

    static readonly HashSet<string> NoAnnotate = new(StringComparer.OrdinalIgnoreCase)
    {
        "yes", "no", "always", "never", "root", "from", "owner", "this", "prev", "value", "factor", "add", "base",
    };

    public static TechDetails Build(string key, PdxBlock block, Localisation loc,
        IReadOnlyDictionary<string, string> locals, IReadOnlyDictionary<string, string> globals,
        Dictionary<string, string?>? annotationCache = null)
    {
        // "# name" annotations: only for identifier-like values with a short localised name. Cached per value when a cache is given.
        string? Annotate(string v)
        {
            if (!Identifier.IsMatch(v) || NoAnnotate.Contains(v)) return null;
            if (annotationCache is not null && annotationCache.TryGetValue(v, out var cached)) return cached;
            var name = loc.Get(v) is { Length: > 0 and <= 60 } n ? n : null;
            if (annotationCache is not null) annotationCache[v] = name;
            return name;
        }

        string? Script(string name) =>
            block.GetBlock(name) is { } b && PdxScriptPrinter.Print(b, Annotate) is { Length: > 0 } text ? text : null;

        var modifierEntries = (block.GetBlock("modifier")?.Entries ?? []).Where(e => e.Value is string).ToList();
        var bonuses = modifierEntries
            .Where(e => !NotBonuses.Contains(e.Key))
            .Select(e => Bonus(e.Key, (string)e.Value, loc, locals, globals))
            .ToList();

        var custom = new List<CustomUnlock>();
        foreach (var e in modifierEntries)
            if (e.Key.Equals("custom_tooltip", StringComparison.OrdinalIgnoreCase)
                || e.Key.Equals("custom_tooltip_with_params", StringComparison.OrdinalIgnoreCase))
            {
                var v = (string)e.Value;
                custom.Add(new CustomUnlock("tooltip", loc.Get(v) ?? v, null));
            }
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
                b.GetBlock("trigger") is { } t && PdxScriptPrinter.Print(t, Annotate) is { Length: > 0 } tt ? tt : null,
                string.Equals(b.GetString("inherit_effects"), "yes", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new TechDetails(
            bonuses,
            custom,
            block.GetBlock("feature_flags")?.StringItems.ToList() ?? [],
            block.GetString("gateway"),
            swaps,
            // A block-form weight is not a plain number; leave Weight null (weight_modifier/ai_weight cover those).
            block.GetString("weight") is { } w ? ScriptedVariables.Resolve(w, locals, globals) : null,
            Script("weight_modifier"),
            Script("ai_weight"),
            Script("potential"),
            PdxScriptPrinter.PrintNamed(key, block, Annotate));
    }

    /// <summary>_mult values show as signed percentages, _add values as signed numbers, anything else as written. Name from mod_KEY localisation.</summary>
    public static StatBonus Bonus(string key, string raw, Localisation loc,
        IReadOnlyDictionary<string, string> locals, IReadOnlyDictionary<string, string> globals)
    {
        var name = loc.Get("mod_" + key) ?? key;
        var tag = loc.FirstIconTag("mod_" + key);
        var variants = loc.Variants("mod_" + key)
            .Select(v => new BonusVariant(v.Text, v.IconTag, v.Condition, v.ConditionScript, v.IsDefault))
            .ToList();
        raw = ScriptedVariables.Resolve(raw, locals, globals);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
            return new StatBonus(key, raw, name, raw, false, tag, variants);
        var display = key.EndsWith("_mult", StringComparison.OrdinalIgnoreCase) ? Signed(v * 100) + "%"
            : key.EndsWith("_add", StringComparison.OrdinalIgnoreCase) ? Signed(v)
            : raw;
        return new StatBonus(key, raw, name, display, v < 0, tag, variants);
    }

    static string Signed(double v)
    {
        if (v == 0) return "0";
        var abs = Math.Abs(v);
        var text = Math.Round(abs, 2) == 0 ? abs.ToString("0.####", CultureInfo.InvariantCulture) : Number(abs);
        return (v < 0 ? "-" : "+") + text;
    }

    static string Number(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
}

using System.Globalization;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Diplomacy;

/// <summary>
/// Diplomatic weight modifiers from one source, as fractions (0.25 = +25%). Delegate (diplo_weight_delegate_mult) counts
/// per skill level of the Galactic Community delegate; CouncilorSkill (councilor_skill_add) adds skill levels to councilors.
/// </summary>
public sealed record DiploMods(double Overall, double Naval, double Economy, double Tech, double Pops, double Council = 0,
    double Delegate = 0, double CouncilorSkill = 0)
{
    public static DiploMods Zero { get; } = new(0, 0, 0, 0, 0, 0);
    public bool IsZero => Overall == 0 && Naval == 0 && Economy == 0 && Tech == 0 && Pops == 0 && Council == 0 && Delegate == 0 && CouncilorSkill == 0;
    public DiploMods Scale(double m) => new(Overall * m, Naval * m, Economy * m, Tech * m, Pops * m, Council * m, Delegate * m, CouncilorSkill * m);
    public static DiploMods operator +(DiploMods a, DiploMods b) =>
        new(a.Overall + b.Overall, a.Naval + b.Naval, a.Economy + b.Economy, a.Tech + b.Tech, a.Pops + b.Pops, a.Council + b.Council,
            a.Delegate + b.Delegate, a.CouncilorSkill + b.CouncilorSkill);
}

public enum DiploSource
{
    Tradition, Perk, Civic, Authority, Policy, Edict, Tech, Relic, StaticModifier, Megastructure, Resolution,
    PopFaction, Councilor, SpeciesTrait,
}

/// <summary>
/// A tradition_swap of a tradition or perk: when its trigger holds, its modifier replaces the base definition's.
/// Only is_galactic_emperor and is_galactic_custodian (with AND/OR/NOT/NOR) are evaluated; any other trigger never applies.
/// </summary>
public sealed record TraditionSwap(string? Name, PdxBlock? Trigger, DiploMods Mods)
{
    public bool Applies(bool emperor, bool custodian) => Trigger is not null && Evaluate(Trigger, emperor, custodian) == true;

    /// <summary>All entries of the block must hold. Null when the result depends on an unsupported trigger.</summary>
    public static bool? Evaluate(PdxBlock block, bool emperor, bool custodian)
    {
        bool? result = true;
        foreach (var e in block.Entries)
        {
            var v = Entry(e, emperor, custodian);
            if (v == false) return false;
            if (v is null) result = null;
        }
        return block.Items.Count > 0 ? null : result;
    }

    static bool? Entry(PdxEntry e, bool emperor, bool custodian)
    {
        switch (e.Key.ToLowerInvariant())
        {
            case "is_galactic_emperor": return Flag(e, emperor);
            case "is_galactic_custodian": return Flag(e, custodian);
            case "and": return e.Value is PdxBlock and ? Evaluate(and, emperor, custodian) : null;
            case "not": return e.Value is PdxBlock not ? !Evaluate(not, emperor, custodian) : null;
            case "or":
            case "nor":
                if (e.Value is not PdxBlock or || or.Items.Count > 0) return null;
                bool? any = false;
                foreach (var x in or.Entries)
                {
                    var v = Entry(x, emperor, custodian);
                    if (v == true) { any = true; break; }
                    if (v is null) any = null;
                }
                return e.Key.Equals("nor", StringComparison.OrdinalIgnoreCase) ? !any : any;
            default: return null;
        }
    }

    static bool? Flag(PdxEntry e, bool actual) => e.Op == "=" && e.Value is string s
        ? s.Equals("yes", StringComparison.OrdinalIgnoreCase) ? actual : s.Equals("no", StringComparison.OrdinalIgnoreCase) ? !actual : null
        : null;
}

/// <summary>
/// diplo_weight_* modifiers per definition (tradition, perk, civic, …), read from the game and mods in load order
/// (file name order, later definitions win). Only modifier containers count, never triggers or weights.
/// </summary>
public sealed class ModifierCatalog
{
    sealed record Spec(DiploSource Source, string Folder, string[]? Containers);

    static readonly Spec[] Specs =
    [
        new(DiploSource.StaticModifier, "common/static_modifiers", null),
        new(DiploSource.Tradition, "common/traditions", ["modifier"]),
        new(DiploSource.Perk, "common/ascension_perks", ["modifier"]),
        new(DiploSource.Civic, "common/governments/civics", ["modifier"]),
        new(DiploSource.Authority, "common/governments/authorities", ["country_modifier", "modifier"]),
        new(DiploSource.Policy, "common/policies", null),
        new(DiploSource.Edict, "common/edicts", ["modifier"]),
        new(DiploSource.Tech, "common/technology", ["modifier"]),
        new(DiploSource.Relic, "common/relics", ["passive_modifier", "triggered_country_modifier", "modifier"]),
        new(DiploSource.Megastructure, "common/megastructures", ["country_modifier"]),
        new(DiploSource.Resolution, "common/resolutions", ["modifier"]),
        new(DiploSource.PopFaction, "common/pop_faction_types", ["country_modifier"]),
        new(DiploSource.Councilor, "common/governments/councilors", ["modifier"]),
        new(DiploSource.SpeciesTrait, "common/traits", ["modifier"]),
    ];

    readonly Dictionary<DiploSource, Dictionary<string, DiploMods>> _maps = [];
    readonly Dictionary<DiploSource, Dictionary<string, IReadOnlyList<TraditionSwap>>> _swaps = [];

    readonly Dictionary<string, (string Category, bool MultipleActive)> _resolutionCategories = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _targeted = new(StringComparer.OrdinalIgnoreCase);

    public static ModifierCatalog Empty { get; } = new();

    /// <summary>The category of a resolution type, and whether several resolutions of it can be active at once.</summary>
    public (string Category, bool MultipleActive)? ResolutionCategory(string type) =>
        _resolutionCategories.TryGetValue(type, out var c) ? c : null;

    /// <summary>True for a resolution with target = yes: it applies to a target country, not to every member.</summary>
    public bool IsTargetedResolution(string type) => _targeted.Contains(type);

    /// <summary>The modifiers of a definition, or null when it has none (or doesn't exist).</summary>
    public DiploMods? Get(DiploSource source, string key) =>
        _maps.TryGetValue(source, out var map) && map.TryGetValue(key, out var mods) ? mods : null;

    /// <summary>The tradition_swap blocks of a tradition or perk, in file order (empty when it has none).</summary>
    public IReadOnlyList<TraditionSwap> Swaps(DiploSource source, string key) =>
        _swaps.TryGetValue(source, out var map) && map.TryGetValue(key, out var list) ? list : [];

    /// <summary>
    /// The modifiers of a definition after tradition swaps: the first swap whose trigger holds for the country's galactic
    /// role replaces the base (Swap is that swap), otherwise the base (Swap is null). Mods is null when there are none.
    /// </summary>
    public (DiploMods? Mods, TraditionSwap? Swap) Resolve(DiploSource source, string key, bool emperor, bool custodian)
    {
        foreach (var swap in Swaps(source, key))
            if (swap.Applies(emperor, custodian)) return (swap.Mods.IsZero ? null : swap.Mods, swap);
        return (Get(source, key), null);
    }

    /// <summary>Sources in load order (game first). Unreadable files are skipped.</summary>
    public static ModifierCatalog Load(IReadOnlyList<ContentSource> sources)
    {
        var catalog = new ModifierCatalog();
        var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, rel) in Ordered(sources, "common/scripted_variables"))
            if (TryParse(sources, source, rel, out var vars, out _))
                foreach (var e in vars.Entries.Where(e => e.Key.StartsWith('@') && e.Value is string))
                    globals[e.Key] = (string)e.Value;

        foreach (var (source, rel) in Ordered(sources, "common/resolution_categories"))
        {
            if (!TryParse(sources, source, rel, out var cats, out _)) continue;
            foreach (var e in cats.Entries)
            {
                if (e.Value is not PdxBlock cat) continue;
                var multiple = cat.GetString("multiple_active_resolutions") == "yes";
                foreach (var type in cat.GetBlock("resolution_types")?.StringItems ?? [])
                    catalog._resolutionCategories[type] = (e.Key, multiple);
            }
        }

        foreach (var spec in Specs)
        {
            // Static modifiers are loaded first; they are only referenced from other categories (one level, no recursion).
            var statics = spec.Source == DiploSource.StaticModifier ? null : catalog._maps[DiploSource.StaticModifier];
            var map = new Dictionary<string, DiploMods>(StringComparer.OrdinalIgnoreCase);
            var swapMap = new Dictionary<string, IReadOnlyList<TraditionSwap>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (source, rel) in Ordered(sources, spec.Folder))
            {
                if (!TryParse(sources, source, rel, out var file, out var locals)) continue;
                foreach (var e in file.Entries)
                {
                    if (e.Value is not PdxBlock block || e.Key.StartsWith('@')) continue;
                    if (spec.Source == DiploSource.Resolution)
                    {
                        if (block.GetString("target") == "yes") catalog._targeted.Add(e.Key);
                        else catalog._targeted.Remove(e.Key);
                    }
                    if (spec.Source == DiploSource.Policy)
                    {
                        foreach (var option in block.Entries.Where(o => o.Key == "option").Select(o => o.Value).OfType<PdxBlock>())
                            if (option.GetString("name") is { } name) Set(map, name, Read(option.GetBlock("modifier"), locals, globals, statics));
                    }
                    else if (spec.Containers is null)
                    {
                        Set(map, e.Key, Read(block, locals, globals, statics));
                    }
                    else
                    {
                        var sum = DiploMods.Zero;
                        foreach (var c in spec.Containers) sum += Read(block.GetBlock(c), locals, globals, statics);
                        Set(map, e.Key, sum);
                    }
                    if (spec.Source is DiploSource.Tradition or DiploSource.Perk)
                    {
                        // tradition_swap = { name = … trigger = { … } modifier = { … } } (often from an inline script).
                        var swaps = block.Entries.Where(s => s.Key.Equals("tradition_swap", StringComparison.OrdinalIgnoreCase))
                            .Select(s => s.Value).OfType<PdxBlock>()
                            .Select(s => new TraditionSwap(s.GetString("name"), s.GetBlock("trigger"), Read(s.GetBlock("modifier"), locals, globals, statics)))
                            .ToList();
                        if (swaps.Count > 0) swapMap[e.Key] = swaps;
                        else swapMap.Remove(e.Key);
                    }
                }
            }
            catalog._maps[spec.Source] = map;
            catalog._swaps[spec.Source] = swapMap;
        }
        return catalog;
    }

    // A later definition replaces an earlier one, including when it has no diplomatic weight bonus.
    static void Set(Dictionary<string, DiploMods> map, string key, DiploMods mods)
    {
        if (mods.IsZero) map.Remove(key);
        else map[key] = mods;
    }

    static DiploMods Read(PdxBlock? block, IReadOnlyDictionary<string, string> locals, IReadOnlyDictionary<string, string> globals,
        IReadOnlyDictionary<string, DiploMods>? statics)
    {
        if (block is null) return DiploMods.Zero;
        double overall = 0, naval = 0, economy = 0, tech = 0, pops = 0, council = 0, delegateMult = 0, councilorSkill = 0;
        var referenced = DiploMods.Zero;
        foreach (var e in block.Entries)
        {
            if (e.Value is not string raw || Value(raw, locals, globals, 0) is not { } v) continue;
            switch (e.Key.ToLowerInvariant())
            {
                case "diplo_weight_mult": overall += v; break;
                case "diplo_weight_naval_mult": naval += v; break;
                case "diplo_weight_economy_mult": economy += v; break;
                case "diplo_weight_technology_mult": tech += v; break;
                case "diplo_weight_pops_mult": pops += v; break;
                case "diplo_weight_council_mult": council += v; break;
                case "diplo_weight_delegate_mult": delegateMult += v; break;
                case "councilor_skill_add": councilorSkill += v; break;
                default:
                    if (statics is not null && statics.TryGetValue(e.Key, out var sm)) referenced += sm.Scale(v);
                    break;
            }
        }
        return new DiploMods(overall, naval, economy, tech, pops, council, delegateMult, councilorSkill) + referenced;
    }

    const int MaxVariableDepth = 5;

    // A number, an @variable (file-local first, then scripted_variables) or an @[ expression ]; null when unknown.
    static double? Value(string raw, IReadOnlyDictionary<string, string> locals, IReadOnlyDictionary<string, string> globals, int depth)
    {
        if (depth > MaxVariableDepth) return null;
        if (raw.StartsWith("@[", StringComparison.Ordinal) && raw.EndsWith(']'))
            return Evaluate(raw[2..^1], name => Value(name, locals, globals, depth + 1)?.ToString("R", CultureInfo.InvariantCulture));
        if (raw.StartsWith('@'))
            return (locals.GetValueOrDefault(raw) ?? globals.GetValueOrDefault(raw)) is { } found ? Value(found, locals, globals, depth + 1) : null;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>
    /// Evaluates a script expression (the inside of @[ … ]): numbers, variable names (with or without @), + - * /, unary
    /// minus and parentheses. <paramref name="variable"/> is asked for "@name" and returns its value as text. Null when the
    /// expression is malformed, divides by zero or names an unknown variable.
    /// </summary>
    public static double? Evaluate(string expression, Func<string, string?> variable)
    {
        var tokens = ExpressionToken.Matches(expression).Select(m => m.Value).ToList();
        if (tokens.Count == 0 || string.Concat(tokens).Length != expression.Count(c => !char.IsWhiteSpace(c))) return null;
        var pos = 0;
        var result = Sum();
        return pos == tokens.Count && result is { } r && double.IsFinite(r) ? r : null;

        double? Sum()
        {
            var left = Product();
            while (left is not null && pos < tokens.Count && tokens[pos] is "+" or "-")
            {
                var op = tokens[pos++];
                var right = Product();
                left = right is null ? null : op == "+" ? left + right : left - right;
            }
            return left;
        }

        double? Product()
        {
            var left = Unary();
            while (left is not null && pos < tokens.Count && tokens[pos] is "*" or "/")
            {
                var op = tokens[pos++];
                var right = Unary();
                if (right is null || (op == "/" && right == 0)) return null;
                left = op == "*" ? left * right : left / right;
            }
            return left;
        }

        double? Unary()
        {
            if (pos >= tokens.Count) return null;
            var t = tokens[pos++];
            if (t == "-") return -Unary();
            if (t == "+") return Unary();
            if (t == "(")
            {
                var inner = Sum();
                if (pos >= tokens.Count || tokens[pos] != ")") return null;
                pos++;
                return inner;
            }
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return n;
            if (char.IsLetter(t[0]) || t[0] is '_' or '@')
                return variable("@" + t.TrimStart('@')) is { } text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
            return null;
        }
    }

    static readonly Regex ExpressionToken = new(@"[0-9]+(?:\.[0-9]+)?|\.[0-9]+|@?[A-Za-z_][A-Za-z0-9_]*|[-+*/()]", RegexOptions.Compiled);

    // The parser splits "@[ a * b ]" into separate words; quote each expression so it stays one value.
    static readonly Regex InlineExpression = new(@"@\[([^\[\]""{}]*)\]", RegexOptions.Compiled);

    static IEnumerable<(ContentSource Source, string Rel)> Ordered(IReadOnlyList<ContentSource> sources, string folder) =>
        sources.SelectMany((s, i) => s.Files(folder, ".txt").Select(rel => (Source: s, Index: i, Rel: rel)))
            .OrderBy(f => Path.GetFileName(f.Rel), StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Index)
            .Select(f => (f.Source, f.Rel));

    const int MaxInlineDepth = 5;

    static readonly Regex InlineBlock = new(@"inline_script\s*=\s*\{([^{}]*)\}", RegexOptions.Compiled);
    static readonly Regex InlineName = new(@"inline_script\s*=\s*(?:""([^""]*)""|([^\s{}""=]+))", RegexOptions.Compiled);
    static readonly Regex Param = new(@"([A-Za-z0-9_]+)\s*=\s*(?:""([^""]*)""|([^\s{}""=]+))", RegexOptions.Compiled);

    // Textual expansion: each inline_script (name or { script = name PARAM = value }) is replaced by the script file's text
    // with $PARAM$ substituted. The last source that has the script wins. Too deep or missing scripts become empty.
    static string ExpandInlineScripts(string text, IReadOnlyList<ContentSource> sources, int depth)
    {
        if (!text.Contains("inline_script", StringComparison.Ordinal)) return text;
        text = InlineBlock.Replace(text, m =>
        {
            string? script = null;
            var args = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match p in Param.Matches(m.Groups[1].Value))
            {
                var value = p.Groups[2].Success ? p.Groups[2].Value : p.Groups[3].Value;
                if (p.Groups[1].Value == "script") script = value;
                else args[p.Groups[1].Value] = value;
            }
            return Load(script, args);
        });
        return InlineName.Replace(text, m => Load(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, null));

        string Load(string? script, Dictionary<string, string>? args)
        {
            if (script is null || depth >= MaxInlineDepth) return "";
            var rel = "common/inline_scripts/" + script.Trim().TrimStart('/') + ".txt";
            string? body = null;
            for (var i = sources.Count - 1; i >= 0 && body is null; i--)
            {
                try
                {
                    if (sources[i].Exists(rel)) body = sources[i].ReadText(rel);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
            }
            if (body is null) return "";
            if (args is not null)
                foreach (var (k, v) in args) body = body.Replace("$" + k + "$", v, StringComparison.Ordinal);
            return ExpandInlineScripts(body, sources, depth + 1);
        }
    }

    static bool TryParse(IReadOnlyList<ContentSource> sources, ContentSource source, string rel, out PdxBlock block, out Dictionary<string, string> locals)
    {
        locals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var text = ExpandInlineScripts(source.ReadText(rel), sources, 0);
            if (text.Contains("@[", StringComparison.Ordinal))
                text = InlineExpression.Replace(text, m => '"' + "@[" + m.Groups[1].Value.Trim() + "]" + '"');
            block = ParadoxScriptParser.Parse(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            block = new PdxBlock();
            return false;
        }
        foreach (var e in block.Entries.Where(e => e.Key.StartsWith('@') && e.Value is string)) locals[e.Key] = (string)e.Value;
        return true;
    }
}

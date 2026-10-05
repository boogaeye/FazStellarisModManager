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

    // The parser splits "@[ a * b ]" into separate words; quote each expression (outside comments and strings) so it stays
    // one value.
    static string QuoteExpressions(string text)
    {
        if (!text.Contains("@[", StringComparison.Ordinal)) return text;
        var sb = new System.Text.StringBuilder(text.Length + 16);
        int i = 0, copied = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '#') { i = LineEnd(text, i); continue; }
            if (c == '"') { i = StringEnd(text, i); continue; }
            if (c == '@' && i + 1 < text.Length && text[i + 1] == '[')
            {
                var end = text.IndexOf(']', i);
                var newline = text.IndexOf((char)10, i);
                if (end < 0 || (newline >= 0 && newline < end)) { i++; continue; }
                sb.Append(text, copied, i - copied).Append('"').Append("@[").Append(text[(i + 2)..end].Trim()).Append(']').Append('"');
                i = copied = end + 1;
                continue;
            }
            i++;
        }
        return copied == 0 ? text : sb.Append(text, copied, text.Length - copied).ToString();
    }

    static IEnumerable<(ContentSource Source, string Rel)> Ordered(IReadOnlyList<ContentSource> sources, string folder) =>
        sources.SelectMany((s, i) => s.Files(folder, ".txt").Select(rel => (Source: s, Index: i, Rel: rel)))
            .OrderBy(f => Path.GetFileName(f.Rel), StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Index)
            .Select(f => (f.Source, f.Rel));

    const int MaxInlineDepth = 5;

    const string InlineKeyword = "inline_script";
    const char Backslash = (char)92;

    // Textual expansion: each inline_script (name or { script = name PARAM = value }) is replaced by the script file's text
    // with $PARAM$ substituted. The last source that has the script wins. Too deep or missing scripts become empty, and so
    // does a script whose expansion has unbalanced braces or quotes (e.g. a script name computed from an @[ ] expression),
    // so that it cannot swallow the definitions after it. Calls in comments and quoted strings are left alone.
    static string ExpandInlineScripts(string text, IReadOnlyList<ContentSource> sources, int depth)
    {
        if (!text.Contains(InlineKeyword, StringComparison.Ordinal)) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        int i = 0, copied = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '#') { i = LineEnd(text, i); continue; }
            if (c == '"') { i = StringEnd(text, i); continue; }
            if (c == 'i' && string.CompareOrdinal(text, i, InlineKeyword, 0, InlineKeyword.Length) == 0
                && (i == 0 || !IsWordChar(text[i - 1])) && !(i + InlineKeyword.Length < text.Length && IsWordChar(text[i + InlineKeyword.Length])))
            {
                var j = SkipSpace(text, i + InlineKeyword.Length);
                if (j < text.Length && text[j] == '=')
                {
                    j = SkipSpace(text, j + 1);
                    string? replacement = null;
                    int end;
                    if (j < text.Length && text[j] == '{')
                    {
                        end = BlockEnd(text, j);
                        if (end < 0) break; // unterminated: leave the rest as written
                        var args = ReadArgs(text[(j + 1)..end]);
                        args.Remove("script", out var script);
                        replacement = Load(script, args);
                        end++;
                    }
                    else
                    {
                        var (name, next) = ReadValue(text, j);
                        end = next;
                        if (name is not null) replacement = Load(name, null);
                    }
                    if (replacement is not null)
                    {
                        sb.Append(text, copied, i - copied).Append(replacement);
                        i = copied = end;
                        continue;
                    }
                }
            }
            i++;
        }
        return copied == 0 ? text : sb.Append(text, copied, text.Length - copied).ToString();

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
            var expanded = ExpandInlineScripts(body, sources, depth + 1);
            return Balanced(expanded) ? expanded : "";
        }
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    static int SkipSpace(string s, int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        return i;
    }

    static int LineEnd(string s, int i)
    {
        while (i < s.Length && s[i] != (char)10) i++;
        return i;
    }

    // Index just past the closing quote of the string starting at i (a backslash escapes the next character), or the text
    // length when the string is not closed.
    static int StringEnd(string s, int i) => ClosedStringEnd(s, i) ?? s.Length;

    static int? ClosedStringEnd(string s, int i)
    {
        for (i++; i < s.Length; i++)
        {
            if (s[i] == Backslash) i++;
            else if (s[i] == '"') return i + 1;
        }
        return null;
    }

    // Index of the '}' matching the '{' at i (comments and quoted strings skipped), or -1.
    static int BlockEnd(string s, int i)
    {
        var depth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '#') { i = LineEnd(s, i); continue; }
            if (c == '"') { i = StringEnd(s, i); continue; }
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return i;
            i++;
        }
        return -1;
    }

    static bool Balanced(string s)
    {
        var depth = 0;
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '#') { i = LineEnd(s, i); continue; }
            if (c == '"')
            {
                if (ClosedStringEnd(s, i) is not { } end) return false;
                i = end;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}' && --depth < 0) return false;
            i++;
        }
        return depth == 0;
    }

    // "KEY = value" pairs of an inline_script block: quoted values (with \" unescaped), @[ expressions ], { blocks } (inner
    // text) or bare words. Tokens not followed by '=' are skipped.
    static Dictionary<string, string> ReadArgs(string s)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = 0;
        while (i < s.Length)
        {
            i = SkipSpace(s, i);
            if (i >= s.Length) break;
            if (s[i] == '#') { i = LineEnd(s, i); continue; }
            var start = i;
            while (i < s.Length && IsWordChar(s[i])) i++;
            if (i == start)
            {
                i = s[start] == '"' ? StringEnd(s, start) : s[start] == '{' && BlockEnd(s, start) is var e and >= 0 ? e + 1 : start + 1;
                continue;
            }
            var key = s[start..i];
            var j = SkipSpace(s, i);
            if (j >= s.Length || s[j] != '=') continue;
            var (value, next) = ReadValue(s, SkipSpace(s, j + 1));
            if (value is not null) args[key] = value;
            i = next;
        }
        return args;
    }

    static (string? Value, int Next) ReadValue(string s, int i)
    {
        if (i >= s.Length) return (null, i);
        if (s[i] == '"')
        {
            var end = StringEnd(s, i);
            var inner = s[(i + 1)..Math.Max(i + 1, end - 1)];
            return (inner.Replace(Backslash.ToString() + '"', '"'.ToString(), StringComparison.Ordinal), end);
        }
        if (s[i] == '{')
        {
            var end = BlockEnd(s, i);
            return end < 0 ? (null, s.Length) : (s[(i + 1)..end], end + 1);
        }
        if (s[i] == '@' && i + 1 < s.Length && s[i + 1] == '[')
        {
            var end = s.IndexOf(']', i);
            return end < 0 ? (null, s.Length) : (s[i..(end + 1)], end + 1);
        }
        var start = i;
        while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not ('{' or '}' or '=' or '"' or '#')) i++;
        return i == start ? (null, i + 1) : (s[start..i], i);
    }

    static bool TryParse(IReadOnlyList<ContentSource> sources, ContentSource source, string rel, out PdxBlock block, out Dictionary<string, string> locals)
    {
        locals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            block = ParadoxScriptParser.Parse(QuoteExpressions(ExpandInlineScripts(source.ReadText(rel), sources, 0)));
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

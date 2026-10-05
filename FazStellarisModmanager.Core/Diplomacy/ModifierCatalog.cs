using System.Globalization;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Diplomacy;

/// <summary>Diplomatic weight modifiers from one source, as fractions (0.25 = +25%).</summary>
public sealed record DiploMods(double Overall, double Naval, double Economy, double Tech, double Pops, double Council = 0)
{
    public static DiploMods Zero { get; } = new(0, 0, 0, 0, 0, 0);
    public bool IsZero => Overall == 0 && Naval == 0 && Economy == 0 && Tech == 0 && Pops == 0 && Council == 0;
    public DiploMods Scale(double m) => new(Overall * m, Naval * m, Economy * m, Tech * m, Pops * m, Council * m);
    public static DiploMods operator +(DiploMods a, DiploMods b) =>
        new(a.Overall + b.Overall, a.Naval + b.Naval, a.Economy + b.Economy, a.Tech + b.Tech, a.Pops + b.Pops, a.Council + b.Council);
}

public enum DiploSource { Tradition, Perk, Civic, Authority, Policy, Edict, Tech, Relic, StaticModifier, Megastructure, Resolution }

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
    ];

    readonly Dictionary<DiploSource, Dictionary<string, DiploMods>> _maps = [];

    public static ModifierCatalog Empty { get; } = new();

    /// <summary>The modifiers of a definition, or null when it has none (or doesn't exist).</summary>
    public DiploMods? Get(DiploSource source, string key) =>
        _maps.TryGetValue(source, out var map) && map.TryGetValue(key, out var mods) ? mods : null;

    /// <summary>Sources in load order (game first). Unreadable files are skipped.</summary>
    public static ModifierCatalog Load(IReadOnlyList<ContentSource> sources)
    {
        var catalog = new ModifierCatalog();
        var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, rel) in Ordered(sources, "common/scripted_variables"))
            if (TryParse(sources, source, rel, out var vars, out _))
                foreach (var e in vars.Entries.Where(e => e.Key.StartsWith('@') && e.Value is string))
                    globals[e.Key] = (string)e.Value;

        foreach (var spec in Specs)
        {
            // Static modifiers are loaded first; they are only referenced from other categories (one level, no recursion).
            var statics = spec.Source == DiploSource.StaticModifier ? null : catalog._maps[DiploSource.StaticModifier];
            var map = new Dictionary<string, DiploMods>(StringComparer.OrdinalIgnoreCase);
            foreach (var (source, rel) in Ordered(sources, spec.Folder))
            {
                if (!TryParse(sources, source, rel, out var file, out var locals)) continue;
                foreach (var e in file.Entries)
                {
                    if (e.Value is not PdxBlock block || e.Key.StartsWith('@')) continue;
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
                }
            }
            catalog._maps[spec.Source] = map;
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
        double overall = 0, naval = 0, economy = 0, tech = 0, pops = 0, council = 0;
        var referenced = DiploMods.Zero;
        foreach (var e in block.Entries)
        {
            if (e.Value is not string raw) continue;
            var value = raw.StartsWith('@') ? locals.GetValueOrDefault(raw) ?? globals.GetValueOrDefault(raw) : raw;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
            switch (e.Key.ToLowerInvariant())
            {
                case "diplo_weight_mult": overall += v; break;
                case "diplo_weight_naval_mult": naval += v; break;
                case "diplo_weight_economy_mult": economy += v; break;
                case "diplo_weight_technology_mult": tech += v; break;
                case "diplo_weight_pops_mult": pops += v; break;
                case "diplo_weight_council_mult": council += v; break;
                default:
                    if (statics is not null && statics.TryGetValue(e.Key, out var sm)) referenced += sm.Scale(v);
                    break;
            }
        }
        return new DiploMods(overall, naval, economy, tech, pops, council) + referenced;
    }

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
            block = ParadoxScriptParser.Parse(ExpandInlineScripts(source.ReadText(rel), sources, 0));
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

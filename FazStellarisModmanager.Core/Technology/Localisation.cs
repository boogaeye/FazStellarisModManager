using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>One possible text of a key whose scripted function ([GetX]) depends on the empire, e.g. "Physicists" vs "Calculators".</summary>
public sealed record LocVariant(string Text, string? IconTag, string Condition, string? ConditionScript, bool IsDefault);

/// <summary>English localisation merged from the base game and mods (later wins; "replace" folders win over everything).</summary>
public sealed class Localisation
{
    static readonly Regex Line = new(@"^\s*([\w.\-]+):\d*\s*""(.*)""[^""]*$", RegexOptions.Compiled);
    static readonly Regex ColorCode = new(@"§.", RegexOptions.Compiled);
    static readonly Regex IconTag = new(@"£[^£]*£", RegexOptions.Compiled);
    static readonly Regex IconName = new(@"£([^£]+)£", RegexOptions.Compiled);
    static readonly Regex Reference = new(@"\$([^$\s]+)\$", RegexOptions.Compiled);
    static readonly Regex Command = new(@"\[([^\[\]]+)\]", RegexOptions.Compiled);
    // The game pairs a non-breaking space (0xA0) with a normal one after inline icons; runs of either collapse to one space.
    static readonly Regex Spaces = new("[ " + (char)9 + (char)0xA0 + "]{2,}", RegexOptions.Compiled);

    const int MaxDepth = 4;

    readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _map.Count;

    /// <summary>Scripted localisation used for [GetX] commands; empty until set.</summary>
    public ScriptedLoc Scripted { get; set; } = ScriptedLoc.Empty;

    public static Localisation Load(IReadOnlyList<ContentSource> sources, ICollection<string> warnings)
    {
        var loc = new Localisation();
        var replace = new List<(ContentSource Source, string Rel)>();
        foreach (var source in sources)
            foreach (var rel in source.Files("localisation", "_l_english.yml"))
            {
                if (rel.Split('/').Any(part => part.Equals("replace", StringComparison.OrdinalIgnoreCase))) replace.Add((source, rel));
                else loc.AddFile(source, rel, warnings);
            }
        foreach (var (source, rel) in replace) loc.AddFile(source, rel, warnings);
        return loc;
    }

    void AddFile(ContentSource source, string rel, ICollection<string> warnings)
    {
        try { AddText(source.ReadText(rel)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
        {
            warnings.Add($"{source.Name}: {rel}: {ex.Message}");
        }
    }

    public void AddText(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } raw)
        {
            var trimmed = raw.AsSpan().TrimStart();
            if (trimmed.IsEmpty || trimmed[0] == '#') continue;
            var m = Line.Match(raw);
            if (m.Success) _map[m.Groups[1].Value] = m.Groups[2].Value;
        }
    }

    /// <summary>
    /// Cleaned text for a key, or null: colour codes and icons removed, $refs$ resolved, [GetX] scripted functions shown with their
    /// default text, [job.GetName]/[job.GetNamePlural] as the job's name and [job.GetIcon] dropped. Other [commands] stay as written.
    /// </summary>
    public string? Get(string key) => _map.TryGetValue(key, out var value) ? Clean(value, 0) : null;

    /// <summary>Name of the first inline icon of a key's text (a "£name£" pair, or "job_x" for [x.GetIcon]), following $refs$ and [GetX] defaults, or null.</summary>
    public string? FirstIconTag(string key) => _map.TryGetValue(key, out var value) ? FirstIcon(value, 0) : null;

    /// <summary>
    /// The texts a key can show when its first multi-branch scripted function ([GetX]) depends on the empire: one per distinct branch,
    /// in file order, with a readable condition. Empty when the key has no such function.
    /// </summary>
    public IReadOnlyList<LocVariant> Variants(string key)
    {
        if (!_map.TryGetValue(key, out var raw)) return [];
        var text = ExpandReferences(raw, 0);
        foreach (Match m in Command.Matches(text))
        {
            if (Scripted.Find(m.Groups[1].Value.Trim()) is not { } fn) continue;
            var groups = new List<(string Key, List<PdxBlock?> Triggers)>();
            foreach (var branch in fn.Branches)
            {
                var i = groups.FindIndex(g => g.Key.Equals(branch.LocKey, StringComparison.OrdinalIgnoreCase));
                if (i < 0) groups.Add((branch.LocKey, [branch.Trigger]));
                else groups[i].Triggers.Add(branch.Trigger);
            }
            var fallback = fn.DefaultKey;
            if (fallback is not null && !groups.Exists(g => g.Key.Equals(fallback, StringComparison.OrdinalIgnoreCase)))
                groups.Add((fallback, []));
            if (groups.Count < 2) continue;

            return groups.Select(g =>
            {
                var substituted = text[..m.Index] + (_map.TryGetValue(g.Key, out var v) ? v : g.Key) + text[(m.Index + m.Length)..];
                var condition = g.Triggers.Count == 0
                    ? "otherwise"
                    : string.Join(" or ", g.Triggers.Select(t => TriggerSummary.Describe(t, Get)).Distinct());
                var scripts = g.Triggers.OfType<PdxBlock>().Select(t => PdxScriptPrinter.Print(t)).Where(s => s.Length > 0).ToList();
                return new LocVariant(
                    Clean(substituted, 1),
                    FirstIcon(substituted, 1),
                    condition,
                    scripts.Count == 0 ? null : string.Join("\n# or\n", scripts),
                    fallback is not null && g.Key.Equals(fallback, StringComparison.OrdinalIgnoreCase));
            }).ToList();
        }
        return [];
    }

    string? FirstIcon(string text, int depth)
    {
        var m = IconName.Match(ExpandForIcons(text, depth));
        return m.Success ? m.Groups[1].Value : null;
    }

    string ExpandReferences(string text, int depth) => Reference.Replace(text, m =>
    {
        var key = m.Groups[1].Value.Split('|')[0];
        return depth < MaxDepth && _map.TryGetValue(key, out var v) ? ExpandReferences(v, depth + 1) : key;
    });

    // Raw text with $refs$ and [GetX] defaults expanded, and [job.GetIcon] turned into a £job_x£ tag.
    string ExpandForIcons(string text, int depth)
    {
        text = Reference.Replace(text, m =>
        {
            var key = m.Groups[1].Value.Split('|')[0];
            return depth < MaxDepth && _map.TryGetValue(key, out var v) ? ExpandForIcons(v, depth + 1) : key;
        });
        return Command.Replace(text, m =>
        {
            var command = m.Groups[1].Value.Trim();
            if (FunctionText(command, depth) is { } raw) return ExpandForIcons(raw, depth + 1);
            return ScopeCall(command) is ({ } scope, "geticon") ? $"£job_{scope}£" : m.Value;
        });
    }

    string Clean(string text, int depth)
    {
        text = ColorCode.Replace(text, "");
        text = IconTag.Replace(text, "");
        text = Reference.Replace(text, m =>
        {
            var key = m.Groups[1].Value.Split('|')[0];
            return depth < MaxDepth && _map.TryGetValue(key, out var v) ? Clean(v, depth + 1) : key;
        });
        text = Command.Replace(text, m =>
        {
            var command = m.Groups[1].Value.Trim();
            if (FunctionText(command, depth) is { } raw) return Clean(raw, depth + 1);
            return ScopeCall(command) switch
            {
                ({ }, "geticon") => "",
                ({ } scope, "getnameplural") => JobName(scope, plural: true, depth),
                ({ } scope, "getname") => JobName(scope, plural: false, depth),
                _ => m.Value,
            };
        });
        text = text.Replace("\\n", "\n").Replace("\\\"", "\"");
        return Spaces.Replace(text, " ").Trim();
    }

    // Raw text of a scripted function's default branch, or null when the command is not a known function.
    string? FunctionText(string command, int depth) =>
        depth < MaxDepth && !command.Contains('.') && Scripted.Find(command)?.DefaultKey is { } key && _map.TryGetValue(key, out var v) ? v : null;

    // "physicist.GetNamePlural" -> ("physicist", "getnameplural") when "physicist" is a job (job_physicist exists);
    // other scopes ([Root.GetName], [fleet.GetName], …) name the reader's own objects and stay as written.
    (string? Scope, string Function) ScopeCall(string command)
    {
        var parts = command.Split('.');
        if (parts.Length != 2 || parts[0].Length == 0) return (null, "");
        var isJob = _map.ContainsKey($"job_{parts[0]}") || _map.ContainsKey($"job_{parts[0]}_plural");
        return isJob ? (parts[0], parts[1].ToLowerInvariant()) : (null, "");
    }

    string JobName(string scope, bool plural, int depth)
    {
        string? Lookup(string key) => depth < MaxDepth && _map.TryGetValue(key, out var v) ? Clean(v, depth + 1) : null;
        return plural
            ? Lookup($"job_{scope}_plural") ?? Lookup($"{scope}_plural") ?? Lookup($"job_{scope}") ?? scope
            : Lookup($"job_{scope}") ?? Lookup(scope) ?? scope;
    }
}

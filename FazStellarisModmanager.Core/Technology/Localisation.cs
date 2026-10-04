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

    /// <summary>Job swaps (common/pop_jobs) used for variants of texts that name a job; empty until set.</summary>
    public JobSwaps Jobs { get; set; } = JobSwaps.Empty;

    /// <summary>Scripted trigger bodies (common/scripted_triggers) used to word variant conditions; empty until set.</summary>
    public IReadOnlyDictionary<string, PdxBlock> ScriptedTriggers { get; set; } = new Dictionary<string, PdxBlock>();

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
    /// The texts a key can show depending on the empire, in file order, each with a readable condition and the trigger script:
    /// first one per distinct branch of its first multi-branch scripted function ([GetX]), then, within each, one per job swap
    /// (common/pop_jobs swap_type) of the job it names, e.g. Bureaucrats / Priests / Managers. Identical texts are merged.
    /// Empty when the key has fewer than two distinct texts.
    /// </summary>
    public IReadOnlyList<LocVariant> Variants(string key)
    {
        if (!_map.TryGetValue(key, out var raw)) return [];
        var variants = new List<LocVariant>();
        foreach (var branch in FunctionBranches(ExpandReferences(raw, 0)))
            foreach (var v in JobSwapBranches(branch))
            {
                var final = new LocVariant(Clean(v.Text, 1), FirstIcon(v.Text, 1), v.Condition, v.Script, v.IsDefault);
                var same = variants.FindIndex(x => x.Text == final.Text);
                if (same < 0) { variants.Add(final); continue; }
                var old = variants[same];
                variants[same] = old with
                {
                    Condition = old.Condition == final.Condition ? old.Condition : $"{old.Condition} or {final.Condition}",
                    ConditionScript = JoinScripts(old.ConditionScript, final.ConditionScript, OrSeparator),
                    IsDefault = old.IsDefault || final.IsDefault,
                };
            }
        return variants.Count < 2 ? [] : variants;
    }

    // Raw text of one variant before cleaning.
    sealed record RawVariant(string Text, string Condition, string? Script, bool IsDefault);

    static readonly string OrSeparator = (char)10 + "# or" + (char)10;
    static readonly string AndSeparator = (char)10 + "# and" + (char)10;

    // Shown when the empire is unknown and nothing else applies.
    const string Always = "always";
    const string Otherwise = "otherwise";

    static string? JoinScripts(string? a, string? b, string separator) =>
        a is null ? b : b is null ? a : a + separator + b;

    string Describe(PdxBlock? trigger) =>
        TriggerSummary.Describe(trigger, Get, n => ScriptedTriggers.TryGetValue(n, out var body) ? body : null);

    static string Print(PdxBlock? trigger) => trigger is null ? "" : PdxScriptPrinter.Print(trigger);

    // One raw variant per distinct branch of the first multi-branch [GetX] in the text, or the text itself.
    IEnumerable<RawVariant> FunctionBranches(string text)
    {
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
                var condition = g.Triggers.Count == 0 ? Otherwise : string.Join(" or ", g.Triggers.Select(Describe).Distinct());
                var scripts = g.Triggers.Select(Print).Where(s => s.Length > 0).ToList();
                return new RawVariant(substituted, condition, scripts.Count == 0 ? null : string.Join(OrSeparator, scripts),
                    fallback is not null && g.Key.Equals(fallback, StringComparison.OrdinalIgnoreCase));
            }).ToList();
        }
        return [new RawVariant(text, Always, null, true)];
    }

    // The variant as written (the job's own name) plus one per swap_type of the first job it names that has swaps.
    IEnumerable<RawVariant> JobSwapBranches(RawVariant v)
    {
        var text = ExpandFunctions(v.Text, 1);
        var job = Command.Matches(text)
            .Select(m => ScopeCall(m.Groups[1].Value.Trim()).Scope)
            .FirstOrDefault(s => s is not null && Jobs.For(s).Count > 0);
        if (job is null)
        {
            yield return v;
            yield break;
        }

        var unconditional = v.Condition is Always or Otherwise;
        yield return v with { Text = text, Condition = unconditional ? Otherwise : $"{v.Condition}, {Otherwise}" };
        foreach (var swap in Jobs.For(job))
        {
            var swapped = Command.Replace(text, m =>
            {
                var (scope, function) = ScopeCall(m.Groups[1].Value.Trim());
                if (!string.Equals(scope, job, StringComparison.OrdinalIgnoreCase)) return m.Value;
                return function switch
                {
                    "geticon" => $"£job_{swap.Icon}£",
                    "getnameplural" => JobName(swap.Name, plural: true, 1),
                    "getname" => JobName(swap.Name, plural: false, 1),
                    _ => m.Value,
                };
            });
            var condition = Describe(swap.Trigger);
            yield return new RawVariant(swapped,
                unconditional ? condition : $"{v.Condition}, {condition}",
                JoinScripts(v.Script, Print(swap.Trigger) is { Length: > 0 } s ? s : null, AndSeparator),
                false);
        }
    }

    // $refs$ and [GetX] defaults expanded; other commands kept.
    string ExpandFunctions(string text, int depth)
    {
        text = ExpandReferences(text, depth);
        return Command.Replace(text, m => FunctionText(m.Groups[1].Value.Trim(), depth) is { } raw ? ExpandFunctions(raw, depth + 1) : m.Value);
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

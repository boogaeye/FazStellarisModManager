using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>Tech -> sources that grant it (events first, then other sources, each by name), plus the events among them.</summary>
public sealed class GrantIndex
{
    readonly IReadOnlyDictionary<string, IReadOnlyList<GrantSource>> _byTech;
    readonly IReadOnlyDictionary<string, GameEvent> _events;

    public GrantIndex(IReadOnlyDictionary<string, IReadOnlyList<GrantSource>> byTech, IReadOnlyDictionary<string, GameEvent> events)
    {
        _byTech = byTech;
        _events = events;
    }

    public static GrantIndex Empty { get; } = new(new Dictionary<string, IReadOnlyList<GrantSource>>(), new Dictionary<string, GameEvent>());

    public IReadOnlyList<GrantSource> For(string tech) => _byTech.TryGetValue(tech, out var list) ? list : [];

    public GameEvent? Event(string id) => _events.TryGetValue(id, out var e) ? e : null;

    public int EventCount => _events.Count;
}

/// <summary>
/// Scans events/ (first definition of an event id wins) and common/ objects (last (folder, id) wins) for tech grants.
/// Files at the same path in a later source replace earlier ones; files are read in load order. Base-game files are only
/// parsed when they mention a grant effect, inline_script or a scripted effect that (transitively) mentions one.
/// </summary>
public static class GrantScanner
{
    static readonly HashSet<string> ExcludedCommon = new(StringComparer.OrdinalIgnoreCase)
    {
        "technology", "scripted_effects", "inline_scripts", "scripted_triggers", "scripted_variables", "script_values",
        "defines", "pop_jobs", "random_names", "name_lists", "on_actions",
    };

    /// <param name="techKey">Maps a tech key to the database's spelling, or null when there is no such tech.</param>
    public static GrantIndex Scan(IReadOnlyList<ContentSource> sources, Localisation loc, ScriptLibrary library,
        Func<string, string?> techKey, ICollection<string> warnings, CancellationToken ct = default)
    {
        string Describe(PdxBlock? b) => TriggerSummary.Describe(b, loc.Get, n => loc.ScriptedTriggers.TryGetValue(n, out var t) ? t : null);
        var finder = new GrantFinder(library, Describe);
        var prefilter = Prefilter(library);

        var byTech = new Dictionary<string, List<GrantSource>>(StringComparer.OrdinalIgnoreCase);
        void AddSource(PendingSource source)
        {
            foreach (var tech in source.Grants.Select(g => g.Tech).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!byTech.TryGetValue(tech, out var list)) byTech[tech] = list = [];
                list.Add(new GrantSource(source.Kind, source.KindFolder, source.Id, source.Name, source.Source,
                    source.Grants.Where(g => g.Tech.Equals(tech, StringComparison.OrdinalIgnoreCase)).Select(g => g.Grant).ToList()));
            }
        }

        // Events: first definition per id wins.
        var events = new Dictionary<string, GameEvent>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ParseAll(sources, "events", f => true, prefilter, warnings, ct))
            foreach (var e in file.Root.Entries)
            {
                if (e.Value is not PdxBlock block || !IsEventKey(e.Key) || block.GetString("id") is not { } id || !seen.Add(id)) continue;
                var analysed = AnalyseEvent(e.Key, id, block, file.Src, loc, library, finder, Describe, techKey);
                if (analysed is null) continue;
                events[id] = analysed.Value.Event;
                AddSource(new PendingSource("Event", GrantSource.EventsFolder, id, analysed.Value.Event.Title, file.Src, analysed.Value.Grants));
            }

        // common/ objects: last (folder, id) wins.
        var objects = new Dictionary<string, PendingSource?>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ParseAll(sources, "common", rel => rel.Split('/') is { Length: >= 3 } p && !ExcludedCommon.Contains(p[1]), prefilter, warnings, ct))
        {
            var folder = file.Src.File.Split('/')[1];
            foreach (var e in file.Root.Entries)
            {
                if (e.Value is not PdxBlock block || e.Key.StartsWith('@')) continue;
                var id = block.GetString("key") ?? e.Key;
                var grants = Place(finder.Find(block), EventPart.Immediate, null, techKey);
                objects[folder + "|" + id] = grants.Count == 0 ? null
                    : new PendingSource(UnlockScanner.KindName(folder), folder, id, loc.Get(id) ?? id, file.Src, grants);
            }
        }
        foreach (var o in objects.Values)
            if (o is not null) AddSource(o);

        return new GrantIndex(
            byTech.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<GrantSource>)kv.Value
                .OrderBy(s => s.IsEvent ? 0 : 1)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .ToList(), StringComparer.OrdinalIgnoreCase),
            events);
    }

    sealed record TechGrantFor(string Tech, TechGrant Grant);

    sealed record PendingSource(string Kind, string KindFolder, string Id, string Name, TechSourceRef Source, List<TechGrantFor> Grants);

    sealed record ParsedFile(TechSourceRef Src, PdxBlock Root);

    static bool IsEventKey(string key) => key.Equals("event", StringComparison.OrdinalIgnoreCase) || key.EndsWith("_event", StringComparison.OrdinalIgnoreCase);

    static List<TechGrantFor> Place(List<FoundGrant> found, EventPart part, int? option, Func<string, string?> techKey) =>
        found.Select(f => techKey(f.Tech) is { } tech ? new TechGrantFor(tech, new TechGrant(f.Kind, f.Progress, part, option, f.Condition, f.Via)) : null)
            .OfType<TechGrantFor>()
            .ToList();

    static (GameEvent Event, List<TechGrantFor> Grants)? AnalyseEvent(string type, string id, PdxBlock block, TechSourceRef src,
        Localisation loc, ScriptLibrary library, GrantFinder finder, Func<PdxBlock?, string> describe, Func<string, string?> techKey)
    {
        block = ExpandInline(block, library, 0);
        var options = block.Entries.Where(e => e.Key.Equals("option", StringComparison.OrdinalIgnoreCase) && e.Value is PdxBlock)
            .Select(e => (PdxBlock)e.Value).ToList();
        var grants = new List<TechGrantFor>();
        if (block.GetBlock("immediate") is { } immediate) grants.AddRange(Place(finder.Find(immediate), EventPart.Immediate, null, techKey));
        for (var i = 0; i < options.Count; i++) grants.AddRange(Place(finder.Find(options[i]), EventPart.Option, i, techKey));
        if (block.GetBlock("after") is { } after) grants.AddRange(Place(finder.Find(after), EventPart.After, null, techKey));
        if (grants.Count == 0) return null;

        var titles = Entries(block, "title");
        var descs = Entries(block, "desc");
        var pictures = Entries(block, "picture");
        var ev = new GameEvent(
            id,
            type,
            titles.Count > 0 ? FirstText(titles[0].Value, loc) ?? id : id,
            descs.Count > 0 ? FirstText(descs[0].Value, loc) : null,
            descs.Count > 1 || descs.Any(d => d.Value is PdxBlock),
            pictures.Count > 0 ? (pictures[0].Value is PdxBlock pb ? pb.GetString("picture") : (string)pictures[0].Value) : null,
            pictures.Count > 1 || pictures.Any(p => p.Value is PdxBlock),
            string.Equals(block.GetString("hide_window"), "yes", StringComparison.OrdinalIgnoreCase),
            options.Select((o, i) => new EventOption(
                Entries(o, "name") is { Count: > 0 } names ? FirstText(names[0].Value, loc) ?? $"Option {i + 1}" : $"Option {i + 1}",
                (o.GetBlock("exclusive_trigger") ?? o.GetBlock("trigger")) is { } t && describe(t) is var c && c != "always" ? c : null)).ToList(),
            src);
        return (ev, grants);
    }

    static List<PdxEntry> Entries(PdxBlock block, string key) =>
        block.Entries.Where(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>A key's localised text, or the first "text" found depth-first in a block (first_valid, random_valid, nested desc…).</summary>
    static string? FirstText(object value, Localisation loc)
    {
        if (value is string key) return loc.Get(key) ?? key;
        var block = (PdxBlock)value;
        if (block.GetString("text") is { } text) return loc.Get(text) ?? text;
        foreach (var e in block.Entries)
            if (e.Value is PdxBlock child && !e.Key.Equals("trigger", StringComparison.OrdinalIgnoreCase) && FirstText(child, loc) is { } found)
                return found;
        return null;
    }

    /// <summary>Replaces event-level inline_script entries by the script's entries (parameters substituted), recursively.</summary>
    static PdxBlock ExpandInline(PdxBlock block, ScriptLibrary library, int depth)
    {
        if (depth > 5 || !block.Entries.Any(e => e.Key.Equals("inline_script", StringComparison.OrdinalIgnoreCase))) return block;
        var copy = new PdxBlock();
        foreach (var e in block.Entries)
        {
            if (!e.Key.Equals("inline_script", StringComparison.OrdinalIgnoreCase)) { copy.Entries.Add(e); continue; }
            var body = e.Value switch
            {
                string path => library.Inline(path, new Dictionary<string, string>()),
                PdxBlock b when b.GetString("script") is { } path => library.Inline(path,
                    b.Entries.Where(x => x.Value is string && !x.Key.Equals("script", StringComparison.OrdinalIgnoreCase))
                        .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => (string)g.Last().Value, StringComparer.OrdinalIgnoreCase)),
                _ => null,
            };
            if (body is not null) copy.Entries.AddRange(ExpandInline(body, library, depth + 1).Entries);
        }
        copy.Items.AddRange(block.Items);
        return copy;
    }

    /// <summary>Matches text that may grant a tech: the effect names, inline_script, or a scripted effect that (transitively) mentions them.</summary>
    static Regex Prefilter(ScriptLibrary library)
    {
        var words = library.Effects.ToDictionary(kv => kv.Key,
            kv => new HashSet<string>(Regex.Matches(PdxScriptPrinter.Print(kv.Value), "[A-Za-z0-9_]+").Select(m => m.Value), StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var granting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, w) in words)
            if (GrantFinder.EffectNames.Any(w.Contains) || w.Contains("inline_script")) granting.Add(name);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (name, w) in words)
                if (!granting.Contains(name) && w.Any(granting.Contains)) { granting.Add(name); changed = true; }
        }
        var names = GrantFinder.EffectNames.Append("inline_script").Concat(granting).Select(Regex.Escape);
        return new Regex(@"\b(?:" + string.Join("|", names) + @")\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    /// <summary>Winning files of a top folder (per-path override), parsed in parallel, returned in load order. Base files must match the prefilter.</summary>
    static List<ParsedFile> ParseAll(IReadOnlyList<ContentSource> sources, string folder, Func<string, bool> include, Regex prefilter,
        ICollection<string> warnings, CancellationToken ct)
    {
        var files = new Dictionary<string, (ContentSource Source, string Rel)>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            foreach (var rel in source.Files(folder, ".txt"))
                if (include(rel)) files[rel] = (source, rel);

        var ordered = files.Values.OrderBy(f => f.Rel, TechDatabase.LoadOrder).ToArray();
        var parsed = new ParsedFile?[ordered.Length];
        var warned = new ConcurrentQueue<string>();
        Parallel.ForEach(Enumerable.Range(0, ordered.Length), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, i =>
        {
            var (source, rel) = ordered[i];
            string text;
            try { text = source.ReadText(rel); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
            {
                warned.Enqueue($"{source.Name}: {rel}: {ex.Message}");
                return;
            }
            if (source.IsBaseGame && !prefilter.IsMatch(text)) return;
            parsed[i] = new ParsedFile(new TechSourceRef(source.Name, source.IsBaseGame, rel), ParadoxScriptParser.Parse(text));
        });
        ct.ThrowIfCancellationRequested();
        foreach (var w in warned) warnings.Add(w);
        return parsed.OfType<ParsedFile>().ToList();
    }
}

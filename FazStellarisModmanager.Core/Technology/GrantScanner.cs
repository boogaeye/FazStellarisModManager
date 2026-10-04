using System.Buffers;
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

/// <summary>Event and common/ files parsed by <see cref="GrantScanner.Read"/>, in load order, with the script library they were filtered by.</summary>
public sealed class GrantFiles
{
    internal GrantFiles(ScriptLibrary library, List<GrantScanner.ParsedFile> events, List<GrantScanner.ParsedFile> common)
    {
        Library = library;
        Events = events;
        Common = common;
    }

    internal ScriptLibrary Library { get; }
    internal List<GrantScanner.ParsedFile> Events { get; }
    internal List<GrantScanner.ParsedFile> Common { get; }
}

/// <summary>
/// Scans events/ (first definition of an event id wins; <c>base = id</c> inheritance is resolved across files) and common/ objects
/// (last (folder, id) wins) for tech grants. Files at the same path in a later source replace earlier ones; files are read in
/// load order. Every event file is parsed. A base-game common/ file is only walked for grants when it mentions a grant effect,
/// inline_script or a scripted effect that (transitively) mentions one; its objects still count as definitions without grants.
/// </summary>
public static class GrantScanner
{
    static readonly HashSet<string> ExcludedCommon = new(StringComparer.OrdinalIgnoreCase)
    {
        "technology", "scripted_effects", "inline_scripts", "scripted_triggers", "scripted_variables", "script_values",
        "defines", "pop_jobs", "random_names", "name_lists", "on_actions",
        // Grants to scripted NPC empires at galaxy generation: hundreds of rows of noise for the player.
        "solar_system_initializers",
    };

    /// <summary>How many base events deep inheritance is followed.</summary>
    const int MaxInheritance = 5;

    static readonly Regex Word = new("[A-Za-z0-9_]+", RegexOptions.Compiled);

    /// <param name="techKey">Maps a tech key to the database's spelling, or null when there is no such tech.</param>
    public static GrantIndex Scan(IReadOnlyList<ContentSource> sources, Localisation loc, ScriptLibrary library,
        Func<string, string?> techKey, ICollection<string> warnings, CancellationToken ct = default) =>
        Scan(Read(sources, library, warnings, ct), loc, techKey, ct);

    /// <summary>
    /// Reads and parses the event and common/ files. Needs no localisation or tech list, so it can run while those load.
    /// </summary>
    public static GrantFiles Read(IReadOnlyList<ContentSource> sources, ScriptLibrary library, ICollection<string> warnings, CancellationToken ct = default)
    {
        var events = ParseAll(sources, "events", f => true, null, warnings, ct);
        var common = ParseAll(sources, "common", rel => rel.Split('/') is { Length: >= 3 } p && !ExcludedCommon.Contains(p[1]), Prefilter(library), warnings, ct);
        return new GrantFiles(library, events, common);
    }

    /// <summary>Finds the grants in files read by <see cref="Read"/>.</summary>
    /// <param name="techKey">Maps a tech key to the database's spelling, or null when there is no such tech.</param>
    public static GrantIndex Scan(GrantFiles files, Localisation loc, Func<string, string?> techKey, CancellationToken ct = default)
    {
        var library = files.Library;
        string Describe(PdxBlock? b) => TriggerSummary.Describe(b, loc.Get, n => loc.ScriptedTriggers.TryGetValue(n, out var t) ? t : null);
        var finder = new GrantFinder(library, Describe);

        var byTech = new Dictionary<string, List<GrantSource>>(StringComparer.OrdinalIgnoreCase);
        void AddSource(PendingSource source)
        {
            foreach (var tech in source.Grants.Select(g => g.Tech).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!byTech.TryGetValue(tech, out var list)) byTech[tech] = list = [];
                list.Add(new GrantSource(source.Kind, source.KindFolder, source.Id, source.Name, source.Source,
                    source.Grants.Where(g => g.Tech.Equals(tech, StringComparison.OrdinalIgnoreCase)).Select(g => g.Grant).Distinct().ToList()));
            }
        }

        // Events: first definition per id wins; inheritance is resolved once every definition is known.
        var definitions = new Dictionary<string, EventDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Events)
            foreach (var e in file.Root.Entries)
                if (e.Value is PdxBlock block && IsEventKey(e.Key) && block.GetString("id") is { } id)
                    definitions.TryAdd(id, new EventDefinition(e.Key, block, file.Src));

        var resolved = new Dictionary<string, PdxBlock>(StringComparer.OrdinalIgnoreCase);
        PdxBlock Resolve(string id, int depth, HashSet<string> resolving)
        {
            if (resolved.TryGetValue(id, out var done)) return done;
            var own = ExpandInline(definitions[id].Block, library, 0);
            var result = own;
            if (own.GetString("base") is { } baseId && depth < MaxInheritance && !Is(baseId, id)
                && definitions.ContainsKey(baseId) && !resolving.Contains(baseId))
            {
                resolving.Add(id);
                result = Inherit(own, Resolve(baseId, depth + 1, resolving));
                resolving.Remove(id);
            }
            return resolved[id] = result;
        }

        // Resolve in order (memoised, single-threaded), then analyse in parallel and merge in order.
        var ordered = definitions.ToArray();
        var blocks = new PdxBlock[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            blocks[i] = Resolve(ordered[i].Key, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
        var analysed = new (GameEvent Event, List<TechGrantFor> Grants)?[ordered.Length];
        Parallel.For(0, ordered.Length, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, i =>
            analysed[i] = AnalyseEvent(ordered[i].Value.Type, ordered[i].Key, blocks[i], ordered[i].Value.Src, loc, finder, Describe, techKey));

        var events = new Dictionary<string, GameEvent>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ordered.Length; i++)
        {
            if (analysed[i] is not { } a) continue;
            events[ordered[i].Key] = a.Event;
            AddSource(new PendingSource("Event", GrantSource.EventsFolder, ordered[i].Key, a.Event.Title, ordered[i].Value.Src, a.Grants));
        }

        // common/ objects: last (folder, id) wins. Objects of files that cannot grant still replace earlier definitions.
        var objects = new Dictionary<string, PendingSource?>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Common)
        {
            ct.ThrowIfCancellationRequested();
            var folder = file.Src.File.Split('/')[1];
            if (file.DefinedOnly is { } defined)
            {
                foreach (var id in defined) objects[folder + "|" + id] = null;
                continue;
            }
            foreach (var e in file.Root.Entries)
            {
                if (e.Value is not PdxBlock block || e.Key.StartsWith('@')) continue;
                var id = block.GetString("key") ?? e.Key;
                var grants = Place(finder.Find(block), EventPart.Immediate, null, techKey);
                objects[folder + "|" + id] = grants.Count == 0 ? null
                    : new PendingSource(UnlockScanner.KindName(folder), folder, id, UnlockScanner.UnlockName(loc, folder, id), file.Src, grants);
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

    /// <summary>A parsed file, or (DefinedOnly set, Root empty) the object ids of a base file the pre-filter ruled out.</summary>
    internal sealed record ParsedFile(TechSourceRef Src, PdxBlock Root, IReadOnlyList<string>? DefinedOnly);

    sealed record EventDefinition(string Type, PdxBlock Block, TechSourceRef Src);

    static bool IsEventKey(string key) => Is(key, "event") || key.EndsWith("_event", StringComparison.OrdinalIgnoreCase);

    static bool Is(string key, string name) => key.Equals(name, StringComparison.OrdinalIgnoreCase);

    static bool Yes(PdxBlock block, string key) => string.Equals(block.GetString(key), "yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A derived event (<c>base = id</c>) on top of its resolved base: title, desc (unless desc_clear = yes), picture, immediate and
    /// after come from the base when the derived event has none; the base's options come first unless option_clear = yes.
    /// </summary>
    static PdxBlock Inherit(PdxBlock own, PdxBlock baseBlock)
    {
        bool Has(string key) => own.Entries.Any(e => Is(e.Key, key));
        var inherit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "title", "picture", "immediate", "after" })
            if (!Has(key)) inherit.Add(key);
        if (!Has("desc") && !Yes(own, "desc_clear")) inherit.Add("desc");
        if (!Yes(own, "option_clear")) inherit.Add("option");

        var merged = new PdxBlock();
        merged.Entries.AddRange(baseBlock.Entries.Where(e => inherit.Contains(e.Key)));
        merged.Entries.AddRange(own.Entries);
        merged.Items.AddRange(own.Items);
        return merged;
    }

    static List<TechGrantFor> Place(List<FoundGrant> found, EventPart part, int? option, Func<string, string?> techKey) =>
        found.Select(f => techKey(f.Tech) is { } tech ? new TechGrantFor(tech, new TechGrant(f.Kind, f.Progress, part, option, f.Condition, f.Via)) : null)
            .OfType<TechGrantFor>()
            .ToList();

    static (GameEvent Event, List<TechGrantFor> Grants)? AnalyseEvent(string type, string id, PdxBlock block, TechSourceRef src,
        Localisation loc, GrantFinder finder, Func<PdxBlock?, string> describe, Func<string, string?> techKey)
    {
        var options = block.Entries.Where(e => Is(e.Key, "option") && e.Value is PdxBlock).Select(e => (PdxBlock)e.Value).ToList();
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
            Yes(block, "hide_window"),
            options.Select((o, i) => new EventOption(
                Entries(o, "name") is { Count: > 0 } names ? FirstText(names[0].Value, loc) ?? $"Option {i + 1}" : $"Option {i + 1}",
                (o.GetBlock("exclusive_trigger") ?? o.GetBlock("trigger")) is { } t && describe(t) is var c && c != "always" ? c : null)).ToList(),
            src);
        return (ev, grants);
    }

    static List<PdxEntry> Entries(PdxBlock block, string key) => block.Entries.Where(e => Is(e.Key, key)).ToList();

    /// <summary>
    /// A key's localised text, or the first "text" found depth-first in a block: first_valid, random_valid, nested desc, and the
    /// <c>trigger = { text = …  if = { limit = { … } text = … } }</c> form. Limit blocks are skipped.
    /// </summary>
    static string? FirstText(object value, Localisation loc)
    {
        if (value is string key) return loc.Get(key) ?? key;
        var block = (PdxBlock)value;
        if (block.GetString("text") is { } text) return loc.Get(text) ?? text;
        foreach (var e in block.Entries)
            if (e.Value is PdxBlock child && !Is(e.Key, "limit") && FirstText(child, loc) is { } found)
                return found;
        return null;
    }

    /// <summary>Replaces event-level inline_script entries by the script's entries (parameters substituted), recursively.</summary>
    static PdxBlock ExpandInline(PdxBlock block, ScriptLibrary library, int depth)
    {
        if (depth > 5 || !block.Entries.Any(e => Is(e.Key, "inline_script"))) return block;
        var copy = new PdxBlock();
        foreach (var e in block.Entries)
        {
            if (!Is(e.Key, "inline_script")) { copy.Entries.Add(e); continue; }
            var body = e.Value switch
            {
                string path => library.Inline(path, new Dictionary<string, string>()),
                PdxBlock b when b.GetString("script") is { } path => library.Inline(path,
                    b.Entries.Where(x => x.Value is string && !Is(x.Key, "script"))
                        .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => (string)g.Last().Value, StringComparer.OrdinalIgnoreCase)),
                _ => null,
            };
            if (body is not null) copy.Entries.AddRange(ExpandInline(body, library, depth + 1).Entries);
        }
        copy.Items.AddRange(block.Items);
        return copy;
    }

    /// <summary>
    /// Words whose presence means a file may grant a tech: the effect names, inline_script, and the scripted effects that
    /// (transitively) mention them. A superset filter: a match inside a longer word counts too.
    /// </summary>
    static SearchValues<string> Prefilter(ScriptLibrary library)
    {
        var words = library.Effects.ToDictionary(kv => kv.Key,
            kv => new HashSet<string>(Word.Matches(PdxScriptPrinter.Print(kv.Value)).Select(m => m.Value), StringComparer.OrdinalIgnoreCase),
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
        return SearchValues.Create([.. GrantFinder.EffectNames, "inline_script", .. granting], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Winning files of a top folder (per-path override), parsed in parallel, returned in load order. With a pre-filter, base
    /// files that mention none of its words are not parsed: only their object ids are read, as definitions without grants.
    /// Mod files are always parsed.
    /// </summary>
    static List<ParsedFile> ParseAll(IReadOnlyList<ContentSource> sources, string folder, Func<string, bool> include, SearchValues<string>? prefilter,
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
            var src = new TechSourceRef(source.Name, source.IsBaseGame, rel);
            parsed[i] = prefilter is null || !source.IsBaseGame || text.AsSpan().ContainsAny(prefilter)
                ? new ParsedFile(src, ParadoxScriptParser.Parse(text), null)
                : new ParsedFile(src, new PdxBlock(), TopLevelIds(text));
        });
        ct.ThrowIfCancellationRequested();
        foreach (var w in warned) warnings.Add(w);
        return parsed.OfType<ParsedFile>().ToList();
    }

    /// <summary>
    /// The ids of the top-level <c>name = { … }</c> objects of a script file, without a full parse: the name, or the object's own
    /// <c>key = …</c> when it has one. Skips comments and quoted strings; <c>@variables</c> are not objects.
    /// </summary>
    internal static List<string> TopLevelIds(string text)
    {
        const char Newline = (char)10;
        var ids = new List<string>();
        var depth = 0;
        string? id = null, word = null;
        var afterEquals = false;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#')
            {
                while (i < text.Length && text[i] != Newline) i++;
                continue;
            }
            if (c == '{')
            {
                if (depth == 0 && afterEquals && word is not null && !word.StartsWith('@')) id = word;
                depth++;
                (word, afterEquals) = (null, false);
                i++;
                continue;
            }
            if (c == '}')
            {
                if (depth > 0 && --depth == 0 && id is not null) { ids.Add(id); id = null; }
                (word, afterEquals) = (null, false);
                i++;
                continue;
            }
            if (c == '=') { afterEquals = word is not null; i++; continue; }
            if (c is '<' or '>' or '!') { (word, afterEquals) = (null, false); i++; continue; }

            string token;
            if (c == '"')
            {
                var end = text.IndexOf('"', i + 1);
                if (end < 0) end = text.Length;
                token = text[(i + 1)..end];
                i = end + 1;
            }
            else
            {
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '=' or '#' or '"' or '<' or '>' or '!')) i++;
                token = text[start..i];
            }
            if (afterEquals)
            {
                if (depth == 1 && id is not null && Is(word!, "key")) id = token;
                (word, afterEquals) = (null, false);
            }
            else word = token;
        }
        return ids;
    }
}

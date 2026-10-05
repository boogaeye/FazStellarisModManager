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
    internal GrantFiles(ScriptLibrary library, IReadOnlySet<string> relevant, List<ParsedFile> events, List<ParsedFile> common)
    {
        Library = library;
        Relevant = relevant;
        Events = events;
        Common = common;
    }

    internal ScriptLibrary Library { get; }
    /// <summary>Scripted effects that can grant (see <see cref="GrantFinder.Relevant"/>).</summary>
    internal IReadOnlySet<string> Relevant { get; }
    internal List<ParsedFile> Events { get; }
    internal List<ParsedFile> Common { get; }
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

    /// <param name="techKey">Maps a tech key to the database's spelling, or null when there is no such tech.</param>
    public static GrantIndex Scan(IReadOnlyList<ContentSource> sources, Localisation loc, ScriptLibrary library,
        Func<string, string?> techKey, ICollection<string> warnings, CancellationToken ct = default) =>
        Scan(Read(sources, library, warnings, ct), loc, techKey, ct);

    /// <summary>
    /// Reads and parses the event and common/ files. Needs no localisation or tech list, so it can run while those load.
    /// </summary>
    public static GrantFiles Read(IReadOnlyList<ContentSource> sources, ScriptLibrary library, ICollection<string> warnings, CancellationToken ct = default)
    {
        var events = ScriptFiles.ParseAll(sources, "events", f => true, null, warnings, ct);
        var relevant = GrantFinder.Relevant(library);
        var prefilter = ScriptFiles.Prefilter(relevant, GrantFinder.EffectNames);
        var common = ScriptFiles.ParseAll(sources, "common", rel => rel.Split('/') is { Length: >= 3 } p && !ExcludedCommon.Contains(p[1]), prefilter, warnings, ct);
        return new GrantFiles(library, relevant, events, common);
    }

    /// <summary>Finds the grants in files read by <see cref="Read"/>.</summary>
    /// <param name="techKey">Maps a tech key to the database's spelling, or null when there is no such tech.</param>
    public static GrantIndex Scan(GrantFiles files, Localisation loc, Func<string, string?> techKey, CancellationToken ct = default)
    {
        var library = files.Library;
        string Describe(PdxBlock? b) => TriggerSummary.Describe(b, loc.Get, n => loc.ScriptedTriggers.TryGetValue(n, out var t) ? t : null);
        var finder = new GrantFinder(library, Describe, files.Relevant);

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

        // Events: first definition per id wins; inheritance is resolved once every definition is known (memoised, single-threaded),
        // then they are analysed in parallel and merged in order.
        var ordered = EventScripts.Resolve(files.Events, library, ct);
        var analysed = new (GameEvent Event, List<TechGrantFor> Grants)?[ordered.Count];
        Parallel.For(0, ordered.Count, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, i =>
            analysed[i] = AnalyseEvent(ordered[i], loc, finder, Describe, techKey));

        var events = new Dictionary<string, GameEvent>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ordered.Count; i++)
        {
            if (analysed[i] is not { } a) continue;
            events[ordered[i].Id] = a.Event;
            AddSource(new PendingSource("Event", GrantSource.EventsFolder, ordered[i].Id, a.Event.Title, ordered[i].Src, a.Grants));
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

    static List<TechGrantFor> Place(List<FoundGrant> found, EventPart part, int? option, Func<string, string?> techKey) =>
        found.Select(f => techKey(f.Tech) is { } tech ? new TechGrantFor(tech, new TechGrant(f.Kind, f.Progress, part, option, f.Condition, f.Via)) : null)
            .OfType<TechGrantFor>()
            .ToList();

    static (GameEvent Event, List<TechGrantFor> Grants)? AnalyseEvent(ResolvedEvent ev, Localisation loc, GrantFinder finder,
        Func<PdxBlock?, string> describe, Func<string, string?> techKey)
    {
        var block = ev.Block;
        var options = EventScripts.Options(block);
        var grants = new List<TechGrantFor>();
        if (block.GetBlock("immediate") is { } immediate) grants.AddRange(Place(finder.Find(immediate), EventPart.Immediate, null, techKey));
        for (var i = 0; i < options.Count; i++) grants.AddRange(Place(finder.Find(options[i]), EventPart.Option, i, techKey));
        if (block.GetBlock("after") is { } after) grants.AddRange(Place(finder.Find(after), EventPart.After, null, techKey));
        if (grants.Count == 0) return null;

        var texts = EventScripts.Texts(block, loc);
        var gameEvent = new GameEvent(
            ev.Id,
            ev.Type,
            texts.Title ?? ev.Id,
            texts.Description,
            texts.DescriptionVaries,
            texts.Picture,
            texts.PictureVaries,
            EventScripts.Yes(block, "hide_window"),
            options.Select((o, i) => new EventOption(
                EventScripts.OptionName(o, i, loc),
                (o.GetBlock("exclusive_trigger") ?? o.GetBlock("trigger")) is { } t && describe(t) is var c && c != "always" ? c : null)).ToList(),
            ev.Src);
        return (gameEvent, grants);
    }
}

using System.Collections.Concurrent;
using System.Globalization;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>Something in common/ that requires a tech. KindFolder is the common/ subfolder; Id is the block's key = "…" or its name.</summary>
public sealed record Unlock(string Kind, string KindFolder, string Id, string Name, TechSourceRef Source, string? Icon, int? IconFrame);

public static class UnlockScanner
{
    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "technology", "scripted_variables", "inline_scripts",
        "scripted_effects", "scripted_triggers", "on_actions", "defines", "script_values", "game_rules", "event_chains",
        "pop_jobs", "random_names", "name_lists", "personalities", "opinion_modifiers", "static_modifiers", "triggered_modifiers",
        "diplomatic_actions", "ai_budget", "economic_categories", "mandates", "message_types", "special_projects",
        "anomalies", "observation_station_missions",
    };

    static readonly Dictionary<string, string> KindNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["component_templates"] = "Ship components",
        ["buildings"] = "Buildings",
        ["ship_sizes"] = "Ship sizes",
        ["section_templates"] = "Ship sections",
        ["starbase_buildings"] = "Starbase buildings",
        ["starbase_modules"] = "Starbase modules",
        ["megastructures"] = "Megastructures",
        ["edicts"] = "Edicts",
        ["districts"] = "Districts",
        ["armies"] = "Armies",
        ["traits"] = "Traits",
        ["deposits"] = "Deposits",
        ["decisions"] = "Decisions",
        ["policies"] = "Policies",
        ["zones"] = "Zones",
        ["strategic_resources"] = "Strategic resources",
        ["bypass"] = "Bypasses",
    };

    /// <summary>Localisation key prefixes tried (after the bare id) per common/ folder; confirmed against the base game's localisation.</summary>
    static readonly Dictionary<string, string[]> LocPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["starbase_modules"] = ["sm_"],
        ["starbase_buildings"] = ["sm_", "sb_"],
        ["edicts"] = ["edict_"],
    };

    static string UnlockName(Localisation loc, string folder, string id)
    {
        if (loc.Get(id) is { } n) return n;
        if (LocPrefixes.TryGetValue(folder, out var prefixes))
            foreach (var p in prefixes)
                if (loc.Get(p + id) is { } pn) return pn;
        return id;
    }

    public static string KindName(string folder)
    {
        if (KindNames.TryGetValue(folder, out var name)) return name;
        var words = folder.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return folder;
        words[0] = char.ToUpperInvariant(words[0][0]) + words[0][1..];
        return string.Join(' ', words);
    }

    /// <summary>
    /// Tech key -> unlocks (sorted by kind, then name). Same override rules as techs: a later source's file at the same path
    /// replaces the earlier one, then objects are read in load order and the last (folder, id) definition wins.
    /// Files are read and parsed in parallel, then merged in load order. Base-game files are only parsed when they mention
    /// "prerequisites" or "show_in_tech"; files from other sources are always parsed, because a mod file can redefine an
    /// object without those keywords (which clears its earlier unlocks).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<Unlock>> Scan(IReadOnlyList<ContentSource> sources, Localisation loc,
        ICollection<string> warnings, CancellationToken ct = default)
    {
        var files = new Dictionary<string, (ContentSource Source, string Rel)>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            foreach (var rel in source.Files("common", ".txt"))
            {
                var parts = rel.Split('/');
                if (parts.Length < 3 || Excluded.Contains(parts[1])) continue;
                files[rel] = (source, rel);
            }

        var ordered = files.Values.OrderBy(f => f.Rel, TechDatabase.LoadOrder).ToArray();
        var parsed = new List<ScannedObject>?[ordered.Length];
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
            if (source.IsBaseGame
                && !text.Contains("prerequisites", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("show_in_tech", StringComparison.OrdinalIgnoreCase)) return;

            var folder = rel.Split('/')[1];
            var src = new TechSourceRef(source.Name, source.IsBaseGame, rel);
            var list = new List<ScannedObject>();
            foreach (var e in ParadoxScriptParser.Parse(text).Entries)
            {
                if (e.Value is not PdxBlock b || e.Key.StartsWith('@')) continue;
                var id = b.GetString("key") ?? e.Key;
                IEnumerable<string> shown = b.GetString("show_in_tech") is { } sit ? [sit] : [];
                var techs = (b.GetBlock("prerequisites")?.StringItems ?? []).Concat(shown).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var frame = int.TryParse(b.GetString("icon_frame"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) ? f : (int?)null;
                var icon = b.GetString("icon");
                // Policies: each option with its own prerequisites is an unlock keyed by the option's name.
                if (folder.Equals("policies", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var oe in b.Entries)
                        if (oe.Key.Equals("option", StringComparison.OrdinalIgnoreCase) && oe.Value is PdxBlock ob
                            && ob.GetString("name") is { } oname && ob.GetBlock("prerequisites") is { } oreq)
                        {
                            var otechs = oreq.StringItems.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                            if (otechs.Length > 0)
                                list.Add(new ScannedObject(folder, oname, otechs, src, ob.GetString("icon"), null));
                        }
                }
                list.Add(new ScannedObject(folder, id, techs, src, icon, frame));
            }
            parsed[i] = list;
        });
        ct.ThrowIfCancellationRequested();
        foreach (var w in warned) warnings.Add(w);

        var objects = new Dictionary<string, ScannedObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in parsed)
            if (list is not null)
                foreach (var o in list) objects[o.Folder + "|" + o.Id] = o;

        var index = new Dictionary<string, List<Unlock>>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in objects.Values)
            foreach (var tech in o.Techs)
            {
                if (!index.TryGetValue(tech, out var list)) index[tech] = list = [];
                list.Add(new Unlock(KindName(o.Folder), o.Folder, o.Id, UnlockName(loc, o.Folder, o.Id), o.Src, o.Icon, o.Frame));
            }

        return index.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<Unlock>)kv.Value
                .OrderBy(u => u.Kind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    sealed record ScannedObject(string Folder, string Id, string[] Techs, TechSourceRef Src, string? Icon, int? Frame);
}

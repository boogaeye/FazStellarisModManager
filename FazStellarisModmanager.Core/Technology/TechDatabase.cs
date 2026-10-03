using System.Globalization;

namespace FazStellarisModmanager.Core.Technology;

public enum TechArea { Physics, Society, Engineering, Other }

/// <summary>Where a definition came from: source name (base game or mod), and the file inside it.</summary>
public sealed record TechSourceRef(string SourceName, bool IsBaseGame, string File);

public sealed record Tech(
    string Key,
    string Name,
    string? Description,
    TechArea Area,
    int? Tier,
    string? Category,
    string Cost,
    IReadOnlyList<string> Prerequisites,
    bool IsStart,
    bool IsRare,
    bool IsDangerous,
    bool IsRepeatable,
    string IconKey,
    IReadOnlyList<string> Dlcs,
    TechSourceRef Source,
    IReadOnlyList<TechSourceRef> Overridden,
    TechDetails Details)
{
    /// <summary>The winning definition comes from a mod (added or overridden by it).</summary>
    public bool ChangedByMods => !Source.IsBaseGame;
}

public sealed record TechFilter(
    string? Search = null,
    TechArea? Area = null,
    int? Tier = null,
    string? Category = null,
    string? Source = null,
    bool RareOnly = false,
    bool DangerousOnly = false,
    bool RepeatableOnly = false,
    bool StartOnly = false,
    bool ChangedByModsOnly = false);

/// <summary>All technologies of a base game + mod list after Stellaris' override rules.</summary>
public sealed class TechDatabase
{
    public const string TechFolder = "common/technology";
    public const string VariablesFolder = "common/scripted_variables";

    readonly Dictionary<string, IReadOnlyList<string>> _dependents;

    TechDatabase(Dictionary<string, Tech> techs, Dictionary<string, IReadOnlyList<string>> dependents, List<string> warnings, List<string> sourceNames)
    {
        Techs = techs;
        _dependents = dependents;
        Warnings = warnings;
        SourceNames = sourceNames;
        Categories = techs.Values.Select(t => t.Category).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        Tiers = techs.Values.Select(t => t.Tier).OfType<int>().Distinct().Order().ToList();
    }

    public IReadOnlyDictionary<string, Tech> Techs { get; }
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Sources that provide at least one winning tech, in load order.</summary>
    public IReadOnlyList<string> SourceNames { get; }

    public IReadOnlyList<string> Categories { get; }
    public IReadOnlyList<int> Tiers { get; }

    /// <summary>Techs that list <paramref name="key"/> as a prerequisite, ordinal-sorted.</summary>
    public IReadOnlyList<string> Dependents(string key) => _dependents.TryGetValue(key, out var list) ? list : [];

    public int Count(TechArea? area) => area is null ? Techs.Count : Techs.Values.Count(t => t.Area == area);

    public IEnumerable<Tech> Query(TechFilter f)
    {
        var search = f.Search?.Trim();
        return Techs.Values
            .Where(t =>
                (f.Area is null || t.Area == f.Area) &&
                (f.Tier is null || t.Tier == f.Tier) &&
                (f.Category is null || string.Equals(t.Category, f.Category, StringComparison.OrdinalIgnoreCase)) &&
                (f.Source is null || string.Equals(t.Source.SourceName, f.Source, StringComparison.OrdinalIgnoreCase)) &&
                (!f.RareOnly || t.IsRare) &&
                (!f.DangerousOnly || t.IsDangerous) &&
                (!f.RepeatableOnly || t.IsRepeatable) &&
                (!f.StartOnly || t.IsStart) &&
                (!f.ChangedByModsOnly || t.ChangedByMods) &&
                (string.IsNullOrEmpty(search) ||
                 t.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 t.Key.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(t => t.Area)
            .ThenBy(t => t.Tier ?? int.MaxValue)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Shortest prerequisite chain to the tech, start first. Prefers the nearest real start_tech; if none is reachable,
    /// the nearest tech without known prerequisites; if neither exists (a cycle), just the tech itself.
    /// </summary>
    public IReadOnlyList<Tech> PathFromStart(string key)
    {
        if (!Techs.TryGetValue(key, out var target)) return [];
        var cameFrom = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [target.Key] = null };
        var queue = new Queue<string>([target.Key]);
        string? firstRoot = null;
        while (queue.Count > 0)
        {
            var current = Techs[queue.Dequeue()];
            var prereqs = current.Prerequisites.Where(Techs.ContainsKey).Select(p => Techs[p].Key).ToList();
            if (current.IsStart) return PathTo(current.Key, cameFrom);
            if (prereqs.Count == 0) firstRoot ??= current.Key;
            foreach (var p in prereqs)
                if (cameFrom.TryAdd(p, current.Key)) queue.Enqueue(p);
        }
        return firstRoot is null ? [target] : PathTo(firstRoot, cameFrom);
    }

    List<Tech> PathTo(string from, Dictionary<string, string?> cameFrom)
    {
        var path = new List<Tech>();
        for (string? k = from; k is not null; k = cameFrom[k]) path.Add(Techs[k]);
        return path;
    }

    // Stellaris loads files in ASCII order and modders use lowercase names, so lowercasing first
    // makes "Zz_x" and "zz_x" sort the same.
    internal static readonly IComparer<string> LoadOrder =
        Comparer<string>.Create((a, b) => string.CompareOrdinal(a.ToLowerInvariant(), b.ToLowerInvariant()));

    sealed record FileWinner(ContentSource Source, string Rel, List<(ContentSource Source, string Rel)> Replaced);

    public static TechDatabase Build(IReadOnlyList<ContentSource> sources, IEnumerable<string>? earlierWarnings = null,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var warnings = new List<string>(earlierWarnings ?? []);
        progress?.Report("Collecting technology files…");
        var techFiles = Winners(sources, TechFolder, topLevelOnly: true);
        var variableFiles = Winners(sources, VariablesFolder, topLevelOnly: false);

        var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in variableFiles.Values.OrderBy(f => f.Rel, LoadOrder))
            if (TryRead(f.Source, f.Rel, warnings) is { } text)
                foreach (var (k, v) in ScriptedVariables.Parse(text)) globals[k] = v;

        var defs = new Dictionary<string, (TechDefinition Def, TechSourceRef Src, Dictionary<string, string> Locals)>(StringComparer.OrdinalIgnoreCase);
        var overridden = new Dictionary<string, List<TechSourceRef>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in techFiles.Values.OrderBy(f => f.Rel, LoadOrder))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Reading {f.Source.Name}: {f.Rel}");
            if (TryRead(f.Source, f.Rel, warnings) is not { } text) continue;

            // Files this one replaced at file level: their techs count as overridden by this file.
            var replaced = new List<(TechSourceRef Ref, HashSet<string> Keys)>();
            foreach (var (rs, rrel) in f.Replaced)
                if (TryRead(rs, rrel, warnings) is { } old)
                    replaced.Add((new TechSourceRef(rs.Name, rs.IsBaseGame, rrel),
                        TechParser.Parse(old).Select(d => d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase)));

            var locals = ScriptedVariables.Parse(text);
            var src = new TechSourceRef(f.Source.Name, f.Source.IsBaseGame, f.Rel);
            foreach (var def in TechParser.Parse(text))
            {
                if (!overridden.TryGetValue(def.Key, out var list)) overridden[def.Key] = list = [];
                if (defs.TryGetValue(def.Key, out var previous) && previous.Src != src) list.Add(previous.Src);
                foreach (var (r, keys) in replaced)
                    if (keys.Contains(def.Key)) list.Add(r);
                defs[def.Key] = (def, src, locals);
            }
        }

        progress?.Report("Reading localisation…");
        var loc = Localisation.Load(sources, warnings);

        var techs = new Dictionary<string, Tech>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, (def, src, locals)) in defs)
        {
            var tierText = def.Tier is null ? null : ScriptedVariables.Resolve(def.Tier, locals, globals);
            techs[key] = new Tech(
                def.Key,
                loc.Get(def.Key) ?? def.Key,
                loc.Get(def.Key + "_desc"),
                ParseArea(def.Area),
                int.TryParse(tierText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tier) ? tier : null,
                def.Category,
                def.Cost is null ? "?" : ScriptedVariables.Resolve(def.Cost, locals, globals),
                def.Prerequisites,
                def.IsStart,
                def.IsRare,
                def.IsDangerous,
                def.IsRepeatable,
                def.Icon ?? def.Key,
                def.Dlcs,
                src,
                overridden.TryGetValue(key, out var o) ? o.Distinct().ToList() : [],
                TechDetailsBuilder.Build(def.Key, def.Block, loc, locals, globals));
        }

        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in techs.Values)
            foreach (var p in t.Prerequisites)
                if (techs.TryGetValue(p, out var pre))
                {
                    if (!dependents.TryGetValue(pre.Key, out var list)) dependents[pre.Key] = list = [];
                    if (!list.Contains(t.Key)) list.Add(t.Key);
                }

        var sourceNames = sources.Select(s => s.Name).Distinct()
            .Where(n => techs.Values.Any(t => t.Source.SourceName == n)).ToList();
        return new TechDatabase(techs,
            dependents.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.Order(StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase),
            warnings, sourceNames);
    }

    static Dictionary<string, FileWinner> Winners(IReadOnlyList<ContentSource> sources, string folder, bool topLevelOnly)
    {
        var depth = folder.Count(c => c == '/') + 1;
        var map = new Dictionary<string, FileWinner>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sources)
            foreach (var rel in s.Files(folder, ".txt"))
            {
                if (topLevelOnly && rel.Count(c => c == '/') != depth) continue;
                List<(ContentSource Source, string Rel)> replaced = map.TryGetValue(rel, out var prev) ? [.. prev.Replaced, (prev.Source, prev.Rel)] : [];
                map[rel] = new FileWinner(s, rel, replaced);
            }
        return map;
    }

    static string? TryRead(ContentSource source, string rel, List<string> warnings)
    {
        try { return source.ReadText(rel); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
        {
            warnings.Add($"{source.Name}: {rel}: {ex.Message}");
            return null;
        }
    }

    static TechArea ParseArea(string? area) => area?.ToLowerInvariant() switch
    {
        "physics" => TechArea.Physics,
        "society" => TechArea.Society,
        "engineering" => TechArea.Engineering,
        _ => TechArea.Other,
    };
}

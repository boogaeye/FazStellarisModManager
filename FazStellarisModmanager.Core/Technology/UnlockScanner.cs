using System.Globalization;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>Something in common/ that requires a tech. KindFolder is the common/ subfolder; Id is the block's key = "…" or its name.</summary>
public sealed record Unlock(string Kind, string KindFolder, string Id, string Name, TechSourceRef Source, string? Icon, int? IconFrame);

public static class UnlockScanner
{
    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "technology", "scripted_variables", "inline_scripts" };

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
    /// Only files mentioning "prerequisites" or "show_in_tech" are parsed.
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

        var objects = new Dictionary<string, (string Folder, string Id, string[] Techs, TechSourceRef Src, string? Icon, int? Frame)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, rel) in files.Values.OrderBy(f => f.Rel, TechDatabase.LoadOrder))
        {
            ct.ThrowIfCancellationRequested();
            string text;
            try { text = source.ReadText(rel); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
            {
                warnings.Add($"{source.Name}: {rel}: {ex.Message}");
                continue;
            }
            if (!text.Contains("prerequisites", StringComparison.OrdinalIgnoreCase) && !text.Contains("show_in_tech", StringComparison.OrdinalIgnoreCase)) continue;

            var folder = rel.Split('/')[1];
            var src = new TechSourceRef(source.Name, source.IsBaseGame, rel);
            foreach (var e in ParadoxScriptParser.Parse(text).Entries)
            {
                if (e.Value is not PdxBlock b || e.Key.StartsWith('@')) continue;
                var id = b.GetString("key") ?? e.Key;
                IEnumerable<string> shown = b.GetString("show_in_tech") is { } sit ? [sit] : [];
                var techs = (b.GetBlock("prerequisites")?.StringItems ?? []).Concat(shown).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var frame = int.TryParse(b.GetString("icon_frame"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) ? f : (int?)null;
                objects[folder + "|" + id] = (folder, id, techs, src, b.GetString("icon"), frame);
            }
        }

        var index = new Dictionary<string, List<Unlock>>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in objects.Values)
            foreach (var tech in o.Techs)
            {
                if (!index.TryGetValue(tech, out var list)) index[tech] = list = [];
                list.Add(new Unlock(KindName(o.Folder), o.Folder, o.Id, loc.Get(o.Id) ?? o.Id, o.Src, o.Icon, o.Frame));
            }

        return index.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<Unlock>)kv.Value
                .OrderBy(u => u.Kind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
    }
}

using System.Text.Json;
using FazStellarisModmanager.Core.IO;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>Techs marked researched and route targets for one mod-list choice. Keys are kept as written, even ones not in the current tree.</summary>
public sealed record ResearchState(IReadOnlyList<string> Researched, IReadOnlyList<string> Targets)
{
    public static ResearchState Empty { get; } = new([], []);
}

/// <summary>Stores each choice's <see cref="ResearchState"/> as a file-safe-label.json file in the directory.</summary>
public sealed class ResearchStateStore(string directory)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    static readonly HashSet<string> Reserved = new(
        ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).Select(i => "COM" + i), .. Enumerable.Range(1, 9).Select(i => "LPT" + i)],
        StringComparer.OrdinalIgnoreCase);

    sealed record StateFile(List<string?>? Researched, List<string?>? Targets);

    public string Directory { get; } = directory;

    /// <summary>The saved state; <see cref="ResearchState.Empty"/> when there is none. A corrupt file gives the empty state plus a warning.</summary>
    public ResearchState Load(string label, ICollection<string>? warnings = null)
    {
        var path = PathFor(label);
        if (!File.Exists(path)) return ResearchState.Empty;
        try
        {
            var file = JsonSerializer.Deserialize<StateFile>(File.ReadAllText(path), Json);
            return new ResearchState(Normalize(file?.Researched), Normalize(file?.Targets));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            warnings?.Add($"Research progress for '{label}' could not be read: {ex.Message}");
            return ResearchState.Empty;
        }
    }

    public void Save(string label, ResearchState state)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var file = new StateFile([.. Normalize(state.Researched)], [.. Normalize(state.Targets)]);
        AtomicFile.WriteAllText(PathFor(label), JsonSerializer.Serialize(file, Json));
    }

    /// <summary>File for a label: invalid characters become "_", reserved device names get a "_" prefix, an empty label is "_".</summary>
    public string PathFor(string label)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(label.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (safe.Length > 100) safe = safe[..100];
        safe = safe.TrimEnd('.', ' ');
        if (safe.Length == 0) safe = "_";
        if (Reserved.Contains(safe.Split('.')[0].TrimEnd(' '))) safe = "_" + safe;
        return Path.Combine(Directory, safe + ".json");
    }

    static List<string> Normalize(IEnumerable<string?>? keys) =>
        (keys ?? []).OfType<string>()
            .Select(k => k.Trim())
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

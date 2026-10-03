using System.Text.Json;
using FazStellarisModmanager.Core.IO;

namespace FazStellarisModmanager.Core.Lists;

/// <summary>Stores each list as &lt;directory&gt;\&lt;sanitized name&gt;.json.</summary>
public sealed class ModListStore(string directory)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string Directory { get; } = directory;

    public IReadOnlyList<ModList> LoadAll() =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, "*.json")
                .Select(ReadFile)
                .OfType<ModList>()
                .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

    public ModList? Load(string name) => ReadOwned(PathFor(name), name, out var list) ? list : null;

    public void Save(ModList list)
    {
        var path = PathFor(list.Name);
        if (File.Exists(path) && ReadFile(path) is { } stored && !SameName(stored.Name, list.Name))
            throw new InvalidOperationException($"A different list ('{stored.Name}') already uses the file name for '{list.Name}'. Choose another name.");
        System.IO.Directory.CreateDirectory(Directory);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(list, Json));
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return;
        var stored = ReadFile(path);
        if (stored is null || SameName(stored.Name, name)) File.Delete(path);
    }

    static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static bool ReadOwned(string path, string name, out ModList? list)
    {
        list = File.Exists(path) ? ReadFile(path) : null;
        if (list is not null && !SameName(list.Name, name)) list = null;
        return list is not null;
    }

    static readonly HashSet<string> Reserved = new(
        ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).Select(i => "COM" + i), .. Enumerable.Range(1, 9).Select(i => "LPT" + i)],
        StringComparer.OrdinalIgnoreCase);

    string PathFor(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (safe.Length > 100) safe = safe[..100];
        safe = safe.TrimEnd('.', ' ');
        if (safe.Length == 0) throw new ArgumentException("List name is empty.", nameof(name));
        if (Reserved.Contains(safe.Split('.')[0].TrimEnd(' '))) safe = "_" + safe;
        return Path.Combine(Directory, safe + ".json");
    }

    static ModList? ReadFile(string path)
    {
        try
        {
            var list = JsonSerializer.Deserialize<ModList>(File.ReadAllText(path), Json);
            return list is { Name: not null, Mods: not null, DisabledDlcs: not null } ? list : null; // corrupt files are skipped, not fatal
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }
}

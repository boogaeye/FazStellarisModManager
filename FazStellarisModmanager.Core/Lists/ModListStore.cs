using System.Text.Json;

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

    public ModList? Load(string name)
    {
        var path = PathFor(name);
        return File.Exists(path) ? ReadFile(path) : null;
    }

    public void Save(ModList list)
    {
        var path = PathFor(list.Name);
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(path, JsonSerializer.Serialize(list, Json));
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    string PathFor(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (safe.Length == 0) throw new ArgumentException("List name is empty.", nameof(name));
        return Path.Combine(Directory, safe + ".json");
    }

    static ModList? ReadFile(string path)
    {
        try { return JsonSerializer.Deserialize<ModList>(File.ReadAllText(path), Json); }
        catch (JsonException) { return null; } // a corrupt list file is skipped, not fatal
    }
}

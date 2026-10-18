using System.Text.Json;
using System.Text.Json.Serialization;
using FazStellarisModmanager.Core.IO;

namespace FazStellarisModmanager.Core.Addons;

/// <summary>Keeps <see cref="AddonSettings"/> in addons.json (see <see cref="AppPaths.Addons"/>).</summary>
public static class AddonSettingsStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Missing, unreadable or corrupt files give the defaults (addons off): the game must never get a half-read addon.</summary>
    public static AddonSettings Load(string file)
    {
        if (!File.Exists(file)) return new AddonSettings();
        try { return Clean(JsonSerializer.Deserialize<AddonSettings>(File.ReadAllText(file), Json) ?? new AddonSettings()); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return new AddonSettings(); }
    }

    public static void Save(string file, AddonSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        AtomicFile.WriteAllText(file, JsonSerializer.Serialize(settings, Json));
    }

    /// <summary>
    /// Without "key": null and blank keys, which a hand-edited file or a session host can send; neither means anything. The same goes for
    /// null techs and null items in a tech's lists; a tech's null texts become empty (which the validator reports).
    /// </summary>
    internal static AddonSettings Clean(AddonSettings s)
    {
        if (s.Marketplace?.Resources is { } resources)
        {
            var kept = new Dictionary<string, ResourceOverride>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in resources.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                if ((ResourceOverride?)value is not null && !string.IsNullOrWhiteSpace(key)) kept[key] = value;
            s = s with { Marketplace = s.Marketplace with { Resources = kept.Count == 0 ? null : kept } };
        }
        if (s.Technologies is { } technologies)
        {
            var techs = (technologies.Techs ?? []).Where(t => (CustomTech?)t is not null).Select(Clean).ToList();
            s = s with { Technologies = technologies with { Techs = techs.Count == 0 ? null : techs } };
        }
        return s;
    }

    static CustomTech Clean(CustomTech t) => t with
    {
        Key = t.Key ?? "",
        Name = t.Name ?? "",
        Description = t.Description ?? "",
        Icon = t.Icon ?? "",
        Area = t.Area ?? "",
        Category = t.Category ?? "",
        Ethics = Kept(t.Ethics?.Where(e => (string?)e is not null)),
        Prerequisites = Kept(t.Prerequisites?.Where(p => (string?)p is not null)),
        Modifiers = Kept(t.Modifiers?.Where(m => (TechModifier?)m is not null).Select(m => m with { Key = m.Key ?? "" })),
    };

    static IReadOnlyList<T>? Kept<T>(IEnumerable<T>? items) => items?.ToList() is { Count: > 0 } list ? list : null;
}

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

    /// <summary>Without "key": null and blank keys, which a hand-edited file or a session host can send; neither means anything.</summary>
    internal static AddonSettings Clean(AddonSettings s)
    {
        if (s.Marketplace?.Resources is not { } resources) return s;
        var kept = new Dictionary<string, ResourceOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in resources.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if ((ResourceOverride?)value is not null && !string.IsNullOrWhiteSpace(key)) kept[key] = value;
        return s with { Marketplace = s.Marketplace with { Resources = kept.Count == 0 ? null : kept } };
    }
}

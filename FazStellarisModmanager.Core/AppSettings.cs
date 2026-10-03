using System.Text.Json;
using FazStellarisModmanager.Core.IO;

namespace FazStellarisModmanager.Core;

/// <summary>User overrides. Null means auto-detect (game dir, user dir) or machine name (player name).</summary>
public sealed record AppSettings(string? GameDir = null, string? UserDir = null, string? PlayerName = null);

public static class SettingsStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load(string file)
    {
        if (!File.Exists(file)) return new AppSettings();
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), Json) ?? new AppSettings();
    }

    public static void Save(string file, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        AtomicFile.WriteAllText(file, JsonSerializer.Serialize(settings, Json));
    }
}

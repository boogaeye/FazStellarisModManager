using System.Text.Encodings.Web;
using System.Text.Json;

namespace FazStellarisModmanager.Core.Game;

/// <summary>Contents of Documents\Paradox Interactive\Stellaris\dlc_load.json. Mod paths are relative to the user dir, e.g. "mod/ugc_123.mod".</summary>
public sealed record DlcLoad(List<string> EnabledMods, List<string> DisabledDlcs);

public static class DlcLoadFile
{
    public const string FileName = "dlc_load.json";

    static readonly JsonSerializerOptions WriteOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static DlcLoad Read(string userDir)
    {
        var path = Path.Combine(userDir, FileName);
        if (!File.Exists(path)) return new DlcLoad([], []);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return new DlcLoad(ReadArray(doc.RootElement, "enabled_mods"), ReadArray(doc.RootElement, "disabled_dlcs"));
    }

    static List<string> ReadArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
            : [];

    /// <summary>Writes dlc_load.json after copying the existing file into backupDir. Returns the backup path, or null if there was no file.</summary>
    public static string? Write(string userDir, DlcLoad load, string backupDir)
    {
        var path = Path.Combine(userDir, FileName);
        string? backup = null;
        if (File.Exists(path))
        {
            Directory.CreateDirectory(backupDir);
            backup = Path.Combine(backupDir, $"dlc_load_{DateTime.Now:yyyyMMdd_HHmmss_fff}.json");
            File.Copy(path, backup, overwrite: true);
        }
        Directory.CreateDirectory(userDir);
        var json = JsonSerializer.Serialize(new { disabled_dlcs = load.DisabledDlcs, enabled_mods = load.EnabledMods }, WriteOptions);
        File.WriteAllText(path, json);
        return backup;
    }
}

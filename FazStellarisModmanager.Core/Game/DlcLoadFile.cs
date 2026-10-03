using System.Text.Encodings.Web;
using System.Text.Json;

namespace FazStellarisModmanager.Core.Game;

/// <summary>Contents of Documents\Paradox Interactive\Stellaris\dlc_load.json. Mod paths are relative to the user dir, e.g. "mod/ugc_123.mod".</summary>
public sealed record DlcLoad(List<string> EnabledMods, List<string> DisabledDlcs);

public static class DlcLoadFile
{
    public const string FileName = "dlc_load.json";

    static readonly JsonSerializerOptions WriteOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Reads dlc_load.json. A missing, empty or whitespace-only file yields an empty DlcLoad. Throws InvalidDataException
    /// rather than returning empty for corrupt files, so a later Write can't wipe the user's list.
    /// </summary>
    public static DlcLoad Read(string userDir)
    {
        var path = Path.Combine(userDir, FileName);
        if (!File.Exists(path)) return new DlcLoad([], []);
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new DlcLoad([], []);
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"'{path}' is not a JSON object.");
            return new DlcLoad(ReadArray(doc.RootElement, "enabled_mods", path), ReadArray(doc.RootElement, "disabled_dlcs", path));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new InvalidDataException($"'{path}' is not a valid dlc_load.json: {e.Message}", e);
        }
    }

    static List<string> ReadArray(JsonElement root, string name, string path)
    {
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind == JsonValueKind.Null) return [];
        if (arr.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"'{path}': '{name}' is not an array.");
        var list = new List<string>();
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"'{path}': '{name}' contains a non-string element.");
            var s = e.GetString();
            if (!string.IsNullOrEmpty(s)) list.Add(s);
        }
        return list;
    }

    /// <summary>Writes dlc_load.json after copying the existing file into backupDir. Returns the backup path, or null if there was no file.</summary>
    public static string? Write(string userDir, DlcLoad load, string backupDir)
    {
        var path = Path.Combine(userDir, FileName);
        string? backup = null;
        if (File.Exists(path))
        {
            Directory.CreateDirectory(backupDir);
            backup = CopyToUniqueBackup(path, backupDir);
        }
        Directory.CreateDirectory(userDir);
        // Unknown root keys are not preserved (Stellaris only uses these two).
        var json = JsonSerializer.Serialize(new { disabled_dlcs = load.DisabledDlcs, enabled_mods = load.EnabledMods }, WriteOptions);
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
        return backup;
    }

    static string CopyToUniqueBackup(string source, string backupDir)
    {
        var stem = $"dlc_load_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}";
        var target = Path.Combine(backupDir, stem + ".json");
        for (var i = 1; File.Exists(target); i++)
            target = Path.Combine(backupDir, $"{stem}_{i}.json");
        File.Copy(source, target, overwrite: false);
        return target;
    }
}

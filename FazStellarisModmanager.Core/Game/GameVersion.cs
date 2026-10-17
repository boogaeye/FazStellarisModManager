using System.Text.Json;

namespace FazStellarisModmanager.Core.Game;

/// <summary>
/// The installed game's version as the game folder states it: launcher-settings.json's "rawVersion" (e.g. "v4.5.2"). It is the same on
/// every PC with the same game version, unlike game.log, which only exists once the game ran.
/// </summary>
public static class GameVersion
{
    public const string FileName = "launcher-settings.json";

    static readonly JsonDocumentOptions Options = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>"rawVersion" of &lt;gameDir&gt;/launcher-settings.json, trimmed; null when the folder or file is missing, unreadable or corrupt, or has no such text.</summary>
    public static string? Read(string? gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir)) return null;
        try
        {
            var file = Path.Combine(gameDir, FileName);
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file), Options);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("rawVersion", out var raw)
                && raw.ValueKind == JsonValueKind.String
                && raw.GetString()?.Trim() is { Length: > 0 } version
                ? version
                : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

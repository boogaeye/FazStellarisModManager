using System.Text.Json;
using FazStellarisModmanager.Core.IO;

namespace FazStellarisModmanager.Core.Saves;

public static class LiveViewer
{
    /// <summary>
    /// The country the user plays in <paramref name="snapshot"/>: the only player's country; else the player whose
    /// name matches one of <paramref name="myNames"/> (ignoring case); else <paramref name="remembered"/> if that is
    /// one of the players' countries; else null (the user must choose).
    /// </summary>
    public static int? Resolve(GameSnapshot snapshot, IEnumerable<string?> myNames, int? remembered)
    {
        if (snapshot.Players.Count == 1) return snapshot.Players[0].CountryId;
        foreach (var name in myNames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var match = snapshot.Players.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.CountryId;
        }
        return remembered is int r && snapshot.Players.Any(p => p.CountryId == r) ? r : null;
    }
}

/// <summary>Remembers which country the user picked per save name (live-game.json). Unreadable files count as empty.</summary>
public sealed class LiveViewerStore(string path)
{
    public int? Get(string saveName) => Load().TryGetValue(saveName, out var id) ? id : null;

    public void Set(string saveName, int countryId)
    {
        var map = Load();
        map[saveName] = countryId;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(map));
    }

    Dictionary<string, int> Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

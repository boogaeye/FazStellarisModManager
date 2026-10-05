namespace FazStellarisModmanager.Core.Saves;

/// <summary>
/// A stat as saved, plus overflow detection: these stats can't be negative, so a negative value means the game's
/// 32-bit (×1000) number wrapped. <see cref="Real"/> undoes one wrap.
/// </summary>
public readonly record struct StatValue(double Raw)
{
    public const double Wrap = 4294967.296;
    public bool Overflowed => Raw < 0;
    public double Real => Overflowed ? Raw + Wrap : Raw;
}

/// <summary>One scoreboard row. Unknown (not contacted) rows have Name "???" and no stats, flag or player.</summary>
public sealed record BoardRow(
    int Id, int Rank, string Name, CountryFlag? Flag, bool Known, bool IsViewer, string? PlayerName,
    StatValue? Score, StatValue? Military, StatValue? Economy, StatValue? Tech, StatValue? Fleet);

public static class LiveBoard
{
    /// <summary>Countries with a victory rank, in rank order, as seen by <paramref name="viewerId"/>.</summary>
    public static IReadOnlyList<BoardRow> Build(GameSnapshot snapshot, int viewerId)
    {
        var viewer = snapshot.Countries.FirstOrDefault(c => c.Id == viewerId);
        var contacted = viewer?.ContactedIds.ToHashSet() ?? [];
        var players = new Dictionary<int, string>();
        foreach (var p in snapshot.Players) players.TryAdd(p.CountryId, p.Name);

        return snapshot.Countries
            .Where(c => c.VictoryRank > 0)
            .OrderBy(c => c.VictoryRank).ThenBy(c => c.Id)
            .Select(c =>
            {
                var isViewer = c.Id == viewerId;
                if (!isViewer && !contacted.Contains(c.Id))
                    return new BoardRow(c.Id, c.VictoryRank, "???", null, false, false, null, null, null, null, null, null);
                return new BoardRow(c.Id, c.VictoryRank, CountryNames.Display(c), c.Flag, true, isViewer, players.GetValueOrDefault(c.Id),
                    new StatValue(c.VictoryScore), new StatValue(c.MilitaryPower), new StatValue(c.EconomyPower),
                    new StatValue(c.TechPower), new StatValue(c.FleetSize));
            })
            .ToList();
    }
}

/// <summary>Readable country names without localisation (part 3 replaces this with the game's own text).</summary>
public static class CountryNames
{
    static readonly string[] Prefixes = ["SPEC_", "EMPIRE_DESIGN_", "NAME_"];

    public static string Display(SaveCountry c)
    {
        var key = c.NameKey;
        if (string.IsNullOrWhiteSpace(key)) return $"Empire {c.Id}";
        if (key.Contains('%'))
        {
            var parts = c.NameVariables.Values.Select(Pretty).Where(p => p.Length > 0).ToList();
            return parts.Count > 0 ? string.Join(" ", parts) : $"Empire {c.Id}";
        }
        if (!key.Contains('_')) return key;
        var pretty = Pretty(key);
        return c.Adjective is { Length: > 0 } adj && !adj.Contains('%') && !adj.Contains('_') ? adj + " " + pretty : pretty;
    }

    static string Pretty(string value)
    {
        foreach (var p in Prefixes)
            if (value.StartsWith(p, StringComparison.Ordinal)) { value = value[p.Length..]; break; }
        return value.Replace('_', ' ').Trim();
    }
}

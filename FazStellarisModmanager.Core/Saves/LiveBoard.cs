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
    StatValue? Score, StatValue? Military, StatValue? Economy, StatValue? Tech, StatValue? Fleet,
    StatValue? EmpireSize = null, StatValue? Pops = null);

/// <summary>A sortable, rankable scoreboard stat.</summary>
public enum BoardStat { Score, Military, Economy, Tech, Fleet, EmpireSize, Pops }

public static class LiveBoard
{
    /// <summary>Country types that are real empires; primitives, leviathans, marauders etc. share a placeholder rank.</summary>
    static readonly HashSet<string> EmpireTypes = new(StringComparer.OrdinalIgnoreCase) { "default", "fallen_empire", "awakened_fallen_empire" };

    /// <summary>Countries with a victory rank, in rank order, as seen by <paramref name="viewerId"/>.</summary>
    public static IReadOnlyList<BoardRow> Build(GameSnapshot snapshot, int viewerId)
    {
        var viewer = snapshot.Countries.FirstOrDefault(c => c.Id == viewerId);
        var contacted = viewer?.ContactedIds.ToHashSet() ?? [];
        var players = new Dictionary<int, string>();
        foreach (var p in snapshot.Players) players.TryAdd(p.CountryId, p.Name);

        return snapshot.Countries
            .Where(c => c.VictoryRank > 0 && EmpireTypes.Contains(c.Type ?? ""))
            .OrderBy(c => c.VictoryRank).ThenBy(c => c.Id)
            .Select(c =>
            {
                var isViewer = c.Id == viewerId;
                if (!isViewer && !contacted.Contains(c.Id))
                    return new BoardRow(c.Id, c.VictoryRank, "???", null, false, false, null, null, null, null, null, null, null, null);
                return new BoardRow(c.Id, c.VictoryRank, CountryNames.Display(c), c.Flag, true, isViewer, players.GetValueOrDefault(c.Id),
                    new StatValue(c.VictoryScore), new StatValue(c.MilitaryPower), new StatValue(c.EconomyPower),
                    new StatValue(c.TechPower), new StatValue(c.FleetSize),
                    new StatValue(c.EmpireSize), new StatValue(c.Pops));
            })
            .ToList();
    }

    /// <summary>Stats where a smaller value ranks better.</summary>
    public static bool LowerIsBetter(BoardStat s) => s == BoardStat.EmpireSize;

    public static StatValue? Value(BoardRow row, BoardStat stat) => stat switch
    {
        BoardStat.Score => row.Score,
        BoardStat.Military => row.Military,
        BoardStat.Economy => row.Economy,
        BoardStat.Tech => row.Tech,
        BoardStat.Fleet => row.Fleet,
        BoardStat.EmpireSize => row.EmpireSize,
        BoardStat.Pops => row.Pops,
        _ => null,
    };

    /// <summary>
    /// Country id to stat to 1-based rank (highest first, ties share a rank: 1, 2, 2, 4). Score uses the game's global
    /// victory rank; the other stats are ranked among the known rows. Unknown rows have no entry.
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyDictionary<BoardStat, int>> Ranks(IReadOnlyList<BoardRow> rows)
    {
        var known = rows.Where(r => r.Known).ToList();
        var result = known.ToDictionary(r => r.Id, r => new Dictionary<BoardStat, int> { [BoardStat.Score] = r.Rank });
        foreach (var stat in Enum.GetValues<BoardStat>())
        {
            if (stat == BoardStat.Score) continue;
            var values = known.Select(r => Value(r, stat)?.Real ?? 0).ToList();
            for (var i = 0; i < known.Count; i++)
                result[known[i].Id][stat] = 1 + (LowerIsBetter(stat) ? values.Count(v => v < values[i]) : values.Count(v => v > values[i]));
        }
        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<BoardStat, int>)kv.Value);
    }

    /// <summary>
    /// Best-first (rank #1 first) or worst-first. Score uses victory order; other stats order known rows by value with ties
    /// by victory rank. Worst-first puts unknown rows last, in victory order. Best-first Score is plain victory order.
    /// </summary>
    public static IReadOnlyList<BoardRow> Sort(IReadOnlyList<BoardRow> rows, BoardStat stat, bool worstFirst = false)
    {
        if (stat == BoardStat.Score)
        {
            if (!worstFirst) return rows.OrderBy(r => r.Rank).ThenBy(r => r.Id).ToList();
            return rows.Where(r => r.Known).OrderByDescending(r => r.Rank).ThenBy(r => r.Id).Concat(rows.Where(r => !r.Known).OrderBy(r => r.Rank).ThenBy(r => r.Id)).ToList();
        }
        var descending = LowerIsBetter(stat) == worstFirst;
        var k = rows.Where(r => r.Known);
        var ordered = descending
            ? k.OrderByDescending(r => Value(r, stat)?.Real ?? 0)
            : k.OrderBy(r => Value(r, stat)?.Real ?? 0);
        return ordered.ThenBy(r => r.Rank).ThenBy(r => r.Id)
            .Concat(rows.Where(r => !r.Known).OrderBy(r => r.Rank).ThenBy(r => r.Id)).ToList();
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
            var parts = c.NameVariables.Values.Select(Pretty).Where(p => p.Length > 0 && !p.Contains('%')).ToList();
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

namespace FazStellarisModmanager.Core.Conflicts;

/// <summary>Files of one mod overwritten by the mod at <see cref="WinnerIndex"/> (the last mod in load order with the same path).</summary>
public sealed record ConflictGroup(int WinnerIndex, IReadOnlyList<string> Files);

/// <summary>How much of one mod's content later mods overwrite.</summary>
public sealed record ModConflicts(int Total, int Overwritten, IReadOnlyList<ConflictGroup> Groups)
{
    /// <summary>Overwritten share in whole percent, rounded down (0 for a mod without content).</summary>
    public int Percent => Total == 0 ? 0 : (int)(Overwritten * 100L / Total);

    public bool IsHeavy => Total > 0 && Percent >= ConflictAnalyzer.HeavyPercent;
}

/// <summary>File-level overrides in a load order: a later mod's file replaces an earlier mod's file at the same path (case-insensitive).</summary>
public static class ConflictAnalyzer
{
    public const int HeavyPercent = 75;

    /// <summary>One result per mod, in the same order as <paramref name="filesInLoadOrder"/>.</summary>
    public static IReadOnlyList<ModConflicts> Analyze(IReadOnlyList<IReadOnlyCollection<string>> filesInLoadOrder)
    {
        var sets = filesInLoadOrder.Select(files =>
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return files.Where(seen.Add).ToList();
        }).ToList();

        var last = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sets.Count; i++)
            foreach (var f in sets[i]) last[f] = i;

        var result = new List<ModConflicts>(sets.Count);
        for (var i = 0; i < sets.Count; i++)
        {
            var index = i;
            var groups = sets[i]
                .Where(f => last[f] > index)
                .GroupBy(f => last[f])
                .OrderBy(g => g.Key)
                .Select(g => new ConflictGroup(g.Key, g.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()))
                .ToList();
            result.Add(new ModConflicts(sets[i].Count, groups.Sum(g => g.Files.Count), groups));
        }
        return result;
    }
}

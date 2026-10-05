namespace FazStellarisModmanager.Core.Conflicts;

/// <summary>A scanned script file: its content-relative path (forward slashes) and the names it defines.</summary>
public sealed record ScriptFile(string Path, IReadOnlyList<string> Names);

/// <summary>One mod's scanned script files.</summary>
public sealed record ModScripts(IReadOnlyList<ScriptFile> Files);

public enum LossReason
{
    /// <summary>A mod later in the list has a file at the same path, so this whole file is not loaded.</summary>
    FileReplaced,
    /// <summary>Last-wins folder: the other definition's file loads later (file-name order).</summary>
    LoadsBeforeWinner,
    /// <summary>First-wins folder: the other definition's file loads earlier.</summary>
    LoadsAfterWinner,
    /// <summary>Duplicated folder or event id: both load (or the game keeps an unpredictable one).</summary>
    Duplicate,
}

/// <summary>A definition of the mod at <see cref="Mod"/> that the game does not use (or loads twice), because of the mod at <see cref="OtherMod"/>.</summary>
public sealed record DefinitionLoss(int Mod, string Folder, string Name, string File, int OtherMod, string OtherFile, LossReason Reason);

/// <summary>Per-mod definition counts (in list order) and every cross-mod loss.</summary>
public sealed record DefinitionReport(IReadOnlyList<int> DefinitionCounts, IReadOnlyList<DefinitionLoss> Losses);

/// <summary>Works out which definitions win in a load order: same path replaces the whole file (last mod wins), then files load in file-name order and the folder's <see cref="OverrideRule"/> decides.</summary>
public static class DefinitionAnalyzer
{
    public static DefinitionReport Analyze(IReadOnlyList<ModScripts> mods)
    {
        var counts = new int[mods.Count];
        var losses = new List<DefinitionLoss>();

        var owner = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < mods.Count; i++)
            foreach (var f in mods[i].Files)
                owner[Normalize(f.Path)] = i;

        var surviving = new List<(int Mod, ScriptFile File, string Folder)>();
        for (var i = 0; i < mods.Count; i++)
        {
            foreach (var f in mods[i].Files)
            {
                if (DefinitionRules.Classify(f.Path) is not { } c) continue;
                var names = f.Names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                counts[i] += names.Count;
                var winner = owner[Normalize(f.Path)];
                if (winner == i)
                {
                    surviving.Add((i, f, c.Folder));
                    continue;
                }
                foreach (var n in names) losses.Add(new DefinitionLoss(i, c.Folder, n, f.Path, winner, f.Path, LossReason.FileReplaced));
            }
        }

        foreach (var folder in surviving.GroupBy(s => s.Folder, StringComparer.OrdinalIgnoreCase))
        {
            var rule = DefinitionRules.RuleFor(folder.Key);
            var byName = new Dictionary<string, List<(int Mod, string File)>>(StringComparer.OrdinalIgnoreCase);
            var loadOrder = folder
                .OrderBy(s => System.IO.Path.GetFileName(s.File.Path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.File.Path, StringComparer.OrdinalIgnoreCase);
            foreach (var s in loadOrder)
            {
                foreach (var n in s.File.Names.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!byName.TryGetValue(n, out var defs)) byName[n] = defs = [];
                    defs.Add((s.Mod, s.File.Path));
                }
            }

            foreach (var (name, defs) in byName)
            {
                if (defs.Count < 2) continue;
                switch (rule)
                {
                    case OverrideRule.LastWins:
                        Lose(defs[^1], defs.Take(defs.Count - 1), LossReason.LoadsBeforeWinner);
                        break;
                    case OverrideRule.FirstWins:
                        Lose(defs[0], defs.Skip(1), LossReason.LoadsAfterWinner);
                        break;
                    default:
                        foreach (var d in defs)
                        {
                            var other = defs.FirstOrDefault(o => o.Mod != d.Mod);
                            if (other.File is not null) losses.Add(new DefinitionLoss(d.Mod, folder.Key, name, d.File, other.Mod, other.File, LossReason.Duplicate));
                        }
                        break;
                }

                void Lose((int Mod, string File) winner, IEnumerable<(int Mod, string File)> losers, LossReason reason)
                {
                    foreach (var d in losers)
                        if (d.Mod != winner.Mod) losses.Add(new DefinitionLoss(d.Mod, folder.Key, name, d.File, winner.Mod, winner.File, reason));
                }
            }
        }

        return new DefinitionReport(counts, losses);
    }

    static string Normalize(string path) => path.Replace((char)92, '/');
}

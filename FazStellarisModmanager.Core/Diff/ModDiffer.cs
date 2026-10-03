using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Diff;

public enum UnitStatus { Ok, Missing, Extra, ContentMismatch }

public sealed record FileDiff(List<string> Changed, List<string> OnlyInTarget, List<string> OnlyInMine)
{
    public bool IsEmpty => Changed.Count == 0 && OnlyInTarget.Count == 0 && OnlyInMine.Count == 0;
}

/// <summary>One DLC or mod compared between target (host) and mine. Orders are 1-based load positions.</summary>
public sealed record UnitDiff(
    string Key,
    string Name,
    string? RemoteId,
    UnitStatus Status,
    int? TargetOrder,
    int? MineOrder,
    bool OutOfOrder,
    string? TargetVersion,
    string? MineVersion,
    FileDiff? Files);

public sealed record DiffResult(string TargetGameVersion, string MineGameVersion, FileDiff BaseFiles, List<UnitDiff> Dlcs, List<UnitDiff> Mods)
{
    public bool GameVersionMatches => TargetGameVersion == MineGameVersion;
    public bool OrderMatches => !Mods.Any(m => m.OutOfOrder);

    public bool IsMatch => GameVersionMatches && BaseFiles.IsEmpty && OrderMatches
        && Dlcs.All(d => d.Status == UnitStatus.Ok) && Mods.All(m => m.Status == UnitStatus.Ok);
}

public static class ModDiffer
{
    static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    /// <summary>Compares my snapshot against the target (host). Result lists target units in load order, then my extras.</summary>
    public static DiffResult Diff(MachineSnapshot target, MachineSnapshot mine) =>
        new(target.GameVersion, mine.GameVersion,
            DiffFiles(target.Base.Files, mine.Base.Files),
            DiffUnits(target.Dlcs, mine.Dlcs, checkOrder: false),
            DiffUnits(target.Mods, mine.Mods, checkOrder: true));

    static List<UnitDiff> DiffUnits(List<ModSnapshot> target, List<ModSnapshot> mine, bool checkOrder)
    {
        var t = Index(target);
        var m = Index(mine);

        // Order is compared only over units both sides have, so a missing mod does not flag every later one.
        var outOfOrder = new HashSet<string>(Cmp);
        if (checkOrder)
        {
            var sharedT = t.Values.OrderBy(u => u.LoadOrder).Select(u => u.Key).Where(m.ContainsKey).ToList();
            var sharedM = m.Values.OrderBy(u => u.LoadOrder).Select(u => u.Key).Where(t.ContainsKey).ToList();
            for (int i = 0; i < sharedT.Count; i++)
                if (!Cmp.Equals(sharedT[i], sharedM[i])) outOfOrder.Add(sharedT[i]);
        }

        var result = new List<UnitDiff>();
        foreach (var x in t.Values.OrderBy(u => u.LoadOrder))
        {
            if (!m.TryGetValue(x.Key, out var y))
            {
                result.Add(new UnitDiff(x.Key, x.Name, x.RemoteId, UnitStatus.Missing, x.LoadOrder, null, false, x.Version, null, null));
                continue;
            }
            var files = DiffFiles(x.Files, y.Files);
            result.Add(new UnitDiff(x.Key, x.Name, x.RemoteId,
                files.IsEmpty ? UnitStatus.Ok : UnitStatus.ContentMismatch,
                x.LoadOrder, y.LoadOrder, outOfOrder.Contains(x.Key), x.Version, y.Version,
                files.IsEmpty ? null : files));
        }
        foreach (var y in m.Values.Where(u => !t.ContainsKey(u.Key)).OrderBy(u => u.LoadOrder))
            result.Add(new UnitDiff(y.Key, y.Name, y.RemoteId, UnitStatus.Extra, null, y.LoadOrder, false, null, y.Version, null));
        return result;
    }

    public static FileDiff DiffFiles(List<ModFile> target, List<ModFile> mine)
    {
        var t = IndexFiles(target);
        var m = IndexFiles(mine);
        return new FileDiff(
            t.Keys.Where(p => m.TryGetValue(p, out var o) && o.Md5 != t[p].Md5).Order(Cmp).ToList(),
            t.Keys.Where(p => !m.ContainsKey(p)).Order(Cmp).ToList(),
            m.Keys.Where(p => !t.ContainsKey(p)).Order(Cmp).ToList());
    }

    // First occurrence wins on duplicate keys / case-variant paths, so malformed snapshots never throw.
    static Dictionary<string, ModSnapshot> Index(List<ModSnapshot> units)
    {
        var d = new Dictionary<string, ModSnapshot>(Cmp);
        foreach (var u in units) d.TryAdd(u.Key, u);
        return d;
    }

    static Dictionary<string, ModFile> IndexFiles(List<ModFile> files)
    {
        var d = new Dictionary<string, ModFile>(Cmp);
        foreach (var f in files) d.TryAdd(f.Path, f);
        return d;
    }
}

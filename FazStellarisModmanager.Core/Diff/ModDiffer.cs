using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Diff;

/// <summary>Only <c>ugc:</c> keys (Workshop) can be auto-installed; <c>local:</c> units that are Missing or ContentMismatch need manual action.</summary>
public enum UnitStatus { Ok, Missing, Extra, ContentMismatch }

public sealed record FileDiff(List<string> Changed, List<string> OnlyInTarget, List<string> OnlyInMine)
{
    public bool IsEmpty => Changed.Count == 0 && OnlyInTarget.Count == 0 && OnlyInMine.Count == 0;
}

/// <summary>
/// One DLC or mod compared between target (host) and mine. Orders are 1-based load positions.
/// Only <c>ugc:</c> keys (Workshop) can be auto-installed; <c>local:</c> Missing or ContentMismatch units need manual action.
/// MineKey/MineName name my paired mod (or the Extra one); Match says how it was paired; LocalCopyOfWorkshop marks a pair where exactly one side is the Workshop item.
/// </summary>
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
    FileDiff? Files,
    string? MineKey = null,
    MatchKind Match = MatchKind.Key,
    bool LocalCopyOfWorkshop = false,
    string? MineName = null);

public sealed record DiffResult(string TargetGameVersion, string MineGameVersion, FileDiff BaseFiles, List<UnitDiff> Dlcs, List<UnitDiff> Mods,
    List<string> TargetWarnings, List<string> MineWarnings)
{
    /// <summary>
    /// False when either scan reported warnings: some differences may come from unreadable files rather than real
    /// differences, so the UI should not auto-fix based on this result.
    /// </summary>
    public bool IsReliable => TargetWarnings.Count == 0 && MineWarnings.Count == 0;

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
            DiffUnits(target.Dlcs, mine.Dlcs, mods: false), // DLC order is intentionally not checked
            DiffUnits(target.Mods, mine.Mods, mods: true),
            target.Warnings ?? [], mine.Warnings ?? []);

    // Mods pair by identity (key, Workshop id, name, files); DLCs by key only. Order is compared only over paired units,
    // so a missing mod does not flag every later one: the minimal set of pairs outside a longest increasing subsequence
    // of my positions (in target order) is flagged.
    static List<UnitDiff> DiffUnits(List<ModSnapshot> target, List<ModSnapshot> mine, bool mods)
    {
        var t = Index(target).Values.OrderBy(u => u.LoadOrder).ToList();
        var m = Index(mine).Values.OrderBy(u => u.LoadOrder).ToList();
        var pairs = ModMatcher.Pair(t, m, ModIdentity.Of, ModIdentity.Of, mods ? ModMatcher.AllRules : [MatchKind.Key]);
        var partner = pairs.ToDictionary(p => p.Target.Key, p => p, Cmp);
        var pairedMine = new HashSet<string>(pairs.Select(p => p.Mine.Key), Cmp);

        var outOfOrder = new HashSet<string>(Cmp);
        if (mods)
        {
            var rank = new Dictionary<string, int>(Cmp);
            foreach (var u in m.Where(u => pairedMine.Contains(u.Key))) rank[u.Key] = rank.Count;
            var shared = t.Where(u => partner.ContainsKey(u.Key)).ToList();
            var keep = LisIndices(shared.Select(u => rank[partner[u.Key].Mine.Key]).ToList());
            for (int i = 0; i < shared.Count; i++)
                if (!keep.Contains(i)) outOfOrder.Add(shared[i].Key);
        }

        var result = new List<UnitDiff>();
        foreach (var x in t)
        {
            if (!partner.TryGetValue(x.Key, out var p))
            {
                result.Add(new UnitDiff(x.Key, x.Name, x.RemoteId, UnitStatus.Missing, x.LoadOrder, null, false, x.Version, null, null));
                continue;
            }
            var y = p.Mine;
            var files = DiffFiles(x.Files, y.Files);
            result.Add(new UnitDiff(x.Key, x.Name, x.RemoteId,
                files.IsEmpty ? UnitStatus.Ok : UnitStatus.ContentMismatch,
                x.LoadOrder, y.LoadOrder, outOfOrder.Contains(x.Key), x.Version, y.Version,
                files.IsEmpty ? null : files,
                y.Key, p.Kind, IsLocalCopyOfWorkshop(x, y), y.Name));
        }
        foreach (var y in m.Where(u => !pairedMine.Contains(u.Key)))
            result.Add(new UnitDiff(y.Key, y.Name, y.RemoteId, UnitStatus.Extra, null, y.LoadOrder, false, null, y.Version, null,
                y.Key, MatchKind.Key, false, y.Name));
        return result;
    }

    // Same Workshop item on both sides, but exactly one side uses the ugc_<id>.mod Workshop descriptor.
    static bool IsLocalCopyOfWorkshop(ModSnapshot x, ModSnapshot y) =>
        (ModKeys.WorkshopId(x.Key) is null) != (ModKeys.WorkshopId(y.Key) is null)
        && ModMatcher.WorkshopIdOf(x.Key, x.RemoteId) is { } id
        && id == ModMatcher.WorkshopIdOf(y.Key, y.RemoteId);

    // O(n log n) patience LIS over distinct values; ties resolve to the earliest-ending subsequence, so output is deterministic.
    static HashSet<int> LisIndices(List<int> a)
    {
        var tails = new List<int>(); // tails[k] = index of the smallest tail of an increasing run of length k+1
        var prev = new int[a.Count];
        for (int i = 0; i < a.Count; i++)
        {
            int lo = 0, hi = tails.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (a[tails[mid]] < a[i]) lo = mid + 1; else hi = mid; }
            prev[i] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count) tails.Add(i); else tails[lo] = i;
        }
        var keep = new HashSet<int>();
        for (int i = tails.Count > 0 ? tails[^1] : -1; i >= 0; i = prev[i]) keep.Add(i);
        return keep;
    }

    public static FileDiff DiffFiles(List<ModFile> target, List<ModFile> mine)
    {
        var t = IndexFiles(target);
        var m = IndexFiles(mine);
        return new FileDiff(
            t.Keys.Where(p => m.TryGetValue(p, out var o) && !Cmp.Equals(o.Md5, t[p].Md5)).Order(Cmp).ToList(),
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

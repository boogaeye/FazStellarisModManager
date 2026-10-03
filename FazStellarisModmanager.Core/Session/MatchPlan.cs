using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;

namespace FazStellarisModmanager.Core.Session;

/// <summary>What "Match host" does on this machine, and what it could not fix.</summary>
public sealed record MatchPlan(
    ModList ToApply,
    List<ModListEntry> NeedsWorkshopInstall,
    List<ModListEntry> NeedsManualInstall,
    List<UnitDiff> NeedsWorkshopUpdate,
    List<UnitDiff> DiffersLocally)
{
    public bool IsComplete =>
        NeedsWorkshopInstall.Count == 0 && NeedsManualInstall.Count == 0 && NeedsWorkshopUpdate.Count == 0 && DiffersLocally.Count == 0;

    /// <summary>
    /// The host's list restricted to mods installed here (in the host's order, pointing at this machine's descriptors),
    /// plus the host's disabled DLCs. Missing mods and content mismatches are reported, split into Workshop (fixable by
    /// sub-project 3) and local (manual).
    /// </summary>
    public static MatchPlan Create(ModList hostList, DiffResult diff, IReadOnlyList<InstalledMod> library)
    {
        var byKey = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in library) byKey.TryAdd(m.Key, m);

        var apply = new List<ModListEntry>();
        var workshop = new List<ModListEntry>();
        var manual = new List<ModListEntry>();
        foreach (var e in hostList.Mods)
        {
            if (byKey.TryGetValue(e.Key, out var mine)) apply.Add(new ModListEntry(mine.Key, mine.Name, mine.DescriptorRel, mine.RemoteId));
            else if (ModKeys.WorkshopId(e.Key) is not null) workshop.Add(e);
            else manual.Add(e);
        }

        var changed = diff.Mods.Where(u => u.Status == UnitStatus.ContentMismatch).ToList();
        return new MatchPlan(
            new ModList(hostList.Name, apply, hostList.DisabledDlcs.ToList()),
            workshop,
            manual,
            changed.Where(u => ModKeys.WorkshopId(u.Key) is not null).ToList(),
            changed.Where(u => ModKeys.WorkshopId(u.Key) is null).ToList());
    }
}

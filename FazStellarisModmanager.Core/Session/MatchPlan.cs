using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Session;

/// <summary>What "Match host" does on this machine, and what it could not fix.</summary>
public sealed record MatchPlan(
    ModList ToApply,
    List<ModListEntry> NeedsWorkshopInstall,
    List<ModListEntry> NeedsManualInstall,
    List<UnitDiff> NeedsWorkshopUpdate,
    List<UnitDiff> DiffersLocally,
    List<UnitDiff> NeedsDlc)
{
    public bool IsComplete =>
        NeedsWorkshopInstall.Count == 0 && NeedsManualInstall.Count == 0 && NeedsWorkshopUpdate.Count == 0
        && DiffersLocally.Count == 0 && NeedsDlc.Count == 0;

    /// <summary>
    /// The host list restricted to mods installed here (in the host order, pointing at this machine descriptors),
    /// plus the host disabled DLCs and any DLC installed here that the host does not use. Missing mods and content
    /// mismatches are reported, split into Workshop (fixable by sub-project 3) and local (manual); DLCs the host has
    /// and this PC lacks are reported in NeedsDlc. <paramref name="hostList"/> and <paramref name="diff"/> must come
    /// from the same host target.
    /// </summary>
    public static MatchPlan Create(ModList hostList, DiffResult diff, IReadOnlyList<InstalledMod> library, MachineSnapshot mine)
    {
        var byKey = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in library) byKey.TryAdd(m.Key, m);

        var apply = new List<ModListEntry>();
        var workshop = new List<ModListEntry>();
        var manual = new List<ModListEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in hostList.Mods)
        {
            if (!seen.Add(e.Key)) continue;
            if (byKey.TryGetValue(e.Key, out var inst)) apply.Add(new ModListEntry(inst.Key, inst.Name, inst.DescriptorRel, inst.RemoteId));
            else if (ModKeys.WorkshopId(e.Key) is not null) workshop.Add(e);
            else manual.Add(e);
        }

        var disabled = hostList.DisabledDlcs.ToList();
        foreach (var u in diff.Dlcs.Where(d => d.Status == UnitStatus.Extra))
        {
            var dlc = mine.Dlcs.FirstOrDefault(d => string.Equals(d.Key, u.Key, StringComparison.OrdinalIgnoreCase));
            if (dlc is not null && !disabled.Contains(dlc.Descriptor, StringComparer.OrdinalIgnoreCase)) disabled.Add(dlc.Descriptor);
        }

        var changed = diff.Mods.Where(u => u.Status == UnitStatus.ContentMismatch).ToList();
        return new MatchPlan(
            new ModList(hostList.Name, apply, disabled),
            workshop,
            manual,
            changed.Where(u => ModKeys.WorkshopId(u.Key) is not null).ToList(),
            changed.Where(u => ModKeys.WorkshopId(u.Key) is null).ToList(),
            diff.Dlcs.Where(d => d.Status == UnitStatus.Missing).ToList());
    }
}

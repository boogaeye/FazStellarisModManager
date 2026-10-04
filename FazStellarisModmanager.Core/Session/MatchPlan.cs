using System.Text.RegularExpressions;
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
    /// plus the host disabled DLCs and any DLC installed here that the host does not use.
    /// Each host mod uses my paired mod (by key, Workshop id, name or files), except that a mod I have installed under
    /// the host's exact key beats a copy the diff paired. Unpaired host mods with a Workshop id need a Workshop install,
    /// others a manual one. Content mismatches need a Workshop update when my mod is the Workshop item, otherwise local
    /// attention. DLCs the host has and this PC lacks are reported in NeedsDlc.
    /// <paramref name="hostList"/> and <paramref name="diff"/> must come from the same host target.
    /// </summary>
    public static MatchPlan Create(ModList hostList, DiffResult diff, IReadOnlyList<InstalledMod> library, MachineSnapshot mine)
    {
        var byKey = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in library) byKey.TryAdd(m.Key, m);

        // My partner for each host mod: first the pairing the diff made (enabled mods, using files too), then a pairing
        // against my whole library (installed but not enabled) by key, Workshop id or name.
        var hostEntries = hostList.Mods.DistinctBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToList();
        var partner = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in diff.Mods)
            if (u.Status is (UnitStatus.Ok or UnitStatus.ContentMismatch) && u.MineKey is { } mineKey && byKey.TryGetValue(mineKey, out var paired))
                partner.TryAdd(u.Key, paired);
        var taken = new HashSet<string>(partner.Values.Select(i => i.Key), StringComparer.OrdinalIgnoreCase);

        // The diff paired a host mod with a copy (other key), but I also have the host's exact mod installed, e.g. the
        // Workshop item itself: use that, it is what the host runs. The copy is freed for the library pairing below.
        var replaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hostKey, copy) in partner.ToList())
            if (!string.Equals(copy.Key, hostKey, StringComparison.OrdinalIgnoreCase)
                && byKey.TryGetValue(hostKey, out var exact) && !taken.Contains(exact.Key))
            {
                partner[hostKey] = exact;
                taken.Remove(copy.Key);
                taken.Add(exact.Key);
                replaced.Add(hostKey);
            }

        var unpaired = hostEntries.Where(e => !partner.ContainsKey(e.Key)).ToList();
        var free = library.Where(i => !taken.Contains(i.Key)).ToList();
        foreach (var p in ModMatcher.Pair(unpaired, free, ModIdentity.Of, ModIdentity.Of, [MatchKind.Key, MatchKind.WorkshopId, MatchKind.Name]))
            partner.TryAdd(p.Target.Key, p.Mine);

        var apply = new List<ModListEntry>();
        var workshop = new List<ModListEntry>();
        var manual = new List<ModListEntry>();
        foreach (var e in hostEntries)
        {
            if (partner.TryGetValue(e.Key, out var inst)) apply.Add(new ModListEntry(inst.Key, inst.Name, inst.DescriptorRel, inst.RemoteId));
            else if (ModMatcher.WorkshopIdOf(e.Key, e.RemoteId) is not null) workshop.Add(e);
            else manual.Add(e);
        }

        // Host-provided entries are untrusted: keep only well-formed "dlc/<folder>/<file>.dlc" paths. Not limited to
        // DLCs enabled here (mine.Dlcs), or a DLC already disabled on this PC would be dropped and get re-enabled.
        var disabled = hostList.DisabledDlcs
            .Select(d => d.Replace('\\', '/'))
            .Where(IsDlcDescriptor)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var u in diff.Dlcs.Where(d => d.Status == UnitStatus.Extra))
        {
            var dlc = mine.Dlcs.FirstOrDefault(d => string.Equals(d.Key, u.Key, StringComparison.OrdinalIgnoreCase));
            if (dlc is not null && !disabled.Contains(dlc.Descriptor, StringComparer.OrdinalIgnoreCase)) disabled.Add(dlc.Descriptor);
        }

        // A replaced copy's differences no longer apply; the mod used instead was not scanned, the rescan will tell.
        var changed = diff.Mods.Where(u => u.Status == UnitStatus.ContentMismatch && !replaced.Contains(u.Key)).ToList();
        return new MatchPlan(
            new ModList(hostList.Name, apply, disabled),
            workshop,
            manual,
            changed.Where(u => ModKeys.WorkshopId(u.MineKey ?? u.Key) is not null).ToList(),
            changed.Where(u => ModKeys.WorkshopId(u.MineKey ?? u.Key) is null).ToList(),
            diff.Dlcs.Where(d => d.Status == UnitStatus.Missing).ToList());
    }

    static readonly Regex DlcDescriptor = new(@"^dlc/[^/:*?""<>|]+/[^/:*?""<>|]+\.dlc$", RegexOptions.IgnoreCase);

    static bool IsDlcDescriptor(string path) => path.Length <= 200 && !path.Contains("..") && DlcDescriptor.IsMatch(path);
}

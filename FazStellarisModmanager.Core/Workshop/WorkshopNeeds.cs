using System.Globalization;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Session;

namespace FazStellarisModmanager.Core.Workshop;

/// <summary>Switch: download the host's Workshop item to use instead of my local copy, whose files differ.</summary>
public enum WorkshopNeedKind { Install, Update, Switch }

public sealed record WorkshopNeed(ulong Id, string Name, WorkshopNeedKind Kind);

/// <summary>
/// What the Workshop pop-up offers for a match plan: installs, updates and switches (distinct ids; for a repeated id the
/// first of install, update, switch wins) and the host's own local mods.
/// </summary>
public sealed record WorkshopNeeds(IReadOnlyList<WorkshopNeed> Items, IReadOnlyList<string> NotInstallable)
{
    /// <summary>True when there is anything to install, update, switch or point out.</summary>
    public bool NeedsPrompt => Items.Count > 0 || NotInstallable.Count > 0;

    public static WorkshopNeeds From(MatchPlan plan)
    {
        var installs = plan.NeedsWorkshopInstall
            .Select(e => ModMatcher.WorkshopIdOf(e.Key, e.RemoteId) is { } id ? new WorkshopNeed(ulong.Parse(id, CultureInfo.InvariantCulture), e.Name, WorkshopNeedKind.Install) : null);
        // Only my Workshop items can be updated through Steam; a local copy that differs needs the user's attention instead.
        var updates = plan.NeedsWorkshopUpdate
            .Select(u => ModKeys.WorkshopId(u.MineKey ?? u.Key) is { } id ? new WorkshopNeed(id, u.MineName ?? u.Name, WorkshopNeedKind.Update) : null);
        // ...unless the host uses the Workshop item itself: then that item can be downloaded and used instead of my copy.
        var switches = plan.NeedsWorkshopSwitch
            .Select(u => ModKeys.WorkshopId(u.Key) is { } id ? new WorkshopNeed(id, u.Name, WorkshopNeedKind.Switch) : null);
        return new WorkshopNeeds(
            installs.Concat(updates).Concat(switches).OfType<WorkshopNeed>().DistinctBy(n => n.Id).ToList(),
            plan.NeedsManualInstall.Select(e => e.Name).ToList());
    }
}

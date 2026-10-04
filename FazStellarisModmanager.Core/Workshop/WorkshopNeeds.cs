using System.Globalization;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Session;

namespace FazStellarisModmanager.Core.Workshop;

public enum WorkshopNeedKind { Install, Update }

public sealed record WorkshopNeed(ulong Id, string Name, WorkshopNeedKind Kind);

/// <summary>What the Workshop pop-up offers for a match plan: installs and updates (distinct ids, host order) and the host's own local mods.</summary>
public sealed record WorkshopNeeds(IReadOnlyList<WorkshopNeed> Items, IReadOnlyList<string> NotInstallable)
{
    /// <summary>True when there is anything to install, update or point out.</summary>
    public bool NeedsPrompt => Items.Count > 0 || NotInstallable.Count > 0;

    public static WorkshopNeeds From(MatchPlan plan)
    {
        var installs = plan.NeedsWorkshopInstall
            .Select(e => ModMatcher.WorkshopIdOf(e.Key, e.RemoteId) is { } id ? new WorkshopNeed(ulong.Parse(id, CultureInfo.InvariantCulture), e.Name, WorkshopNeedKind.Install) : null);
        // Only my Workshop items can be updated through Steam; a local copy that differs needs the user's attention instead.
        var updates = plan.NeedsWorkshopUpdate
            .Select(u => ModKeys.WorkshopId(u.MineKey ?? u.Key) is { } id ? new WorkshopNeed(id, u.MineName ?? u.Name, WorkshopNeedKind.Update) : null);
        return new WorkshopNeeds(
            installs.Concat(updates).OfType<WorkshopNeed>().DistinctBy(n => n.Id).ToList(),
            plan.NeedsManualInstall.Select(e => e.Name).ToList());
    }
}

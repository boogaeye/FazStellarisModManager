using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Core.Workshop;

namespace FazStellarisModmanager.Tests;

public class WorkshopNeedsTests
{
    static UnitDiff Changed(string key, string? mineKey, string? mineName = null) =>
        new(key, key + " name", null, UnitStatus.ContentMismatch, 1, 1, false, null, null, new FileDiff(["f"], [], []),
            mineKey, MatchKind.Key, false, mineName);

    [Fact]
    public void Lists_installs_and_updates_by_workshop_id_and_names_what_cannot_be_installed()
    {
        var plan = new MatchPlan(
            new ModList("H", [], []),
            [new("ugc:5", "Five", "mod/ugc_5.mod", "5"), new("local:copy.mod", "(H) Seven", "mod/copy.mod", "7"), new("ugc:5", "Five again", "mod/ugc_5.mod", "5")],
            [new("local:only.mod", "Host only", "mod/only.mod", null)],
            [Changed("ugc:9", "ugc:9", "My Nine"), Changed("ugc:5", "ugc:5")],
            [],
            []);

        var needs = WorkshopNeeds.From(plan);

        Assert.Equal(
            [(5UL, "Five", WorkshopNeedKind.Install), (7UL, "(H) Seven", WorkshopNeedKind.Install), (9UL, "My Nine", WorkshopNeedKind.Update)],
            needs.Items.Select(i => (i.Id, i.Name, i.Kind)));
        Assert.Equal(["Host only"], needs.NotInstallable);
        Assert.True(needs.NeedsPrompt);
    }

    [Fact]
    public void Updates_use_my_key_and_a_complete_plan_needs_no_prompt()
    {
        var local = new MatchPlan(new ModList("H", [], []), [], [], [Changed("ugc:5", "local:copy.mod")], [], []);
        var none = new MatchPlan(new ModList("H", [], []), [], [], [], [], []);

        Assert.Empty(WorkshopNeeds.From(local).Items);
        Assert.False(WorkshopNeeds.From(none).NeedsPrompt);
    }

    [Fact]
    public void A_host_local_copy_paired_with_my_workshop_item_is_an_update_named_after_the_host_when_mine_has_no_name()
    {
        var plan = new MatchPlan(new ModList("H", [], []), [], [], [Changed("local:copy.mod", "ugc:9")], [], []);

        var need = Assert.Single(WorkshopNeeds.From(plan).Items);

        Assert.Equal((9UL, "local:copy.mod name", WorkshopNeedKind.Update), (need.Id, need.Name, need.Kind));
    }
}

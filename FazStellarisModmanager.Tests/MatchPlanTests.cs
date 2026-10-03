using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Core.Snapshots;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class MatchPlanTests
{
    static InstalledMod Installed(string key, string rel, string name) =>
        new(key, name, rel, null, null, null, "", key.StartsWith("ugc:") ? ModSource.Workshop : ModSource.Local, []);

    static DiffResult Diff(params UnitDiff[] mods) => new("v", "v", new FileDiff([], [], []), [], mods.ToList(), [], []);

    static UnitDiff Changed(string key) =>
        new(key, key, null, UnitStatus.ContentMismatch, 1, 1, false, null, null, new FileDiff(["f.txt"], [], []));

    [Fact]
    public void Applies_installed_mods_in_host_order_and_reports_missing_ones()
    {
        var host = new ModList("Host list",
        [
            new("ugc:2", "Two", "mod/ugc_2.mod", "2"),
            new("ugc:1", "One", "mod/ugc_1.mod", "1"),
            new("local:b.mod", "B", "mod/b.mod", null),
            new("local:a.mod", "A", "mod/a.mod", null),
        ], ["dlc/dlc001_x/dlc001.dlc"]);
        var library = new[] { Installed("local:a.mod", "mod/a.mod", "My A"), Installed("ugc:1", "mod/ugc_1.mod", "My One") };

        var plan = MatchPlan.Create(host, Diff(), library, TestSnapshots.Machine("M"));

        Assert.Equal(new[] { "mod/ugc_1.mod", "mod/a.mod" }, plan.ToApply.Mods.Select(m => m.DescriptorRel));
        Assert.Equal(new[] { "My One", "My A" }, plan.ToApply.Mods.Select(m => m.Name));
        Assert.Equal(new[] { "dlc/dlc001_x/dlc001.dlc" }, plan.ToApply.DisabledDlcs);
        Assert.Equal(new[] { "ugc:2" }, plan.NeedsWorkshopInstall.Select(m => m.Key));
        Assert.Equal(new[] { "local:b.mod" }, plan.NeedsManualInstall.Select(m => m.Key));
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void Splits_content_mismatches_by_source()
    {
        var host = new ModList("Host list", [new("ugc:1", "One", "mod/ugc_1.mod", "1"), new("local:a.mod", "A", "mod/a.mod", null)], []);
        var library = new[] { Installed("ugc:1", "mod/ugc_1.mod", "One"), Installed("local:a.mod", "mod/a.mod", "A") };

        var plan = MatchPlan.Create(host, Diff(Changed("ugc:1"), Changed("local:a.mod")), library, TestSnapshots.Machine("M"));

        Assert.Equal(new[] { "ugc:1" }, plan.NeedsWorkshopUpdate.Select(u => u.Key));
        Assert.Equal(new[] { "local:a.mod" }, plan.DiffersLocally.Select(u => u.Key));
        Assert.Equal(2, plan.ToApply.Mods.Count);
    }

    [Fact]
    public void Fully_installed_matching_list_is_complete()
    {
        var host = new ModList("Host list", [new("ugc:1", "One", "mod/ugc_1.mod", "1")], []);

        var plan = MatchPlan.Create(host, Diff(), [Installed("UGC:1", "mod/ugc_1.mod", "One")], TestSnapshots.Machine("M"));

        Assert.True(plan.IsComplete);
        Assert.Single(plan.ToApply.Mods);
    }

    [Fact]
    public void Repeated_host_keys_are_applied_once()
    {
        var host = new ModList("Host list", [new("ugc:1", "One", "mod/ugc_1.mod", "1"), new("UGC:1", "One again", "mod/ugc_1.mod", "1")], []);

        var plan = MatchPlan.Create(host, Diff(), [Installed("ugc:1", "mod/ugc_1.mod", "One")], TestSnapshots.Machine("M"));

        Assert.Single(plan.ToApply.Mods);
    }

    static UnitDiff Dlc(string key, UnitStatus status) => new(key, key, null, status, 1, 1, false, null, null, null);

    [Fact]
    public void Extra_dlcs_here_are_added_to_the_disabled_list()
    {
        var host = new ModList("Host list", [], ["dlc/dlc001_x/dlc001.dlc"]);
        var mine = TestSnapshots.Machine("M") with
        {
            Dlcs = [new ModSnapshot("dlc:dlc002", "Extra DLC", "dlc/dlc002_x/dlc002.dlc", null, null, null, "", 0, [])],
        };
        var diff = Diff() with { Dlcs = [Dlc("DLC:dlc002", UnitStatus.Extra)] };

        var plan = MatchPlan.Create(host, diff, [], mine);

        Assert.Equal(new[] { "dlc/dlc001_x/dlc001.dlc", "dlc/dlc002_x/dlc002.dlc" }, plan.ToApply.DisabledDlcs);
    }

    [Fact]
    public void Missing_dlcs_are_reported_and_make_the_plan_incomplete()
    {
        var host = new ModList("Host list", [], []);
        var diff = Diff() with { Dlcs = [Dlc("dlc:dlc003", UnitStatus.Missing)] };

        var plan = MatchPlan.Create(host, diff, [], TestSnapshots.Machine("M"));

        Assert.Equal(new[] { "dlc:dlc003" }, plan.NeedsDlc.Select(u => u.Key));
        Assert.False(plan.IsComplete);
    }
}

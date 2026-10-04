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

        // mine must own the host's disabled DLC, otherwise it is dropped (host DLC names are not trusted).
        var mine = TestSnapshots.Machine("M") with
        {
            Dlcs = [new ModSnapshot("dlc:dlc001", "DLC 1", "dlc/dlc001_x/dlc001.dlc", null, null, null, "", 0, [])],
        };
        var plan = MatchPlan.Create(host, Diff(), library, mine);

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
            Dlcs =
            [
                new ModSnapshot("dlc:dlc001", "DLC 1", "DLC/dlc001_x/dlc001.dlc", null, null, null, "", 0, []),
                new ModSnapshot("dlc:dlc002", "Extra DLC", "dlc/dlc002_x/dlc002.dlc", null, null, null, "", 0, []),
            ],
        };
        var diff = Diff() with { Dlcs = [Dlc("DLC:dlc002", UnitStatus.Extra)] };

        var plan = MatchPlan.Create(host, diff, [], mine);

        Assert.Equal(new[] { "dlc/dlc001_x/dlc001.dlc", "dlc/dlc002_x/dlc002.dlc" }, plan.ToApply.DisabledDlcs);
    }

    [Fact]
    public void Host_disabled_dlcs_must_be_well_formed_descriptor_paths()
    {
        var host = new ModList("Host list", [],
            ["dlc/evil.dlc", "../../Windows/x.dlc", "dlc/a/../../b.dlc", "C:/x/dlc/a/b.dlc", "dlc/dlc001_x/dlc001.dlc", @"dlc\dlc002_y\dlc002.dlc"]);

        // dlc002 is not enabled here (maybe already disabled): it must still be kept, or applying would re-enable it.
        var plan = MatchPlan.Create(host, Diff(), [], TestSnapshots.Machine("M"));

        Assert.Equal(new[] { "dlc/dlc001_x/dlc001.dlc", "dlc/dlc002_y/dlc002.dlc" }, plan.ToApply.DisabledDlcs);
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

    static InstalledMod Inst(string key, string rel, string name, string? remoteId) =>
        new(key, name, rel, remoteId, null, null, "", key.StartsWith("ugc:") ? ModSource.Workshop : ModSource.Local, []);

    [Fact]
    public void Uses_my_copies_and_offers_workshop_installs_for_host_copies_with_a_workshop_id()
    {
        var host = new ModList("H",
        [
            new("ugc:5", "Five", "mod/ugc_5.mod", "5"),
            new("local:hostcopy.mod", "(H) Seven", "mod/hostcopy.mod", "7"),
            new("local:eth.mod", "(H) Ethics Fix", "mod/eth.mod", null),
            new("local:only.mod", "Only host", "mod/only.mod", null),
        ], []);
        var library = new[]
        {
            Inst("local:8cde_1.mod", "mod/8cde_1.mod", "(Mine) Five", "5"),
            Inst("local:mine_eth.mod", "mod/mine_eth.mod", "Ethics Fix", null),
        };

        var plan = MatchPlan.Create(host, Diff(), library, TestSnapshots.Machine("M"));

        Assert.Equal(["local:8cde_1.mod", "local:mine_eth.mod"], plan.ToApply.Mods.Select(m => m.Key));
        Assert.Equal(["mod/8cde_1.mod", "mod/mine_eth.mod"], plan.ToApply.Mods.Select(m => m.DescriptorRel));
        Assert.Equal(["local:hostcopy.mod"], plan.NeedsWorkshopInstall.Select(e => e.Key));
        Assert.Equal(["local:only.mod"], plan.NeedsManualInstall.Select(e => e.Key));
    }

    [Fact]
    public void Differences_need_a_workshop_update_only_when_my_mod_is_the_workshop_item()
    {
        var host = new ModList("H", [new("ugc:5", "Five", "mod/ugc_5.mod", "5"), new("ugc:6", "Six", "mod/ugc_6.mod", "6")], []);
        var library = new[] { Inst("local:copy.mod", "mod/copy.mod", "Five", "5"), Inst("ugc:6", "mod/ugc_6.mod", "Six", "6") };
        var diff = Diff(
            Changed("ugc:5") with { MineKey = "local:copy.mod", Match = MatchKind.WorkshopId, LocalCopyOfWorkshop = true },
            Changed("ugc:6") with { MineKey = "ugc:6" });

        var plan = MatchPlan.Create(host, diff, library, TestSnapshots.Machine("M"));

        Assert.Equal(["local:copy.mod", "ugc:6"], plan.ToApply.Mods.Select(m => m.Key));
        Assert.Equal(["ugc:6"], plan.NeedsWorkshopUpdate.Select(u => u.Key));
        Assert.Equal(["ugc:5"], plan.DiffersLocally.Select(u => u.Key));
    }

    static UnitDiff Same(string key, string mineKey, MatchKind match) =>
        new(key, key, null, UnitStatus.Ok, 1, 1, false, null, null, null, mineKey, match);

    [Fact]
    public void The_diff_partner_is_used_even_without_a_shared_workshop_id_or_name()
    {
        var host = new ModList("H", [new("local:a.mod", "Foo", "mod/a.mod", null)], []);
        var library = new[] { Inst("local:b.mod", "mod/b.mod", "Bar", null) };

        var plan = MatchPlan.Create(host, Diff(Same("local:a.mod", "local:b.mod", MatchKind.Files)), library, TestSnapshots.Machine("M"));

        Assert.Equal(["mod/b.mod"], plan.ToApply.Mods.Select(m => m.DescriptorRel));
        Assert.Empty(plan.NeedsManualInstall);
    }

    [Fact]
    public void A_mod_the_diff_gave_to_one_host_mod_is_not_reused_for_another()
    {
        var host = new ModList("H", [new("local:h1.mod", "One", "mod/h1.mod", null), new("local:h2.mod", "Shared", "mod/h2.mod", null)], []);
        var library = new[] { Inst("local:x.mod", "mod/x.mod", "Shared", null) };

        var plan = MatchPlan.Create(host, Diff(Same("local:h1.mod", "local:x.mod", MatchKind.Files)), library, TestSnapshots.Machine("M"));

        Assert.Equal(["local:x.mod"], plan.ToApply.Mods.Select(m => m.Key));
        Assert.Equal(["local:h2.mod"], plan.NeedsManualInstall.Select(e => e.Key));
    }

    [Fact]
    public void My_installed_workshop_item_beats_the_copy_the_diff_paired()
    {
        var host = new ModList("H", [new("ugc:5", "Five", "mod/ugc_5.mod", "5"), new("ugc:6", "Six", "mod/ugc_6.mod", "6")], []);
        var library = new[]
        {
            Inst("local:copy5.mod", "mod/copy5.mod", "Five", "5"), Inst("ugc:5", "mod/ugc_5.mod", "Five", "5"),
            Inst("local:copy6.mod", "mod/copy6.mod", "Six", "6"), Inst("ugc:6", "mod/ugc_6.mod", "Six", "6"),
        };
        var diff = Diff(
            Same("ugc:5", "local:copy5.mod", MatchKind.WorkshopId) with { LocalCopyOfWorkshop = true },
            Changed("ugc:6") with { MineKey = "local:copy6.mod", Match = MatchKind.WorkshopId, LocalCopyOfWorkshop = true });

        var plan = MatchPlan.Create(host, diff, library, TestSnapshots.Machine("M"));

        Assert.Equal(["ugc:5", "ugc:6"], plan.ToApply.Mods.Select(m => m.Key));
        Assert.Equal(["mod/ugc_5.mod", "mod/ugc_6.mod"], plan.ToApply.Mods.Select(m => m.DescriptorRel));
        // The copy's differences no longer apply: the Workshop item's files were not compared.
        Assert.Empty(plan.DiffersLocally);
        Assert.Empty(plan.NeedsWorkshopUpdate);
    }

    [Fact]
    public void A_mod_with_the_host_key_is_not_preferred_when_another_host_mod_has_it()
    {
        var host = new ModList("H", [new("local:a.mod", "A", "mod/a.mod", null), new("local:b.mod", "B", "mod/b.mod", null)], []);
        var library = new[] { Inst("local:b.mod", "mod/b.mod", "Other", null), Inst("local:c.mod", "mod/c.mod", "B", null) };
        var diff = Diff(Same("local:a.mod", "local:b.mod", MatchKind.Files), Same("local:b.mod", "local:c.mod", MatchKind.Name));

        var plan = MatchPlan.Create(host, diff, library, TestSnapshots.Machine("M"));

        Assert.Equal(["local:b.mod", "local:c.mod"], plan.ToApply.Mods.Select(m => m.Key));
    }
}

using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Tests;

public class ModDifferTests
{
    static ModSnapshot Unit(string key, int order, params (string Path, string Md5)[] files) =>
        new(key, key.ToUpperInvariant(), $"mod/{key}.mod", null, null, null, "", order,
            files.Select(f => new ModFile(f.Path, f.Md5, 1)).ToList());

    static MachineSnapshot Machine(string version, params ModSnapshot[] mods) =>
        new("m", version, "", DateTime.UtcNow, Unit("base", 0, ("common/a.txt", "aa")), [Unit("dlc:dlc001", 1, ("x", "crc32:1"))], mods.ToList());

    [Fact]
    public void Identical_snapshots_match()
    {
        var a = Machine("v4.4", Unit("ugc:1", 1, ("f", "1")), Unit("ugc:2", 2, ("g", "2")));
        var b = Machine("v4.4", Unit("ugc:1", 1, ("f", "1")), Unit("ugc:2", 2, ("g", "2")));

        var r = ModDiffer.Diff(a, b);

        Assert.True(r.IsMatch);
        Assert.All(r.Mods, m => Assert.Equal(UnitStatus.Ok, m.Status));
    }

    [Fact]
    public void Reports_missing_and_extra_mods()
    {
        var target = Machine("v4.4", Unit("ugc:1", 1), Unit("ugc:2", 2));
        var mine = Machine("v4.4", Unit("ugc:1", 1), Unit("local:x.mod", 2));

        var r = ModDiffer.Diff(target, mine);

        Assert.False(r.IsMatch);
        Assert.Equal(new[] { ("ugc:1", UnitStatus.Ok), ("ugc:2", UnitStatus.Missing), ("local:x.mod", UnitStatus.Extra) },
            r.Mods.Select(m => (m.Key, m.Status)));
    }

    [Fact]
    public void Reports_content_mismatch_with_file_details()
    {
        var target = Machine("v4.4", Unit("ugc:1", 1, ("same", "s"), ("changed", "1"), ("only_target", "t")));
        var mine = Machine("v4.4", Unit("ugc:1", 1, ("same", "s"), ("CHANGED", "2"), ("only_mine", "m")));

        var mod = Assert.Single(ModDiffer.Diff(target, mine).Mods);

        Assert.Equal(UnitStatus.ContentMismatch, mod.Status);
        Assert.Equal(new[] { "changed" }, mod.Files!.Changed);
        Assert.Equal(new[] { "only_target" }, mod.Files.OnlyInTarget);
        Assert.Equal(new[] { "only_mine" }, mod.Files.OnlyInMine);
    }

    [Fact]
    public void Reports_load_order_mismatch()
    {
        var target = Machine("v4.4", Unit("ugc:1", 1), Unit("ugc:2", 2), Unit("ugc:3", 3));
        var mine = Machine("v4.4", Unit("ugc:2", 1), Unit("ugc:1", 2), Unit("ugc:3", 3));

        var r = ModDiffer.Diff(target, mine);

        Assert.False(r.OrderMatches);
        Assert.False(r.IsMatch);
        // A 2-swap is fixed by moving one mod, so exactly one of the pair is flagged.
        Assert.Equal(1, r.Mods.Take(2).Count(m => m.OutOfOrder));
        Assert.False(r.Mods[2].OutOfOrder);
        Assert.All(r.Mods, m => Assert.Equal(UnitStatus.Ok, m.Status));
    }

    [Fact]
    public void Flags_only_the_moved_mod()
    {
        var target = Machine("v4.4", Unit("ugc:1", 1), Unit("ugc:2", 2), Unit("ugc:3", 3), Unit("ugc:4", 4), Unit("ugc:5", 5));
        var mine = Machine("v4.4", Unit("ugc:2", 1), Unit("ugc:3", 2), Unit("ugc:4", 3), Unit("ugc:5", 4), Unit("ugc:1", 5));

        var r = ModDiffer.Diff(target, mine);

        Assert.Equal(new[] { "ugc:1" }, r.Mods.Where(m => m.OutOfOrder).Select(m => m.Key));
    }

    [Fact]
    public void Interleaved_extra_mods_do_not_flag_order()
    {
        var target = Machine("v4.4", Unit("ugc:1", 1), Unit("ugc:2", 2), Unit("ugc:3", 3));
        var mine = Machine("v4.4", Unit("local:a", 1), Unit("ugc:1", 2), Unit("local:b", 3), Unit("ugc:2", 4), Unit("ugc:3", 5), Unit("local:c", 6));

        var r = ModDiffer.Diff(target, mine);

        Assert.True(r.OrderMatches);
        Assert.DoesNotContain(r.Mods, m => m.OutOfOrder);
    }

    [Fact]
    public void Content_mismatch_and_out_of_order_can_both_be_set()
    {
        var target = Machine("v4.4", Unit("ugc:1", 1, ("f", "1")), Unit("ugc:2", 2), Unit("ugc:3", 3));
        var mine = Machine("v4.4", Unit("ugc:2", 1), Unit("ugc:3", 2), Unit("ugc:1", 3, ("f", "2")));

        var mod = ModDiffer.Diff(target, mine).Mods.Single(m => m.Key == "ugc:1");

        Assert.Equal(UnitStatus.ContentMismatch, mod.Status);
        Assert.True(mod.OutOfOrder);
    }

    [Fact]
    public void Md5_comparison_ignores_case()
    {
        var target = Machine("v4.4", Unit("ugc:1", 1, ("f", "ABCDEF")));
        var mine = Machine("v4.4", Unit("ugc:1", 1, ("f", "abcdef")));

        Assert.Equal(UnitStatus.Ok, Assert.Single(ModDiffer.Diff(target, mine).Mods).Status);
    }

    [Fact]
    public void Scan_warnings_surface_and_make_result_unreliable()
    {
        var target = Machine("v4.4") with { Warnings = ["t: unreadable a.txt"] };
        var mine = Machine("v4.4") with { Warnings = ["m: unreadable b.txt", "m: bad unit"] };

        var r = ModDiffer.Diff(target, mine);

        Assert.Equal(new[] { "t: unreadable a.txt" }, r.TargetWarnings);
        Assert.Equal(new[] { "m: unreadable b.txt", "m: bad unit" }, r.MineWarnings);
        Assert.False(r.IsReliable);
    }

    [Fact]
    public void Without_warnings_result_is_reliable()
    {
        var r = ModDiffer.Diff(Machine("v4.4"), Machine("v4.4"));

        Assert.Empty(r.TargetWarnings);
        Assert.Empty(r.MineWarnings);
        Assert.True(r.IsReliable);
    }

    [Fact]
    public void Missing_mod_does_not_count_as_order_mismatch()
    {
        var target = Machine("v4.4", Unit("ugc:1", 1), Unit("ugc:2", 2), Unit("ugc:3", 3));
        var mine = Machine("v4.4", Unit("ugc:1", 1), Unit("ugc:3", 2));

        Assert.True(ModDiffer.Diff(target, mine).OrderMatches);
    }

    [Fact]
    public void Reports_game_version_base_and_dlc_differences()
    {
        var target = Machine("v4.4");
        var mine = Machine("v4.3") with
        {
            Base = Unit("base", 0, ("common/a.txt", "zz")),
            Dlcs = [],
        };

        var r = ModDiffer.Diff(target, mine);

        Assert.False(r.GameVersionMatches);
        Assert.Equal(new[] { "common/a.txt" }, r.BaseFiles.Changed);
        Assert.Equal(UnitStatus.Missing, Assert.Single(r.Dlcs).Status);
    }

    [Fact]
    public void Duplicate_keys_and_case_variant_paths_do_not_throw()
    {
        var target = Machine("v4.4", Unit("ugc:1", 1, ("a", "1"), ("A", "1")), Unit("ugc:1", 2));
        var mine = Machine("v4.4", Unit("UGC:1", 1, ("a", "1")));

        var r = ModDiffer.Diff(target, mine);

        Assert.Equal(UnitStatus.Ok, Assert.Single(r.Mods).Status);
    }

    static ModSnapshot Mod(string key, int order, string name, string? remoteId, params (string Path, string Md5)[] files) =>
        new(key, name, $"mod/{key}.mod", remoteId, null, null, "", order, files.Select(f => new ModFile(f.Path, f.Md5, 1)).ToList());

    [Fact]
    public void A_local_copy_of_a_workshop_mod_is_the_same_mod()
    {
        var target = Machine("v4.4", Mod("ugc:5", 1, "Mod Five", "5", ("f", "1")));
        var mine = Machine("v4.4", Mod("local:8cde_1.mod", 1, "(Coll) Mod Five", "5", ("f", "1")));

        var r = ModDiffer.Diff(target, mine);

        var u = Assert.Single(r.Mods);
        Assert.Equal((UnitStatus.Ok, "local:8cde_1.mod", MatchKind.WorkshopId, true, "(Coll) Mod Five"),
            (u.Status, u.MineKey, u.Match, u.LocalCopyOfWorkshop, u.MineName));
        Assert.True(r.IsMatch);
    }

    [Fact]
    public void Prefixed_names_and_identical_files_pair_local_mods()
    {
        var target = Machine("v4.4", Mod("local:a.mod", 1, "Ethics Fix", null, ("e", "1")), Mod("local:b.mod", 2, "Sound", null, ("s", "1")));
        var mine = Machine("v4.4", Mod("local:c.mod", 1, "(Coll) Ethics Fix", null, ("e", "2")), Mod("local:d.mod", 2, "Totally different", null, ("s", "1")));

        var r = ModDiffer.Diff(target, mine);

        Assert.Equal(
            [("local:a.mod", UnitStatus.ContentMismatch, "local:c.mod", MatchKind.Name), ("local:b.mod", UnitStatus.Ok, "local:d.mod", MatchKind.Files)],
            r.Mods.Select(m => (m.Key, m.Status, m.MineKey, m.Match)));
        Assert.All(r.Mods, m => Assert.False(m.LocalCopyOfWorkshop));
    }

    [Fact]
    public void Load_order_is_compared_across_pairs()
    {
        var target = Machine("v4.4", Mod("ugc:1", 1, "One", "1", ("a", "1")), Mod("ugc:2", 2, "Two", "2", ("b", "1")));
        var mine = Machine("v4.4", Mod("local:two.mod", 1, "Two", "2", ("b", "1")), Mod("local:one.mod", 2, "One", "1", ("a", "1")));

        var r = ModDiffer.Diff(target, mine);

        Assert.Equal(1, r.Mods.Count(m => m.OutOfOrder));
        Assert.False(r.OrderMatches);
        Assert.All(r.Mods, m => Assert.Equal(UnitStatus.Ok, m.Status));
    }

    [Fact]
    public void Dlcs_pair_only_by_key()
    {
        var target = Machine("v4.4") with { Dlcs = [Unit("dlc:dlc001", 1, ("x", "crc32:1"))] };
        var mine = Machine("v4.4") with { Dlcs = [Unit("dlc:dlc002", 1, ("x", "crc32:1"))] };

        var r = ModDiffer.Diff(target, mine);

        Assert.Equal([UnitStatus.Missing, UnitStatus.Extra], r.Dlcs.Select(d => d.Status));
    }
}

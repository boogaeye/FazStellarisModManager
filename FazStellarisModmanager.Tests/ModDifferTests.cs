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
        Assert.Equal(new[] { true, true, false }, r.Mods.Select(m => m.OutOfOrder));
        Assert.All(r.Mods, m => Assert.Equal(UnitStatus.Ok, m.Status));
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
}

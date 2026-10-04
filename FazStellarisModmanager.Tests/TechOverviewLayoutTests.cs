using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class TechOverviewLayoutTests
{
    const string Tree = """
        p0 = { area = physics tier = 0 category = { particles } }
        p1b = { area = physics tier = 1 category = { particles } prerequisites = { "p0" } }
        p1a = { area = physics tier = 1 category = { computing } prerequisites = { "p0" } }
        p1c = { area = physics tier = 1 }
        x2 = { area = physics tier = 2 category = { particles } prerequisites = { "p1c" } }
        y2 = { area = physics tier = 2 category = { particles } prerequisites = { "p1a" } }
        z2 = { area = physics tier = 2 category = { particles } }
        s0 = { area = society tier = 0 category = { biology } }
        s1 = { area = society tier = 1 category = { biology } prerequisites = { "s0" "p0" "ghost" } }
        e3 = { area = engineering tier = 3 category = { industry } prerequisites = { "e3b" } }
        e3b = { area = engineering tier = 3 category = { industry } }
        rep = { area = physics tier = 1 levels = -1 category = { particles } prerequisites = { "p1b" } }
        none = { area = society category = { biology } }
        """;

    static (TechDatabase Db, IDisposable Cleanup) Db(string text)
    {
        var tmp = new TempDir();
        tmp.Write("g/common/technology/00_t.txt", text);
        var source = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        var db = TechDatabase.Build([source]);
        source.Dispose();
        return (db, tmp);
    }

    [Fact]
    public void Columns_are_used_tiers_then_repeatable_then_unknown_and_bands_are_the_areas()
    {
        var (db, cleanup) = Db(Tree);
        using var _ = cleanup;

        var o = TechOverviewLayout.Build(db);

        Assert.Equal(["Tier 0", "Tier 1", "Tier 2", "Tier 3", "Repeatable", "?"], o.Columns.Select(c => c.Label));
        Assert.Equal([TechArea.Physics, TechArea.Society, TechArea.Engineering], o.Bands.Select(b => b.Area));
        Assert.Equal(["Physics", "Society", "Engineering"], o.Bands.Select(b => b.Label));
        Assert.Equal(4, o.Find("rep")!.ColumnIndex);
        Assert.Equal(5, o.Find("none")!.ColumnIndex);
        Assert.Equal(13, o.Nodes.Count);
    }

    [Fact]
    public void Cells_order_by_category_then_prerequisite_position_then_name()
    {
        var (db, cleanup) = Db(Tree);
        using var _ = cleanup;

        var o = TechOverviewLayout.Build(db);

        string[] Cell(TechArea area, int column) =>
            o.Nodes.Where(n => n.Area == area && n.ColumnIndex == column).OrderBy(n => n.Row).Select(n => n.Key).ToArray();
        Assert.Equal(["p1a", "p1b", "p1c"], Cell(TechArea.Physics, 1));
        Assert.Equal(["y2", "x2", "z2"], Cell(TechArea.Physics, 2));
        Assert.Equal(["e3", "e3b"], Cell(TechArea.Engineering, 3));
    }

    [Fact]
    public void Geometry_follows_the_constants_and_nodes_never_overlap()
    {
        var (db, cleanup) = Db(Tree);
        using var _ = cleanup;

        var o = TechOverviewLayout.Build(db);

        const double pitch = TechOverviewLayout.RowPitch;
        Assert.Equal(TechOverviewLayout.HeaderHeight, o.Bands[0].Y);
        Assert.Equal(3 * pitch, o.Bands[0].Height);
        Assert.Equal(o.Bands[0].Y + o.Bands[0].Height + TechOverviewLayout.BandGap, o.Bands[1].Y);
        Assert.Equal(1 * pitch, o.Bands[1].Height);
        Assert.Equal(2 * pitch, o.Bands[2].Height);
        Assert.Equal(o.Bands[2].Y + o.Bands[2].Height, o.Height);
        Assert.Equal(TechOverviewLayout.LabelGutter + 6 * TechOverviewLayout.ColumnWidth, o.Width);

        var p1b = o.Find("p1b")!;
        Assert.Equal((TechOverviewLayout.LabelGutter + TechOverviewLayout.ColumnWidth, o.Bands[0].Y + pitch), (p1b.X, p1b.Y));

        foreach (var a in o.Nodes)
            foreach (var b in o.Nodes)
                if (!ReferenceEquals(a, b))
                    Assert.False(Math.Abs(a.X - b.X) < TechOverviewLayout.NodeWidth && Math.Abs(a.Y - b.Y) < TechOverviewLayout.NodeHeight,
                        $"{a.Key} overlaps {b.Key}");
    }

    [Fact]
    public void Edges_skip_missing_prerequisites_and_flag_forward_links()
    {
        var (db, cleanup) = Db(Tree);
        using var _ = cleanup;

        var o = TechOverviewLayout.Build(db);

        Assert.Equal(8, o.Edges.Count);
        Assert.Contains(new OverviewEdge("p0", "s1", true), o.Edges);
        Assert.Contains(new OverviewEdge("e3b", "e3", false), o.Edges);
        Assert.DoesNotContain(o.Edges, e => e.From == "ghost");
    }

    [Fact]
    public void An_Other_band_appears_only_when_needed()
    {
        var (db, cleanup) = Db("p = { area = physics tier = 0 }\no = { area = weird tier = 0 }\n");
        using var _ = cleanup;

        var o = TechOverviewLayout.Build(db);

        Assert.Equal([TechArea.Physics, TechArea.Society, TechArea.Engineering, TechArea.Other], o.Bands.Select(b => b.Area));
        Assert.Equal("Other", o.Bands[3].Label);
        Assert.Equal(["Tier 0"], o.Columns.Select(c => c.Label));
    }

    [Fact]
    public void A_with_copy_finds_its_own_nodes()
    {
        var (db, cleanup) = Db(Tree);
        using var _ = cleanup;

        var o = TechOverviewLayout.Build(db);
        Assert.NotNull(o.Find("p0"));

        var copy = o with { Nodes = [new OverviewNode("only", TechArea.Physics, 0, 0, 1, 2)] };

        Assert.Null(copy.Find("p0"));
        Assert.Equal(1, copy.Find("ONLY")!.X);
        Assert.NotNull(o.Find("p0"));
    }
}

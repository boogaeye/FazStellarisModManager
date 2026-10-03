using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class TechGraphLayoutTests
{
    static (TechDatabase Db, IDisposable Cleanup) Db(string text)
    {
        var tmp = new TempDir();
        tmp.Write("g/common/technology/00_t.txt", text);
        var source = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        var db = TechDatabase.Build([source]);
        source.Dispose();
        return (db, tmp);
    }

    const string Diamond = """
        z_first = { area = physics }
        a_second = { area = physics }
        p1 = { area = physics prerequisites = { "z_first" } }
        p2 = { area = physics prerequisites = { "a_second" } }
        f = { area = physics prerequisites = { "p1" "p2" } }
        c1 = { area = physics prerequisites = { "f" } }
        c2 = { area = physics prerequisites = { "f" } }
        d1 = { area = physics prerequisites = { "c1" } }
        """;

    [Fact]
    public void Places_ancestors_left_and_descendants_right()
    {
        var (db, cleanup) = Db(Diamond);
        using var _ = cleanup;

        var g = TechGraphLayout.Build(db, "f", depth: 2);

        string[] Col(int c) => g.Nodes.Where(n => n.Column == c).OrderBy(n => n.Row).Select(n => n.Key).ToArray();
        Assert.Equal(("f", -2, 2, 2), (g.Focus, g.MinColumn, g.MaxColumn, g.MaxRows));
        Assert.Equal(new[] { "f" }, Col(0));
        Assert.Equal(new[] { "p1", "p2" }, Col(-1));
        Assert.Equal(new[] { "z_first", "a_second" }, Col(-2)); // barycenter beats alphabetical
        Assert.Equal(new[] { "c1", "c2" }, Col(1));
        Assert.Equal(new[] { "d1" }, Col(2));
        Assert.True(g.Nodes.Single(n => n.Key == "f").IsFocus);
        Assert.Equal(
            new[] { "a_second>p2", "c1>d1", "f>c1", "f>c2", "p1>f", "p2>f", "z_first>p1" },
            g.Edges.Select(e => $"{e.From}>{e.To}").Order(StringComparer.Ordinal));
        Assert.Empty(g.More);
    }

    [Fact]
    public void Depth_limits_columns()
    {
        var (db, cleanup) = Db(Diamond);
        using var _ = cleanup;

        var g = TechGraphLayout.Build(db, "f", depth: 1);

        Assert.Equal((-1, 1), (g.MinColumn, g.MaxColumn));
        Assert.Equal(5, g.Nodes.Count);
    }

    [Fact]
    public void Caps_crowded_columns()
    {
        var text = "root = { area = physics }\n" + string.Concat(Enumerable.Range(0, 30).Select(i => $"k{i:00} = {{ area = physics prerequisites = {{ \"root\" }} }}\n"));
        var (db, cleanup) = Db(text);
        using var _ = cleanup;

        var g = TechGraphLayout.Build(db, "root", depth: 2);

        Assert.Equal(TechGraphLayout.MaxPerColumn, g.Nodes.Count(n => n.Column == 1));
        Assert.Equal(new GraphMore(1, 5), Assert.Single(g.More));
        Assert.Equal(TechGraphLayout.MaxPerColumn, g.Edges.Count);
    }

    [Fact]
    public void Unknown_focus_throws()
    {
        var (db, cleanup) = Db(Diamond);
        using var _ = cleanup;

        Assert.Throws<ArgumentException>(() => TechGraphLayout.Build(db, "nope", 2));
    }

    [Fact]
    public void Capped_columns_leave_no_orphans()
    {
        var sb = new System.Text.StringBuilder().AppendLine("root = { area = physics }");
        for (int i = 0; i < 30; i++) sb.AppendLine($"k{i:00} = {{ area = physics prerequisites = {{ \"root\" }} }}");
        for (int i = 0; i < 30; i++) sb.AppendLine($"m{i:00} = {{ area = physics prerequisites = {{ \"k{i:00}\" }} }}");
        var (db, cleanup) = Db(sb.ToString());
        using var _ = cleanup;

        var g = TechGraphLayout.Build(db, "root", depth: 2);

        string[] Col(int c) => g.Nodes.Where(n => n.Column == c).OrderBy(n => n.Row).Select(n => n.Key).ToArray();
        var ks = Enumerable.Range(0, 25).Select(i => $"k{i:00}").ToArray();
        Assert.Equal(ks, Col(1));
        Assert.Equal(ks.Select(k => "m" + k[1..]).Order(StringComparer.Ordinal), Col(2).Order(StringComparer.Ordinal));
        Assert.Contains(new GraphMore(1, 5), g.More);
        Assert.Contains(new GraphMore(2, 5), g.More);
        Assert.Equal(2, g.More.Count);
        foreach (var m in Col(2)) Assert.Contains(g.Edges, e => e.To == m);
    }

    [Fact]
    public void A_fully_hidden_column_still_counts_toward_min_and_max()
    {
        var sb = new System.Text.StringBuilder().AppendLine("root = { area = physics }");
        for (int i = 0; i < 30; i++) sb.AppendLine($"k{i:00} = {{ area = physics prerequisites = {{ \"root\" }} }}");
        sb.AppendLine("m = { area = physics prerequisites = { \"k29\" } }");
        var (db, cleanup) = Db(sb.ToString());
        using var _ = cleanup;

        var g = TechGraphLayout.Build(db, "root", depth: 2);

        Assert.DoesNotContain(g.Nodes, n => n.Column == 2);
        Assert.Contains(new GraphMore(2, 1), g.More);
        Assert.Equal((0, 2), (g.MinColumn, g.MaxColumn));
    }
}

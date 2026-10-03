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
}

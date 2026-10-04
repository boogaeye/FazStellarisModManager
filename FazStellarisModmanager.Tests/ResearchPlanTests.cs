using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ResearchPlanTests
{
    // Names are the keys (no localisation). start is a starting tech; ghost is missing; loop1/loop2 depend on each other.
    const string Tree = """
        start = { area = physics tier = 0 start_tech = yes cost = 10 }
        a = { area = physics tier = 1 cost = 100 prerequisites = { "start" } }
        b = { area = engineering tier = 1 cost = 200 prerequisites = { "start" } }
        c = { area = physics tier = 2 cost = 300 prerequisites = { "a" "b" } }
        d = { area = society tier = 2 cost = @unknown prerequisites = { "a" } }
        e = { area = society tier = 3 cost = 500 prerequisites = { "c" "d" "ghost" } }
        rep = { area = physics tier = 1 cost = 40 levels = -1 prerequisites = { "a" } }
        loop1 = { area = physics tier = 1 cost = 1 prerequisites = { "loop2" } }
        loop2 = { area = physics tier = 1 cost = 1 prerequisites = { "loop1" } }
        after = { area = physics tier = 2 cost = 1 prerequisites = { "loop2" } }
        f = { area = society tier = 3 cost = 1 prerequisites = { "ghost" "GHOST" "e" } }
        """;

    static (TechDatabase Db, IDisposable Cleanup) Db()
    {
        var tmp = new TempDir();
        tmp.Write("g/common/technology/00_t.txt", Tree);
        var source = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        var db = TechDatabase.Build([source]);
        source.Dispose();
        return (db, tmp);
    }

    [Fact]
    public void Orders_prerequisites_first_then_tier_area_and_name_and_sums_costs()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, ["e"], []);

        Assert.Equal(["a", "b", "c", "d", "e"], route.Steps);
        Assert.Equal(1100, route.TotalCost);
        Assert.Equal(1, route.UnknownCostCount);
        Assert.Equal((0, 1), (route.SkippedResearched, route.SkippedStarting));
        Assert.Equal([new MissingPrerequisite("e", "ghost")], route.MissingPrerequisites);
        Assert.Empty(route.Cycles);
        Assert.Empty(route.UnknownTargets);
    }

    [Fact]
    public void Several_targets_share_prerequisites()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, ["c", "d"], []);

        Assert.Equal(["a", "b", "c", "d"], route.Steps);
        Assert.Equal(600, route.TotalCost);
    }

    [Fact]
    public void Researched_techs_are_skipped_and_not_expanded()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, ["c"], ["A", "not_a_tech"]);

        Assert.Equal(["b", "c"], route.Steps);
        Assert.Equal((1, 1), (route.SkippedResearched, route.SkippedStarting));
    }

    [Fact]
    public void Repeatables_come_after_tiered_techs_that_are_ready_at_the_same_time()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        Assert.Equal(["a", "b", "rep"], ResearchPlan.Build(db, ["rep", "b"], []).Steps);
    }

    [Fact]
    public void Prerequisite_loops_terminate_and_are_reported()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, ["loop1"], []);

        Assert.Equal(["loop1", "loop2"], route.Steps);
        Assert.Equal(["loop1", "loop2"], route.Cycles);
    }

    [Fact]
    public void Unknown_targets_are_reported_once_and_keys_are_case_insensitive()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, ["nope", "A", "NOPE"], []);

        Assert.Equal(["a"], route.Steps);
        Assert.Equal(["nope"], route.UnknownTargets);
        Assert.Contains("A", route.Needed);
    }

    [Fact]
    public void No_targets_gives_an_empty_route()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, [], ["a"]);

        Assert.Empty(route.Steps);
        Assert.Equal(0, route.TotalCost);
    }

    [Fact]
    public void A_duplicate_missing_prerequisite_is_reported_once_and_sorted()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, ["f"], []);

        Assert.Equal([new MissingPrerequisite("e", "ghost"), new MissingPrerequisite("f", "ghost")], route.MissingPrerequisites);
    }

    [Fact]
    public void A_tech_downstream_of_a_loop_is_in_Cycles()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, ["after"], []);

        Assert.Equal(["loop1", "loop2", "after"], route.Steps);
        Assert.Equal(["loop1", "loop2", "after"], route.Cycles);
    }

    [Fact]
    public void Null_and_blank_keys_are_ignored()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, [null!, " ", "a"], [null!, ""]);

        Assert.Equal(["a"], route.Steps);
        Assert.Empty(route.UnknownTargets);
    }

    [Fact]
    public void A_with_copy_reports_the_new_Needed()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, ["a"], []);
        Assert.Contains("a", route.Needed);

        var copy = route with { Steps = ["b"] };

        Assert.Contains("B", copy.Needed);
        Assert.DoesNotContain("a", copy.Needed);
        Assert.Contains("a", route.Needed);
    }
}

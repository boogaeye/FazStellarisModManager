using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Events;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class EventGraphServiceTests
{
    static (FakeInstall Fake, TechTreeService Trees, EventGraphService Events) Create()
    {
        var fake = new FakeInstall();
        fake.Write("lib/steamapps/common/Stellaris/common/technology/00_t.txt", "tech_a = { area = physics tier = 0 start_tech = yes }");
        fake.Write("lib/steamapps/common/Stellaris/events/e.txt",
            "country_event = { id = e.1 title = e.1.name is_triggered_only = yes option = { name = ok fire_e2 = yes } }"
            + " country_event = { id = e.2 is_triggered_only = yes }");
        fake.Write("lib/steamapps/common/Stellaris/common/scripted_effects/00_e.txt", "fire_e2 = { country_event = { id = e.2 } }");
        fake.Write("lib/steamapps/common/Stellaris/common/on_actions/00_on.txt", "on_game_start = { events = { e.1 } }");
        fake.Write("lib/steamapps/common/Stellaris/localisation/english/e_l_english.yml", "l_english:" + (char)10 + " e.1.name: \"Start\"" + (char)10);
        var paths = new AppPaths(fake.DataDir);
        SettingsStore.Save(paths.Settings, new AppSettings(UserDir: fake.UserDir));
        var trees = new TechTreeService(new ModManagerService(paths, _ => fake.GameDir));
        return (fake, trees, new EventGraphService(trees));
    }

    [Fact]
    public async Task Builds_the_tech_tree_first_when_none_is_loaded_and_uses_its_localisation_and_scripts()
    {
        var (fake, trees, events) = Create();
        using var _cleanup = fake;
        Assert.Null(trees.Current);

        var graph = await events.EnsureAsync();

        Assert.NotNull(trees.Current);
        Assert.Same(graph, events.Current);
        Assert.True(events.IsUpToDate);
        Assert.Equal("Start", graph!.Event("e.1")!.Title);
        Assert.Equal(("on_game_start", CallerKind.OnAction), (graph.CallersOf("e.1")[0].CallerId, graph.CallersOf("e.1")[0].CallerKind));
        Assert.Equal(("e.1", "fire_e2"), (graph.CallersOf("e.2")[0].CallerId, graph.CallersOf("e.2")[0].Via));
        Assert.Null(events.Progress);
        Assert.Null(events.Error);
        Assert.False(events.IsBuilding);
    }

    [Fact]
    public async Task The_graph_is_cached_per_tree_and_rebuilt_when_the_tree_changes()
    {
        var (fake, trees, events) = Create();
        using var _cleanup = fake;
        var changed = 0;
        events.Changed += () => Interlocked.Increment(ref changed);

        var first = await events.EnsureAsync();
        Assert.Same(first, await events.EnsureAsync());
        Assert.True(changed > 0);

        await trees.BuildAsync(trees.Choices()[0]);
        Assert.False(events.IsUpToDate);
        var second = await events.EnsureAsync();

        Assert.NotSame(first, second);
        Assert.True(events.IsUpToDate);
        Assert.Contains("e.1", second!.Events.Keys);
    }

    [Fact]
    public async Task A_failing_tree_build_is_reported_as_an_error()
    {
        var fake = new FakeInstall();
        using var _cleanup = fake;
        var paths = new AppPaths(fake.DataDir);
        SettingsStore.Save(paths.Settings, new AppSettings(UserDir: fake.UserDir));
        var events = new EventGraphService(new TechTreeService(new ModManagerService(paths, _ => null)));

        Assert.Null(await events.EnsureAsync());

        Assert.Contains("Stellaris install not found", events.Error);
        Assert.Null(events.Current);
    }
}

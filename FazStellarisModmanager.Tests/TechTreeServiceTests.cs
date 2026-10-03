using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class TechTreeServiceTests
{
    static (FakeInstall Fake, ModManagerService Manager, TechTreeService Tree) Create()
    {
        var fake = new FakeInstall();
        fake.Write("lib/steamapps/common/Stellaris/common/technology/00_t.txt", "tech_a = { area = physics tier = 0 start_tech = yes }\n");
        fake.Write("user/mod/local/common/technology/zz_local.txt", "tech_l = { area = society tier = 1 prerequisites = { \"tech_a\" } }\n");
        var icon = Path.Combine(fake.GameDir, "gfx", "interface", "icons", "technologies", "tech_a.dds");
        Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
        File.WriteAllBytes(icon, DdsBuilder.Bgra32(1, 1, (9, 9, 9, 255)));
        var paths = new AppPaths(fake.DataDir);
        SettingsStore.Save(paths.Settings, new AppSettings(UserDir: fake.UserDir));
        var manager = new ModManagerService(paths, _ => fake.GameDir);
        return (fake, manager, new TechTreeService(manager));
    }

    [Fact]
    public async Task Builds_the_current_game_tree_with_mod_sources_and_icons()
    {
        var (fake, _, tree) = Create();
        using var _cleanup = fake;

        var choices = tree.Choices();
        await tree.BuildAsync(choices[0]);

        var current = tree.Current!;
        Assert.Equal("Current game (dlc_load.json)", current.Label);
        Assert.Equal(new[] { "tech_a", "tech_l" }, current.Database.Techs.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Local Mod", current.Database.Techs["tech_l"].Source.SourceName);
        Assert.StartsWith("data:image/png;base64,", tree.IconUri(current.Database.Techs["tech_a"]));
        Assert.Null(tree.IconUri(current.Database.Techs["tech_l"]));
        Assert.False(tree.IsBuilding);
        Assert.Null(tree.Progress);
    }

    [Fact]
    public async Task Saved_lists_are_offered_and_build_their_own_tree()
    {
        var (fake, manager, tree) = Create();
        using var _cleanup = fake;
        manager.Lists.Save(new ModList("Vanilla", [], []));

        var choices = tree.Choices();
        Assert.Equal(new[] { "Current game (dlc_load.json)", "Vanilla" }, choices.Select(c => c.Label));
        await tree.BuildAsync(choices[1]);

        Assert.Equal("Vanilla", tree.Current!.Label);
        Assert.Equal(new[] { "tech_a" }, tree.Current.Database.Techs.Keys);
    }

    [Fact]
    public async Task Missing_saved_list_throws_and_keeps_the_previous_tree()
    {
        var (fake, _, tree) = Create();
        using var _cleanup = fake;
        await tree.BuildAsync(tree.Choices()[0]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => tree.BuildAsync(new TechTreeChoice("Gone", "Gone")));

        Assert.Equal("Current game (dlc_load.json)", tree.Current!.Label);
    }
}

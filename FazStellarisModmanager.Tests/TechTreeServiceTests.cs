using System.IO.Compression;
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

        await tree.IconsReady;
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

    [Fact]
    public void IconUri_is_null_without_a_tree_and_does_not_throw()
    {
        var (fake, _, tree) = Create();
        using var _cleanup = fake;

        Assert.Null(tree.IconUri(new Tech("tech_a", "A", null, TechArea.Physics, 0, null, "", [], false, false, false, false, "tech_a", [], new TechSourceRef("x", true, "f"), [])));
    }

    [Fact]
    public async Task Rebuilding_disposes_the_old_trees_zip_sources()
    {
        var (fake, manager, tree) = Create();
        using var _cleanup = fake;
        fake.Write("user/mod/zipmod.mod", "name=\"Zip\"\narchive=\"mod/zipmod.zip\"\n");
        var zipPath = Path.Combine(fake.UserDir, "mod", "zipmod.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("common/technology/zz_zip.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write("tech_z = { area = engineering tier = 0 start_tech = yes }\n");
        }
        manager.Lists.Save(new ModList("Zipped", [new ModListEntry("local:zipmod.mod", "Zip", "mod/zipmod.mod", null)], []));

        await tree.BuildAsync(new TechTreeChoice("Zipped", "Zipped"));
        var first = tree.Current!;
        Assert.Contains("tech_z", first.Database.Techs.Keys);
        var zipSource = first.Sources.Single(s => !s.IsBaseGame && s.Exists("common/technology/zz_zip.txt"));

        await tree.BuildAsync(tree.Choices()[0]);

        Assert.Throws<ObjectDisposedException>(() => zipSource.Exists("common/technology/zz_zip.txt"));
    }

    [Fact]
    public void Prune_deletes_old_pngs_and_keeps_fresh_ones()
    {
        using var tmp = new TempDir();
        var dir = tmp.Mkdir("cache");
        var oldPng = Path.Combine(dir, "old.png");
        var freshPng = Path.Combine(dir, "fresh.png");
        File.WriteAllBytes(oldPng, [1]);
        File.WriteAllBytes(freshPng, [1]);
        File.SetLastWriteTimeUtc(oldPng, DateTime.UtcNow.AddDays(-90));
        File.SetLastWriteTimeUtc(freshPng, DateTime.UtcNow.AddDays(-1));

        new IconCache(dir).Prune(TimeSpan.FromDays(60));

        Assert.False(File.Exists(oldPng));
        Assert.True(File.Exists(freshPng));
    }
}

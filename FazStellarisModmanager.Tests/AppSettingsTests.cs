using FazStellarisModmanager.Core;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Paths_live_under_root()
    {
        var p = new AppPaths(@"C:\data");

        Assert.Equal(@"C:\data\lists", p.Lists);
        Assert.Equal(@"C:\data\backups", p.Backups);
        Assert.Equal(@"C:\data\hashcache.json", p.HashCache);
        Assert.Equal(@"C:\data\settings.json", p.Settings);
    }

    [Fact]
    public void Missing_settings_file_gives_defaults()
    {
        using var tmp = new TempDir();

        Assert.Equal(new AppSettings(), SettingsStore.Load(Path.Combine(tmp.Path, "settings.json")));
    }

    [Fact]
    public void Settings_round_trip()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, "sub", "settings.json");
        var s = new AppSettings(GameDir: @"D:\Games\Stellaris", UserDir: null, PlayerName: "Faz");

        SettingsStore.Save(file, s);

        Assert.Equal(s, SettingsStore.Load(file));
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("settings.json", "{not json");

        Assert.Equal(new AppSettings(), SettingsStore.Load(file));
    }
}

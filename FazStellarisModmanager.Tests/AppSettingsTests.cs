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
    public void Old_settings_without_session_fields_load_with_defaults()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("settings.json", """{ "GameDir": null, "UserDir": null, "PlayerName": "Faz", "CheckForUpdates": false }""");

        var s = SettingsStore.Load(file);

        Assert.Equal("Faz", s.PlayerName);
        Assert.False(s.CheckForUpdates);
        Assert.Null(s.RecentHosts);
        Assert.Null(s.HostPort);
        Assert.False(s.StreamLiveGame);
        Assert.True(s.StreamTechnology);
    }

    [Fact]
    public void Session_fields_round_trip()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, "settings.json");
        var s = new AppSettings(RecentHosts: ["1.2.3.4:27015", "friend:5000"], HostPort: 5000, StreamLiveGame: true, StreamTechnology: false);

        SettingsStore.Save(file, s);
        var back = SettingsStore.Load(file);

        Assert.Equal(["1.2.3.4:27015", "friend:5000"], back.RecentHosts);
        Assert.Equal(5000, back.HostPort);
        Assert.True(back.StreamLiveGame);
        Assert.False(back.StreamTechnology);
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("settings.json", "{not json");

        Assert.Equal(new AppSettings(), SettingsStore.Load(file));
    }

    [Fact]
    public void Icons_path_is_under_root() =>
        Assert.Equal(@"C:\data\icons", new AppPaths(@"C:\data").Icons);
}

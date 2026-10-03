using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class SessionServiceTests
{
    /// <summary>One player's machine: a fake install plus its own app data, enabled mods and player name.</summary>
    sealed class Rig : IAsyncDisposable
    {
        public FakeInstall Fake { get; } = new();
        public SessionService Session { get; }

        public Rig(string playerName, string enabledModsJson)
        {
            var paths = new AppPaths(Fake.DataDir);
            SettingsStore.Save(paths.Settings, new AppSettings(UserDir: Fake.UserDir, PlayerName: playerName));
            Fake.Write("user/dlc_load.json", "{\"disabled_dlcs\":[],\"enabled_mods\":" + enabledModsJson + "}");
            Session = new SessionService(new ModManagerService(paths, _ => Fake.GameDir));
        }

        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            Fake.Dispose();
        }
    }

    [Fact]
    public async Task Joining_shows_the_difference_and_match_host_fixes_it()
    {
        await using var host = new Rig("Hosty", "[\"mod/ugc_111.mod\",\"mod/local.mod\"]");
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]");

        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);

        Assert.Equal(SessionRole.Client, client.Session.Role);
        Assert.Equal(UnitStatus.Missing, client.Session.MyDiff!.Mods.Single(m => m.Key == "ugc:111").Status);
        await Wait.Until(() => host.Session.Players.Any(p => p.Name == "Cli" && p.Status == PlayerStatus.Mismatch), "host sees mismatch");

        var plan = await client.Session.MatchHostAsync();

        Assert.True(plan.IsComplete);
        Assert.Equal(new[] { "mod/ugc_111.mod", "mod/local.mod" }, DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);
        Assert.True(client.Session.MyDiff!.IsMatch);
        await Wait.Until(() => host.Session.Players.Any(p => p.Name == "Cli" && p.Status == PlayerStatus.Ready), "host sees ready");
    }

    [Fact]
    public async Task Host_rescan_pushes_the_new_list_to_clients()
    {
        await using var host = new Rig("Hosty", "[\"mod/local.mod\"]");
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]");
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);
        Assert.True(client.Session.MyDiff!.IsMatch);

        host.Fake.Write("user/dlc_load.json", "{\"disabled_dlcs\":[],\"enabled_mods\":[\"mod/ugc_111.mod\",\"mod/local.mod\"]}");
        await host.Session.RescanAsync();

        await Wait.Until(() => client.Session.MyDiff is { IsMatch: false }, "client re-diffed against new host list");
    }

    [Fact]
    public async Task Leaving_and_stopping_reset_state()
    {
        await using var host = new Rig("Hosty", "[\"mod/local.mod\"]");
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]");
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);
        await Wait.Until(() => host.Session.Players.Count == 2, "client joined");

        await client.Session.LeaveAsync();
        Assert.Equal(SessionRole.None, client.Session.Role);
        await Wait.Until(() => host.Session.Players.Count == 1, "client gone from host roster");

        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);
        await host.Session.LeaveAsync();
        Assert.Equal(SessionRole.None, host.Session.Role);
        await Wait.Until(() => client.Session.Role == SessionRole.None, "client notices host stopped");
        Assert.Contains("Host closed", client.Session.LastDisconnectReason);
    }

    [Fact]
    public async Task Cannot_host_twice()
    {
        await using var host = new Rig("Hosty", "[]");
        await host.Session.HostAsync(0);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Session.HostAsync(0));
    }
}

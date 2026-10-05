using System.Net;
using System.Net.Sockets;
using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Core.Workshop;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class SessionServiceTests
{
    /// <summary>One player's machine: a fake install plus its own app data, enabled mods and player name.</summary>
    sealed class Rig : IAsyncDisposable
    {
        public FakeInstall Fake { get; } = new();
        public SessionService Session { get; }

        public Rig(string playerName, string enabledModsJson, Func<FakeInstall, IWorkshopService>? workshop = null)
        {
            var paths = new AppPaths(Fake.DataDir);
            SettingsStore.Save(paths.Settings, new AppSettings(UserDir: Fake.UserDir, PlayerName: playerName));
            Fake.Write("user/dlc_load.json", "{\"disabled_dlcs\":[],\"enabled_mods\":" + enabledModsJson + "}");
            Session = new SessionService(new ModManagerService(paths, _ => Fake.GameDir), workshop?.Invoke(Fake));
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
    public async Task Cancelled_match_does_not_leave_the_host_showing_busy()
    {
        await using var host = new Rig("Hosty", "[\"mod/ugc_111.mod\",\"mod/local.mod\"]");
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]");
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);
        client.Session.Changed += () =>
        {
            if (client.Session.Activity?.StartsWith("Applying") == true) client.Session.CancelCurrent();
        };

        await Assert.ThrowsAnyAsync<Exception>(() => client.Session.MatchHostAsync());

        await Wait.Until(() => host.Session.Players.Any(p => p.Name == "Cli" && p.Status != PlayerStatus.Busy), "host sees client not busy");
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
    public async Task Roster_updates_do_not_recompute_the_diff()
    {
        await using var host = new Rig("Hosty", "[\"mod/local.mod\"]");
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]");
        await using var other = new Rig("Other", "[\"mod/local.mod\"]");
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);
        await Wait.Until(() => client.Session.Players.Count == 2, "client sees itself in the roster");
        var diff = client.Session.MyDiff;

        await other.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);
        await Wait.Until(() => client.Session.Players.Count == 3, "client sees the other player");

        Assert.NotNull(diff);
        Assert.Same(diff, client.Session.MyDiff);
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

    [Fact]
    public async Task A_throwing_Changed_handler_does_not_wedge_the_session()
    {
        await using var host = new Rig("Hosty", "[]");
        host.Session.Changed += () => throw new InvalidOperationException("boom");

        await host.Session.HostAsync(0).WaitAsync(TimeSpan.FromSeconds(5));
        await host.Session.LeaveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await host.Session.HostAsync(0).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Match_host_after_the_host_left_throws_and_leaves_the_load_file_alone()
    {
        await using var host = new Rig("Hosty", "[\"mod/ugc_111.mod\",\"mod/local.mod\"]");
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]");
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);
        var before = DlcLoadFile.Read(client.Fake.UserDir).EnabledMods.ToArray();

        await host.Session.LeaveAsync();
        await Wait.Until(() => client.Session.Role == SessionRole.None, "client notices host stopped");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Session.MatchHostAsync());
        Assert.Equal(before, DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);
        Assert.Null(client.Session.LastPlan);
    }

    [Fact]
    public async Task Leave_cancels_a_join_that_is_waiting_for_the_host()
    {
        await using var client = new Rig("Cli", "[]");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var join = client.Session.JoinAsync("127.0.0.1", port);
            await Wait.Until(() => client.Session.Activity?.StartsWith("Connecting") == true, "join is connecting");

            await client.Session.LeaveAsync().WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => join.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(SessionRole.None, client.Session.Role);
        }
        finally { listener.Stop(); }
    }

    [Theory]
    [InlineData("", 5000)]
    [InlineData("127.0.0.1", 0)]
    [InlineData("127.0.0.1", 70000)]
    public async Task Join_rejects_bad_input(string address, int port)
    {
        await using var client = new Rig("Cli", "[]");
        await Assert.ThrowsAsync<ArgumentException>(() => client.Session.JoinAsync(address, port));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task Host_rejects_bad_port(int port)
    {
        await using var host = new Rig("Hosty", "[]");
        await Assert.ThrowsAsync<ArgumentException>(() => host.Session.HostAsync(port));
    }

    sealed class NoProgress : IProgress<WorkshopProgress>
    {
        public void Report(WorkshopProgress value) { }
    }

    [Fact]
    public async Task Workshop_install_downloads_missing_mods_then_matches_the_host()
    {
        await using var host = new Rig("Hosty", "[\"mod/ugc_222.mod\",\"mod/local.mod\"]");
        host.Fake.AddWorkshopItem(222);
        FakeWorkshop? workshop = null;
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]", f => workshop = new FakeWorkshop(id => { f.AddWorkshopItem(id); return true; }));
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);

        var needs = WorkshopNeeds.From(await client.Session.PlanAsync());
        Assert.Equal([222UL], needs.Items.Select(i => i.Id));
        Assert.Equal(["mod/local.mod"], DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);

        var result = await client.Session.InstallFromWorkshopAndMatchAsync([222UL], new NoProgress());

        Assert.Equal([222UL], workshop!.Requested);
        Assert.True(Assert.Single(result.Items).Success);
        Assert.True(result.Plan!.IsComplete);
        Assert.Null(result.Note);
        Assert.Equal(["mod/ugc_222.mod", "mod/local.mod"], DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);
        Assert.True(client.Session.MyDiff!.IsMatch);
    }

    [Fact]
    public async Task A_failed_workshop_item_is_reported_and_the_rest_is_still_matched()
    {
        await using var host = new Rig("Hosty", "[\"mod/ugc_222.mod\",\"mod/local.mod\"]");
        host.Fake.AddWorkshopItem(222);
        await using var client = new Rig("Cli", "[]", _ => new FakeWorkshop(_ => false));
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);

        var result = await client.Session.InstallFromWorkshopAndMatchAsync([222UL], new NoProgress());

        var item = Assert.Single(result.Items);
        Assert.Equal((false, "Not available."), (item.Success, item.Error));
        Assert.Equal(["mod/local.mod"], DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);
        Assert.Equal(["ugc:222"], result.Plan!.NeedsWorkshopInstall.Select(e => e.Key));
        Assert.False(client.Session.MyDiff!.IsMatch);
    }

    [Fact]
    public async Task Matching_without_installing_needs_no_workshop_service_but_installing_does()
    {
        await using var host = new Rig("Hosty", "[\"mod/local.mod\"]");
        await using var client = new Rig("Cli", "[]");
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);

        var result = await client.Session.InstallFromWorkshopAndMatchAsync([], new NoProgress());

        Assert.Empty(result.Items);
        Assert.Equal(["mod/local.mod"], DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.Session.InstallFromWorkshopAndMatchAsync([5UL], new NoProgress()));
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public async Task Steam_being_unavailable_propagates_leaves_the_load_file_alone_and_the_host_not_busy()
    {
        await using var host = new Rig("Hosty", "[\"mod/ugc_222.mod\",\"mod/local.mod\"]");
        bool HostSeesBusy() => host.Session.Players.Any(p => p.Name == "Cli" && p.Status == PlayerStatus.Busy);
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]", _ => new FakeWorkshop(async (_, _) =>
        {
            await Wait.Until(HostSeesBusy, "host sees the client downloading");
            throw new WorkshopUnavailableException("Steam is not running.");
        }));
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);
        await Wait.Until(() => host.Session.Players.Any(p => p.Name == "Cli" && p.Status != PlayerStatus.Busy), "host sees the client's snapshot");

        await Assert.ThrowsAsync<WorkshopUnavailableException>(() => client.Session.InstallFromWorkshopAndMatchAsync([222UL], new NoProgress()));

        Assert.Equal(["mod/local.mod"], DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);
        await Wait.Until(() => host.Session.Players.Any(p => p.Name == "Cli" && p.Status != PlayerStatus.Busy), "host sees client not busy");
    }

    [Fact]
    public async Task Cancelling_a_workshop_download_applies_nothing()
    {
        await using var host = new Rig("Hosty", "[\"mod/ugc_222.mod\",\"mod/local.mod\"]");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]", _ => new FakeWorkshop(async (_, ct) =>
        {
            started.TrySetResult();
            return await never.Task.WaitAsync(ct);
        }));
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);

        var run = client.Session.InstallFromWorkshopAndMatchAsync([222UL], new NoProgress());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        client.Session.CancelCurrent();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(["mod/local.mod"], DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);
        Assert.Null(client.Session.LastPlan);
    }

    [Fact]
    public async Task When_the_host_leaves_during_the_download_the_results_are_kept_and_nothing_is_applied()
    {
        await using var host = new Rig("Hosty", "[\"mod/ugc_222.mod\",\"mod/local.mod\"]");
        SessionService? clientSession = null;
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]", _ => new FakeWorkshop(async (_, _) =>
        {
            await host.Session.LeaveAsync();
            await Wait.Until(() => clientSession!.Role == SessionRole.None, "client notices host stopped");
            return true;
        }));
        clientSession = client.Session;
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);

        var result = await client.Session.InstallFromWorkshopAndMatchAsync([222UL], new NoProgress());

        Assert.True(Assert.Single(result.Items).Success);
        Assert.Null(result.Plan);
        Assert.Equal("The host disconnected; the downloads finished but the host's list was not applied.", result.Note);
        Assert.Equal(["mod/local.mod"], DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);
    }
}

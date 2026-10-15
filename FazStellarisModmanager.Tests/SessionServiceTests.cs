using System.Net;
using System.Net.Sockets;
using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Saves;
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

        public Rig(string playerName, string enabledModsJson, Func<FakeInstall, IWorkshopService>? workshop = null,
            LiveGameService? live = null, Func<string, GameSnapshot>? readTechs = null, string? saveGamesDir = null)
        {
            var paths = new AppPaths(Fake.DataDir);
            SettingsStore.Save(paths.Settings, new AppSettings(UserDir: Fake.UserDir, PlayerName: playerName));
            Fake.Write("user/dlc_load.json", "{\"disabled_dlcs\":[],\"enabled_mods\":" + enabledModsJson + "}");
            Session = new SessionService(new ModManagerService(paths, _ => Fake.GameDir) { IsLauncherRunning = () => false }, workshop?.Invoke(Fake),
                live, readTechs, saveGamesDir is null ? null : () => saveGamesDir);
        }

        /// <summary>The settings file as saved on disk.</summary>
        public AppSettings SavedSettings => SettingsStore.Load(new AppPaths(Fake.DataDir).Settings);

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
    public async Task Match_host_renames_a_clashing_copy_of_a_listed_mod()
    {
        await using var host = new Rig("Hosty", "[\"mod/ugc_111.mod\",\"mod/local.mod\"]");
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]");
        client.Fake.Write("user/mod/copy.mod", "name=" + '"' + "Local Mod" + '"' + (char)10 + "path=" + '"' + "mod/local" + '"' + (char)10);
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);

        await client.Session.MatchHostAsync();

        Assert.Equal(1, client.Session.LastDupeRenames);
        Assert.Null(client.Session.LastLauncherWarning);
        Assert.Contains("Local Mod (duplicate: copy)", File.ReadAllText(Path.Combine(client.Fake.UserDir, "mod", "copy.mod")));
        Assert.DoesNotContain("duplicate", File.ReadAllText(Path.Combine(client.Fake.UserDir, "mod", "local.mod")));
        Assert.Equal(new[] { "mod/ugc_111.mod", "mod/local.mod" }, DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);
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
            Assert.Null(client.SavedSettings.RecentHosts); // a join that never finished is not remembered
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Joining_remembers_the_address_and_forgetting_removes_it()
    {
        await using var host = new Rig("Hosty", "[]");
        await using var client = new Rig("Cli", "[]");
        await host.Session.HostAsync(0);
        var address = $"127.0.0.1:{host.Session.HostPort!.Value}";

        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);

        Assert.Equal([address], client.SavedSettings.RecentHosts);
        Assert.Equal("Cli", client.SavedSettings.PlayerName); // the other settings are kept

        await client.Session.LeaveAsync();
        client.Session.ForgetRecentHost(address);

        Assert.Empty(client.SavedSettings.RecentHosts!);
    }

    [Fact]
    public async Task Hosting_remembers_the_port()
    {
        await using var host = new Rig("Hosty", "[]");
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        await host.Session.HostAsync(port);

        Assert.Equal(port, host.SavedSettings.HostPort);
        Assert.Equal("Hosty", host.SavedSettings.PlayerName);
    }

    /// <summary>A save in which Hosty plays country 0 and Cli country 1; country 2 is an AI.</summary>
    static GameSnapshot SaveAt(string path, bool techOnly)
    {
        static SaveCountry C(int id, params string[] techs) =>
            new(id, "default", "N" + id, new Dictionary<string, string>(), null, null, id + 1, 10, 1, 1, 1, 1, 1, 1, null, [], techs);
        return new("MP", "2300.01.01", "v", path, File.GetLastWriteTimeUtc(path), [new SavePlayer("Hosty", 0), new SavePlayer("Cli", 1)],
            techOnly ? [C(0, "tech_a"), C(1, "tech_b", "tech_c")] : [C(0, "tech_a"), C(1, "tech_b", "tech_c"), C(2, "tech_z")], TechOnly: techOnly);
    }

    [Fact]
    public async Task Streaming_settings_persist_and_apply_while_hosting()
    {
        await using var host = new Rig("Hosty", "[]");
        Assert.Null(host.Session.HostStreaming);

        await host.Session.SetStreamingAsync(true, false);

        Assert.Equal((true, false), (host.SavedSettings.StreamLiveGame, host.SavedSettings.StreamTechnology));
        Assert.Equal("Hosty", host.SavedSettings.PlayerName);
        Assert.Null(host.Session.HostStreaming); // not hosting

        await host.Session.HostAsync(0);
        Assert.Equal(new StreamingState(true, true), host.Session.HostStreaming);

        await host.Session.SetStreamingAsync(false, false);
        Assert.Equal(new StreamingState(false, false), host.Session.HostStreaming);
        Assert.Equal((false, false), (host.SavedSettings.StreamLiveGame, host.SavedSettings.StreamTechnology));
    }

    [Fact]
    public async Task Technology_mode_streams_the_light_tech_read_of_the_save_games_folder()
    {
        var techReads = 0;
        await using var host = new Rig("Hosty", "[]", readTechs: p =>
        {
            Interlocked.Increment(ref techReads);
            return SaveAt(p, techOnly: true);
        });
        host.Fake.Write("user/save games/mp_game/autosave.sav", "x");
        await using var client = new Rig("Cli", "[]");

        await host.Session.HostAsync(0); // the default settings stream technology only
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);

        Assert.Equal(new StreamingState(false, true), host.Session.HostStreaming);
        await Wait.Until(() => client.Session.HostLive is not null, "tech view from the light reader", 15_000);
        var view = client.Session.HostLive!.Snapshot;
        Assert.True(view.TechOnly);
        Assert.Equal(1, client.Session.HostLive.ViewerId);
        Assert.Equal(["tech_b", "tech_c"], Assert.Single(view.Countries).Techs);
        Assert.Equal(new StreamingState(false, true), client.Session.HostStreaming);
        Assert.True(Volatile.Read(ref techReads) >= 1);
        await Wait.Until(() => host.Session.LastLiveSentBytes > 0, "host records the size sent");
        Assert.Equal("2300.01.01", host.Session.LastLiveSentDate);

        // Off stops the light reader: a newer save is not read (a reader still watching would after its 2 s stable wait).
        await host.Session.SetStreamingAsync(false, false);
        var readsBefore = Volatile.Read(ref techReads);
        host.Fake.Write("user/save games/mp_game/autosave_2.sav", "y");
        await Task.Delay(3000);
        Assert.Equal(readsBefore, Volatile.Read(ref techReads));
    }

    [Fact]
    public async Task The_full_reader_is_held_only_while_streaming_the_live_game()
    {
        using var saves = new TempDir();
        saves.Write("mp_game/autosave.sav", "x");
        using var full = new LiveGameService(() => saves.Path, p => SaveAt(p, techOnly: false), TimeSpan.Zero);
        await using var host = new Rig("Hosty", "[]", live: full, readTechs: p => SaveAt(p, techOnly: true), saveGamesDir: saves.Path);
        await host.Session.SetStreamingAsync(true, false);

        await host.Session.HostAsync(0);
        Assert.True(full.IsWatching);

        await host.Session.SetStreamingAsync(false, true);
        Assert.False(full.IsWatching);

        await host.Session.SetStreamingAsync(true, false);
        Assert.True(full.IsWatching);
        using (full.Acquire()) // a page's own lease keeps it watching after the stream lets go
        {
            await host.Session.SetStreamingAsync(false, false);
            Assert.True(full.IsWatching);
        }
        Assert.False(full.IsWatching);

        await host.Session.SetStreamingAsync(true, false);
        Assert.True(full.IsWatching);
        await host.Session.LeaveAsync();
        Assert.False(full.IsWatching);
    }

    [Fact]
    public async Task Switching_streaming_resends_in_the_new_mode_and_off_drops_the_clients_host_data()
    {
        using var saves = new TempDir();
        saves.Write("mp_game/autosave.sav", "x");
        using var full = new LiveGameService(() => saves.Path, p => SaveAt(p, techOnly: false), TimeSpan.Zero);
        await using var host = new Rig("Hosty", "[]", live: full, readTechs: p => SaveAt(p, techOnly: true), saveGamesDir: saves.Path);
        await using var client = new Rig("Cli", "[]");
        await host.Session.SetStreamingAsync(true, true);
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);

        await Wait.Until(() => client.Session.HostLive is { Snapshot.TechOnly: false }, "full view");
        Assert.Equal(3, client.Session.HostLive!.Snapshot.Countries.Count);
        Assert.Equal(new StreamingState(true, true), client.Session.HostStreaming);

        await host.Session.SetStreamingAsync(false, true);
        await Wait.Until(() => client.Session.HostLive is { Snapshot.TechOnly: true }, "tech view resent from the full save");
        Assert.Equal(["tech_b", "tech_c"], Assert.Single(client.Session.HostLive!.Snapshot.Countries).Techs);
        Assert.Equal(new StreamingState(false, true), client.Session.HostStreaming);

        await host.Session.SetStreamingAsync(false, false);
        await Wait.Until(() => client.Session.HostLive is null, "the client drops the host's live data");
        Assert.Null(client.Session.HostLiveReceivedUtc);
        Assert.Equal(new StreamingState(false, false), client.Session.HostStreaming);
        Assert.Equal(SessionRole.Client, client.Session.Role);
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

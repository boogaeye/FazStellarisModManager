using System.Net;
using System.Text.Json;
using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Tests.TestUtil;
using static FazStellarisModmanager.Tests.TestUtil.TestSnapshots;

namespace FazStellarisModmanager.Tests;

public class SessionLiveTests
{
    static SaveCountry C(int id, int rank, params int[] contacts) =>
        new(id, "default", "N" + id, new Dictionary<string, string>(), null, null, rank, rank * 10, 1, 1, 1, 1, 1, 1, null, contacts, ["tech_" + id]);

    static GameSnapshot Save(string date = "2300.01.01") =>
        new("Game", date, "v", "x.sav", DateTime.UtcNow, [new SavePlayer("Hosty", 0), new SavePlayer("Alice", 1), new SavePlayer("Bob", 2)],
            [C(0, 1, 1), C(1, 2, 0), C(2, 3), C(3, 4)]);

    static SessionHost StartHost(StreamMode mode = StreamMode.LiveGame)
    {
        var host = new SessionHost("Hosty", List("ugc:1"), Machine("H", "ugc:1"), mode);
        host.Start(0, IPAddress.Loopback);
        return host;
    }

    static int JsonSize(GameSnapshot view, int? viewer) =>
        JsonSerializer.SerializeToUtf8Bytes<SessionMessage>(new LiveUpdate(view, viewer), Framing.Json).Length;

    static async Task<SessionClient> Connect(int port, string name)
    {
        var c = await SessionClient.ConnectAsync("127.0.0.1", port, name);
        c.Start();
        return c;
    }

    [Fact]
    public async Task Clients_get_their_filtered_view_by_name_and_updates()
    {
        await using var host = StartHost();
        await using var alice = await Connect(host.Port, "alice");
        host.UpdateLive(Save());
        await Wait.Until(() => alice.Live is not null, "first live update");
        Assert.Equal(1, alice.Live!.ViewerId);
        Assert.Equal(["tech_1"], alice.Live.Snapshot.Countries.Single(c => c.Id == 1).Techs);
        Assert.Null(alice.Live.Snapshot.Countries.Single(c => c.Id == 2).NameKey); // not contacted by Alice

        host.UpdateLive(Save("2300.02.01"));
        await Wait.Until(() => alice.Live!.Snapshot.Date == "2300.02.01", "second update");
    }

    [Fact]
    public async Task Late_joiner_gets_the_current_save_and_can_choose()
    {
        await using var host = StartHost();
        host.UpdateLive(Save());
        await using var carol = await Connect(host.Port, "Carol");
        await Wait.Until(() => carol.Live is not null, "update on join");
        Assert.Null(carol.Live!.ViewerId);
        Assert.Empty(carol.Live.Snapshot.Countries);
        Assert.Equal(3, carol.Live.Snapshot.Players.Count);

        await carol.SendViewAsAsync(99); // not a player country: ignored
        await carol.SendViewAsAsync(2);
        await Wait.Until(() => carol.Live!.ViewerId == 2, "chosen view");
        Assert.Equal(["tech_2"], carol.Live!.Snapshot.Countries.Single(c => c.Id == 2).Techs);
    }

    [Fact]
    public async Task No_live_data_sends_nothing()
    {
        await using var host = StartHost();
        await using var alice = await Connect(host.Port, "Alice");
        await Wait.Until(() => alice.Roster.Count == 2, "roster");
        await Task.Delay(200);
        Assert.Null(alice.Live);
    }

    [Fact]
    public async Task Off_tells_peers_and_sends_no_live_data()
    {
        await using var host = StartHost(StreamMode.Off);
        await using var alice = await Connect(host.Port, "Alice");
        await Wait.Until(() => alice.Streaming is not null, "streaming state");
        Assert.Equal(new StreamingState(false, false), alice.Streaming);

        host.UpdateLive(Save());
        await using var bob = await Connect(host.Port, "Bob"); // joining while off: no live data either
        await Wait.Until(() => bob.Streaming is not null, "bob's streaming state");
        await Task.Delay(200);

        Assert.Null(alice.Live);
        Assert.Null(bob.Live);
        Assert.Null(host.LastLiveSentDate);
        Assert.Equal(0, host.LastLiveSentBytes);
    }

    [Fact]
    public async Task Technology_sends_each_peer_only_its_own_techs_and_records_the_size()
    {
        await using var host = StartHost(StreamMode.Technology);
        await using var alice = await Connect(host.Port, "Alice");
        await using var carol = await Connect(host.Port, "Carol"); // not in the save
        await Wait.Until(() => alice.Streaming is not null && carol.Streaming is not null, "streaming states");
        Assert.Equal(new StreamingState(false, true), alice.Streaming);
        var save = Save();

        host.UpdateLive(save);

        await Wait.Until(() => alice.Live is not null && carol.Live is not null, "tech views");
        var view = alice.Live!.Snapshot;
        Assert.True(view.TechOnly);
        Assert.Equal(1, alice.Live.ViewerId);
        Assert.Equal(3, view.Players.Count);
        var mine = Assert.Single(view.Countries);
        Assert.Equal((1, (string?)null, 0), (mine.Id, mine.NameKey, mine.VictoryRank));
        Assert.Equal(save.Countries.Single(c => c.Id == 1).Techs, mine.Techs);
        Assert.Null(view.Diplo);
        Assert.Null(view.Community);
        Assert.Empty(carol.Live!.Snapshot.Countries);

        var largest = Math.Max(JsonSize(LiveFilter.TechOnly(save, 1), 1), JsonSize(LiveFilter.TechOnly(save, null), null));
        await Wait.Until(() => host.LastLiveSentBytes == largest, "size of the largest view sent");
        Assert.Equal("2300.01.01", host.LastLiveSentDate);
    }

    [Fact]
    public async Task Switching_modes_tells_peers_and_resends_what_the_host_has()
    {
        await using var host = StartHost(StreamMode.LiveGame);
        await using var alice = await Connect(host.Port, "Alice");
        var save = Save();
        host.UpdateLive(save);
        await Wait.Until(() => alice.Live is { Snapshot.Countries.Count: 4 }, "full view");
        Assert.Equal(new StreamingState(true, true), alice.Streaming);
        Assert.False(alice.Live!.Snapshot.TechOnly);

        host.SetMode(StreamMode.Technology);

        await Wait.Until(() => alice.Live!.Snapshot.TechOnly, "tech view resent without a new save");
        Assert.Equal(StreamMode.Technology, host.Mode);
        Assert.Equal(new StreamingState(false, true), alice.Streaming);
        Assert.Equal(["tech_1"], Assert.Single(alice.Live!.Snapshot.Countries).Techs);
        await Wait.Until(() => host.LastLiveSentBytes == JsonSize(LiveFilter.TechOnly(save, 1), 1), "tech view size");

        host.SetMode(StreamMode.Off);
        await Wait.Until(() => alice.Streaming == new StreamingState(false, false), "off");
        var last = alice.Live;
        host.UpdateLive(Save("2300.02.01"));
        await Task.Delay(200);
        Assert.Same(last, alice.Live);
        Assert.Null(host.LastLiveSentDate);

        host.SetMode(StreamMode.LiveGame);
        await Wait.Until(() => alice.Live!.Snapshot.Date == "2300.02.01" && !alice.Live.Snapshot.TechOnly, "full view of the newest save");
        Assert.Equal(new StreamingState(true, true), alice.Streaming);
    }

    [Fact]
    public async Task A_late_joiner_gets_the_streaming_state_before_the_live_data()
    {
        await using var host = StartHost(StreamMode.Technology);
        host.UpdateLive(Save());
        await using var alice = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Alice");
        StreamingState? stateAtFirstLive = null;
        var sawLive = false;
        alice.Changed += () =>
        {
            if (sawLive || alice.Live is null) return;
            stateAtFirstLive = alice.Streaming;
            Volatile.Write(ref sawLive, true);
        };
        alice.Start();

        await Wait.Until(() => Volatile.Read(ref sawLive), "live data");
        Assert.Equal(new StreamingState(false, true), stateAtFirstLive);
        Assert.True(alice.Live!.Snapshot.TechOnly);
    }

    [Fact]
    public async Task The_same_save_is_not_sent_again()
    {
        await using var host = StartHost(StreamMode.Technology);
        await using var alice = await Connect(host.Port, "Alice");
        var save = Save();
        host.UpdateLive(save);
        await Wait.Until(() => alice.Live is not null, "first view");
        var first = alice.Live;

        host.UpdateLive(save); // the live services report every refresh, mostly with an unchanged save
        await Task.Delay(200);

        Assert.Same(first, alice.Live);
    }
}

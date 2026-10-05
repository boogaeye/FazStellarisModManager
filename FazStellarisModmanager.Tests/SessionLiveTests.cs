using System.Net;
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

    static SessionHost StartHost()
    {
        var host = new SessionHost("Hosty", List("ugc:1"), Machine("H", "ugc:1"));
        host.Start(0, IPAddress.Loopback);
        return host;
    }

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
}

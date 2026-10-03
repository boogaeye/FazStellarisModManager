using System.Net;
using System.Net.Sockets;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Tests.TestUtil;
using static FazStellarisModmanager.Tests.TestUtil.TestSnapshots;

namespace FazStellarisModmanager.Tests;

public class SessionHostClientTests
{
    static SessionHost StartHost(params string[] modKeys)
    {
        var host = new SessionHost("Hosty", List(modKeys), Machine("H", modKeys));
        host.Start(0, IPAddress.Loopback);
        return host;
    }

    [Fact]
    public async Task Clients_get_the_target_and_the_roster_reflects_their_diffs()
    {
        await using var host = StartHost("ugc:1", "ugc:2");
        await using var alice = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Alice");
        await using var bob = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Bob");

        Assert.Equal(new[] { "ugc:1", "ugc:2" }, alice.Target.HostList.Mods.Select(m => m.Key));
        await alice.SendSnapshotAsync(Machine("A", "ugc:1", "ugc:2"));
        await bob.SendSnapshotAsync(Machine("B", "ugc:1"));
        await Wait.Until(() => alice.Roster.Count == 3 && alice.Roster.All(p => p.Status != PlayerStatus.Waiting), "roster with both diffs");

        Assert.Equal(("Hosty", true), (alice.Roster[0].Name, alice.Roster[0].IsHost));
        Assert.Equal(PlayerStatus.Ready, alice.Roster.Single(p => p.Name == "Alice").Status);
        var b = alice.Roster.Single(p => p.Name == "Bob");
        Assert.Equal((PlayerStatus.Mismatch, 1), (b.Status, b.Summary!.Missing));
        Assert.Equal(UnitStatus.Missing, host.DiffFor(b.Id)!.Mods.Single(m => m.Key == "ugc:2").Status);
        Assert.Equal(3, host.Players.Count);
    }

    [Fact]
    public async Task Busy_shows_until_the_next_snapshot()
    {
        await using var host = StartHost("ugc:1");
        await using var alice = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Alice");

        await alice.SendBusyAsync("Matching host…");
        await Wait.Until(() => host.Players.Any(p => p.Status == PlayerStatus.Busy && p.Activity == "Matching host…"), "busy status");
        await alice.SendSnapshotAsync(Machine("A", "ugc:1"));
        await Wait.Until(() => host.Players.Any(p => p.Name == "Alice" && p.Status == PlayerStatus.Ready), "ready after snapshot");
    }

    [Fact]
    public async Task Host_updates_reach_clients_and_rediff_them()
    {
        await using var host = StartHost("ugc:1");
        await using var alice = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Alice");
        await alice.SendSnapshotAsync(Machine("A", "ugc:1"));
        await Wait.Until(() => alice.Roster.Any(p => p.Name == "Alice" && p.Status == PlayerStatus.Ready), "initially ready");

        await host.UpdateHostAsync(List("ugc:1", "ugc:3"), Machine("H", "ugc:1", "ugc:3"));

        await Wait.Until(() => alice.Target.HostList.Mods.Count == 2, "new host target");
        await Wait.Until(() => alice.Roster.Any(p => p.Name == "Alice" && p.Status == PlayerStatus.Mismatch), "re-diffed as mismatch");
    }

    [Fact]
    public async Task Leaving_clients_disappear_from_the_roster()
    {
        await using var host = StartHost("ugc:1");
        await using var alice = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Alice");
        var bob = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Bob");
        await Wait.Until(() => host.Players.Count == 3, "both joined");

        await bob.DisposeAsync();

        await Wait.Until(() => host.Players.Count == 2 && alice.Roster.Count == 2, "bob removed everywhere");
    }

    [Fact]
    public async Task Closing_the_host_tells_clients_why()
    {
        var host = StartHost("ugc:1");
        await using var alice = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Alice");
        string? reason = null;
        alice.Disconnected += r => reason = r;

        await host.DisposeAsync();
        await host.DisposeAsync(); // idempotent

        await Wait.Until(() => reason is not null, "disconnect notification");
        Assert.Contains("Host closed", reason);
        Assert.False(alice.IsConnected);
    }

    [Fact]
    public async Task Wrong_protocol_version_is_rejected()
    {
        await using var host = StartHost("ugc:1");
        using var raw = new TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, host.Port);

        await Framing.WriteAsync(raw.GetStream(), new Hello(99, "x", "Old"));
        var reply = await Framing.ReadAsync(raw.GetStream());

        Assert.Contains("99", Assert.IsType<Reject>(reply).Reason);
        Assert.Single(host.Players); // only the host
    }

    [Fact]
    public async Task A_garbage_client_does_not_break_the_host()
    {
        await using var host = StartHost("ugc:1");
        using (var raw = new TcpClient())
        {
            await raw.ConnectAsync(IPAddress.Loopback, host.Port);
            await raw.GetStream().WriteAsync(new byte[] { 0xff, 0xff, 0xff, 0x7f, 1, 2, 3 });
        }

        await using var alice = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Alice");

        await Wait.Until(() => host.Players.Count == 2, "alice joined despite garbage client");
    }

    [Fact]
    public async Task Connecting_to_a_closed_port_fails()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        await Assert.ThrowsAnyAsync<SocketException>(() => SessionClient.ConnectAsync("127.0.0.1", port, "Nobody"));
    }
}

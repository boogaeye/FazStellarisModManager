using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Core.Snapshots;
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

    static async Task<SessionClient> Connect(int port, string name)
    {
        var client = await SessionClient.ConnectAsync("127.0.0.1", port, name);
        client.Start();
        return client;
    }

    [Fact]
    public async Task Clients_get_the_target_and_the_roster_reflects_their_diffs()
    {
        await using var host = StartHost("ugc:1", "ugc:2");
        await using var alice = await Connect(host.Port, "Alice");
        await using var bob = await Connect(host.Port, "Bob");

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
        await using var alice = await Connect(host.Port, "Alice");

        await alice.SendBusyAsync("Matching host…");
        await Wait.Until(() => host.Players.Any(p => p.Status == PlayerStatus.Busy && p.Activity == "Matching host…"), "busy status");
        await alice.SendSnapshotAsync(Machine("A", "ugc:1"));
        await Wait.Until(() => host.Players.Any(p => p.Name == "Alice" && p.Status == PlayerStatus.Ready), "ready after snapshot");
    }

    [Fact]
    public async Task Host_updates_reach_clients_and_rediff_them()
    {
        await using var host = StartHost("ugc:1");
        await using var alice = await Connect(host.Port, "Alice");
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
        await using var alice = await Connect(host.Port, "Alice");
        var bob = await Connect(host.Port, "Bob");
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
        alice.Start();

        await host.DisposeAsync();
        await host.DisposeAsync(); // idempotent

        await Wait.Until(() => reason is not null, "disconnect notification");
        Assert.Contains("Host closed", reason);
        Assert.False(alice.IsConnected);
    }

    [Fact]
    public async Task Disconnected_fires_once_and_disposing_from_the_handler_does_not_deadlock()
    {
        var host = StartHost("ugc:1");
        var alice = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Alice");
        var count = 0;
        string? reason = null;
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        alice.Disconnected += async r =>
        {
            Interlocked.Increment(ref count);
            reason = r;
            try
            {
                await alice.DisposeAsync();
                disposed.TrySetResult();
            }
            catch (Exception ex)
            {
                disposed.TrySetException(ex);
            }
        };
        alice.Start();

        await host.DisposeAsync();
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await alice.DisposeAsync(); // idempotent
        await Task.Delay(200);

        Assert.Equal(1, Volatile.Read(ref count));
        Assert.Contains("Host closed", reason);
        Assert.Equal(reason, alice.DisconnectReason);
        Assert.False(alice.IsConnected);
    }

    [Fact]
    public async Task Wrong_protocol_version_is_rejected()
    {
        await using var host = StartHost("ugc:1");
        using var raw = new TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, host.Port);

        await Framing.WriteAsync(raw.GetStream(), new Hello(99, "x", "Old"));
        var reply = await Framing.ReadAsync(raw.GetStream()).WaitAsync(TimeSpan.FromSeconds(10));

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

        await using var alice = await Connect(host.Port, "Alice");

        await Wait.Until(() => host.Players.Count == 2, "alice joined despite garbage client");
    }

    [Fact]
    public async Task An_oversized_hello_is_refused()
    {
        await using var host = StartHost("ugc:1");
        using (var raw = new TcpClient())
        {
            await raw.ConnectAsync(IPAddress.Loopback, host.Port);
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, 1024 * 1024);
            await raw.GetStream().WriteAsync(header);

            SessionMessage? reply = null;
            try
            {
                reply = await Framing.ReadAsync(raw.GetStream()).WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (IOException)
            {
                // a reset is as good as a close
            }
            Assert.Null(reply);
        }

        await using var alice = await Connect(host.Port, "Alice");
        await Wait.Until(() => host.Players.Count == 2, "alice joined after the oversized hello");
    }

    [Fact]
    public async Task Names_and_activities_are_cleaned()
    {
        await using var host = StartHost("ugc:1");
        var name = "Bo" + (char)7 + new string('x', 60);
        await using var bob = await Connect(host.Port, name);
        await Wait.Until(() => host.Players.Count == 2, "bob joined");

        var shown = host.Players.Single(p => !p.IsHost).Name;
        Assert.Equal(40, shown.Length);
        Assert.StartsWith("Bo x", shown);
        Assert.DoesNotContain(shown, char.IsControl);

        await bob.SendBusyAsync(new string('y', 500));
        await Wait.Until(() => host.Players.Any(p => p.Status == PlayerStatus.Busy), "busy");
        Assert.Equal(new string('y', 80), host.Players.Single(p => !p.IsHost).Activity);
    }

    [Fact]
    public async Task The_connection_cap_turns_away_extra_clients()
    {
        await using var host = StartHost("ugc:1");
        host.MaxPeers = 2;
        await using var alice = await Connect(host.Port, "Alice");
        await using var bob = await Connect(host.Port, "Bob");

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => SessionClient.ConnectAsync("127.0.0.1", host.Port, "Carol"));

        Assert.True(ex is IOException or SocketException or TimeoutException, $"Unexpected {ex.GetType().Name}: {ex.Message}");
        Assert.Equal(3, host.Players.Count);
    }

    [Fact]
    public async Task A_peer_that_stops_reading_does_not_stall_the_host()
    {
        var host = StartHost("ugc:1");
        host.WriteTimeout = TimeSpan.FromSeconds(1);

        using var stuck = new TcpClient { ReceiveBufferSize = 4096 };
        await stuck.ConnectAsync(IPAddress.Loopback, host.Port);
        await Framing.WriteAsync(stuck.GetStream(), new Hello(SessionProtocol.Version, "x", "Stuck"));
        await using var alice = await Connect(host.Port, "Alice");
        await Wait.Until(() => host.Players.Count == 3, "both joined");

        for (var i = 0; i < 5; i++)
        {
            var big = Machine("H" + i, "ugc:1");
            var files = Enumerable.Range(0, 100_000)
                .Select(n => new ModFile($"common/file_{n}.txt", Guid.NewGuid().ToString("N"), n))
                .ToList();
            big = big with { Base = big.Base with { Files = files } };

            var sw = Stopwatch.StartNew();
            await host.UpdateHostAsync(List("ugc:1"), big).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
        }

        await Wait.Until(() => alice.Target.HostSnapshot.Machine == "H4", "alice has the latest target", 20_000);
        await Wait.Until(() => host.Players.All(p => p.Name != "Stuck"), "stuck peer removed", 20_000);

        await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
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

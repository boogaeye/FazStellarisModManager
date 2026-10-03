using System.Net;
using System.Net.Sockets;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Session;

/// <summary>TCP host: accepts any number of clients, diffs each against the host's snapshot and broadcasts the roster.</summary>
public sealed class SessionHost : IAsyncDisposable
{
    public static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);
    public const string HostId = "host";
    public const int MaxNameLength = 40;

    sealed class Peer(string id, TcpClient tcp)
    {
        public string Id { get; } = id;
        public TcpClient Tcp { get; } = tcp;
        public NetworkStream Stream { get; } = tcp.GetStream();
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public string Name { get; set; } = "";
        public PlayerStatus Status { get; set; } = PlayerStatus.Waiting;
        public string? Activity { get; set; }
        public MachineSnapshot? Snapshot { get; set; }
        public DiffResult? Diff { get; set; }
    }

    readonly Lock _gate = new();
    readonly Dictionary<string, Peer> _peers = new();
    readonly CancellationTokenSource _cts = new();
    readonly string _hostName;
    TcpListener? _listener;
    Task? _acceptLoop;
    ModList _hostList;
    MachineSnapshot _hostSnapshot;
    bool _disposed;

    public SessionHost(string hostName, ModList hostList, MachineSnapshot hostSnapshot)
    {
        _hostName = hostName;
        _hostList = hostList;
        _hostSnapshot = hostSnapshot;
    }

    /// <summary>The port actually listened on (useful when started with port 0).</summary>
    public int Port { get; private set; }

    /// <summary>Raised on a background thread whenever the roster changes.</summary>
    public event Action? RosterChanged;

    /// <summary>Starts listening. Throws SocketException if the port is in use.</summary>
    public void Start(int port, IPAddress? address = null)
    {
        _listener = new TcpListener(address ?? IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    /// <summary>Host first, then clients by name.</summary>
    public IReadOnlyList<PlayerInfo> Players
    {
        get { lock (_gate) return BuildRoster(); }
    }

    /// <summary>The full diff of a client against the host (null before their first snapshot).</summary>
    public DiffResult? DiffFor(string playerId)
    {
        lock (_gate) return _peers.TryGetValue(playerId, out var p) ? p.Diff : null;
    }

    /// <summary>The host rescanned or changed its list: re-diff everyone and push the new target.</summary>
    public async Task UpdateHostAsync(ModList hostList, MachineSnapshot hostSnapshot)
    {
        List<Peer> peers;
        lock (_gate)
        {
            _hostList = hostList;
            _hostSnapshot = hostSnapshot;
            foreach (var p in _peers.Values) Rediff(p);
            peers = _peers.Values.ToList();
        }
        var update = new HostTargetUpdate(hostList, hostSnapshot);
        await Task.WhenAll(peers.Select(p => SendAsync(p, update)));
        await BroadcastRosterAsync();
    }

    /// <summary>Peers are untrusted: trim, turn control characters into spaces and cap the length.</summary>
    static string CleanName(string? raw)
    {
        var s = new string((raw ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        if (s.Length > MaxNameLength) s = s[..MaxNameLength].TrimEnd();
        return s.Length == 0 ? "Player" : s;
    }

    async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await _listener!.AcceptTcpClientAsync(ct); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException || ct.IsCancellationRequested) { break; }
            catch (SocketException) { continue; }
            _ = Task.Run(() => HandleClientAsync(tcp, ct), CancellationToken.None);
        }
    }

    async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        tcp.NoDelay = true;
        var peer = new Peer(Guid.NewGuid().ToString("N")[..8], tcp);
        try
        {
            using (var helloCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                helloCts.CancelAfter(HelloTimeout);
                // Untrusted until a valid Hello arrives, so the first frame gets the small handshake cap.
                if (await Framing.ReadAsync(peer.Stream, helloCts.Token, Framing.MaxHandshakeBytes) is not Hello hello) return;
                if (hello.ProtocolVersion != SessionProtocol.Version)
                {
                    await SendAsync(peer, new Reject(
                        $"Your mod manager speaks protocol {hello.ProtocolVersion}, the host speaks {SessionProtocol.Version}. Update to the same version."));
                    return;
                }
                peer.Name = CleanName(hello.PlayerName);
            }

            // Welcome goes out before the peer joins _peers, so it is always the first message the client sees.
            Welcome welcome;
            lock (_gate) welcome = new Welcome(peer.Id, _hostList, _hostSnapshot);
            await SendAsync(peer, welcome);
            HostTargetUpdate? catchUp = null;
            lock (_gate)
            {
                _peers[peer.Id] = peer;
                if (!ReferenceEquals(welcome.HostSnapshot, _hostSnapshot)) catchUp = new HostTargetUpdate(_hostList, _hostSnapshot);
            }
            if (catchUp is not null) await SendAsync(peer, catchUp);
            await BroadcastRosterAsync();

            while (await Framing.ReadAsync(peer.Stream, ct) is { } message)
            {
                switch (message)
                {
                    case SnapshotUpdate s:
                        lock (_gate)
                        {
                            peer.Snapshot = s.Snapshot;
                            peer.Activity = null;
                            Rediff(peer);
                        }
                        break;
                    case ClientBusy b:
                        lock (_gate)
                        {
                            peer.Status = PlayerStatus.Busy;
                            peer.Activity = b.Activity;
                        }
                        break;
                    case Bye:
                        return;
                    default:
                        continue;
                }
                await BroadcastRosterAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // A misbehaving or vanished client only drops itself.
        }
        finally
        {
            bool removed;
            lock (_gate) removed = _peers.Remove(peer.Id);
            tcp.Dispose();
            if (removed && !ct.IsCancellationRequested) await BroadcastRosterAsync();
        }
    }

    // Caller holds _gate.
    void Rediff(Peer p)
    {
        if (p.Snapshot is null)
        {
            p.Diff = null;
            if (p.Status != PlayerStatus.Busy) p.Status = PlayerStatus.Waiting;
            return;
        }
        p.Diff = ModDiffer.Diff(_hostSnapshot, p.Snapshot);
        p.Status = SessionProtocol.StatusFor(p.Diff);
    }

    // Caller holds _gate.
    List<PlayerInfo> BuildRoster()
    {
        var hostStatus = (_hostSnapshot.Warnings?.Count ?? 0) > 0 ? PlayerStatus.Unreliable : PlayerStatus.Ready;
        var roster = new List<PlayerInfo> { new(HostId, _hostName, true, hostStatus, null, null) };
        roster.AddRange(_peers.Values
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PlayerInfo(p.Id, p.Name, false, p.Status, p.Diff is null ? null : DiffSummary.From(p.Diff), p.Activity)));
        return roster;
    }

    async Task BroadcastRosterAsync()
    {
        Roster roster;
        List<Peer> peers;
        lock (_gate)
        {
            roster = new Roster(BuildRoster());
            peers = _peers.Values.ToList();
        }
        RosterChanged?.Invoke();
        await Task.WhenAll(peers.Select(p => SendAsync(p, roster)));
    }

    // Every write to a peer goes through its WriteLock so frames never interleave.
    async Task SendAsync(Peer p, SessionMessage message)
    {
        try
        {
            await p.WriteLock.WaitAsync(_cts.Token);
            try { await Framing.WriteAsync(p.Stream, message, _cts.Token); }
            finally { p.WriteLock.Release(); }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
            p.Tcp.Dispose(); // its reader loop notices and removes the peer
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        List<Peer> peers;
        lock (_gate) peers = _peers.Values.ToList();
        await Task.WhenAll(peers.Select(p => SendAsync(p, new Bye("Host closed the session."))));
        _cts.Cancel();
        _listener?.Stop();
        foreach (var p in peers) p.Tcp.Dispose();
        if (_acceptLoop is not null) await _acceptLoop;
    }
}

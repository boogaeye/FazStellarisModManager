using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Session;

/// <summary>
/// TCP host: accepts clients, diffs each against the host's snapshot and broadcasts the roster.
/// Peers are untrusted. Each peer has a bounded outbox drained by a single writer task with a write timeout,
/// so a peer that stops reading only ever drops itself and never stalls the host or other peers.
/// </summary>
public sealed class SessionHost : IAsyncDisposable
{
    public static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Once a frame header has arrived, its body must follow within this time.</summary>
    public static readonly TimeSpan BodyTimeout = TimeSpan.FromSeconds(60);

    public const string HostId = "host";
    public const int MaxNameLength = 40;
    public const int MaxActivityLength = 80;

    /// <summary>Connections still in their handshake allowed on top of <see cref="MaxPeers"/>.</summary>
    public const int MaxPendingHandshakes = 8;

    const int OutboxCapacity = 32;
    static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Queued instead of a roster: the writer builds the roster when it dequeues this, so it is always the latest.</summary>
    sealed class RosterDirty
    {
        public static readonly RosterDirty Instance = new();
    }

    sealed class Peer(string id, TcpClient tcp, string name)
    {
        public string Id { get; } = id;
        public TcpClient Tcp { get; } = tcp;
        public Socket Socket { get; } = tcp.Client;
        public NetworkStream Stream { get; } = tcp.GetStream();
        public Channel<object> Outbox { get; } = Channel.CreateBounded<object>(
            new BoundedChannelOptions(OutboxCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        public Task? Writer { get; set; }

        /// <summary>1 while a <see cref="RosterDirty"/> marker is queued but not yet written.</summary>
        public int RosterPending;

        // Guarded by _gate.
        public string Name { get; } = name;
        public PlayerStatus Status { get; set; } = PlayerStatus.Waiting;
        public string? Activity { get; set; }
        public MachineSnapshot? Snapshot { get; set; }
        public DiffResult? Diff { get; set; }
        public DiffSummary? Summary { get; set; }
    }

    readonly Lock _gate = new();
    readonly Dictionary<string, Peer> _peers = new();
    readonly CancellationTokenSource _cts = new();
    readonly string _hostName;
    TcpListener? _listener;
    Task? _acceptLoop;
    ModList _hostList;
    MachineSnapshot _hostSnapshot;
    int _pendingHandshakes;
    int _disposed;

    public SessionHost(string hostName, ModList hostList, MachineSnapshot hostSnapshot)
    {
        _hostName = hostName;
        _hostList = hostList;
        _hostSnapshot = hostSnapshot;
    }

    /// <summary>The port actually listened on (useful when started with port 0).</summary>
    public int Port { get; private set; }

    /// <summary>A single write to a peer that takes longer than this drops that peer.</summary>
    public TimeSpan WriteTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Joined clients allowed at once (the host not counted). Further connections are closed straight away.</summary>
    public int MaxPeers { get; set; } = 32;

    /// <summary>Raised on a background thread whenever the roster changes.</summary>
    public event Action? RosterChanged;

    bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Starts listening. Throws SocketException if the port is in use.</summary>
    public void Start(int port, IPAddress? address = null)
    {
        if (address is not null)
        {
            _listener = new TcpListener(address, port);
            _listener.Start();
        }
        else
        {
            try
            {
                _listener = new TcpListener(IPAddress.IPv6Any, port);
                _listener.Server.DualMode = true;
                _listener.Start();
            }
            catch (SocketException)
            {
                // IPv6 unavailable (or the port is taken: the IPv4 attempt then reports that).
                _listener?.Stop();
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
            }
        }
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

    /// <summary>
    /// The host rescanned or changed its list: push the new target to everyone and re-diff them.
    /// Never waits on peers' connections. Throws InvalidDataException if the target is too large to send.
    /// </summary>
    public async Task UpdateHostAsync(ModList hostList, MachineSnapshot hostSnapshot)
    {
        // Encoded once for everyone, off the caller's thread; queued per peer in the same lock that changes the
        // target, so every peer's single writer delivers targets in the order they were set.
        var frame = await Task.Run(() => Framing.Encode(new HostTargetUpdate(hostList, hostSnapshot)));
        List<Peer> peers;
        lock (_gate)
        {
            _hostList = hostList;
            _hostSnapshot = hostSnapshot;
            peers = _peers.Values.ToList();
            foreach (var p in peers) Enqueue(p, frame);
        }
        await Task.WhenAll(peers.Select(p => Task.Run(() => Rediff(p))));
        BroadcastRoster();
    }

    /// <summary>Peers are untrusted: trim, turn control characters into spaces and cap the length. Null if nothing is left.</summary>
    static string? CleanText(string? raw, int maxLength)
    {
        var s = new string((raw ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        if (s.Length > maxLength) s = s[..maxLength].TrimEnd();
        return s.Length == 0 ? null : s;
    }

    static string CleanName(string? raw) => CleanText(raw, MaxNameLength) ?? "Player";

    async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await _listener!.AcceptTcpClientAsync(ct);
            }
            catch (Exception) when (ct.IsCancellationRequested || IsDisposed)
            {
                break;
            }
            catch (SocketException)
            {
                // E.g. a connection reset before we accepted it; don't spin if it keeps happening.
                try { await Task.Delay(100, ct); }
                catch (OperationCanceledException) { break; }
                continue;
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
                break;
            }

            bool full;
            lock (_gate) full = _peers.Count >= MaxPeers;
            if (full || Interlocked.Increment(ref _pendingHandshakes) > MaxPendingHandshakes)
            {
                if (!full) Interlocked.Decrement(ref _pendingHandshakes);
                tcp.Dispose();
                continue;
            }
            _ = Task.Run(() => HandleClientAsync(tcp, ct), CancellationToken.None);
        }
    }

    async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        var pending = true;
        Peer? peer = null;
        try
        {
            tcp.NoDelay = true;
            SessionProtocol.EnableKeepAlive(tcp.Client);
            var stream = tcp.GetStream();

            Hello hello;
            using (var helloCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                helloCts.CancelAfter(HelloTimeout);
                // Untrusted until a valid Hello arrives, so the first frame gets the small handshake cap.
                if (await Framing.ReadAsync(stream, helloCts.Token, Framing.MaxHandshakeBytes, BodyTimeout) is not Hello h) return;
                hello = h;
            }
            if (hello.ProtocolVersion != SessionProtocol.Version)
            {
                await WriteDirectAsync(stream, new Reject(
                    $"Your mod manager speaks protocol {hello.ProtocolVersion}, the host speaks {SessionProtocol.Version}. Update to the same version."), ct);
                return;
            }

            var joined = false;
            var full = false;
            lock (_gate)
            {
                if (IsDisposed || ct.IsCancellationRequested) return;
                if (_peers.Count >= MaxPeers)
                {
                    full = true;
                }
                else
                {
                    peer = new Peer(Guid.NewGuid().ToString("N")[..8], tcp, CleanName(hello.PlayerName));
                    var p = peer;
                    _peers[p.Id] = p;
                    p.Writer = Task.Run(() => WriteLoopAsync(p), CancellationToken.None);
                    // Queued in the same lock that publishes the peer, so Welcome is always first and any later
                    // target update (queued under this lock too) comes after it.
                    Enqueue(p, new Welcome(p.Id, _hostList, _hostSnapshot));
                    joined = true;
                }
            }
            Interlocked.Decrement(ref _pendingHandshakes);
            pending = false;
            if (full)
            {
                await WriteDirectAsync(stream, new Reject("The session is full."), ct);
                return;
            }
            if (!joined || peer is null) return;
            BroadcastRoster();

            while (await Framing.ReadAsync(stream, ct, Framing.MaxFrameBytes, BodyTimeout) is { } message)
            {
                switch (message)
                {
                    case SnapshotUpdate s:
                        lock (_gate)
                        {
                            peer.Snapshot = s.Snapshot;
                            peer.Activity = null;
                        }
                        Rediff(peer);
                        break;
                    case ClientBusy b:
                        lock (_gate)
                        {
                            peer.Status = PlayerStatus.Busy;
                            peer.Activity = CleanText(b.Activity, MaxActivityLength);
                        }
                        break;
                    case Bye:
                        return;
                    default:
                        continue;
                }
                BroadcastRoster();
            }
        }
        catch (Exception)
        {
            // A misbehaving, malformed or vanished client only drops itself; nothing escapes this task.
        }
        finally
        {
            if (pending) Interlocked.Decrement(ref _pendingHandshakes);
            var removed = false;
            if (peer is not null)
            {
                lock (_gate) removed = _peers.Remove(peer.Id);
                peer.Outbox.Writer.TryComplete();
            }
            tcp.Dispose();
            if (removed && !ct.IsCancellationRequested) BroadcastRoster();
        }
    }

    /// <summary>Writes before the peer has a writer task (Reject), bounded by <see cref="WriteTimeout"/>.</summary>
    async Task WriteDirectAsync(NetworkStream stream, SessionMessage message, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(WriteTimeout);
        await Framing.WriteAsync(stream, message, timeout.Token);
    }

    /// <summary>The only code that writes to a joined peer's stream, so frames never interleave and stay in order.</summary>
    async Task WriteLoopAsync(Peer p)
    {
        try
        {
            var reader = p.Outbox.Reader;
            while (await reader.WaitToReadAsync(_cts.Token))
            {
                while (reader.TryRead(out var item))
                {
                    byte[] frame;
                    switch (item)
                    {
                        case RosterDirty:
                            // Clear first: any change after this point marks the peer dirty again.
                            Interlocked.Exchange(ref p.RosterPending, 0);
                            Roster roster;
                            lock (_gate) roster = new Roster(BuildRoster());
                            frame = Framing.Encode(roster);
                            break;
                        case byte[] encoded:
                            frame = encoded;
                            break;
                        case SessionMessage message:
                            frame = Framing.Encode(message);
                            break;
                        default:
                            continue;
                    }
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    timeout.CancelAfter(WriteTimeout);
                    await p.Stream.WriteAsync(frame, timeout.Token);
                }
            }
        }
        catch (Exception)
        {
            // Timed out, IO error or cancelled: close the socket; the reader loop then ends and removes the peer.
            Drop(p);
        }
    }

    // Never blocks: a peer whose outbox is full is not keeping up and gets dropped.
    void Enqueue(Peer p, object item)
    {
        if (IsDisposed) return;
        if (!p.Outbox.Writer.TryWrite(item)) Drop(p);
    }

    static void Drop(Peer p)
    {
        try { p.Tcp.Dispose(); }
        catch (Exception) { }
    }

    void MarkRosterDirty(Peer p)
    {
        if (Interlocked.CompareExchange(ref p.RosterPending, 1, 0) == 0) Enqueue(p, RosterDirty.Instance);
    }

    /// <summary>Marks every peer's roster dirty (their writers send the latest one) and notifies the UI. Never waits.</summary>
    void BroadcastRoster()
    {
        if (IsDisposed) return;
        List<Peer> peers;
        lock (_gate) peers = _peers.Values.ToList();
        foreach (var p in peers) MarkRosterDirty(p);
        try { RosterChanged?.Invoke(); }
        catch (Exception) { /* a faulty subscriber must not take a peer down with it */ }
    }

    /// <summary>
    /// Diffs the peer against the current host snapshot outside the lock and commits only if neither side changed
    /// meanwhile, otherwise tries again. A malformed snapshot that makes the differ throw drops only this peer.
    /// </summary>
    void Rediff(Peer p)
    {
        try
        {
            // No retry loop: whoever changed an input after we read it (a new snapshot or host target)
            // runs its own Rediff afterwards, and that one commits. Retrying here could be starved by a spamming peer.
            {
                MachineSnapshot host;
                MachineSnapshot? mine;
                lock (_gate)
                {
                    if (!_peers.ContainsKey(p.Id)) return;
                    host = _hostSnapshot;
                    mine = p.Snapshot;
                }

                var diff = mine is null ? null : ModDiffer.Diff(host, mine);
                var summary = diff is null ? null : DiffSummary.From(diff);

                lock (_gate)
                {
                    if (!ReferenceEquals(host, _hostSnapshot) || !ReferenceEquals(mine, p.Snapshot)) return; // stale: the newer change re-diffs
                    p.Diff = diff;
                    p.Summary = summary;
                    if (diff is null)
                    {
                        if (p.Status != PlayerStatus.Busy) p.Status = PlayerStatus.Waiting;
                    }
                    else
                    {
                        p.Status = SessionProtocol.StatusFor(diff);
                    }
                    return;
                }
            }
        }
        catch (Exception)
        {
            Drop(p);
        }
    }

    // Caller holds _gate.
    List<PlayerInfo> BuildRoster()
    {
        var hostStatus = (_hostSnapshot.Warnings?.Count ?? 0) > 0 ? PlayerStatus.Unreliable : PlayerStatus.Ready;
        var roster = new List<PlayerInfo> { new(HostId, _hostName, true, hostStatus, null, null) };
        roster.AddRange(_peers.Values
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PlayerInfo(p.Id, p.Name, false, p.Status, p.Summary, p.Activity)));
        return roster;
    }

    /// <summary>Says Bye to everyone, gives their writers up to 2 seconds to flush, then closes. Returns within about 3 seconds.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        List<Peer> peers;
        lock (_gate) peers = _peers.Values.ToList();

        var bye = new Bye("Host closed the session.");
        foreach (var p in peers)
        {
            p.Outbox.Writer.TryWrite(bye); // if the outbox is full this peer just doesn't get a goodbye
            p.Outbox.Writer.TryComplete();
        }
        try
        {
            await Task.WhenAll(peers.Select(p => p.Writer ?? Task.CompletedTask)).WaitAsync(DisposeDrainTimeout);
        }
        catch (TimeoutException)
        {
            // Someone isn't reading; cancelling below aborts their pending write.
        }

        _cts.Cancel();
        foreach (var p in peers)
        {
            try { p.Socket.Shutdown(SocketShutdown.Both); }
            catch (Exception) { }
            Drop(p);
        }
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (TimeoutException) { }
        }
    }
}

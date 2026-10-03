using System.Net.Sockets;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Session;

public sealed class SessionRejectedException(string reason) : Exception(reason);

/// <summary>TCP client side of a session. Subscribe to the events, then call <see cref="Start"/>.</summary>
public sealed class SessionClient : IAsyncDisposable
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan ByeTimeout = TimeSpan.FromSeconds(2);

    readonly TcpClient _tcp;
    readonly NetworkStream _stream;
    readonly SemaphoreSlim _writeLock = new(1, 1);
    readonly CancellationTokenSource _cts = new();
    Task? _receiveLoop;
    int _started;
    int _disposed;

    SessionClient(TcpClient tcp, Welcome welcome)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        PlayerId = welcome.PlayerId;
        Target = new HostTargetUpdate(welcome.HostList, welcome.HostSnapshot);
    }

    public string PlayerId { get; }

    /// <summary>The host's current list and snapshot (replaced as a whole on every update).</summary>
    public HostTargetUpdate Target { get; private set; }

    public IReadOnlyList<PlayerInfo> Roster { get; private set; } = [];
    public bool IsConnected { get; private set; } = true;

    /// <summary>Why the connection ended; set before <see cref="Disconnected"/> fires, null while connected.</summary>
    public string? DisconnectReason { get; private set; }

    /// <summary>A single send that takes longer than this closes the connection and throws TimeoutException.</summary>
    public TimeSpan WriteTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Raised on a background thread when the roster or host target changes.</summary>
    public event Action? Changed;

    /// <summary>Raised once, on a background thread, when the connection ends; the argument says why.</summary>
    public event Action<string>? Disconnected;

    /// <summary>Connects and completes the handshake. Nothing is received until <see cref="Start"/> is called.</summary>
    /// <exception cref="SessionRejectedException">The host refused us (e.g. protocol version).</exception>
    /// <exception cref="TimeoutException">No answer within <see cref="ConnectTimeout"/>.</exception>
    /// <exception cref="SocketException">Nothing listening / unreachable.</exception>
    public static async Task<SessionClient> ConnectAsync(string host, int port, string playerName, CancellationToken ct = default)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ConnectTimeout);
            await tcp.ConnectAsync(host, port, timeout.Token);
            SessionProtocol.EnableKeepAlive(tcp.Client);
            var stream = tcp.GetStream();
            await Framing.WriteAsync(stream, new Hello(SessionProtocol.Version, SessionProtocol.AppVersion, playerName), timeout.Token);
            switch (await Framing.ReadAsync(stream, timeout.Token))
            {
                case Welcome welcome:
                    return new SessionClient(tcp, welcome);
                case Reject reject:
                    throw new SessionRejectedException(reject.Reason);
                default:
                    throw new IOException("The host closed the connection without welcoming us.");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            tcp.Dispose();
            throw new TimeoutException($"No answer from {host}:{port} within {ConnectTimeout.TotalSeconds:0} seconds.");
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    /// <summary>Starts receiving (at most once). Subscribe to <see cref="Changed"/> and <see cref="Disconnected"/> first.</summary>
    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        Volatile.Write(ref _receiveLoop, Task.Run(ReceiveLoopAsync));
    }

    public Task SendSnapshotAsync(MachineSnapshot snapshot, CancellationToken ct = default) => SendAsync(new SnapshotUpdate(snapshot), ct);

    public Task SendBusyAsync(string activity, CancellationToken ct = default) => SendAsync(new ClientBusy(activity), ct);

    async Task SendAsync(SessionMessage message, CancellationToken ct)
    {
        var frame = await Task.Run(() => Framing.Encode(message)).ConfigureAwait(false);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(WriteTimeout);
            try
            {
                await Framing.WriteFrameAsync(_stream, frame, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A frame may be half written, so the stream can't be used any more.
                _tcp.Dispose();
                if (ct.IsCancellationRequested) throw;
                throw new TimeoutException($"Sending to the host took longer than {WriteTimeout.TotalSeconds:0} seconds.");
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    async Task ReceiveLoopAsync()
    {
        var reason = "The host closed the connection.";
        try
        {
            while (await Framing.ReadAsync(_stream, _cts.Token, bodyTimeout: TimeSpan.FromSeconds(60)) is { } message)
            {
                switch (message)
                {
                    case Roster r:
                        Roster = r.Players;
                        break;
                    case HostTargetUpdate t:
                        Target = t;
                        break;
                    case Bye bye:
                        reason = bye.Reason ?? "The host ended the session.";
                        return;
                    default:
                        continue;
                }
                try { Changed?.Invoke(); }
                catch (Exception) { /* a faulty subscriber must not end the session */ }
            }
        }
        catch (OperationCanceledException)
        {
            reason = "You left the session.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SocketException or ObjectDisposedException or TimeoutException)
        {
            reason = _cts.IsCancellationRequested ? "You left the session." : $"Connection lost: {ex.Message}";
        }
        catch (Exception ex)
        {
            reason = $"Connection error: {ex.Message}";
        }
        finally
        {
            IsConnected = false;
            DisconnectReason = reason;
            try { Disconnected?.Invoke(reason); }
            catch (Exception) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (IsConnected)
        {
            using var byeTimeout = new CancellationTokenSource(ByeTimeout);
            try { await SendAsync(new Bye("Player left."), byeTimeout.Token); }
            catch (Exception) { /* best effort */ }
        }
        _cts.Cancel();
        _tcp.Dispose();
        var loop = Volatile.Read(ref _receiveLoop);
        if (loop is not null) await loop;
        else
        {
            IsConnected = false;
            DisconnectReason ??= "You left the session.";
        }
    }
}

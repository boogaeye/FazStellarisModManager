using System.Net.Sockets;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Session;

public sealed class SessionRejectedException(string reason) : Exception(reason);

/// <summary>TCP client side of a session.</summary>
public sealed class SessionClient : IAsyncDisposable
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    readonly TcpClient _tcp;
    readonly NetworkStream _stream;
    readonly SemaphoreSlim _writeLock = new(1, 1);
    readonly CancellationTokenSource _cts = new();
    Task? _receiveLoop;
    bool _disposed;

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

    /// <summary>Raised on a background thread when the roster or host target changes.</summary>
    public event Action? Changed;

    /// <summary>Raised once, on a background thread, when the connection ends; the argument says why.</summary>
    public event Action<string>? Disconnected;

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
            var stream = tcp.GetStream();
            await Framing.WriteAsync(stream, new Hello(SessionProtocol.Version, SessionProtocol.AppVersion, playerName), timeout.Token);
            switch (await Framing.ReadAsync(stream, timeout.Token))
            {
                case Welcome welcome:
                    var client = new SessionClient(tcp, welcome);
                    client._receiveLoop = client.ReceiveLoopAsync();
                    return client;
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

    public Task SendSnapshotAsync(MachineSnapshot snapshot, CancellationToken ct = default) => SendAsync(new SnapshotUpdate(snapshot), ct);

    public Task SendBusyAsync(string activity, CancellationToken ct = default) => SendAsync(new ClientBusy(activity), ct);

    async Task SendAsync(SessionMessage message, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try { await Framing.WriteAsync(_stream, message, ct); }
        finally { _writeLock.Release(); }
    }

    async Task ReceiveLoopAsync()
    {
        var reason = "The host closed the connection.";
        try
        {
            while (await Framing.ReadAsync(_stream, _cts.Token) is { } message)
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
                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            reason = "You left the session.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SocketException or ObjectDisposedException)
        {
            reason = _cts.IsCancellationRequested ? "You left the session." : $"Connection lost: {ex.Message}";
        }
        finally
        {
            IsConnected = false;
            Disconnected?.Invoke(reason);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsConnected)
        {
            try { await SendAsync(new Bye("Player left."), CancellationToken.None); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException) { }
        }
        _cts.Cancel();
        _tcp.Dispose();
        if (_receiveLoop is not null) await _receiveLoop;
    }
}

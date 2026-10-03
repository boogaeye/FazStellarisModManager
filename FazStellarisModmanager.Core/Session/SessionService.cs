using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Hashing;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Session;

public enum SessionRole { None, Host, Client }

/// <summary>
/// UI-facing multiplayer state. One operation (host, join, rescan, match, leave) runs at a time;
/// <see cref="Changed"/> may fire on any thread.
/// </summary>
public sealed class SessionService(ModManagerService manager) : IAsyncDisposable
{
    readonly SemaphoreSlim _op = new(1, 1);
    volatile SessionHost? _host;
    volatile SessionClient? _client;
    volatile DiffResult? _myDiff;
    volatile string? _hostAddress;
    volatile MatchPlan? _lastPlan;
    volatile MachineSnapshot? _mySnapshot;
    volatile string? _activity;
    volatile string? _lastDisconnectReason;
    volatile CancellationTokenSource? _opCts;
    long _lastProgressRaise;
    HashCache? _cache;

    public SessionRole Role => _host is not null ? SessionRole.Host : _client is not null ? SessionRole.Client : SessionRole.None;
    public int? HostPort => _host?.Port;
    public string? HostAddress => _hostAddress;
    public MachineSnapshot? MySnapshot => _mySnapshot;

    /// <summary>Client only: this machine compared with the host.</summary>
    public DiffResult? MyDiff => _myDiff;

    public IReadOnlyList<PlayerInfo> Players => _host?.Players ?? _client?.Roster ?? [];
    public MatchPlan? LastPlan => _lastPlan;

    /// <summary>What the current operation is doing (e.g. the scan's progress); null when idle.</summary>
    public string? Activity => _activity;

    /// <summary>Why the last client connection ended on its own (host stopped, network lost).</summary>
    public string? LastDisconnectReason => _lastDisconnectReason;

    public event Action? Changed;

    string PlayerName => string.IsNullOrWhiteSpace(manager.Settings.PlayerName) ? Environment.MachineName : manager.Settings.PlayerName.Trim();

    /// <summary>Host only: a client's full diff against the host.</summary>
    public DiffResult? DiffFor(string playerId) => _host?.DiffFor(playerId);

    public Task HostAsync(int port, CancellationToken ct = default) => Exclusive(async ct =>
    {
        if (port is < 0 or > 65535) throw new ArgumentException("The port must be between 0 and 65535.", nameof(port));
        if (Role != SessionRole.None) throw new InvalidOperationException("Already in a session. Leave it first.");
        // Import before scanning so the host list and the snapshot describe the same state.
        await manager.RefreshLibraryAsync();
        var list = manager.ImportCurrent("Host list");
        var snapshot = await ScanAsync(ct);
        var host = new SessionHost(PlayerName, list, snapshot);
        host.RosterChanged += RaiseChanged;
        try { host.Start(port); }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
        _host = host;
        _lastDisconnectReason = null;
        return true;
    }, ct);

    public Task JoinAsync(string address, int port, CancellationToken ct = default) => Exclusive(async ct =>
    {
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Enter the host's address.", nameof(address));
        if (port is < 1 or > 65535) throw new ArgumentException("The port must be between 1 and 65535.", nameof(port));
        if (Role != SessionRole.None) throw new InvalidOperationException("Already in a session. Leave it first.");
        var snapshot = await ScanAsync(ct);
        SetActivity($"Connecting to {address}:{port}…");
        var client = await SessionClient.ConnectAsync(address, port, PlayerName, ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            client.Changed += () =>
            {
                if (!ReferenceEquals(_client, client)) return;
                RecomputeDiff();
                RaiseChanged();
            };
            client.Disconnected += reason => OnDisconnected(client, reason);
            _client = client;
            _hostAddress = $"{address}:{port}";
            _lastDisconnectReason = null;
            RecomputeDiff();
            client.Start();
            await client.SendSnapshotAsync(snapshot, ct);
            return true;
        }
        catch
        {
            if (ReferenceEquals(_client, client))
            {
                _client = null;
                _myDiff = null;
                _hostAddress = null;
            }
            await client.DisposeAsync();
            throw;
        }
    }, ct);

    /// <summary>Rescans this machine. As host, pushes the new list to everyone; as client, sends the new snapshot.</summary>
    public Task RescanAsync(CancellationToken ct = default) => Exclusive(async ct =>
    {
        ModList? hostList = null;
        if (_host is not null)
        {
            await manager.RefreshLibraryAsync();
            hostList = manager.ImportCurrent("Host list");
        }
        var snapshot = await ScanAsync(ct);
        if (_host is { } host && hostList is not null) await host.UpdateHostAsync(hostList, snapshot);
        else if (_client is { } client)
        {
            RecomputeDiff();
            await client.SendSnapshotAsync(snapshot, CancellationToken.None);
        }
        return true;
    }, ct);

    /// <summary>Client only: writes the host's list (installed mods, host order) to dlc_load.json, then rescans and reports.</summary>
    public Task<MatchPlan> MatchHostAsync(CancellationToken ct = default) => Exclusive(async ct =>
    {
        var client = _client ?? throw new InvalidOperationException("Not connected to a host.");
        var mine = MySnapshot ?? throw new InvalidOperationException("Your mods have not been scanned yet.");
        var target = client.Target; // one target for the whole match
        var diff = ModDiffer.Diff(target.HostSnapshot, mine);
        await client.SendBusyAsync("Matching host…", CancellationToken.None);
        SetActivity("Applying the host's mod list…");
        await manager.RefreshLibraryAsync();
        var plan = MatchPlan.Create(target.HostList, diff, manager.Library, mine);
        ct.ThrowIfCancellationRequested();
        if (!client.IsConnected || !ReferenceEquals(_client, client)) throw new InvalidOperationException("The host has disconnected.");
        manager.Apply(plan.ToApply);
        _lastPlan = plan;
        var snapshot = await ScanAsync(ct);
        RecomputeDiff();
        await client.SendSnapshotAsync(snapshot, CancellationToken.None);
        return plan;
    }, ct);

    /// <summary>Cancels the running operation, if any; it throws OperationCanceledException to its caller.</summary>
    public void CancelCurrent()
    {
        try { _opCts?.Cancel(); }
        catch (ObjectDisposedException) { /* the operation just finished */ }
    }

    public Task LeaveAsync()
    {
        CancelCurrent();
        return Exclusive(async _ =>
        {
            await CloseAsync();
            return true;
        }, CancellationToken.None);
    }

    async Task CloseAsync()
    {
        var host = _host;
        var client = _client;
        _host = null;
        _client = null;
        _myDiff = null;
        _hostAddress = null;
        _lastPlan = null;
        if (host is not null)
        {
            host.RosterChanged -= RaiseChanged;
            await host.DisposeAsync();
        }
        if (client is not null) await client.DisposeAsync();
        RaiseChanged();
    }

    void OnDisconnected(SessionClient client, string reason)
    {
        if (!ReferenceEquals(_client, client)) return; // we left on purpose
        _client = null;
        _myDiff = null;
        _hostAddress = null;
        _lastPlan = null;
        _lastDisconnectReason = reason;
        RaiseChanged();
        _ = client.DisposeAsync().AsTask();
    }

    async Task<MachineSnapshot> ScanAsync(CancellationToken ct)
    {
        SetActivity("Scanning mods…");
        await manager.RefreshLibraryAsync();
        var paths = manager.Resolve();
        var gameDir = paths.GameDir ?? throw new InvalidOperationException("Stellaris install not found. Set the game folder in Settings.");
        var cache = _cache ??= HashCache.Load(manager.Paths.HashCache);
        var progress = new ActivityProgress(this);
        var name = PlayerName;
        var snapshot = await Task.Run(async () =>
        {
            var missesBefore = cache.Misses;
            try { return await SnapshotScanner.ScanAsync(paths.UserDir, gameDir, name, cache, progress, ct); }
            finally
            {
                if (cache.Misses != missesBefore)
                {
                    try { cache.Save(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* only a cache */ }
                }
            }
        }, ct);
        _mySnapshot = snapshot;
        return snapshot;
    }

    async Task<T> Exclusive<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        await _op.WaitAsync(ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _opCts = cts;
        try
        {
            try { return await action(cts.Token); }
            finally
            {
                _opCts = null;
                _activity = null;
            }
        }
        finally
        {
            _op.Release();
            RaiseChanged();
        }
    }

    sealed class ActivityProgress(SessionService owner) : IProgress<string>
    {
        public void Report(string value) => owner.ReportProgress(value.Trim());
    }

    /// <summary>Operation phases: always raises.</summary>
    void SetActivity(string? text)
    {
        _activity = text;
        Interlocked.Exchange(ref _lastProgressRaise, Environment.TickCount64);
        RaiseChanged();
    }

    /// <summary>Scan progress: updates Activity, raises Changed at most every 150 ms.</summary>
    void ReportProgress(string text)
    {
        _activity = text;
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastProgressRaise);
        if (now - last < 150 || Interlocked.CompareExchange(ref _lastProgressRaise, now, last) != last) return;
        RaiseChanged();
    }

    void RecomputeDiff() =>
        _myDiff = _client is { } client && MySnapshot is { } mine ? ModDiffer.Diff(client.Target.HostSnapshot, mine) : null;

    void RaiseChanged()
    {
        var handlers = Changed;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList())
        {
            try { ((Action)handler)(); }
            catch { /* a faulty subscriber must not break the session */ }
        }
    }

    public async ValueTask DisposeAsync() => await LeaveAsync();
}

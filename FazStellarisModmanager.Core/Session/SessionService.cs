using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Hashing;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Core.Snapshots;
using FazStellarisModmanager.Core.Workshop;

namespace FazStellarisModmanager.Core.Session;

public enum SessionRole { None, Host, Client }

/// <summary>
/// UI-facing multiplayer state. One operation (host, join, rescan, match, leave) runs at a time;
/// <see cref="Changed"/> may fire on any thread.
/// </summary>
public sealed class SessionService(ModManagerService manager, IWorkshopService? workshop = null, LiveGameService? live = null) : IAsyncDisposable, IHostLiveSource
{
    readonly IWorkshopService? _workshop = workshop;
    readonly SemaphoreSlim _op = new(1, 1);
    volatile SessionHost? _host;
    volatile SessionClient? _client;
    volatile DiffResult? _myDiff;
    readonly Lock _diffLock = new();
    (HostTargetUpdate Target, MachineSnapshot Mine)? _diffInputs; // what _myDiff was computed from; guarded by _diffLock
    volatile string? _hostAddress;
    volatile MatchPlan? _lastPlan;
    volatile MachineSnapshot? _mySnapshot;
    volatile string? _activity;
    volatile string? _lastDisconnectReason;
    volatile CancellationTokenSource? _opCts;
    volatile string? _lastBackupPath;
    volatile LiveUpdate? _hostLive;
    DateTime? _hostLiveReceivedUtc;
    LiveUpdate? _seenLive;
    int _busyCount;
    long _lastProgressRaise;
    HashCache? _cache;

    public SessionRole Role => _host is not null ? SessionRole.Host : _client is not null ? SessionRole.Client : SessionRole.None;
    public int? HostPort => _host?.Port;
    public string? HostAddress => _hostAddress;
    public MachineSnapshot? MySnapshot => _mySnapshot;

    public bool IsClient => _client is not null;
    public string? HostName => _client?.Roster.FirstOrDefault(p => p.IsHost)?.Name;
    public LiveUpdate? HostLive => _hostLive;
    public DateTime? HostLiveReceivedUtc => _hostLiveReceivedUtc;

    /// <summary>Client only: this machine compared with the host.</summary>
    public DiffResult? MyDiff => _myDiff;

    public IReadOnlyList<PlayerInfo> Players => _host?.Players ?? _client?.Roster ?? [];
    public MatchPlan? LastPlan => _lastPlan;

    /// <summary>The dlc_load.json backup written by the last Match host (null if none was needed or written).</summary>
    public string? LastBackupPath => _lastBackupPath;

    /// <summary>This player's id on the host (null when hosting or not in a session).</summary>
    public string? MyPlayerId => _client?.PlayerId;

    /// <summary>True while any operation is running or waiting for its turn.</summary>
    public bool IsBusy => Volatile.Read(ref _busyCount) > 0;

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
        if (live is not null)
        {
            live.Changed += PushLive;
            host.UpdateLive(live.Current);
        }
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
        _hostLive = null;
        _hostLiveReceivedUtc = null;
        _seenLive = null;
        var client = await SessionClient.ConnectAsync(address, port, PlayerName, ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var lastTarget = client.Target;
            client.Changed += () =>
            {
                if (!ReferenceEquals(_client, client)) return;
                if (!ReferenceEquals(lastTarget, client.Target))
                {
                    lastTarget = client.Target; // the host's target changed: the old plan no longer describes it
                    _lastPlan = null;
                    _lastBackupPath = null;
                }
                if (client.Live is { } l && !ReferenceEquals(l, _seenLive))
                {
                    _seenLive = l;
                    _hostLive = l;
                    _hostLiveReceivedUtc = DateTime.UtcNow;
                }
                RecomputeDiff(onlyIfInputsChanged: true); // most messages are roster updates: same target, same diff
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
                ClearDiff();
                _hostAddress = null;
            }
            await client.DisposeAsync();
            throw;
        }
    }, ct);

    /// <summary>Rescans this machine. As host, pushes the new list to everyone; as client, sends the new snapshot.</summary>
    public Task RescanAsync(CancellationToken ct = default) => Exclusive(async ct =>
    {
        _lastPlan = null;
        _lastBackupPath = null;
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
    public Task<MatchPlan> MatchHostAsync(CancellationToken ct = default) => Exclusive(MatchCoreAsync, ct);

    // Runs inside Exclusive. Shared by Match host and Workshop install-then-match.
    async Task<MatchPlan> MatchCoreAsync(CancellationToken ct)
    {
        var client = _client ?? throw new InvalidOperationException("Not connected to a host.");
        var mine = MySnapshot ?? throw new InvalidOperationException("Your mods have not been scanned yet.");
        var target = client.Target; // one target for the whole match
        var diff = await Task.Run(() => ModDiffer.Diff(target.HostSnapshot, mine), CancellationToken.None);
        await client.SendBusyAsync("Matching host…", CancellationToken.None);
        var applied = false;
        try
        {
            SetActivity("Applying the host's mod list…");
            await manager.RefreshLibraryAsync();
            var plan = await Task.Run(() => MatchPlan.Create(target.HostList, diff, manager.Library, mine), CancellationToken.None);
            ct.ThrowIfCancellationRequested();
            if (!client.IsConnected || !ReferenceEquals(_client, client)) throw new InvalidOperationException("The host has disconnected.");
            _lastBackupPath = manager.Apply(plan.ToApply);
            applied = true;
            _lastPlan = plan;
            var snapshot = await ScanAsync(ct);
            RecomputeDiff();
            await client.SendSnapshotAsync(snapshot, CancellationToken.None);
            return plan;
        }
        catch (Exception ex)
        {
            // Never leave the host showing "Matching host…": put our last known snapshot back.
            if (_mySnapshot is { } last)
            {
                try { await client.SendSnapshotAsync(last, CancellationToken.None); }
                catch { /* best effort */ }
            }
            if (applied)
                throw new InvalidOperationException($"Applied the host's list, but the rescan did not finish: {ex.Message}. Click Rescan my mods.", ex);
            throw;
        }
    }

    /// <summary>Client only: what Match host would do right now (the library is refreshed), without applying anything.</summary>
    public Task<MatchPlan> PlanAsync(CancellationToken ct = default) => Exclusive(async ct =>
    {
        var client = _client ?? throw new InvalidOperationException("Not connected to a host.");
        var mine = MySnapshot ?? throw new InvalidOperationException("Your mods have not been scanned yet.");
        var target = client.Target;
        SetActivity("Checking what the host's list needs…");
        await manager.RefreshLibraryAsync();
        ct.ThrowIfCancellationRequested();
        return await Task.Run(() => MatchPlan.Create(target.HostList, ModDiffer.Diff(target.HostSnapshot, mine), manager.Library, mine), CancellationToken.None);
    }, ct);

    public const string HostLeftNote = "The host disconnected; the downloads finished but the host's list was not applied.";

    /// <summary>
    /// Client only: downloads the given Workshop items (subscribing when needed), then matches the host like <see cref="MatchHostAsync"/>.
    /// Per-item failures don't stop the match; Steam being unavailable or a cancel does (nothing is applied then). When the host
    /// disconnects during the downloads, the item results come back with a null plan and <see cref="HostLeftNote"/>.
    /// With no ids this is a plain match. Installing needs a Workshop service.
    /// </summary>
    public Task<WorkshopMatchResult> InstallFromWorkshopAndMatchAsync(IReadOnlyList<ulong> ids, IProgress<WorkshopProgress> progress,
        CancellationToken ct = default) => Exclusive(async ct =>
    {
        var client = _client ?? throw new InvalidOperationException("Not connected to a host.");
        IReadOnlyList<WorkshopItemResult> items = [];
        if (ids.Count > 0)
        {
            var service = _workshop ?? throw new InvalidOperationException("Steam Workshop support is not available in this build.");
            SetActivity($"Downloading {ids.Count} Workshop mod(s)…");
            await client.SendBusyAsync("Downloading Workshop mods…", CancellationToken.None);
            try
            {
                items = await service.InstallAsync(ids, progress, ct);
                ct.ThrowIfCancellationRequested();
            }
            catch
            {
                // Never leave the host showing "Downloading…": put our last known snapshot back.
                if (_mySnapshot is { } last)
                {
                    try { await client.SendSnapshotAsync(last, CancellationToken.None); }
                    catch { /* best effort */ }
                }
                throw;
            }
            // The downloads are done and on disk; keep their results even though there is no host list to apply.
            if (!ReferenceEquals(_client, client) || !client.IsConnected)
                return new WorkshopMatchResult(items, null, HostLeftNote);
        }
        var plan = await MatchCoreAsync(ct);
        return new WorkshopMatchResult(items, plan);
    }, ct);

    /// <summary>Cancels the running operation, if any; it throws OperationCanceledException to its caller.</summary>
    public void CancelCurrent()
    {
        try { _opCts?.Cancel(); }
        catch (ObjectDisposedException) { /* the operation just finished */ }
    }

    public Task ViewAsAsync(int countryId) => _client is { } c ? c.SendViewAsAsync(countryId) : Task.CompletedTask;

    public void ForgetHostLive()
    {
        if (_client is not null) return;
        _hostLive = null;
        _hostLiveReceivedUtc = null;
        RaiseChanged();
    }

    void PushLive()
    {
        if (_host is { } h && live is not null) h.UpdateLive(live.Current);
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
        ClearDiff();
        _hostAddress = null;
        _lastPlan = null;
        _lastBackupPath = null;
        if (host is not null)
        {
            host.RosterChanged -= RaiseChanged;
            if (live is not null) live.Changed -= PushLive;
            await host.DisposeAsync();
        }
        if (client is not null) await client.DisposeAsync();
        RaiseChanged();
    }

    void OnDisconnected(SessionClient client, string reason)
    {
        if (!ReferenceEquals(_client, client)) return; // we left on purpose
        _client = null;
        ClearDiff();
        _hostAddress = null;
        _lastPlan = null;
        _lastBackupPath = null;
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
        Interlocked.Increment(ref _busyCount);
        RaiseChanged();
        var acquired = false;
        try
        {
            await _op.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _opCts = cts;
            try
            {
                // On the thread pool, so scanning, diffing and encoding never run on the caller's (UI) context.
                return await Task.Run(() => action(cts.Token), CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _opCts = null;
                _activity = null;
            }
        }
        finally
        {
            if (acquired) _op.Release();
            Interlocked.Decrement(ref _busyCount);
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

    /// <summary>
    /// Diffs my snapshot against the client's current target. Inputs are read under the lock, so the last diff written
    /// always uses inputs at least as new as any earlier one; with <paramref name="onlyIfInputsChanged"/> the diff is kept
    /// when it was already computed from the same target and snapshot instances.
    /// </summary>
    void RecomputeDiff(bool onlyIfInputsChanged = false)
    {
        lock (_diffLock)
        {
            if (_client is not { } client || MySnapshot is not { } mine)
            {
                _myDiff = null;
                _diffInputs = null;
                return;
            }
            var target = client.Target;
            if (onlyIfInputsChanged && _myDiff is not null && _diffInputs is { } last
                && ReferenceEquals(last.Target, target) && ReferenceEquals(last.Mine, mine)) return;
            _myDiff = ModDiffer.Diff(target.HostSnapshot, mine);
            _diffInputs = (target, mine);
        }
    }

    void ClearDiff()
    {
        lock (_diffLock)
        {
            _myDiff = null;
            _diffInputs = null;
        }
    }

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

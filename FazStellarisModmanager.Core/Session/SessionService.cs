using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Hashing;
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
    HashCache? _cache;

    public SessionRole Role => _host is not null ? SessionRole.Host : _client is not null ? SessionRole.Client : SessionRole.None;
    public int? HostPort => _host?.Port;
    public string? HostAddress { get; private set; }
    public MachineSnapshot? MySnapshot { get; private set; }

    /// <summary>Client only: this machine compared with the host.</summary>
    public DiffResult? MyDiff => _myDiff;

    public IReadOnlyList<PlayerInfo> Players => _host?.Players ?? _client?.Roster ?? [];
    public MatchPlan? LastPlan { get; private set; }

    /// <summary>What the current operation is doing (e.g. the scan's progress); null when idle.</summary>
    public string? Activity { get; private set; }

    /// <summary>Why the last client connection ended on its own (host stopped, network lost).</summary>
    public string? LastDisconnectReason { get; private set; }

    public event Action? Changed;

    string PlayerName => string.IsNullOrWhiteSpace(manager.Settings.PlayerName) ? Environment.MachineName : manager.Settings.PlayerName.Trim();

    /// <summary>Host only: a client's full diff against the host.</summary>
    public DiffResult? DiffFor(string playerId) => _host?.DiffFor(playerId);

    public Task HostAsync(int port, CancellationToken ct = default) => Exclusive(async () =>
    {
        if (Role != SessionRole.None) throw new InvalidOperationException("Already in a session. Leave it first.");
        var snapshot = await ScanAsync(ct);
        var host = new SessionHost(PlayerName, manager.ImportCurrent("Host list"), snapshot);
        host.RosterChanged += RaiseChanged;
        try { host.Start(port); }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
        _host = host;
        LastDisconnectReason = null;
        return true;
    }, ct);

    public Task JoinAsync(string address, int port, CancellationToken ct = default) => Exclusive(async () =>
    {
        if (Role != SessionRole.None) throw new InvalidOperationException("Already in a session. Leave it first.");
        var snapshot = await ScanAsync(ct);
        SetActivity($"Connecting to {address}:{port}…");
        var client = await SessionClient.ConnectAsync(address, port, PlayerName, ct);
        client.Changed += () =>
        {
            if (!ReferenceEquals(_client, client)) return;
            RecomputeDiff();
            RaiseChanged();
        };
        client.Disconnected += reason => OnDisconnected(client, reason);
        _client = client;
        HostAddress = $"{address}:{port}";
        LastDisconnectReason = null;
        RecomputeDiff();
        client.Start();
        await client.SendSnapshotAsync(snapshot, CancellationToken.None);
        return true;
    }, ct);

    /// <summary>Rescans this machine. As host, pushes the new list to everyone; as client, sends the new snapshot.</summary>
    public Task RescanAsync(CancellationToken ct = default) => Exclusive(async () =>
    {
        var snapshot = await ScanAsync(ct);
        if (_host is { } host) await host.UpdateHostAsync(manager.ImportCurrent("Host list"), snapshot);
        else if (_client is { } client)
        {
            RecomputeDiff();
            await client.SendSnapshotAsync(snapshot, CancellationToken.None);
        }
        return true;
    }, ct);

    /// <summary>Client only: writes the host's list (installed mods, host order) to dlc_load.json, then rescans and reports.</summary>
    public Task<MatchPlan> MatchHostAsync(CancellationToken ct = default) => Exclusive(async () =>
    {
        var client = _client ?? throw new InvalidOperationException("Not connected to a host.");
        var diff = _myDiff ?? throw new InvalidOperationException("Your mods have not been scanned yet.");
        var mine = MySnapshot ?? throw new InvalidOperationException("Your mods have not been scanned yet.");
        await client.SendBusyAsync("Matching host…", CancellationToken.None);
        SetActivity("Applying the host's mod list…");
        await manager.RefreshLibraryAsync();
        var plan = MatchPlan.Create(client.Target.HostList, diff, manager.Library, mine);
        manager.Apply(plan.ToApply);
        LastPlan = plan;
        var snapshot = await ScanAsync(ct);
        RecomputeDiff();
        await client.SendSnapshotAsync(snapshot, CancellationToken.None);
        return plan;
    }, ct);

    public Task LeaveAsync() => Exclusive(async () =>
    {
        await CloseAsync();
        return true;
    }, CancellationToken.None);

    async Task CloseAsync()
    {
        var host = _host;
        var client = _client;
        _host = null;
        _client = null;
        _myDiff = null;
        HostAddress = null;
        LastPlan = null;
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
        HostAddress = null;
        LastDisconnectReason = reason;
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
        var snapshot = await Task.Run(() => SnapshotScanner.ScanAsync(paths.UserDir, gameDir, PlayerName, cache, progress, ct), ct);
        try { cache.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* only a cache */ }
        MySnapshot = snapshot;
        return snapshot;
    }

    async Task<T> Exclusive<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _op.WaitAsync(ct);
        try { return await action(); }
        finally
        {
            SetActivity(null);
            _op.Release();
        }
    }

    sealed class ActivityProgress(SessionService owner) : IProgress<string>
    {
        public void Report(string value) => owner.SetActivity(value.Trim());
    }

    void SetActivity(string? text)
    {
        Activity = text;
        RaiseChanged();
    }

    void RecomputeDiff() =>
        _myDiff = _client is { } client && MySnapshot is { } mine ? ModDiffer.Diff(client.Target.HostSnapshot, mine) : null;

    void RaiseChanged() => Changed?.Invoke();

    public async ValueTask DisposeAsync() => await LeaveAsync();
}

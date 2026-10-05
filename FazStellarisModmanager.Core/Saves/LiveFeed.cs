namespace FazStellarisModmanager.Core.Saves;

public enum LiveSource
{
    /// <summary>This PC's own saves (single player, or hosting).</summary>
    Local,
    /// <summary>Connected to a session host that sent live data.</summary>
    Host,
    /// <summary>The last host data, kept after the connection ended.</summary>
    HostDisconnected,
}

/// <summary>
/// The app's one view of the live game: the host's data while connected as a client (and after it disconnects, until
/// <see cref="UseLocal"/>), otherwise this PC's saves. Knows which country is "you" and its researched techs.
/// <see cref="Changed"/> fires on any thread.
/// </summary>
public sealed class LiveFeed : IDisposable
{
    readonly LiveGameService _local;
    readonly IHostLiveSource _host;
    readonly LiveViewerStore _viewers;
    readonly Func<IEnumerable<string?>> _myNames;
    GameSnapshot? _resolvedFor;
    int? _localViewer;

    public LiveFeed(LiveGameService local, IHostLiveSource host, LiveViewerStore viewers, Func<IEnumerable<string?>> myNames)
    {
        _local = local;
        _host = host;
        _viewers = viewers;
        _myNames = myNames;
        _local.Changed += Raise;
        _host.Changed += Raise;
    }

    public event Action? Changed;

    public LiveSource Source => _host.HostLive is null ? LiveSource.Local : _host.IsClient ? LiveSource.Host : LiveSource.HostDisconnected;

    public GameSnapshot? Current => Source == LiveSource.Local ? _local.Current : _host.HostLive?.Snapshot;

    /// <summary>When the host data arrived (host sources only).</summary>
    public DateTime? ReceivedUtc => Source == LiveSource.Local ? null : _host.HostLiveReceivedUtc;

    public string? HostName => _host.HostName;
    public string? Status => Source == LiveSource.Local ? _local.Status : null;
    public string? Error => Source == LiveSource.Local ? _local.Error : null;
    public bool Reading => Source == LiveSource.Local && _local.Reading;

    public int? ViewerId => Source == LiveSource.Local ? LocalViewer() : _host.HostLive?.ViewerId;

    /// <summary>The viewer's researched tech keys (empty when unknown).</summary>
    public IReadOnlySet<string> ResearchedTechs =>
        Current is { } s && ViewerId is int id && s.Countries.FirstOrDefault(c => c.Id == id) is { } me
            ? me.Techs.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public void Start() => _local.Start();

    /// <summary>Local: remembered for this save. Host: sent to the host, which replies with a new view.</summary>
    public async Task SetViewerAsync(int? countryId)
    {
        if (Source == LiveSource.Local)
        {
            _localViewer = countryId;
            _resolvedFor = _local.Current;
            if (countryId is int id && _local.Current is { } s)
            {
                try { _viewers.Set(s.SaveName, id); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            Raise();
        }
        else if (Source == LiveSource.Host && countryId is int hostId)
        {
            await _host.ViewAsAsync(hostId);
        }
    }

    /// <summary>Stop showing kept host data and go back to this PC's saves.</summary>
    public void UseLocal() => _host.ForgetHostLive();

    int? LocalViewer()
    {
        var s = _local.Current;
        if (s is null) return null;
        if (!ReferenceEquals(s, _resolvedFor))
        {
            _resolvedFor = s;
            _localViewer = LiveViewer.Resolve(s, _myNames(), _viewers.Get(s.SaveName));
        }
        return _localViewer;
    }

    void Raise()
    {
        try { Changed?.Invoke(); }
        catch (Exception) { /* a faulty subscriber must not break the feed */ }
    }

    public void Dispose()
    {
        _local.Changed -= Raise;
        _host.Changed -= Raise;
    }
}

using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Events;

/// <summary>
/// UI facade for the Events tab (a singleton): builds the <see cref="EventGraph"/> from <see cref="TechTreeService.Current"/> (same
/// sources, localisation and script library), lazily, one build at a time, and caches it per tree. When no tree is loaded it
/// builds the current game's tree first. <see cref="Changed"/> may fire on any thread.
/// </summary>
public sealed class EventGraphService
{
    // A tree swapped while the graph was being built (its sources are then disposed): build again, a few times at most.
    const int MaxAttempts = 3;

    readonly TechTreeService _trees;
    readonly SemaphoreSlim _build = new(1, 1);
    volatile Built? _built;
    volatile string? _progress;
    volatile string? _error;
    volatile bool _waitingForTree;
    long _lastProgress;
    readonly Lock _runningLock = new();
    Task<EventGraph?>? _running;

    sealed record Built(TechTree Tree, EventGraph Graph);

    public EventGraphService(TechTreeService trees)
    {
        _trees = trees;
        _trees.Changed += OnTreesChanged;
    }

    /// <summary>The last graph built, or null. It may belong to an older tree; see <see cref="IsUpToDate"/>.</summary>
    public EventGraph? Current => _built?.Graph;

    /// <summary>True when <see cref="Current"/> was built from the tech tree that is current now.</summary>
    public bool IsUpToDate => _built is { } b && ReferenceEquals(b.Tree, _trees.Current);

    public bool IsBuilding => _build.CurrentCount == 0;

    /// <summary>What the build is doing ("Reading event files…"), or null when idle.</summary>
    public string? Progress => _progress;

    /// <summary>Why the last build failed, or null; cleared when a build starts.</summary>
    public string? Error => _error;

    public event Action? Changed;

    /// <summary>
    /// The graph for the current tree: the cached one, or a new build (building the current game's tech tree first when none is
    /// loaded). Null when building failed; see <see cref="Error"/>. Cancelling <paramref name="ct"/> only stops waiting: the build
    /// is shared and keeps running in the background, so leaving the page and coming back picks up where it is.
    /// </summary>
    public Task<EventGraph?> EnsureAsync(CancellationToken ct = default)
    {
        if (_built is { } cached && ReferenceEquals(cached.Tree, _trees.Current)) return Task.FromResult<EventGraph?>(cached.Graph);
        Task<EventGraph?> running;
        lock (_runningLock)
        {
            if (_running is null || _running.IsCompleted) _running = Task.Run(BuildLatestAsync);
            running = _running;
        }
        return running.WaitAsync(ct);
    }

    // Never cancelled: callers that stop waiting don't stop the work.
    async Task<EventGraph?> BuildLatestAsync()
    {
        var ct = CancellationToken.None;
        await _build.WaitAsync(ct);
        try
        {
            _error = null;
            for (var attempt = 1; ; attempt++)
            {
                var tree = _trees.Current;
                if (tree is null)
                {
                    _waitingForTree = true;
                    Report("Building the technology tree…", force: true);
                    try { await _trees.BuildAsync(new TechTreeChoice(TechTreeService.CurrentGameLabel, null), ct); }
                    finally { _waitingForTree = false; }
                    tree = _trees.Current!;
                }
                if (_built is { } done && ReferenceEquals(done.Tree, tree)) return done.Graph;

                Report("Reading events…", force: true);
                try
                {
                    var graph = await Task.Run(() => EventGraphScanner.Build(tree.Sources, tree.Database.Localisation, tree.Database.Scripts,
                        new ProgressSink(this), ct), ct);
                    _built = new Built(tree, graph);
                    if (ReferenceEquals(tree, _trees.Current) || attempt >= MaxAttempts) return graph;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !ReferenceEquals(tree, _trees.Current) && attempt < MaxAttempts)
                {
                    // The tree was replaced (and its sources disposed) during the build: try the new one.
                }
            }
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            return null;
        }
        finally
        {
            _build.Release();
            Report(null, force: true);
        }
    }

    /// <summary>PNG data URI of the event's picture (decoded once per tree), or null.</summary>
    public Task<string?> EventPictureAsync(EventInfo ev) => _trees.EventPictureAsync(ev.Picture);

    void OnTreesChanged()
    {
        if (_waitingForTree)
        {
            Report(_trees.Progress is { } p ? "Building the technology tree: " + p : "Building the technology tree…", force: false);
            return;
        }
        // A new tree makes the cached graph stale; tell the page so it can call EnsureAsync again.
        if (_built is { } b && !ReferenceEquals(b.Tree, _trees.Current)) Raise();
    }

    sealed class ProgressSink(EventGraphService owner) : IProgress<string>
    {
        public void Report(string value) => owner.Report(value, force: false);
    }

    void Report(string? text, bool force)
    {
        _progress = text;
        var now = Environment.TickCount64;
        if (!force && now - Interlocked.Read(ref _lastProgress) < 150) return;
        Interlocked.Exchange(ref _lastProgress, now);
        Raise();
    }

    void Raise()
    {
        if (Changed is not { } handlers) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try { handler(); }
            catch (Exception) { /* a faulty subscriber must not break building */ }
        }
    }
}

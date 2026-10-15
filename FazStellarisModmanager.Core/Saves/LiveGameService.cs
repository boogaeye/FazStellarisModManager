namespace FazStellarisModmanager.Core.Saves;

/// <summary>
/// Keeps <see cref="Current"/> as the newest readable save while someone holds a lease (<see cref="Acquire"/>): only then
/// is the save games folder watched, since reading a save is heavy and can make the game stutter. Reads run one at a time
/// on a dedicated background thread at below-normal priority, so a running game keeps priority over the app; <c>enrich</c>
/// (optional) runs on that same thread after each read. A failed read keeps the previous snapshot and is not retried until the file changes.
/// <see cref="Changed"/> fires on any thread.
/// </summary>
public sealed class LiveGameService(Func<string?> saveGamesDir, Func<string, GameSnapshot>? read = null, TimeSpan? stableDelay = null, Func<GameSnapshot, GameSnapshot>? enrich = null) : IDisposable
{
    readonly Func<string, GameSnapshot> _read = read ?? SaveReader.Read;
    readonly TimeSpan _stableDelay = stableDelay ?? TimeSpan.FromSeconds(2);
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly CancellationTokenSource _stop = new();
    readonly Lock _watchGate = new();
    int _leases; // guarded by _watchGate, like the watcher and the poll timer
    FileSystemWatcher? _watcher;
    Timer? _poll;
    (string Path, DateTime Time)? _failed;

    public GameSnapshot? Current { get; private set; }
    public string? Status { get; private set; }
    public string? Error { get; private set; }
    public bool Reading { get; private set; }
    public event Action? Changed;

    /// <summary>True while the folder is watched: at least one lease is held and the service is not disposed.</summary>
    public bool IsWatching
    {
        get { lock (_watchGate) return _poll is not null; }
    }

    /// <summary>
    /// Holds the reader until the lease is disposed (disposing twice is harmless). The first lease starts watching and
    /// reads the newest save; disposing the last stops watching. Current, Status and Error stay; a read already running finishes.
    /// </summary>
    public IDisposable Acquire()
    {
        bool start;
        lock (_watchGate)
        {
            start = _leases == 0 && !_stop.IsCancellationRequested;
            if (start) StartWatching();
            _leases++;
        }
        // Outside the lock: a refresh may raise Changed on this thread, and its handlers may take their own locks.
        if (start) Kick();
        return new Lease(this);
    }

    sealed class Lease(LiveGameService owner) : IDisposable
    {
        int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Release();
        }
    }

    void Release()
    {
        lock (_watchGate)
        {
            if (--_leases == 0) StopWatching();
        }
    }

    // Under _watchGate.
    void StartWatching()
    {
        var dir = saveGamesDir();
        if (dir is not null && Directory.Exists(dir))
        {
            try
            {
                _watcher = new FileSystemWatcher(dir, "*.sav") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                _watcher.Created += (_, _) => Poke();
                _watcher.Changed += (_, _) => Poke();
                _watcher.Renamed += (_, _) => Poke();
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
            {
                _watcher?.Dispose();
                _watcher = null; // polling still works
            }
        }
        _poll = new Timer(_ => Poke(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    // Under _watchGate.
    void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _poll?.Dispose();
        _poll = null;
    }

    // Watcher and poll callbacks: one may still arrive just after watching stopped.
    void Poke()
    {
        if (IsWatching) Kick();
    }

    void Kick() => _ = RefreshAsync();

    /// <summary>Reads the newest save if it is new or changed. Safe to call any time; calls are serialised.</summary>
    public async Task RefreshAsync()
    {
        if (_stop.IsCancellationRequested) return;
        await _gate.WaitAsync();
        try
        {
            var dir = saveGamesDir();
            if (dir is null || !Directory.Exists(dir))
            {
                Status = "No save games folder found. Check the Stellaris user folder in Settings.";
                return;
            }
            var newest = SaveFiles.Newest(dir);
            if (newest is null)
            {
                Status = "Waiting for a save… (play until the first autosave, or save the game)";
                return;
            }
            var stamp = (newest.FullName, newest.LastWriteTimeUtc);
            if (Current is { } c && c.SavePath == stamp.FullName && c.SavedUtc == stamp.LastWriteTimeUtc) return;
            if (_failed == stamp) return;

            if (!await WaitUntilStableAsync(newest.FullName)) return;
            Reading = true;
            Changed?.Invoke();
            try
            {
                string? enrichError = null;
                Current = await RunLowPriority(() =>
                {
                    var snap = _read(newest.FullName);
                    if (enrich is null) return snap;
                    try { return enrich(snap); }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                    {
                        enrichError = "Diplomatic weight could not be calculated: " + ex.Message;
                        return snap;
                    }
                });
                Error = enrichError;
                Status = null;
                _failed = null;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Error = $"Could not read {Path.GetFileName(newest.FullName)}: {ex.Message}";
                newest.Refresh();
                _failed = (newest.FullName, newest.LastWriteTimeUtc);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
        finally
        {
            Reading = false;
            _gate.Release();
            Changed?.Invoke();
        }
    }

    // Like Task.Run, but on a new background thread at below-normal priority. Exceptions reach the awaiting code as they
    // would from Task.Run; continuations run on the thread pool, not on the read thread.
    static Task<T> RunLowPriority<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "Save read",
        };
        thread.Start();
        return done.Task;
    }

    // A save is written in one go; wait until its size stops changing (up to a minute).
    async Task<bool> WaitUntilStableAsync(string path)
    {
        for (var i = 0; i < 30; i++)
        {
            var before = new FileInfo(path);
            if (!before.Exists) return false;
            if (_stableDelay > TimeSpan.Zero) await Task.Delay(_stableDelay);
            var after = new FileInfo(path);
            if (after.Exists && after.Length == before.Length && after.LastWriteTimeUtc == before.LastWriteTimeUtc) return true;
        }
        return false;
    }

    public void Dispose()
    {
        lock (_watchGate)
        {
            _stop.Cancel();
            StopWatching();
        }
    }
}

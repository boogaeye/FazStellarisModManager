namespace FazStellarisModmanager.Core.Saves;

/// <summary>
/// Watches the save games folder and keeps <see cref="Current"/> as the newest readable save. Reads run on the thread
/// pool, one at a time; <c>enrich</c> (optional) runs on that same thread after each read. A failed read keeps the previous snapshot and is not retried until the file changes.
/// <see cref="Changed"/> fires on any thread.
/// </summary>
public sealed class LiveGameService(Func<string?> saveGamesDir, Func<string, GameSnapshot>? read = null, TimeSpan? stableDelay = null, Func<GameSnapshot, GameSnapshot>? enrich = null) : IDisposable
{
    readonly Func<string, GameSnapshot> _read = read ?? SaveReader.Read;
    readonly TimeSpan _stableDelay = stableDelay ?? TimeSpan.FromSeconds(2);
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly CancellationTokenSource _stop = new();
    FileSystemWatcher? _watcher;
    Timer? _poll;
    (string Path, DateTime Time)? _failed;
    bool _started;

    public GameSnapshot? Current { get; private set; }
    public string? Status { get; private set; }
    public string? Error { get; private set; }
    public bool Reading { get; private set; }
    public event Action? Changed;

    /// <summary>Starts watching (once) and reads the newest save.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        var dir = saveGamesDir();
        if (dir is not null && Directory.Exists(dir))
        {
            try
            {
                _watcher = new FileSystemWatcher(dir, "*.sav") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                _watcher.Created += (_, _) => Kick();
                _watcher.Changed += (_, _) => Kick();
                _watcher.Renamed += (_, _) => Kick();
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
            {
                _watcher = null; // polling still works
            }
        }
        _poll = new Timer(_ => Kick(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        Kick();
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
                Current = await Task.Run(() =>
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
        _stop.Cancel();
        _watcher?.Dispose();
        _poll?.Dispose();
    }
}

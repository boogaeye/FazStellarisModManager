using System.Diagnostics;
using FazStellarisModmanager.Core.Workshop;
using Steamworks;
using Steamworks.Ugc;

namespace FazStellarisModmanager.Steam;

/// <summary>
/// Steam Workshop access. <see cref="GetInfoAsync"/> uses Steam's public Web API and never touches the Steam client.
/// <see cref="InstallAsync"/> connects to the running Steam client as Stellaris through Facepunch.Steamworks (Steam shows
/// "Playing Stellaris" meanwhile) and disconnects before returning.
/// <para>
/// Threading: the client is initialised without Facepunch's background pump, and Steam callbacks are pumped manually
/// (<see cref="SteamClient.RunCallbacks"/>) inside each call, so the calls are safe from any thread (including
/// Task.Run) but must not overlap; <c>_gate</c> ensures that. None of our code runs inside a Steam callback, and
/// <see cref="SteamClient.Shutdown"/> is only called from the sequential flow, never while a callback frame is running.
/// </para>
/// </summary>
public sealed class SteamWorkshopService(TimeSpan? stallTimeout = null, HttpClient? http = null) : IWorkshopService
{
    public const uint StellarisAppId = 281990;

    static readonly TimeSpan SteamCallTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan Pump = TimeSpan.FromMilliseconds(16);
    static readonly TimeSpan ReportEvery = TimeSpan.FromMilliseconds(250);

    readonly SemaphoreSlim _gate = new(1, 1);
    readonly TimeSpan _stall = stallTimeout ?? TimeSpan.FromMinutes(2);
    readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    volatile bool _active;

    public bool IsActive => _active;

    /// <summary>Titles and download sizes from Steam's public Web API; no Steam client connection.</summary>
    public Task<IReadOnlyList<WorkshopItemInfo>> GetInfoAsync(IReadOnlyList<ulong> ids, CancellationToken ct) =>
        WorkshopWebApi.GetDetailsAsync(_http, ids, ct);

    public Task<IReadOnlyList<WorkshopItemResult>> InstallAsync(IReadOnlyList<ulong> ids, IProgress<WorkshopProgress> progress, CancellationToken ct) =>
        Connected<IReadOnlyList<WorkshopItemResult>>(async () =>
        {
            foreach (var id in ids) progress.Report(new WorkshopProgress(id, WorkshopItemState.Waiting, 0));
            var results = new List<WorkshopItemResult>();
            foreach (var id in ids)
            {
                if (ct.IsCancellationRequested)
                {
                    results.Add(Cancelled(id, progress));
                    continue;
                }
                results.Add(await InstallOne(id, progress, ct));
            }
            return results;
        }, ct);

    async Task<WorkshopItemResult> InstallOne(ulong id, IProgress<WorkshopProgress> progress, CancellationToken ct)
    {
        try
        {
            progress.Report(new WorkshopProgress(id, WorkshopItemState.Subscribing, 0));
            // Facepunch returns an item even for a missing id; its Result says whether Steam found it.
            if (await Pumped(() => Item.GetAsync(id), SteamCallTimeout, ct) is not { } item || item.Result != Steamworks.Result.OK)
                return Fail(id, progress, "Not found on the Workshop, or not visible to this Steam account.");
            if (!item.IsSubscribed && !await Pumped(item.Subscribe, SteamCallTimeout, ct))
                return Fail(id, progress, "Steam refused the subscription.");
            // Always ask: for an installed item this is what makes Steam check for an update. DownloadWatch then waits
            // for Steam to react (or a grace period) before trusting an "installed and up to date" state.
            if (!item.Download(true)) return Fail(id, progress, "Steam would not start the download.");

            // Item.DownloadAsync only reports progress once a download completes, so poll Steam's byte counters instead
            // (State, the byte counters and Directory are live reads from the local Steam client).
            var watch = new DownloadWatch(_stall);
            var clock = Stopwatch.StartNew();
            var reported = -ReportEvery; // report on the first pass
            while (true)
            {
                SteamClient.RunCallbacks();
                var downloaded = item.DownloadBytesDownloaded;
                var total = item.DownloadBytesTotal;
                var elapsed = clock.Elapsed;
                switch (watch.Step(new DownloadSnapshot(item.IsInstalled, item.IsDownloading, item.IsDownloadPending, item.NeedsUpdate, downloaded, elapsed)))
                {
                    case DownloadVerdict.Done: return Done(id, item, progress);
                    case DownloadVerdict.Stalled: return Fail(id, progress, DownloadWatch.StalledMessage);
                    case DownloadVerdict.QueuedNotStarted: return Fail(id, progress, DownloadWatch.QueuedNotStartedMessage);
                }
                if (elapsed - reported >= ReportEvery)
                {
                    reported = elapsed;
                    var fraction = total > 0 ? Math.Clamp(downloaded / (double)total, 0, 1) : 0;
                    progress.Report(new WorkshopProgress(id, WorkshopItemState.Downloading, fraction));
                }
                await Task.Delay(Pump, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Cancelled(id, progress);
        }
        catch (TimeoutException)
        {
            return Fail(id, progress, "Steam did not respond.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Fail(id, progress, ex.Message);
        }
    }

    /// <summary>
    /// Starts a Facepunch call and pumps Steam callbacks on this thread until it completes. The call's internal
    /// continuations run inside <see cref="SteamClient.RunCallbacks"/>; ours only run after the task has completed.
    /// </summary>
    static async Task<T> Pumped<T>(Func<Task<T>> start, TimeSpan timeout, CancellationToken ct)
    {
        var task = start();
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            SteamClient.RunCallbacks();
            if (task.IsCompleted) return await task;
            if (deadline.Elapsed >= timeout) throw new TimeoutException();
            await Task.Delay(Pump, ct);
        }
    }

    static WorkshopItemResult Done(ulong id, Item item, IProgress<WorkshopProgress> progress)
    {
        progress.Report(new WorkshopProgress(id, WorkshopItemState.Installed, 1));
        return new WorkshopItemResult(id, true, item.Directory, null);
    }

    static WorkshopItemResult Cancelled(ulong id, IProgress<WorkshopProgress> progress)
    {
        progress.Report(new WorkshopProgress(id, WorkshopItemState.Cancelled, 0));
        return new WorkshopItemResult(id, false, null, "Cancelled.");
    }

    static WorkshopItemResult Fail(ulong id, IProgress<WorkshopProgress> progress, string error)
    {
        progress.Report(new WorkshopProgress(id, WorkshopItemState.Failed, 0, error));
        return new WorkshopItemResult(id, false, null, error);
    }

    async Task<T> Connected<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Dispatch.OnException ??= ex => Debug.WriteLine($"Steam callback failed: {ex}");
            try { SteamClient.Init(StellarisAppId, asyncCallbacks: false); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A partly finished Init would make every later Init fail.
                ShutdownQuietly();
                ClearSteamEnvironment();
                if (IsNativeLibraryProblem(ex))
                    throw new WorkshopUnavailableException("Steam's library is missing or doesn't match this app (64-bit Windows only).", ex);
                throw new WorkshopUnavailableException(
                    $"Could not connect to Steam ({ex.Message}). Make sure Steam is running, you are logged in, and this account owns Stellaris.", ex);
            }
            _active = true;
            try { return await work(); }
            finally
            {
                ShutdownQuietly();
                ClearSteamEnvironment();
                _active = false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    static bool IsNativeLibraryProblem(Exception ex) =>
        ex is DllNotFoundException or BadImageFormatException
        || ex is TypeInitializationException { InnerException: DllNotFoundException or BadImageFormatException };

    static void ShutdownQuietly()
    {
        try { SteamClient.Shutdown(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* nothing more to clean up */ }
    }

    // SteamClient.Init sets these; the game launched later must not inherit them.
    static void ClearSteamEnvironment()
    {
        Environment.SetEnvironmentVariable("SteamAppId", null);
        Environment.SetEnvironmentVariable("SteamGameId", null);
    }
}

using FazStellarisModmanager.Core.Workshop;
using Steamworks;
using Steamworks.Ugc;

namespace FazStellarisModmanager.Steam;

/// <summary>
/// Steam Workshop through Facepunch.Steamworks. Each call connects to the running Steam client as Stellaris (Steam shows
/// "Playing Stellaris" meanwhile) and disconnects before returning; calls are serialised.
/// </summary>
public sealed class SteamWorkshopService(TimeSpan? stallTimeout = null) : IWorkshopService
{
    public const uint StellarisAppId = 281990;

    readonly SemaphoreSlim _gate = new(1, 1);
    readonly TimeSpan _stall = stallTimeout ?? TimeSpan.FromMinutes(2);
    volatile bool _active;

    public bool IsActive => _active;

    public Task<IReadOnlyList<WorkshopItemInfo>> GetInfoAsync(IReadOnlyList<ulong> ids, CancellationToken ct) =>
        Connected<IReadOnlyList<WorkshopItemInfo>>(async () =>
        {
            var list = new List<WorkshopItemInfo>();
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                var item = await Item.GetAsync(id);
                list.Add(new WorkshopItemInfo(id, string.IsNullOrEmpty(item?.Title) ? null : item.Value.Title, item is { } i && i.SizeBytes > 0 ? i.SizeBytes : null));
            }
            return list;
        }, ct);

    public Task<IReadOnlyList<WorkshopItemResult>> InstallAsync(IReadOnlyList<ulong> ids, IProgress<WorkshopProgress> progress, CancellationToken ct) =>
        Connected<IReadOnlyList<WorkshopItemResult>>(async () =>
        {
            foreach (var id in ids) progress.Report(new WorkshopProgress(id, WorkshopItemState.Waiting, 0));
            var results = new List<WorkshopItemResult>();
            foreach (var id in ids)
            {
                if (ct.IsCancellationRequested)
                {
                    progress.Report(new WorkshopProgress(id, WorkshopItemState.Cancelled, 0));
                    results.Add(new WorkshopItemResult(id, false, null, "Cancelled."));
                    continue;
                }
                results.Add(await InstallOne(id, progress, ct));
            }
            return results;
        }, ct);

    async Task<WorkshopItemResult> InstallOne(ulong id, IProgress<WorkshopProgress> progress, CancellationToken ct)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            progress.Report(new WorkshopProgress(id, WorkshopItemState.Subscribing, 0));
            if (await Item.GetAsync(id) is not { } item)
                return Fail(id, progress, "Not found on the Workshop, or not visible to this Steam account.");
            if (!item.IsSubscribed && !await item.Subscribe())
                return Fail(id, progress, "Steam refused the subscription.");

            stall.CancelAfter(_stall);
            var last = -1f;
            var ok = await item.DownloadAsync(fraction =>
            {
                if (fraction > last)
                {
                    last = fraction;
                    stall.CancelAfter(_stall); // progress: restart the stall timer
                }
                progress.Report(new WorkshopProgress(id, WorkshopItemState.Downloading, Math.Clamp(fraction, 0, 1)));
            }, 250, stall.Token);
            ct.ThrowIfCancellationRequested();
            if (stall.IsCancellationRequested) return Fail(id, progress, "The download stalled.");

            if (!ok || await Item.GetAsync(id, 0) is not { IsInstalled: true, NeedsUpdate: false } done)
                return Fail(id, progress, "Steam did not finish installing it.");
            progress.Report(new WorkshopProgress(id, WorkshopItemState.Installed, 1));
            return new WorkshopItemResult(id, true, done.Directory, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            progress.Report(new WorkshopProgress(id, WorkshopItemState.Cancelled, 0));
            return new WorkshopItemResult(id, false, null, "Cancelled.");
        }
        catch (OperationCanceledException)
        {
            return Fail(id, progress, "The download stalled.");
        }
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
            try { SteamClient.Init(StellarisAppId, asyncCallbacks: true); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new WorkshopUnavailableException(
                    $"Could not connect to Steam ({ex.Message}). Make sure Steam is running, you are logged in, and this account owns Stellaris.", ex);
            }
            _active = true;
            try { return await work(); }
            finally
            {
                SteamClient.Shutdown();
                _active = false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

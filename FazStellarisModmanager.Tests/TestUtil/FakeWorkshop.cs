using FazStellarisModmanager.Core.Workshop;

namespace FazStellarisModmanager.Tests.TestUtil;

/// <summary>"Downloads" by calling <paramref name="install"/> (true = installed) and records what was requested.</summary>
public sealed class FakeWorkshop(Func<ulong, bool> install) : IWorkshopService
{
    public List<ulong> Requested { get; } = [];
    public bool IsActive { get; private set; }

    public Task<IReadOnlyList<WorkshopItemInfo>> GetInfoAsync(IReadOnlyList<ulong> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<WorkshopItemInfo>>(ids.Select(id => new WorkshopItemInfo(id, $"Item {id}", 1000)).ToList());

    public Task<IReadOnlyList<WorkshopItemResult>> InstallAsync(IReadOnlyList<ulong> ids, IProgress<WorkshopProgress> progress, CancellationToken ct)
    {
        IsActive = true;
        try
        {
            var results = new List<WorkshopItemResult>();
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                Requested.Add(id);
                var ok = install(id);
                progress.Report(new WorkshopProgress(id, ok ? WorkshopItemState.Installed : WorkshopItemState.Failed, ok ? 1 : 0));
                results.Add(new WorkshopItemResult(id, ok, ok ? "folder" : null, ok ? null : "Not available."));
            }
            return Task.FromResult<IReadOnlyList<WorkshopItemResult>>(results);
        }
        finally
        {
            IsActive = false;
        }
    }
}

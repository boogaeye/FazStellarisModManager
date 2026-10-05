using FazStellarisModmanager.Core.Workshop;

namespace FazStellarisModmanager.Tests.TestUtil;

/// <summary>"Downloads" by calling the install function (true = installed) and records what was requested.</summary>
public sealed class FakeWorkshop(Func<ulong, CancellationToken, Task<bool>> install) : IWorkshopService
{
    public FakeWorkshop(Func<ulong, bool> install) : this((id, _) => Task.FromResult(install(id))) { }

    public List<ulong> Requested { get; } = [];
    public bool IsActive { get; private set; }

    public Task<IReadOnlyList<WorkshopItemInfo>> GetInfoAsync(IReadOnlyList<ulong> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<WorkshopItemInfo>>(ids.Select(id => new WorkshopItemInfo(id, $"Item {id}", 1000)).ToList());

    public async Task<IReadOnlyList<WorkshopItemResult>> InstallAsync(IReadOnlyList<ulong> ids, IProgress<WorkshopProgress> progress, CancellationToken ct)
    {
        IsActive = true;
        try
        {
            var results = new List<WorkshopItemResult>();
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                Requested.Add(id);
                var ok = await install(id, ct);
                progress.Report(new WorkshopProgress(id, ok ? WorkshopItemState.Installed : WorkshopItemState.Failed, ok ? 1 : 0));
                results.Add(new WorkshopItemResult(id, ok, ok ? "folder" : null, ok ? null : "Not available."));
            }
            return results;
        }
        finally
        {
            IsActive = false;
        }
    }
}

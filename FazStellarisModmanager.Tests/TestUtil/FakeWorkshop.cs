using FazStellarisModmanager.Core.Workshop;

namespace FazStellarisModmanager.Tests.TestUtil;

/// <summary>"Downloads" by calling the install function and records what was requested.</summary>
public sealed class FakeWorkshop : IWorkshopService
{
    readonly Func<ulong, CancellationToken, Task<WorkshopItemResult>> _install;

    /// <summary>true = installed (into "folder"), false = failed with "Not available.".</summary>
    public FakeWorkshop(Func<ulong, CancellationToken, Task<bool>> install) =>
        _install = async (id, ct) => await install(id, ct)
            ? new WorkshopItemResult(id, true, "folder", null)
            : new WorkshopItemResult(id, false, null, "Not available.");

    public FakeWorkshop(Func<ulong, bool> install) : this((id, _) => Task.FromResult(install(id))) { }

    /// <summary>Each item's result as given; an error of "Cancelled." reports the item as cancelled.</summary>
    public FakeWorkshop(Func<ulong, WorkshopItemResult> install) => _install = (id, _) => Task.FromResult(install(id));

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
                var result = await _install(id, ct);
                var state = result.Success ? WorkshopItemState.Installed
                    : result.Error == "Cancelled." ? WorkshopItemState.Cancelled
                    : WorkshopItemState.Failed;
                progress.Report(new WorkshopProgress(id, state, result.Success ? 1 : 0));
                results.Add(result);
            }
            return results;
        }
        finally
        {
            IsActive = false;
        }
    }
}

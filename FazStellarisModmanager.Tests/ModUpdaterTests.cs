using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Workshop;
using FazStellarisModmanager.Tests.TestUtil;
using static FazStellarisModmanager.Core.Workshop.ModUpdateOutcome;

namespace FazStellarisModmanager.Tests;

public class ModUpdaterTests
{
    sealed class Recorder : IProgress<WorkshopProgress>
    {
        public List<WorkshopProgress> Reports { get; } = [];
        public void Report(WorkshopProgress value)
        {
            lock (Reports) Reports.Add(value);
        }
    }

    // Answers with fixed results, whatever was asked for.
    sealed class Answers(params WorkshopItemResult[] results) : IWorkshopService
    {
        public bool IsActive => false;
        public Task<IReadOnlyList<WorkshopItemInfo>> GetInfoAsync(IReadOnlyList<ulong> ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkshopItemResult>> InstallAsync(IReadOnlyList<ulong> ids, IProgress<WorkshopProgress> progress, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorkshopItemResult>>(results);
    }

    static ModListEntry Entry(string key) => new(key, key + " name", "mod/" + key.Replace(':', '_') + ".mod", null);

    // An installed Workshop mod folder with one file.
    static string Folder(TempDir t, ulong id)
    {
        t.Write($"ws/{id}/a.txt", "old");
        return Path.Combine(t.Path, "ws", id.ToString());
    }

    [Fact]
    public void WorkshopIds_are_the_workshop_entries_in_list_order_once_each()
    {
        var list = new ModList("L", [Entry("ugc:30"), Entry("local:a.mod"), Entry("ugc:10"), Entry("UGC:30"), Entry("ugc:x"), Entry("ugc:20")], []);

        Assert.Equal([30UL, 10UL, 20UL], ModUpdater.WorkshopIds(list));
    }

    [Fact]
    public async Task Tells_updated_from_up_to_date_by_the_folders_and_passes_failures_and_cancels_through()
    {
        using var t = new TempDir();
        var folders = new Dictionary<ulong, string> { [1] = Folder(t, 1), [2] = Folder(t, 2), [3] = Folder(t, 3), [4] = Folder(t, 4), [6] = Folder(t, 6) };
        var five = Path.Combine(t.Path, "ws", "5");
        var workshop = new FakeWorkshop(id =>
        {
            switch (id)
            {
                case 1:
                    File.WriteAllText(Path.Combine(folders[1], "a.txt"), "a newer version");
                    break;
                case 3:
                    return new WorkshopItemResult(id, false, null, "Not available.");
                case 4:
                    return new WorkshopItemResult(id, false, null, "Cancelled.");
                case 5: // not installed before
                    t.Write("ws/5/a.txt", "new");
                    return new WorkshopItemResult(id, true, five, null);
                case 6: // Steam didn't say where: the folder from before is checked
                    return new WorkshopItemResult(id, true, null, null);
            }
            return new WorkshopItemResult(id, true, folders[id], null);
        });
        var progress = new Recorder();

        var results = await ModUpdater.RunAsync(workshop, [1, 2, 3, 4, 5, 6], id => folders.GetValueOrDefault(id), progress, CancellationToken.None);

        Assert.Equal(
            [new ModUpdateResult(1, Updated, null), new(2, UpToDate, null), new(3, Failed, "Not available."), new(4, Cancelled, null),
             new(5, Updated, null), new(6, UpToDate, null)],
            results);
        Assert.Equal([1UL, 2UL, 3UL, 4UL, 5UL, 6UL], workshop.Requested);
        Assert.Equal(
            [(1UL, WorkshopItemState.Installed), (2UL, WorkshopItemState.Installed), (3UL, WorkshopItemState.Failed), (4UL, WorkshopItemState.Cancelled),
             (5UL, WorkshopItemState.Installed), (6UL, WorkshopItemState.Installed)],
            progress.Reports.Select(p => (p.Id, p.State)));
    }

    [Fact]
    public async Task An_id_without_a_result_is_cancelled()
    {
        using var t = new TempDir();
        var folder = Folder(t, 1);

        var results = await ModUpdater.RunAsync(new Answers(new WorkshopItemResult(1, true, folder, null)), [1, 2], id => id == 1 ? folder : null,
            new Recorder(), CancellationToken.None);

        Assert.Equal([new ModUpdateResult(1, UpToDate, null), new(2, Cancelled, null)], results);
    }

    [Fact]
    public async Task A_cancel_before_steam_answers_cancels_every_mod()
    {
        using var cts = new CancellationTokenSource();
        var workshop = new FakeWorkshop(_ =>
        {
            cts.Cancel();
            return true;
        });

        var results = await ModUpdater.RunAsync(workshop, [1, 2], _ => null, new Recorder(), cts.Token);

        Assert.Equal([1UL], workshop.Requested);
        Assert.Equal([new ModUpdateResult(1, Cancelled, null), new(2, Cancelled, null)], results);
    }

    [Fact]
    public async Task Steam_being_unavailable_propagates()
    {
        var workshop = new FakeWorkshop(async (_, _) =>
        {
            await Task.Yield();
            throw new WorkshopUnavailableException("Steam is not running.");
        });

        await Assert.ThrowsAsync<WorkshopUnavailableException>(() => ModUpdater.RunAsync(workshop, [1], _ => null, new Recorder(), CancellationToken.None));
    }

    [Fact]
    public void Summary_counts_each_outcome_and_mentions_cancels_only_when_there_are_some()
    {
        Assert.Equal("Updated 1 · Already up to date 2 · Failed 0",
            ModUpdater.Summary([new(1, Updated, null), new(2, UpToDate, null), new(3, UpToDate, null)]));
        Assert.Equal("Updated 0 · Already up to date 0 · Failed 1 · Cancelled 2",
            ModUpdater.Summary([new(1, Failed, "x"), new(2, Cancelled, null), new(3, Cancelled, null)]));
    }
}

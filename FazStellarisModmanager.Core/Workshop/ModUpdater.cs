using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;

namespace FazStellarisModmanager.Core.Workshop;

public enum ModUpdateOutcome { Updated, UpToDate, Failed, Cancelled }

/// <summary>What "Update mods" did to one Workshop mod. <see cref="Error"/> is set when it failed.</summary>
public sealed record ModUpdateResult(ulong Id, ModUpdateOutcome Outcome, string? Error);

/// <summary>
/// "Update mods": has Steam download every Workshop mod of a list, which makes it check each one for an update, and tells
/// the mods it updated from those that were already up to date by stamping their folders before and after.
/// </summary>
public static class ModUpdater
{
    // The error Workshop services give an item they didn't get to because of a cancel (see SteamWorkshopService).
    const string CancelledError = "Cancelled.";

    /// <summary>The ids of the list's Workshop (ugc:) entries, in list order, each once.</summary>
    public static IReadOnlyList<ulong> WorkshopIds(ModList list) =>
        list.Mods.Select(e => ModKeys.WorkshopId(e.Key)).OfType<ulong>().Distinct().ToList();

    /// <summary>
    /// Installs <paramref name="ids"/> through <paramref name="workshop"/> and returns one result per id, in order.
    /// <paramref name="folderOf"/> gives a mod's install folder before the run (null when it isn't installed); it is called
    /// on the calling thread, and the folders are stamped off it. After a cancel the item being downloaded finishes or
    /// stops and the rest are cancelled. Throws <see cref="WorkshopUnavailableException"/> when Steam can't be reached.
    /// </summary>
    public static async Task<IReadOnlyList<ModUpdateResult>> RunAsync(IWorkshopService workshop, IReadOnlyList<ulong> ids,
        Func<ulong, string?> folderOf, IProgress<WorkshopProgress> progress, CancellationToken ct)
    {
        var folders = ids.Select(folderOf).ToList();
        var before = await Task.Run(() => folders.Select(FolderStamp.Of).ToList(), CancellationToken.None);
        IReadOnlyList<WorkshopItemResult> items;
        try
        {
            items = await workshop.InstallAsync(ids, progress, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            items = []; // cancelled before Steam gave any results: every mod counts as cancelled
        }

        var byId = new Dictionary<ulong, WorkshopItemResult>();
        foreach (var item in items) byId.TryAdd(item.Id, item);
        return await Task.Run(() => ids.Select((id, i) => Classify(id, folders[i], before[i], byId.GetValueOrDefault(id))).ToList(), CancellationToken.None);
    }

    static ModUpdateResult Classify(ulong id, string? folder, FolderStamp? before, WorkshopItemResult? item)
    {
        if (item is null || item.Error == CancelledError) return new(id, ModUpdateOutcome.Cancelled, null);
        if (!item.Success) return new(id, ModUpdateOutcome.Failed, item.Error ?? "Steam gave no reason.");
        var after = FolderStamp.Of(string.IsNullOrEmpty(item.InstallFolder) ? folder : item.InstallFolder);
        // No folder before: Steam installed the mod just now. A folder that can't be read afterwards counts as changed.
        return new(id, before is null || after != before ? ModUpdateOutcome.Updated : ModUpdateOutcome.UpToDate, null);
    }

    /// <summary>"Updated U · Already up to date A · Failed F", followed by " · Cancelled C" when some were cancelled.</summary>
    public static string Summary(IReadOnlyList<ModUpdateResult> results)
    {
        int Count(ModUpdateOutcome outcome) => results.Count(r => r.Outcome == outcome);
        var text = $"Updated {Count(ModUpdateOutcome.Updated)} · Already up to date {Count(ModUpdateOutcome.UpToDate)} · Failed {Count(ModUpdateOutcome.Failed)}";
        return Count(ModUpdateOutcome.Cancelled) is var cancelled and > 0 ? $"{text} · Cancelled {cancelled}" : text;
    }
}

using FazStellarisModmanager.Core.Session;

namespace FazStellarisModmanager.Core.Workshop;

public enum WorkshopItemState { Waiting, Subscribing, Downloading, Installed, Failed, Cancelled }

/// <summary>Progress of one Workshop item. Fraction is 0..1 while downloading.</summary>
public sealed record WorkshopProgress(ulong Id, WorkshopItemState State, double Fraction, string? Message = null);

/// <summary>What Steam says about an item; null fields are unknown.</summary>
public sealed record WorkshopItemInfo(ulong Id, string? Title, long? SizeBytes);

public sealed record WorkshopItemResult(ulong Id, bool Success, string? InstallFolder, string? Error);

/// <summary>
/// Result of "install from the Workshop, then match the host". <see cref="Plan"/> is null when the host's list was not
/// applied (the host disconnected during the downloads); <see cref="Note"/> then says why.
/// </summary>
public sealed record WorkshopMatchResult(IReadOnlyList<WorkshopItemResult> Items, MatchPlan? Plan, string? Note = null);

/// <summary>Steam could not be reached (not running, not logged in, or the account doesn't own the game).</summary>
public sealed class WorkshopUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Steam Workshop access. Implementations connect to Steam only inside a call and disconnect before returning.</summary>
public interface IWorkshopService
{
    /// <summary>True while a call is connected to Steam; the game must not be launched then.</summary>
    bool IsActive { get; }

    /// <summary>Titles and sizes. Throws <see cref="WorkshopUnavailableException"/> when Steam can't be reached.</summary>
    Task<IReadOnlyList<WorkshopItemInfo>> GetInfoAsync(IReadOnlyList<ulong> ids, CancellationToken ct);

    /// <summary>
    /// Subscribes (when needed) and downloads each item until Steam reports it installed and up to date. Per-item failures are
    /// results, not exceptions; <see cref="WorkshopUnavailableException"/> when Steam can't be reached at all.
    /// </summary>
    Task<IReadOnlyList<WorkshopItemResult>> InstallAsync(IReadOnlyList<ulong> ids, IProgress<WorkshopProgress> progress, CancellationToken ct);
}

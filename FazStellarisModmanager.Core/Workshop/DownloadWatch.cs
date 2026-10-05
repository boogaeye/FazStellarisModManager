namespace FazStellarisModmanager.Core.Workshop;

/// <summary>What the Workshop download poll should do after a <see cref="DownloadWatch"/> step.</summary>
public enum DownloadVerdict { Continue, Done, Stalled, QueuedNotStarted }

/// <summary>
/// One look at a Workshop item's Steam state while it downloads. <paramref name="Elapsed"/> is the time since the
/// download was requested.
/// </summary>
public readonly record struct DownloadSnapshot(bool Installed, bool Downloading, bool Pending, bool NeedsUpdate, long Bytes, TimeSpan Elapsed);

/// <summary>
/// Decides when a Workshop download has finished, stalled or never started, from successive snapshots of the item.
/// <para>
/// Requesting a download is also what makes Steam check an installed item for updates, and Steam may still report
/// the old copy as finished for a moment. So a finished state only counts once Steam has visibly reacted (the item
/// was missing, downloading, queued or flagged as needing an update at some point), or after <c>grace</c> has passed
/// with the item still finished, which means it really is up to date.
/// </para>
/// <para>
/// Growing byte counts restart the stall timer. An item that isn't finished and whose bytes don't grow for
/// <c>stallTimeout</c> has stalled, or was queued but never started when it is still pending with nothing downloaded.
/// A finished item is never reported as stalled.
/// </para>
/// </summary>
public sealed class DownloadWatch(TimeSpan stallTimeout, TimeSpan? grace = null)
{
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(8);

    public const string StalledMessage = "The download stalled.";
    public const string QueuedNotStartedMessage = "Steam queued the download but didn't start it; check Steam's Downloads page.";

    readonly TimeSpan _grace = grace ?? DefaultGrace;
    bool _reacted;
    long _bytes = -1;
    TimeSpan _progressAt;

    public DownloadVerdict Step(DownloadSnapshot s)
    {
        if (!s.Installed || s.Downloading || s.Pending || s.NeedsUpdate) _reacted = true;
        if (s.Bytes > _bytes)
        {
            _bytes = s.Bytes;
            _progressAt = s.Elapsed;
        }

        var finished = s.Installed && !s.Downloading && !s.Pending && !s.NeedsUpdate;
        if (finished) return _reacted || s.Elapsed >= _grace ? DownloadVerdict.Done : DownloadVerdict.Continue;

        if (s.Elapsed - _progressAt < stallTimeout) return DownloadVerdict.Continue;
        return s.Pending && _bytes <= 0 ? DownloadVerdict.QueuedNotStarted : DownloadVerdict.Stalled;
    }
}

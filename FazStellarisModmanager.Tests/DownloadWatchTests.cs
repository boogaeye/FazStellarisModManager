using FazStellarisModmanager.Core.Workshop;

namespace FazStellarisModmanager.Tests;

public class DownloadWatchTests
{
    static readonly TimeSpan Stall = TimeSpan.FromSeconds(30);

    static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    static DownloadSnapshot Idle(long bytes, double at) => new(true, false, false, false, bytes, S(at));

    static DownloadSnapshot Downloading(long bytes, double at) => new(true, true, false, true, bytes, S(at));

    static DownloadSnapshot Queued(double at) => new(true, false, true, true, 0, S(at));

    [Fact]
    public void An_up_to_date_item_is_done_only_after_the_grace_period()
    {
        var watch = new DownloadWatch(Stall);
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Idle(0, 0)));
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Idle(0, 4)));
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Idle(0, 7.9)));
        Assert.Equal(DownloadVerdict.Done, watch.Step(Idle(0, 8)));
    }

    [Fact]
    public void An_up_to_date_item_is_not_reported_stalled_when_the_stall_timeout_is_shorter_than_the_grace()
    {
        var watch = new DownloadWatch(S(1));
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Idle(0, 0)));
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Idle(0, 5)));
        Assert.Equal(DownloadVerdict.Done, watch.Step(Idle(0, 8)));
    }

    [Fact]
    public void An_outdated_item_that_starts_downloading_is_done_when_it_finishes_without_waiting_for_the_grace()
    {
        var watch = new DownloadWatch(Stall);
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Idle(0, 0))); // Steam hasn't noticed the update yet
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Downloading(100, 0.5)));
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Downloading(500, 1)));
        Assert.Equal(DownloadVerdict.Done, watch.Step(Idle(1000, 1.5)));
    }

    [Fact]
    public void An_item_that_is_not_installed_is_done_once_installed()
    {
        var watch = new DownloadWatch(Stall);
        Assert.Equal(DownloadVerdict.Continue, watch.Step(new(false, true, false, false, 10, S(0))));
        Assert.Equal(DownloadVerdict.Continue, watch.Step(new(false, false, false, false, 20, S(1))));
        Assert.Equal(DownloadVerdict.Done, watch.Step(Idle(20, 1.2)));
    }

    [Fact]
    public void Bytes_that_stop_growing_stall_and_growth_restarts_the_timer()
    {
        var watch = new DownloadWatch(Stall);
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Downloading(100, 0)));
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Downloading(100, 29)));
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Downloading(200, 29.5))); // progress: timer restarts
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Downloading(200, 59)));
        Assert.Equal(DownloadVerdict.Stalled, watch.Step(Downloading(200, 59.5)));
    }

    [Fact]
    public void A_pending_item_that_downloads_some_bytes_then_stops_has_stalled()
    {
        var watch = new DownloadWatch(Stall);
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Downloading(50, 0)));
        Assert.Equal(DownloadVerdict.Stalled, watch.Step(new(true, false, true, true, 50, S(31))));
    }

    [Fact]
    public void A_queued_download_that_never_starts_is_reported_as_queued_not_started()
    {
        var watch = new DownloadWatch(Stall);
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Queued(0)));
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Queued(29)));
        Assert.Equal(DownloadVerdict.QueuedNotStarted, watch.Step(Queued(30)));
    }

    [Fact]
    public void A_download_that_finishes_just_before_the_stall_is_done()
    {
        var watch = new DownloadWatch(Stall);
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Downloading(100, 0)));
        Assert.Equal(DownloadVerdict.Continue, watch.Step(Downloading(100, 29.9)));
        Assert.Equal(DownloadVerdict.Done, watch.Step(Idle(100, 30)));
    }
}

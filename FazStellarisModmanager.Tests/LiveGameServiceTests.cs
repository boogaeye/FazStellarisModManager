using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class LiveGameServiceTests
{
    static GameSnapshot Snap(string path) => new("S", "2300.01.01", "v", path, File.GetLastWriteTimeUtc(path), [], []);

    [Fact]
    public async Task Reads_newest_then_newer_and_keeps_last_good_on_failure()
    {
        using var t = new TempDir();
        var first = Path.GetFullPath(t.Write("g/1.sav", "x"));
        File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddMinutes(-5));
        var reads = 0;
        GameSnapshot Read(string p)
        {
            reads++;
            if (p.EndsWith("bad.sav")) throw new InvalidDataException("corrupt");
            return Snap(p);
        }
        using var live = new LiveGameService(() => t.Path, Read, stableDelay: TimeSpan.Zero);

        await live.RefreshAsync();
        Assert.Equal(first, live.Current!.SavePath);

        await live.RefreshAsync();
        Assert.Equal(1, reads); // unchanged: not re-read

        var second = Path.GetFullPath(t.Write("g/2.sav", "y"));
        await live.RefreshAsync();
        Assert.Equal(second, live.Current!.SavePath);
        Assert.Null(live.Error);

        var bad = Path.GetFullPath(t.Write("g/bad.sav", "z"));
        File.SetLastWriteTimeUtc(bad, DateTime.UtcNow.AddMinutes(1));
        await live.RefreshAsync();
        Assert.Equal(second, live.Current!.SavePath);
        Assert.Contains("corrupt", live.Error);
        await live.RefreshAsync();
        Assert.Equal(3, reads); // the same bad file is not retried until it changes
    }

    [Fact]
    public async Task No_folder_or_no_save_sets_status()
    {
        using var t = new TempDir();
        using var none = new LiveGameService(() => null, Snap, TimeSpan.Zero);
        await none.RefreshAsync();
        Assert.Null(none.Current);
        Assert.NotNull(none.Status);

        using var empty = new LiveGameService(() => t.Path, Snap, TimeSpan.Zero);
        await empty.RefreshAsync();
        Assert.Null(empty.Current);
        Assert.Contains("Waiting", empty.Status);
    }

    static GameSnapshot WithPlayers(params SavePlayer[] players) => new("Save A", "d", "v", "p", DateTime.UtcNow, players, []);

    [Fact]
    public void Viewer_single_player_name_match_or_remembered()
    {
        Assert.Equal(4, LiveViewer.Resolve(WithPlayers(new SavePlayer("Anyone", 4)), ["me"], null));
        var mp = WithPlayers(new SavePlayer("SCP Fazbear", 0), new SavePlayer("Flamgop", 1));
        Assert.Equal(1, LiveViewer.Resolve(mp, [null, "flamgop"], null));
        Assert.Equal(0, LiveViewer.Resolve(mp, ["nobody"], 0));
        Assert.Null(LiveViewer.Resolve(mp, ["nobody"], 9));
        Assert.Null(LiveViewer.Resolve(mp, ["nobody"], null));
    }

    [Fact]
    public void Viewer_store_round_trips()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "live-game.json");
        new LiveViewerStore(path).Set("Save A", 3);
        Assert.Equal(3, new LiveViewerStore(path).Get("Save A"));
        Assert.Null(new LiveViewerStore(path).Get("Other"));
        File.WriteAllText(path, "{ broken");
        Assert.Null(new LiveViewerStore(path).Get("Save A"));
    }
}

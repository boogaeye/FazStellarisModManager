using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class LiveFeedTests
{
    sealed class FakeHost : IHostLiveSource
    {
        public LiveUpdate? HostLive { get; set; }
        public DateTime? HostLiveReceivedUtc { get; set; }
        public bool IsClient { get; set; }
        public string? HostName { get; set; } = "Hosty";
        public event Action? Changed;
        public List<int> Sent { get; } = [];
        public Task ViewAsAsync(int countryId) { Sent.Add(countryId); return Task.CompletedTask; }
        public void ForgetHostLive() { if (!IsClient) { HostLive = null; Raise(); } }
        public void Raise() => Changed?.Invoke();
    }

    static SaveCountry C(int id, params string[] techs) =>
        new(id, "default", "N" + id, new Dictionary<string, string>(), null, null, id + 1, 1, 1, 1, 1, 1, 1, 1, null, [], techs);

    static GameSnapshot Snap(string name, params SavePlayer[] players) =>
        new(name, "2300.01.01", "v", "x.sav", DateTime.UtcNow, players, [C(0, "tech_a"), C(1, "tech_b")]);

    [Fact]
    public async Task Local_source_resolves_viewer_and_researched()
    {
        using var t = new TempDir();
        var save = t.Write("g/1.sav", "x");
        using var local = new LiveGameService(() => t.Path, _ => Snap("Solo", new SavePlayer("Me", 1)), TimeSpan.Zero);
        var host = new FakeHost();
        using var feed = new LiveFeed(local, host, new LiveViewerStore(Path.Combine(t.Path, "v.json")), () => ["Me"]);
        await local.RefreshAsync();

        Assert.Equal(LiveSource.Local, feed.Source);
        Assert.Equal(1, feed.ViewerId);
        Assert.Equal(["tech_b"], feed.ResearchedTechs.Order());
    }

    [Fact]
    public async Task Host_source_while_connected_then_kept_after_disconnect()
    {
        using var t = new TempDir();
        using var local = new LiveGameService(() => null, _ => throw new InvalidOperationException(), TimeSpan.Zero);
        var host = new FakeHost { IsClient = true, HostLive = new LiveUpdate(Snap("MP", new SavePlayer("A", 0), new SavePlayer("B", 1)), 0), HostLiveReceivedUtc = DateTime.UtcNow };
        using var feed = new LiveFeed(local, host, new LiveViewerStore(Path.Combine(t.Path, "v.json")), () => ["B"]);
        var changes = 0;
        feed.Changed += () => changes++;
        host.Raise();

        Assert.Equal(LiveSource.Host, feed.Source);
        Assert.Equal(0, feed.ViewerId); // the host decides
        Assert.Equal(["tech_a"], feed.ResearchedTechs);
        Assert.True(changes > 0);

        await feed.SetViewerAsync(1);
        Assert.Equal([1], host.Sent);

        host.IsClient = false;
        host.Raise();
        Assert.Equal(LiveSource.HostDisconnected, feed.Source);
        Assert.NotNull(feed.Current);

        feed.UseLocal();
        Assert.Equal(LiveSource.Local, feed.Source);
        Assert.Null(feed.Current);
    }

    [Fact]
    public async Task Local_viewer_choice_is_remembered()
    {
        using var t = new TempDir();
        t.Write("g/1.sav", "x");
        var store = new LiveViewerStore(Path.Combine(t.Path, "v.json"));
        using var local = new LiveGameService(() => t.Path, _ => Snap("Duo", new SavePlayer("A", 0), new SavePlayer("B", 1)), TimeSpan.Zero);
        using var feed = new LiveFeed(local, new FakeHost(), store, () => ["nobody"]);
        await local.RefreshAsync();
        Assert.Null(feed.ViewerId);
        Assert.Empty(feed.ResearchedTechs);

        await feed.SetViewerAsync(1);
        Assert.Equal(1, feed.ViewerId);
        Assert.Equal(1, store.Get("Duo"));
    }
}

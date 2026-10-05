using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Core.Session;

namespace FazStellarisModmanager.Tests;

public class LiveFilterTests
{
    static SaveCountry C(int id, int rank, IReadOnlyList<int> contacts, IReadOnlyList<string> techs) =>
        new(id, "default", "Name" + id, new Dictionary<string, string> { ["a"] = "b" }, "Adj",
            new CountryFlag("c", "f.dds", "b", "bg.dds", ["red"]), rank, 100 + id, 200, 300, 400, 500, 600, 700, 800, contacts, techs);

    static GameSnapshot S() => new("Save", "2300.01.01", "v", "C:/Users/host/Documents/save games/x/autosave.sav", DateTime.UtcNow,
        [new SavePlayer("Host", 0), new SavePlayer("Guest", 1)],
        [C(0, 1, [1, 2], ["t0"]), C(1, 2, [0], ["t1", "t2"]), C(2, 3, [0], ["t9"]), C(3, 4, [], ["t3"])]);

    [Fact]
    public void Viewer_gets_own_country_in_full_others_trimmed_unknowns_blank()
    {
        var f = LiveFilter.For(S(), viewerId: 1);
        Assert.Equal("autosave.sav", f.SavePath);
        Assert.Equal(2, f.Players.Count);

        var mine = f.Countries.Single(c => c.Id == 1);
        Assert.Equal(["t1", "t2"], mine.Techs);
        Assert.Equal([0], mine.ContactedIds);
        Assert.Equal(300, mine.EconomyPower);

        var host = f.Countries.Single(c => c.Id == 0);
        Assert.Equal("Name0", host.NameKey);
        Assert.Equal(200, host.MilitaryPower);
        Assert.Empty(host.Techs);
        Assert.Empty(host.ContactedIds);

        var unknown = f.Countries.Single(c => c.Id == 2);
        Assert.Equal(3, unknown.VictoryRank);
        Assert.Equal("default", unknown.Type);
        Assert.Null(unknown.NameKey);
        Assert.Null(unknown.Flag);
        Assert.Null(unknown.Adjective);
        Assert.Empty(unknown.NameVariables);
        Assert.Equal(0, unknown.VictoryScore);
        Assert.Equal(0, unknown.MilitaryPower);
        Assert.Null(unknown.CachedDiploWeight);
        Assert.Empty(unknown.Techs);
    }

    [Fact]
    public void No_viewer_sends_players_only()
    {
        var f = LiveFilter.For(S(), viewerId: null);
        Assert.Equal(2, f.Players.Count);
        Assert.Empty(f.Countries);
        Assert.Equal("Save", f.SaveName);
    }

    [Fact]
    public async Task Live_messages_survive_framing()
    {
        var update = new LiveUpdate(LiveFilter.For(S(), 1), 1);
        using var stream = new MemoryStream();
        await Framing.WriteAsync(stream, update);
        await Framing.WriteAsync(stream, new LiveViewAs(1));
        stream.Position = 0;
        var back = Assert.IsType<LiveUpdate>(await Framing.ReadAsync(stream));
        Assert.Equal(1, back.ViewerId);
        Assert.Equal(["t1", "t2"], back.Snapshot.Countries.Single(c => c.Id == 1).Techs);
        Assert.Equal("b", back.Snapshot.Countries.Single(c => c.Id == 1).NameVariables["a"]);
        Assert.Equal(["red"], back.Snapshot.Countries.Single(c => c.Id == 0).Flag!.Colors);
        Assert.Equal(1, Assert.IsType<LiveViewAs>(await Framing.ReadAsync(stream)).CountryId);
    }
}

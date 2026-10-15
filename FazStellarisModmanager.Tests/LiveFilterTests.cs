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

    static GameSnapshot Rich()
    {
        var bd = new FazStellarisModmanager.Core.Diplomacy.DiploBreakdown(new(0, 0, []), new(0, 0, []), new(0, 0, []), new(0, 0, []), [], false);
        return S() with
        {
            Countries = S().Countries.Select(c => c with
            {
                Holdings = CountryHoldings.Empty with { Origin = "o" },
                Roster = CountryRoster.Empty,
                Flags = ["f"],
                Ethics = ["e"],
            }).ToList(),
            Community = new GalacticCommunity([0, 1], [0], ["r"]),
            Megastructures = new Dictionary<int, IReadOnlyList<string>> { [1] = ["m"] },
            Diplo = new Dictionary<int, FazStellarisModmanager.Core.Diplomacy.DiploBreakdown> { [0] = bd, [1] = bd },
            GlobalFlags = ["g"],
            Dlcs = ["Utopia"],
        };
    }

    static void AssertTechOnlyView(GameSnapshot f)
    {
        Assert.True(f.TechOnly);
        Assert.Equal("autosave.sav", f.SavePath);
        Assert.Equal(2, f.Players.Count);
        var mine = Assert.Single(f.Countries);
        Assert.Equal(1, mine.Id);
        Assert.Equal("default", mine.Type);
        Assert.Equal(["t1", "t2"], mine.Techs);
        Assert.Null(mine.NameKey);
        Assert.Empty(mine.NameVariables);
        Assert.Null(mine.Adjective);
        Assert.Null(mine.Flag);
        Assert.Equal(0, mine.VictoryRank);
        Assert.Equal(0, mine.VictoryScore);
        Assert.Equal(0, mine.MilitaryPower);
        Assert.Equal(0, mine.EconomyPower);
        Assert.Equal(0, mine.Pops);
        Assert.Null(mine.CachedDiploWeight);
        Assert.Empty(mine.ContactedIds);
        Assert.Null(mine.Holdings);
        Assert.Null(mine.Roster);
        Assert.Null(mine.Flags);
        Assert.Null(mine.Ethics);
        Assert.Null(f.Community);
        Assert.Null(f.Megastructures);
        Assert.Null(f.Diplo);
        Assert.Null(f.GlobalFlags);
    }

    [Fact]
    public void TechOnly_keeps_only_the_viewers_techs()
    {
        var f = LiveFilter.TechOnly(Rich(), viewerId: 1);

        AssertTechOnlyView(f);
        Assert.Equal(["Utopia"], f.Dlcs);
        Assert.Equal("Save", f.SaveName);
    }

    [Fact]
    public void TechOnly_works_on_a_tech_only_snapshot()
    {
        var techOnly = Rich() with { TechOnly = true };
        techOnly = techOnly with { Countries = [.. techOnly.Countries.Select(c => new SaveCountry(c.Id, c.Type, null, new Dictionary<string, string>(), null, null, 0, 0, 0, 0, 0, 0, 0, 0, null, [], c.Techs))] };

        AssertTechOnlyView(LiveFilter.TechOnly(techOnly, viewerId: 1));
    }

    [Fact]
    public void TechOnly_without_a_known_viewer_sends_players_only()
    {
        foreach (var viewer in new int?[] { null, 99 })
        {
            var f = LiveFilter.TechOnly(Rich(), viewer);
            Assert.True(f.TechOnly);
            Assert.Equal(2, f.Players.Count);
            Assert.Empty(f.Countries);
            Assert.Null(f.Diplo);
            Assert.Null(f.GlobalFlags);
        }
    }

    [Fact]
    public async Task A_tech_only_view_survives_framing()
    {
        using var stream = new MemoryStream();
        await Framing.WriteAsync(stream, new LiveUpdate(LiveFilter.TechOnly(Rich(), 1), 1));
        stream.Position = 0;

        var back = Assert.IsType<LiveUpdate>(await Framing.ReadAsync(stream));

        Assert.True(back.Snapshot.TechOnly);
        Assert.Equal(["t1", "t2"], Assert.Single(back.Snapshot.Countries).Techs);
    }

    [Fact]
    public void Holdings_diplo_community_and_megastructures_only_for_viewer()
    {
        var h = CountryHoldings.Empty with { Origin = "origin_x" };
        var bd = new FazStellarisModmanager.Core.Diplomacy.DiploBreakdown(new(0, 0, []), new(0, 0, []), new(0, 0, []), new(0, 0, []), [], false);
        var s = S() with
        {
            Countries = S().Countries.Select(c => c with { Holdings = h }).ToList(),
            Community = new GalacticCommunity([0, 1], [0], ["r"]),
            Megastructures = new Dictionary<int, IReadOnlyList<string>> { [1] = ["m"] },
            Diplo = new Dictionary<int, FazStellarisModmanager.Core.Diplomacy.DiploBreakdown> { [0] = bd, [1] = bd },
        };
        var f = LiveFilter.For(s, 1);
        Assert.NotNull(f.Countries.Single(c => c.Id == 1).Holdings);
        Assert.All(f.Countries.Where(c => c.Id != 1), c => Assert.Null(c.Holdings));
        Assert.Equal([1], f.Diplo!.Keys);
        Assert.Null(f.Community);
        Assert.Null(f.Megastructures);

        var none = LiveFilter.For(s, null);
        Assert.Null(none.Diplo);
        Assert.Null(none.Community);
        Assert.Null(none.Megastructures);
        Assert.Null(LiveFilter.For(s, 99).Diplo);
    }
}

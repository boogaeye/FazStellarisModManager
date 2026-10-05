using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class LiveBoardTests
{
    static SaveCountry C(int id, int rank, string name = "Empire", IReadOnlyList<int>? contacts = null, double score = 10,
        string? adjective = null, IReadOnlyDictionary<string, string>? vars = null) =>
        new(id, "default", name, vars ?? new Dictionary<string, string>(), adjective, null, rank, score, 1, 2, 3, 4, 5, 6, null, contacts ?? [], []);

    static GameSnapshot S(params SaveCountry[] countries) =>
        new("Save", "2300.01.01", "v", "x.sav", DateTime.UtcNow, [new SavePlayer("Alice", 0), new SavePlayer("Bob", 1)], countries);

    [Fact]
    public void Ranked_countries_in_rank_order_with_unknowns_hidden()
    {
        var rows = LiveBoard.Build(S(C(0, 2, "Mine", contacts: [1]), C(1, 1, "Friend"), C(2, 3, "Stranger"), C(3, 0, "Unranked")), viewerId: 0);
        Assert.Equal([1, 0, 2], rows.Select(r => r.Id));
        Assert.True(rows[0].Known);
        Assert.Equal("Bob", rows[0].PlayerName);
        Assert.True(rows[1].IsViewer);
        Assert.Equal("Alice", rows[1].PlayerName);
        var stranger = rows[2];
        Assert.False(stranger.Known);
        Assert.Equal("???", stranger.Name);
        Assert.Null(stranger.Score);
        Assert.Null(stranger.Flag);
        Assert.Null(stranger.PlayerName);
    }

    [Fact]
    public void Viewer_is_always_known()
    {
        var rows = LiveBoard.Build(S(C(0, 1, "Mine")), viewerId: 0);
        Assert.True(rows[0].Known);
        Assert.Equal(10, rows[0].Score!.Value.Real);
    }

    [Theory]
    [InlineData(3547190.9, false, 3547190.9)]
    [InlineData(-747776.373, true, 3547190.923)]
    public void Stat_value_flags_impossible_negatives(double raw, bool overflowed, double real)
    {
        var v = new StatValue(raw);
        Assert.Equal(overflowed, v.Overflowed);
        Assert.Equal(real, v.Real, 3);
    }

    [Theory]
    [InlineData("Interstellar Battlecat Regime", null, "Interstellar Battlecat Regime")]
    [InlineData("Imperial_Core", "Fazbear", "Fazbear Imperial Core")]
    [InlineData("EMPIRE_DESIGN_zrobots_machine_age", null, "zrobots machine age")]
    [InlineData(null, null, "Empire 7")]
    [InlineData("", null, "Empire 7")]
    public void Display_names(string? key, string? adjective, string expected)
    {
        Assert.Equal(expected, CountryNames.Display(C(7, 1, key!, adjective: adjective) with { NameKey = key }));
    }

    [Fact]
    public void Display_name_from_template_variables()
    {
        var c = C(3, 1, "%ADJECTIVE%", vars: new Dictionary<string, string> { ["adjective"] = "SPEC_EssJaggon", ["1"] = "Commonwealth" });
        Assert.Equal("EssJaggon Commonwealth", CountryNames.Display(c));
    }

    [Fact]
    public void Diplo_estimate_uses_defines()
    {
        var c = C(0, 1) with { MilitaryPower = 1000, EconomyPower = 100, TechPower = 10, Pops = 50 };
        var e = DiploWeight.Estimate(c, DiploDefines.Vanilla);
        Assert.Equal(25, e.Naval, 6);
        Assert.Equal(15, e.Economy, 6);
        Assert.Equal(1, e.Tech, 6);
        Assert.Equal(0.5, e.PopsMin, 6);
        Assert.Equal(100.5, e.PopsMax, 6);
        Assert.Equal(41.5, e.Min, 6);
        Assert.Equal(141.5, e.Max, 6);
    }

    [Fact]
    public void Diplo_defines_last_file_wins_and_defaults_fill_gaps()
    {
        using var t = new TempDir();
        t.Write("game/common/defines/00_defines.txt", "NGameplay = { DIPLOMACY_WEIGHT_NAVAL_FACTOR = 0.025 DIPLOMACY_WEIGHT_ECONOMY_FACTOR = 0.15 }");
        t.Write("mod/common/defines/zz_mod.txt", "NGameplay = { DIPLOMACY_WEIGHT_NAVAL_FACTOR = 0.05 }");
        using var game = ContentSource.FromPath("game", Path.Combine(t.Path, "game"), isBaseGame: true);
        using var mod = ContentSource.FromPath("mod", Path.Combine(t.Path, "mod"));
        var d = DiploDefines.Load([game, mod]);
        Assert.Equal(0.05, d.Naval);
        Assert.Equal(0.15, d.Economy);
        Assert.Equal(DiploDefines.Vanilla.Technology, d.Technology);
        Assert.Equal(DiploDefines.Vanilla.PopHappiness, d.PopHappiness);
    }
}

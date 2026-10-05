using FazStellarisModmanager.Core.Saves;

namespace FazStellarisModmanager.Tests;

public class LiveRanksTests
{
    static SaveCountry C(int id, int rank, double economy = 2, double military = 1, IReadOnlyList<int>? contacts = null) =>
        new(id, "default", "Empire", new Dictionary<string, string>(), null, null, rank, 10, military, economy, 3, 4, 5, 6, null, contacts ?? [], []);

    static GameSnapshot S(params SaveCountry[] countries) =>
        new("Save", "2300.01.01", "v", "x.sav", DateTime.UtcNow, [new SavePlayer("Alice", 0)], countries);

    [Fact]
    public void Competition_ranking_with_ties()
    {
        var rows = LiveBoard.Build(S(
            C(0, 1, economy: 100, contacts: [1, 2, 3]), C(1, 2, economy: 50), C(2, 3, economy: 50), C(3, 4, economy: 10)), viewerId: 0);
        var ranks = LiveBoard.Ranks(rows);
        Assert.Equal(1, ranks[0][BoardStat.Economy]);
        Assert.Equal(2, ranks[1][BoardStat.Economy]);
        Assert.Equal(2, ranks[2][BoardStat.Economy]);
        Assert.Equal(4, ranks[3][BoardStat.Economy]);
    }

    [Fact]
    public void Unknown_rows_have_no_rank_and_do_not_count()
    {
        var rows = LiveBoard.Build(S(C(0, 1, economy: 5, contacts: [1]), C(1, 2, economy: 3), C(2, 3, economy: 999)), viewerId: 0);
        var ranks = LiveBoard.Ranks(rows);
        Assert.False(ranks.ContainsKey(2));
        Assert.Equal(1, ranks[0][BoardStat.Economy]);
        Assert.Equal(2, ranks[1][BoardStat.Economy]);
    }

    [Fact]
    public void Score_rank_is_victory_rank()
    {
        var rows = LiveBoard.Build(S(C(0, 7, contacts: [1]), C(1, 3)), viewerId: 0);
        var ranks = LiveBoard.Ranks(rows);
        Assert.Equal(7, ranks[0][BoardStat.Score]);
        Assert.Equal(3, ranks[1][BoardStat.Score]);
    }

    [Fact]
    public void Sort_by_economy_puts_unknowns_last_in_victory_order()
    {
        var rows = LiveBoard.Build(S(
            C(0, 1, economy: 1, contacts: [1, 3]), C(1, 2, economy: 50), C(2, 3, economy: 999), C(3, 4, economy: 20), C(4, 5, economy: 5)), viewerId: 0);
        Assert.Equal([1, 3, 0, 2, 4], LiveBoard.Sort(rows, BoardStat.Economy).Select(r => r.Id));
    }

    [Fact]
    public void Sort_by_economy_breaks_ties_by_victory_rank()
    {
        var rows = LiveBoard.Build(S(C(0, 3, economy: 5, contacts: [1, 2]), C(1, 1, economy: 5), C(2, 2, economy: 5)), viewerId: 0);
        Assert.Equal([1, 2, 0], LiveBoard.Sort(rows, BoardStat.Economy).Select(r => r.Id));
    }

    [Fact]
    public void Sort_by_score_uses_victory_order()
    {
        var rows = LiveBoard.Build(S(C(0, 3, economy: 100, contacts: [1]), C(1, 1, economy: 1), C(2, 2)), viewerId: 0);
        Assert.Equal([1, 2, 0], LiveBoard.Sort(rows, BoardStat.Score).Select(r => r.Id));
    }

    [Fact]
    public void Value_picks_the_stat()
    {
        var rows = LiveBoard.Build(S(C(0, 1)), viewerId: 0);
        Assert.Equal(5, LiveBoard.Value(rows[0], BoardStat.EmpireSize)!.Value.Real);
        Assert.Equal(6, LiveBoard.Value(rows[0], BoardStat.Pops)!.Value.Real);
    }
}

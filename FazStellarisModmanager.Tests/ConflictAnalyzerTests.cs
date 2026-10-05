using FazStellarisModmanager.Core.Conflicts;

namespace FazStellarisModmanager.Tests;

public class ConflictAnalyzerTests
{
    static IReadOnlyCollection<string> F(params string[] files) => files;

    [Fact]
    public void Last_mod_wins_and_groups_are_by_winner()
    {
        var r = ConflictAnalyzer.Analyze([
            F("a/1.txt", "a/2.txt", "a/3.txt", "a/4.txt"),
            F("a/1.txt", "a/2.txt"),
            F("A/2.TXT", "a/3.txt"),
        ]);
        Assert.Equal(4, r[0].Total);
        Assert.Equal(3, r[0].Overwritten);
        Assert.Equal(75, r[0].Percent);
        Assert.True(r[0].IsHeavy);
        Assert.Equal([1, 2], r[0].Groups.Select(g => g.WinnerIndex));
        Assert.Equal(["a/1.txt"], r[0].Groups[0].Files);
        Assert.Equal(["a/2.txt", "a/3.txt"], r[0].Groups[1].Files);

        Assert.Equal(1, r[1].Overwritten);
        Assert.Equal(50, r[1].Percent);
        Assert.False(r[1].IsHeavy);
        Assert.Equal(0, r[2].Overwritten);
        Assert.Empty(r[2].Groups);
    }

    [Fact]
    public void Threshold_is_75_percent_rounded_down()
    {
        var mine = Enumerable.Range(0, 100).Select(i => $"c/{i}.txt").ToArray();
        var r = ConflictAnalyzer.Analyze([F(mine), F(mine.Take(74).ToArray())]);
        Assert.Equal(74, r[0].Percent);
        Assert.False(r[0].IsHeavy);
    }

    [Fact]
    public void Empty_mod_and_duplicate_files_in_a_mod()
    {
        var r = ConflictAnalyzer.Analyze([F(), F("x/a.txt", "X/A.txt"), F("x/a.txt")]);
        Assert.Equal(0, r[0].Total);
        Assert.False(r[0].IsHeavy);
        Assert.Equal(0, r[0].Percent);
        Assert.Equal(1, r[1].Total);
        Assert.Equal(100, r[1].Percent);
    }

    [Fact]
    public void Earlier_copy_of_same_mod_is_fully_overwritten()
    {
        var r = ConflictAnalyzer.Analyze([F("a/1.txt", "a/2.txt"), F("a/1.txt", "a/2.txt")]);
        Assert.Equal(100, r[0].Percent);
        Assert.Equal(0, r[1].Percent);
    }
}

using FazStellarisModmanager.Core.Conflicts;

namespace FazStellarisModmanager.Tests;

public class DefinitionAnalyzerTests
{
    static ScriptFile F(string path, params string[] names) => new(path, names);
    static ModScripts M(params ScriptFile[] files) => new(files);

    [Fact]
    public void Whole_file_replacement_like_galactic_market_expansion()
    {
        var r = DefinitionAnalyzer.Analyze([
            M(F("common/strategic_resources/00_strategic_resources.txt", "minerals", "energy"),
              F("common/strategic_resources/giga_strategic_resources.txt", "giga_x")),
            M(F("common/strategic_resources/acot_special_resources.txt", "acot_a")),
            M(F("common/strategic_resources/giga_strategic_resources.txt", "giga_x", "giga_y")),
            M(F("common/strategic_resources/00_strategic_resources.txt", "minerals", "energy", "food")),
        ]);
        Assert.Equal([3, 1, 2, 3], r.DefinitionCounts);
        var lost = r.Losses.Where(l => l.Mod == 0).OrderBy(l => l.Name).ToList();
        Assert.Equal(["energy", "giga_x", "minerals"], lost.Select(l => l.Name));
        Assert.All(lost, l => Assert.Equal(LossReason.FileReplaced, l.Reason));
        Assert.Equal(2, lost.Single(l => l.Name == "giga_x").OtherMod);
        Assert.Equal(3, lost.Single(l => l.Name == "minerals").OtherMod);
        Assert.DoesNotContain(r.Losses, l => l.Mod != 0);
    }

    [Fact]
    public void Last_wins_by_file_name_not_list_order()
    {
        var r = DefinitionAnalyzer.Analyze([
            M(F("common/buildings/zz_mine.txt", "b1")),
            M(F("common/buildings/00_other.txt", "b1", "b2")),
        ]);
        var loss = Assert.Single(r.Losses);
        Assert.Equal(1, loss.Mod);
        Assert.Equal(0, loss.OtherMod);
        Assert.Equal("common/buildings/zz_mine.txt", loss.OtherFile);
        Assert.Equal(LossReason.LoadsBeforeWinner, loss.Reason);
    }

    [Fact]
    public void First_wins_folder()
    {
        var r = DefinitionAnalyzer.Analyze([
            M(F("common/scripted_variables/zz_a.txt", "@v")),
            M(F("common/scripted_variables/00_b.txt", "@v")),
        ]);
        var loss = Assert.Single(r.Losses);
        Assert.Equal(0, loss.Mod);
        Assert.Equal(1, loss.OtherMod);
        Assert.Equal(LossReason.LoadsAfterWinner, loss.Reason);
    }

    [Fact]
    public void Duplicated_folder_and_events_report_every_copy()
    {
        var r = DefinitionAnalyzer.Analyze([
            M(F("common/strategic_resources/a.txt", "sr_x"), F("events/a.txt", "ev.1")),
            M(F("common/strategic_resources/b.txt", "sr_x"), F("events/b.txt", "ev.1")),
        ]);
        Assert.Equal(4, r.Losses.Count);
        Assert.All(r.Losses, l => Assert.Equal(LossReason.Duplicate, l.Reason));
        Assert.Contains(r.Losses, l => l.Mod == 0 && l.Name == "sr_x" && l.OtherMod == 1);
        Assert.Contains(r.Losses, l => l.Mod == 1 && l.Name == "ev.1" && l.OtherMod == 0);
    }

    [Fact]
    public void Same_mod_twice_in_its_own_files_is_not_a_conflict()
    {
        var r = DefinitionAnalyzer.Analyze([
            M(F("common/buildings/a.txt", "b1"), F("common/buildings/b.txt", "B1")),
            M(F("common/buildings/c.txt", "b2")),
        ]);
        Assert.Empty(r.Losses);
        Assert.Equal([2, 1], r.DefinitionCounts);
    }

    [Fact]
    public void Mod_listed_twice_loses_everything_in_its_first_copy()
    {
        var files = M(F("common/buildings/a.txt", "b1", "b2"));
        var r = DefinitionAnalyzer.Analyze([files, files]);
        Assert.Equal(2, r.Losses.Count);
        Assert.All(r.Losses, l => { Assert.Equal(0, l.Mod); Assert.Equal(1, l.OtherMod); Assert.Equal(LossReason.FileReplaced, l.Reason); });
    }

    [Fact]
    public void Unscanned_files_are_ignored()
    {
        var r = DefinitionAnalyzer.Analyze([
            M(F("common/on_actions/a.txt", "on_game_start")),
            M(F("common/on_actions/b.txt", "on_game_start")),
        ]);
        Assert.Empty(r.Losses);
        Assert.Equal([0, 0], r.DefinitionCounts);
    }
}

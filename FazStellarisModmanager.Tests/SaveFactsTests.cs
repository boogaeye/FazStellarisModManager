using System.Text;
using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class SaveFactsTests
{
    const string State = """
        flags={ galactic_market_founded=2300.01.01 first_contact_done=0 }
        player={ { name="Host" country=0 } }
        country=
        {
        	0=
        	{
        		type="default"
        		victory_rank=1
        		flags={ my_flag=2200.03.04 other_flag=12 }
        		ethos={ ethic="ethic_militarist" ethic="ethic_fanatic_xenophile" }
        	}
        	1=
        	{
        		type="default"
        		victory_rank=2
        	}
        }
        """;

    [Fact]
    public void Reads_country_flags_and_ethics()
    {
        var (_, countries, _, _, _) = GamestateScanner.ScanWithGlobals(Encoding.UTF8.GetBytes(State));
        var c = countries.Single(x => x.Id == 0);
        Assert.Equal(["my_flag", "other_flag"], c.Flags);
        Assert.Equal(["ethic_militarist", "ethic_fanatic_xenophile"], c.Ethics);
        Assert.Empty(countries.Single(x => x.Id == 1).Flags!);
        Assert.Empty(countries.Single(x => x.Id == 1).Ethics!);
    }

    [Fact]
    public void Reads_global_flags_without_confusing_country_flags()
    {
        var (_, _, _, _, global) = GamestateScanner.ScanWithGlobals(Encoding.UTF8.GetBytes(State));
        Assert.Equal(["galactic_market_founded", "first_contact_done"], global);
    }

    [Fact]
    public void Save_reader_reads_dlcs_and_global_flags()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "x", "a.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry("meta").Open()))
                w.Write("version=\"v\"" + (char)10 + "name=\"n\"" + (char)10 + "required_dlcs={ \"Utopia\" \"Apocalypse\" }" + (char)10);
            using (var w = new StreamWriter(zip.CreateEntry("gamestate").Open())) w.Write(State);
        }
        var s = SaveReader.Read(path);
        Assert.Equal(["Utopia", "Apocalypse"], s.Dlcs);
        Assert.Contains("first_contact_done", s.GlobalFlags!);
        Assert.Equal(["my_flag", "other_flag"], s.Countries.Single(c => c.Id == 0).Flags);
    }

    [Fact]
    public void Filter_keeps_flags_and_ethics_for_viewer_only()
    {
        SaveCountry C(int id) => new(id, "default", "n", new Dictionary<string, string>(), "a", null, id, 1, 1, 1, 1, 1, 1, 1, null,
            [0, 1], ["t"], null, null, ["f"], ["ethic_x"]);
        var s = new GameSnapshot("S", "d", "v", "C:/x/a.sav", DateTime.UtcNow, [new SavePlayer("H", 0)], [C(0), C(1), C(2)],
            GlobalFlags: ["g"], Dlcs: ["Utopia"]);
        var f = LiveFilter.For(s, 0);
        Assert.Equal(["f"], f.Countries.Single(c => c.Id == 0).Flags);
        Assert.Equal(["ethic_x"], f.Countries.Single(c => c.Id == 0).Ethics);
        Assert.All(f.Countries.Where(c => c.Id != 0), c => { Assert.Null(c.Flags); Assert.Null(c.Ethics); });
        Assert.Equal(["g"], f.GlobalFlags);
        Assert.Equal(["Utopia"], f.Dlcs);
        var none = LiveFilter.For(s, null);
        Assert.Equal(["g"], none.GlobalFlags);
        Assert.Equal(["Utopia"], none.Dlcs);
    }
}

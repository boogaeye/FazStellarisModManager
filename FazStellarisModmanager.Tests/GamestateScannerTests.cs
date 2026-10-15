using System.Text;
using FazStellarisModmanager.Core.Saves;

namespace FazStellarisModmanager.Tests;

public class GamestateScannerTests
{
    internal const string Fixture = """
        version="Cygnus v4.5.1"
        name="Test"
        date="2300.01.01"
        player=
        {
        	{
        		name="Alice"
        		country=0
        	}
        	{
        		name="Bob"
        		country=1
        	}
        }
        species_db=
        {
        	0={ name="has } brace" # comment with { brace
        	}
        }
        country=
        {
        	0=
        	{
        		flag=
        		{
        			icon={ category="special" file="the_empire.dds" }
        			background={ category="backgrounds" file="00_solid.dds" }
        			colors={ "red" "black" "null" }
        		}
        		name={ key="Imperial_Core" }
        		adjective={ key="%ADJECTIVE%" variables={ { key="adjective" value={ key="Fazbear" } } } }
        		tech_status={ technology="tech_a" level=1 technology="tech_b" level=1 }
        		victory_rank=1
        		victory_score=3547190.92253
        		military_power=3231227834.78125
        		economy_power=-1818745.755
        		tech_power=1741561.25
        		fleet_size=4755
        		empire_size=50
        		num_sapient_pops=399283
        		type="default"
        		budget={ current_month={ income={ x={ energy=5 } } } }
        		variables={ egm_cached_diplo_weight=179343485.88589 other=1 }
        		relations_manager=
        		{
        			relation={ owner=0 country=1 contact=yes communications=yes }
        			relation={ owner=0 country=2 hostile=yes }
        		}
        	}
        	1=
        	{
        		name={ key="Interstellar Battlecat Regime" }
        		victory_rank=2
        		victory_score=222820.87
        		type="default"
        	}
        	2=none
        	3=
        	{
        		name={ key="%ADJECTIVE%" variables={ { key="adjective" value={ key="SPEC_EssJaggon" } } { key="1" value={ key="Commonwealth" } } } }
        		victory_rank=3
        		type="fallen_empire"
        	}
        }
        galactic_community={ members={ 0 1 } }
        """;

    static (List<SavePlayer> Players, List<SaveCountry> Countries) Scan(string text) => GamestateScanner.Scan(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Reads_players()
    {
        var (players, _) = Scan(Fixture);
        Assert.Equal([new SavePlayer("Alice", 0), new SavePlayer("Bob", 1)], players);
    }

    [Fact]
    public void Reads_countries_skipping_none()
    {
        var (_, countries) = Scan(Fixture);
        Assert.Equal([0, 1, 3], countries.Select(c => c.Id));
    }

    [Fact]
    public void Reads_country_fields()
    {
        var c = Scan(Fixture).Countries[0];
        Assert.Equal("default", c.Type);
        Assert.Equal(1, c.VictoryRank);
        Assert.Equal(3547190.92253, c.VictoryScore, 5);
        Assert.Equal(3231227834.78125, c.MilitaryPower, 5);
        Assert.Equal(-1818745.755, c.EconomyPower, 3);
        Assert.Equal(1741561.25, c.TechPower, 2);
        Assert.Equal(4755, c.FleetSize);
        Assert.Equal(50, c.EmpireSize);
        Assert.Equal(399283, c.Pops);
        Assert.Equal(179343485.88589, c.CachedDiploWeight!.Value, 5);
        Assert.Equal("Imperial_Core", c.NameKey);
        Assert.Equal("Fazbear", c.Adjective);
        Assert.Equal(["tech_a", "tech_b"], c.Techs);
        Assert.Equal([1], c.ContactedIds);
        Assert.Equal(new CountryFlag("special", "the_empire.dds", "backgrounds", "00_solid.dds", ["red", "black", "null"]), c.Flag, new FlagComparer());
    }

    [Fact]
    public void Reads_name_variables_and_missing_fields()
    {
        var countries = Scan(Fixture).Countries;
        var c3 = countries.Single(c => c.Id == 3);
        Assert.Equal("%ADJECTIVE%", c3.NameKey);
        Assert.Equal("SPEC_EssJaggon", c3.NameVariables["adjective"]);
        Assert.Equal("Commonwealth", c3.NameVariables["1"]);
        Assert.Equal("fallen_empire", c3.Type);
        var c1 = countries.Single(c => c.Id == 1);
        Assert.Null(c1.Flag);
        Assert.Null(c1.CachedDiploWeight);
        Assert.Empty(c1.ContactedIds);
        Assert.Equal(0, c1.MilitaryPower);
    }

    [Fact]
    public void Resolves_nested_name_templates()
    {
        const string text = "country={ 0={ name={ key=\"%ADJECTIVE%\" variables={ { key=\"adjective\" value={ key=\"Mechazur\" } } { key=\"1\" value={ key=\"%ADJ%\" variables={ { key=\"1\" value={ key=\"Daroan\" } } } } } } } victory_rank=1 } }";
        var c = Scan(text).Countries.Single();
        Assert.Equal("Daroan", c.NameVariables["1"]);
        Assert.Equal("Mechazur", c.NameVariables["adjective"]);
    }

    [Fact]
    public void Empty_or_broken_input_does_not_throw()
    {
        Assert.Empty(Scan("").Countries);
        Assert.Empty(Scan("country={ 0={ name={ key=\"x\" ").Countries.Where(c => c.VictoryRank > 0));
    }

    const string TechPlayers = """
        player=
        {
        	{ name="Alice" country=0 }
        	{ name="Bob" country=2 }
        }
        """;

    const string TechCountries = """
        country=
        {
        	0=
        	{
        		name={ key="Alice Empire" }
        		flag={ colors={ "red" } }
        		tech_status=
        		{
        			technology="tech_a" level=1
        			technology="tech_b" level=1
        			technology="tech_a" level=2
        			potential={ tech_x="1" }
        			physics_queue={ { progress=5 technology="tech_q" } }
        		}
        		victory_rank=1
        		military_power=500
        		type="default"
        		relations_manager={ relation={ owner=0 country=2 contact=yes } }
        		flags={ some_flag=1 }
        	}
        	1=
        	{
        		name={ key="AI } with a brace" } # a comment with { a brace
        		tech_status={ technology="tech_ai" level=1 }
        		type="default"
        	}
        	2=
        	{
        		type="fallen_empire"
        		budget={ current_month={ income={ x={ energy=5 } } } }
        		tech_status={ technology="tech_c" level=1 }
        	}
        	3=none
        }
        """;

    [Fact]
    public void ScanTechs_reads_player_countries_only_with_the_same_techs_as_a_full_scan()
    {
        var text = TechPlayers + (char)10 + TechCountries;
        var data = Encoding.UTF8.GetBytes(text);

        var (players, countries) = GamestateScanner.ScanTechs(data);

        Assert.Equal([new SavePlayer("Alice", 0), new SavePlayer("Bob", 2)], players);
        Assert.Equal([0, 2], countries.Select(c => c.Id));
        var full = GamestateScanner.ScanWithGlobals(data).Countries;
        foreach (var c in countries) Assert.Equal(full.Single(f => f.Id == c.Id).Techs, c.Techs);
        Assert.Equal(["tech_a", "tech_b"], countries[0].Techs);
        Assert.Equal(["default", "fallen_empire"], countries.Select(c => c.Type));
    }

    [Fact]
    public void ScanTechs_leaves_everything_but_type_and_techs_empty()
    {
        var c = GamestateScanner.ScanTechs(Encoding.UTF8.GetBytes(TechPlayers + (char)10 + TechCountries)).Countries[0];

        Assert.Null(c.NameKey);
        Assert.Empty(c.NameVariables);
        Assert.Null(c.Adjective);
        Assert.Null(c.Flag);
        Assert.Equal(0, c.VictoryRank);
        Assert.Equal(0, c.MilitaryPower);
        Assert.Null(c.CachedDiploWeight);
        Assert.Empty(c.ContactedIds);
        Assert.Null(c.Holdings);
        Assert.Null(c.Roster);
        Assert.Null(c.Flags);
        Assert.Null(c.Ethics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScanTechs_finds_the_blocks_by_search_in_a_save_shaped_file(bool playersLast)
    {
        // Real saves: top-level keys after other lines, so the blocks are found by searching "\nplayer=" / "\ncountry=".
        var nl = (char)10 + "";
        var before = "version=" + '"' + "4.5.2" + '"' + nl + "planets={ 1={ name=" + '"' + "country=" + '"' + " } }" + nl;
        var text = playersLast
            ? before + TechCountries + nl + TechPlayers + nl + "fleet={ 0={ } }"
            : before + TechPlayers + nl + TechCountries + nl + "fleet={ 0={ } }";
        var data = Encoding.UTF8.GetBytes(text);

        var (players, countries) = GamestateScanner.ScanTechs(data);

        Assert.Equal(2, players.Count);
        Assert.Equal([0, 2], countries.Select(c => c.Id));
        var full = GamestateScanner.ScanWithGlobals(data).Countries;
        foreach (var c in countries) Assert.Equal(full.Single(f => f.Id == c.Id).Techs, c.Techs);
    }

    [Fact]
    public void ScanTechs_handles_a_player_block_after_the_countries()
    {
        var (players, countries) = GamestateScanner.ScanTechs(Encoding.UTF8.GetBytes(TechCountries + (char)10 + TechPlayers));

        Assert.Equal(2, players.Count);
        Assert.Equal([0, 2], countries.Select(c => c.Id));
        Assert.Equal(["tech_c"], countries[1].Techs);
    }

    [Fact]
    public void ScanTechs_without_players_or_with_broken_input_gives_no_countries()
    {
        Assert.Empty(GamestateScanner.ScanTechs(Encoding.UTF8.GetBytes(TechCountries)).Countries);
        Assert.Empty(GamestateScanner.ScanTechs([]).Players);
        var broken = TechPlayers + (char)10 + "country={ 0={ type=" + '"' + "default" + '"' + " tech_status={ technology=" + '"' + "tech_a";
        var (_, countries) = GamestateScanner.ScanTechs(Encoding.UTF8.GetBytes(broken));
        Assert.Equal([0], countries.Select(c => c.Id));
    }

    sealed class FlagComparer : IEqualityComparer<CountryFlag?>
    {
        public bool Equals(CountryFlag? a, CountryFlag? b) =>
            a is not null && b is not null && a.IconCategory == b.IconCategory && a.IconFile == b.IconFile
            && a.BackgroundCategory == b.BackgroundCategory && a.BackgroundFile == b.BackgroundFile && a.Colors.SequenceEqual(b.Colors);
        public int GetHashCode(CountryFlag? f) => 0;
    }
}

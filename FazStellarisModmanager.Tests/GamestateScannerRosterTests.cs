using System.Text;
using FazStellarisModmanager.Core.Saves;

namespace FazStellarisModmanager.Tests;

public class GamestateScannerRosterTests
{
    const string State = """
        player=
        {
        	{ name="Host" country=0 }
        }
        species_db=
        {
        	5={ traits={ trait="trait_wrong" } }
        	11580=
        	{
        		name={ key="The Better Fazbear" literal=yes }
        		traits=
        		{
        			trait="trait_psionic"
        			trait="trait_uncanny_intuition"
        		}
        		home_planet={ type=planet reference=1 }
        	}
        }
        country=
        {
        	0=
        	{
        		founder_species_ref=11580
        		government=
        		{
        			authority="auth_imperial"
        			council_positions={ 16777459 3858759680 323 }
        		}
        		owned_leaders={ 134217730 2281701410 }
        		victory_rank=1
        	}
        	1=
        	{
        		founder_species_ref=5
        		owned_leaders={ 77 }
        		victory_rank=2
        	}
        }
        leaders=
        {
        	134217730=
        	{
        		name={ full_names={ key="REP2_CHR_Zetioz" } }
        		country=0
        		location={ type=galactic_community assignment=none id=0 position=0 }
        		traits="leader_trait_shadow_broker"
        		level=10
        		bonus_skill_level=3
        	}
        	2281701410=
        	{
        		country=0
        		location={ type=planet id=287 }
        		level=4
        	}
        	900=
        	{
        		country=0
        		level=8
        		bonus_skill_level=1
        	}
        	77={ country=1 level=2 }
        }
        council_positions=
        {
        	council_positions=
        	{
        		3858759680=none
        		16777459=
        		{
        			country=0
        			type="councilor_secret_societies"
        			leader=900
        		}
        		323=
        		{
        			country=0
        			type="councilor_gestalt_x"
        		}
        		16777460={ country=1 type="councilor_state" leader=77 }
        	}
        }
        pop_factions=
        {
        	83886080=
        	{
        		country=0
        		type="the_masquerade_militarist"
        		name={ key="%ADJ%" variables={ { key="1" value={ key="X" } } } }
        		support_percent=0.3
        		support_power=238.43035
        		faction_approval=0.75
        		members={ 1 2 3 }
        	}
        	83886081={ country=1 type="prosperity" support_power=10 faction_approval=0.5 }
        }
        galactic_community=
        {
        	leader=0
        	members={ 0 1 }
        	council={ 0 }
        	passed={ }
        	empire=yes
        }
        """;

    static (List<SavePlayer> Players, List<SaveCountry> Countries, GalacticCommunity? Community, IReadOnlyDictionary<int, IReadOnlyList<string>> Megas) Scan() =>
        GamestateScanner.ScanAll(Encoding.UTF8.GetBytes(State));

    [Fact]
    public void Player_country_gets_owned_leaders_with_skill_and_location()
    {
        var r = Scan().Countries.Single(c => c.Id == 0).Roster!;
        var zetioz = r.Leaders.Single(l => l.Id == 134217730);
        Assert.Equal(10, zetioz.Level);
        Assert.Equal(3, zetioz.BonusSkillLevel);
        Assert.Equal(13, zetioz.Skill);
        Assert.Equal("galactic_community", zetioz.LocationType);
        var other = r.Leaders.Single(l => l.Id == 2281701410);
        Assert.Equal(4, other.Skill);
        Assert.Equal("planet", other.LocationType);
        Assert.Same(zetioz, r.Delegate);
    }

    [Fact]
    public void Player_country_gets_councilors_factions_and_founder_traits()
    {
        var r = Scan().Countries.Single(c => c.Id == 0).Roster!;
        Assert.Equal([new SaveCouncilor("councilor_secret_societies", 900), new SaveCouncilor("councilor_gestalt_x", null)], r.Councilors);
        // A councilor's leader is known even when it is not in owned_leaders.
        Assert.Equal(9, r.Leader(900)!.Skill);
        Assert.Equal([new SaveFaction("the_masquerade_militarist", 238.43035, 0.75)], r.Factions);
        Assert.Equal(["trait_psionic", "trait_uncanny_intuition"], r.FounderTraits);
    }

    [Fact]
    public void Only_player_countries_get_a_roster()
    {
        Assert.Null(Scan().Countries.Single(c => c.Id == 1).Roster);
    }

    [Fact]
    public void Community_has_its_leader_and_empire_flag()
    {
        var gc = Scan().Community!;
        Assert.Equal(0, gc.Leader);
        Assert.True(gc.Empire);
    }

    [Fact]
    public void Live_filter_strips_other_countries_rosters()
    {
        var (players, countries, _, _) = GamestateScanner.ScanAll(Encoding.UTF8.GetBytes(State.Replace("""{ name="Host" country=0 }""", """{ name="Host" country=0 } { name="Guest" country=1 }""")));
        countries = countries.Select(c => c with { ContactedIds = c.Id == 1 ? [0] : [1] }).ToList();
        var s = new GameSnapshot("Save", "2387.09.17", "v", "x.sav", DateTime.UtcNow, players, countries);
        Assert.NotNull(s.Countries.Single(c => c.Id == 0).Roster);
        Assert.NotNull(s.Countries.Single(c => c.Id == 1).Roster);
        var f = LiveFilter.For(s, 1);
        Assert.Null(f.Countries.Single(c => c.Id == 0).Roster);
        Assert.NotNull(f.Countries.Single(c => c.Id == 1).Roster);
    }
}

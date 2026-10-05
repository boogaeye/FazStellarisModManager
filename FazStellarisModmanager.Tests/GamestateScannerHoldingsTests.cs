using System.Text;
using FazStellarisModmanager.Core.Saves;

namespace FazStellarisModmanager.Tests;

public class GamestateScannerHoldingsTests
{
    const string State = """
        resolution=
        {
        	0={ type="resolution_galactic_focus_x" }
        	1={ type="resolution_mutualdefense_renegade_containment" }
        	2={ type="resolution_never_passed" }
        }
        country=
        {
        	0=
        	{
        		government=
        		{
        			type="gov_imperial_domain"
        			authority="auth_imperial"
        			civics={ "civic_secret_societies" "civic_galactic_sovereign" }
        			origin="origin_alderson"
        		}
        		traditions={ "tr_diplomacy_finish" "tr_politics_finish" }
        		ascension_perks={ "ap_lord_of_war" }
        		active_policies=
        		{
        			{ policy="diplomatic_stance" selected="diplo_stance_condescending_authority_advanced" date="2306.12.02" }
        			{ policy="war_philosophy" selected="unrestricted_wars" }
        		}
        		edicts={ { edict="diplomatic_grants" perpetual=yes } }
        		timed_modifier=
        		{
        			items=
        			{
        				{ modifier="galactic_market_founder" days=-1 }
        				{ multiplier=1.15 modifier="FW_x" days=-1 }
        			}
        		}
        		relics={ "r_ancient_sword" }
        		victory_rank=1
        		type="default"
        	}
        }
        megastructures=
        {
        	10={ type="interstellar_assembly_4" owner=0 }
        	11={ type="interstellar_assembly_restored" owner=0 }
        	12={ type="dyson_sphere_5" owner=7 }
        	13={ type="ring_world_0" owner=4294967295 }
        }
        galactic_community=
        {
        	members={ 0 1 7 }
        	council={ 0 1 }
        	passed={ 0 1 }
        }
        """;

    static (List<SavePlayer>, List<SaveCountry>, GalacticCommunity?, IReadOnlyDictionary<int, IReadOnlyList<string>>) Scan() =>
        GamestateScanner.ScanAll(Encoding.UTF8.GetBytes(State));

    [Fact]
    public void Reads_country_holdings()
    {
        var h = Scan().Item2.Single().Holdings!;
        Assert.Equal(["civic_secret_societies", "civic_galactic_sovereign"], h.Civics);
        Assert.Equal("origin_alderson", h.Origin);
        Assert.Equal("auth_imperial", h.Authority);
        Assert.Equal(["tr_diplomacy_finish", "tr_politics_finish"], h.Traditions);
        Assert.Equal(["ap_lord_of_war"], h.Perks);
        Assert.Equal(["diplo_stance_condescending_authority_advanced", "unrestricted_wars"], h.Policies);
        Assert.Equal(["diplomatic_grants"], h.Edicts);
        Assert.Equal(["r_ancient_sword"], h.Relics);
        Assert.Equal([new TimedModifier("galactic_market_founder", 1), new TimedModifier("FW_x", 1.15)], h.Timed);
    }

    [Fact]
    public void Reads_community_with_passed_types_and_owned_megastructures()
    {
        var (_, _, community, megas) = Scan();
        Assert.Equal([0, 1, 7], community!.Members);
        Assert.Equal([0, 1], community.Council);
        Assert.Equal(["resolution_galactic_focus_x", "resolution_mutualdefense_renegade_containment"], community.PassedResolutions);
        Assert.Equal(["interstellar_assembly_4", "interstellar_assembly_restored"], megas[0]);
        Assert.Equal(["dyson_sphere_5"], megas[7]);
        Assert.False(megas.ContainsKey(unchecked((int)4294967295)));
    }
}

using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Diplomacy;
using FazStellarisModmanager.Core.Saves;

namespace FazStellarisModmanager.Tests;

/// <summary>The research case: save 2387.09.17, country 0 (Galactic Emperor, delegate skill 17).</summary>
public class DiploCalculatorLeadersTests
{
    static readonly Dictionary<(DiploSource, string), DiploMods> Map = new()
    {
        [(DiploSource.StaticModifier, "galactic_community_delegate")] = new DiploMods(0, 0, 0, 0, 0, Delegate: 0.10),
        [(DiploSource.StaticModifier, "prototype_vir_core_modifier")] = new DiploMods(0, 0, 0, 0, 0, CouncilorSkill: 4),
        [(DiploSource.StaticModifier, "achievement_01")] = new DiploMods(0.05, 0, 0, 0, 0),
        [(DiploSource.Tradition, "tr_politics_gravitas")] = new DiploMods(0, 0, 0, 0, 0, Delegate: 0.025),
        [(DiploSource.Perk, "ap_shared_destiny")] = new DiploMods(0.2, 0, 0, 0, 0),
        [(DiploSource.PopFaction, "the_masquerade_militarist")] = new DiploMods(0.003, 0, 0, 0, 0),
        [(DiploSource.Councilor, "councilor_secret_societies")] = new DiploMods(0.01, 0, 0, 0, 0),
        [(DiploSource.SpeciesTrait, "trait_uncanny_intuition")] = new DiploMods(0, 0, 0, 0, 0, CouncilorSkill: 2),
    };

    static readonly PdxBlock EmperorOrCustodian = ParadoxScriptParser.Parse("OR = { is_galactic_custodian = yes is_galactic_emperor = yes }");

    static IReadOnlyList<TraditionSwap> Swaps(DiploSource s, string k) => (s, k) switch
    {
        (DiploSource.Tradition, "tr_politics_adopt") => [new TraditionSwap("tr_politics_adopt_advanced", EmperorOrCustodian, new DiploMods(0, 0, 0, 0, 0, Delegate: 0.025))],
        (DiploSource.Tradition, "tr_politics_gravitas") =>
        [
            new TraditionSwap("tr_politics_gravitas_custodian", ParadoxScriptParser.Parse("is_galactic_custodian = yes"), new DiploMods(0.10, 0, 0, 0, 0, Delegate: 0.025)),
            new TraditionSwap("tr_politics_gravitas_emperor", ParadoxScriptParser.Parse("is_galactic_emperor = yes"), new DiploMods(0, 0, 0, 0, 0, Delegate: 0.025)),
        ],
        _ => [],
    };

    static readonly Dictionary<string, string> Names = new()
    {
        ["galactic_community_delegate"] = "Galactic Community Delegate",
        ["tr_politics_adopt"] = "Politics Traditions",
        ["tr_politics_gravitas"] = "Gravitas",
        ["councilor_secret_societies"] = "Conspirator Liaison",
        ["ap_shared_destiny"] = "Shared Destiny",
        ["achievement_01"] = "Kardashev's Type 2",
    };

    static SaveCountry Country(CountryRoster? roster) =>
        new(0, "default", "Me", new Dictionary<string, string>(), null, null, 1, 0, 0, 0, 0, 0, 0, 0, null, [], [],
            CountryHoldings.Empty with
            {
                Traditions = ["tr_politics_adopt", "tr_politics_gravitas"],
                Perks = ["ap_shared_destiny"],
                Timed = [new TimedModifier("prototype_vir_core_modifier", 1), new TimedModifier("achievement_01", 1)],
            },
            roster);

    static CountryRoster Roster(int delegateSkill = 17) => new(
        [
            new SaveLeader(134217730, delegateSkill - 3, 3, "galactic_community"),
            new SaveLeader(2281701410, 4, 0, "planet"),
            new SaveLeader(900, 8, 1, null),
        ],
        [new SaveCouncilor("councilor_secret_societies", 900), new SaveCouncilor("councilor_empty", null)],
        [
            new SaveFaction("the_masquerade_militarist", 238.43035, 1),
            new SaveFaction("the_curtain_materialist", 351.7201, 1),
        ],
        ["trait_psionic", "trait_uncanny_intuition"]);

    static DiploBreakdown Compute(SaveCountry c, GalacticCommunity? gc) =>
        DiploCalculator.Compute(c, new GameSnapshot("S", "2387.09.17", "v", "x.sav", DateTime.UtcNow, [new SavePlayer("Me", 0)], [c], gc),
            (s, k) => Map.GetValueOrDefault((s, k)), DiploDefines.Vanilla, (s, k) => Names.GetValueOrDefault(k, k), swaps: Swaps);

    static readonly GalacticCommunity Imperium = new([0, 1], [0], [], Leader: 0, Empire: true);

    static double Line(DiploBreakdown b, string name) => Assert.Single(b.Overall, l => l.Name == name).Percent;

    [Fact]
    public void Reproduces_the_research_case()
    {
        var b = Compute(Country(Roster()), Imperium);
        Assert.Equal(1.70, Line(b, "Delegate"), 6);
        Assert.Equal(0.425, Line(b, "Politics Traditions"), 6);
        Assert.Equal(0.425, Line(b, "Gravitas"), 6);
        Assert.Equal(0.7152910, Line(b, "From Factions"), 6);
        Assert.Equal(0.15, Line(b, "Conspirator Liaison"), 6); // 0.01 × (9 + 4 + 2)
        Assert.Equal(0.20, Line(b, "Shared Destiny"), 6);
        Assert.Equal(0.05, Line(b, "Kardashev's Type 2"), 6);
    }

    [Fact]
    public void Without_a_delegate_delegate_bonuses_are_zero()
    {
        var roster = Roster() with { Leaders = Roster().Leaders.Where(l => l.LocationType != "galactic_community").ToList() };
        var b = Compute(Country(roster), Imperium);
        Assert.DoesNotContain(b.Overall, l => l.Name is "Delegate" or "Gravitas" or "Politics Traditions");
    }

    [Fact]
    public void Delegate_static_modifier_only_for_members()
    {
        var b = Compute(Country(Roster()), new GalacticCommunity([5], [], [], Leader: 5, Empire: true));
        Assert.DoesNotContain(b.Overall, l => l.Name == "Delegate");
        // Not the emperor: the base gravitas still has its delegate bonus, the adopt swap does not apply.
        Assert.Equal(0.425, Line(b, "Gravitas"), 6);
        Assert.DoesNotContain(b.Overall, l => l.Name == "Politics Traditions");
    }

    [Fact]
    public void Custodian_takes_the_custodian_swap()
    {
        var b = Compute(Country(Roster()), new GalacticCommunity([0], [0], [], Leader: 0, Empire: false));
        Assert.Equal(0.10 + 0.425, Line(b, "Gravitas"), 6);
        Assert.Equal(0.425, Line(b, "Politics Traditions"), 6);
    }

    [Fact]
    public void Without_a_roster_only_holdings_count()
    {
        var b = Compute(Country(null), Imperium);
        Assert.DoesNotContain(b.Overall, l => l.Name is "Delegate" or "From Factions" or "Conspirator Liaison");
        Assert.Equal(0.20, Line(b, "Shared Destiny"), 6);
    }

    [Fact]
    public void Councilor_skill_add_comes_from_holdings_and_founder_traits()
    {
        var roster = Roster() with { FounderTraits = [] };
        var c = Country(roster) with { Holdings = CountryHoldings.Empty };
        var b = Compute(c, Imperium);
        Assert.Equal(0.09, Line(b, "Conspirator Liaison"), 6);
    }
}

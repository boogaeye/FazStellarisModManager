using FazStellarisModmanager.Core.Diplomacy;
using FazStellarisModmanager.Core.Saves;

namespace FazStellarisModmanager.Tests;

public class DiploCalculatorTests
{
    sealed class FakeCatalog
    {
        public readonly Dictionary<(DiploSource, string), DiploMods> Map = [];
        public FakeCatalog Add(DiploSource s, string k, DiploMods m) { Map[(s, k)] = m; return this; }
    }

    static DiploMods O(double v) => new(v, 0, 0, 0, 0);

    static SaveCountry Me(CountryHoldings h) =>
        new(0, "default", "Me", new Dictionary<string, string>(), null, null, 1, 0, 644875181.10937, 2479558, 1761010, 0, 0, 399579, null, [], ["tech_xeno_diplomacy"], h);

    static GameSnapshot Snap(SaveCountry c, GalacticCommunity? gc = null, Dictionary<int, IReadOnlyList<string>>? megas = null) =>
        new("S", "2387.09.17", "v", "x.sav", DateTime.UtcNow, [new SavePlayer("Me", 0)], [c], gc, megas);

    static Func<DiploSource, string, DiploMods?> Lookup(FakeCatalog c) => (s, k) => c.Map.GetValueOrDefault((s, k));

    [Fact]
    public void Rebuilds_the_tooltip_base_parts_and_overall_bonuses()
    {
        var catalog = new FakeCatalog()
            .Add(DiploSource.Perk, "ap_galactic_force_projection", new DiploMods(0, 0.10, 0, 0, 0))
            .Add(DiploSource.Perk, "ap_lord_of_war", new DiploMods(0, 0.25, 0, 0, 0))
            .Add(DiploSource.Resolution, "resolution_mutualdefense_renegade_containment", new DiploMods(0, 1.0, 0, 0, 0))
            .Add(DiploSource.Resolution, "resolution_rulesofwar_guardian_angels", new DiploMods(0, -0.20, 0, 0, 0))
            .Add(DiploSource.StaticModifier, "galactic_market_founder", new DiploMods(0, 0, 0.15, 0, 0))
            .Add(DiploSource.Tradition, "tr_gigaengineering_mega_monuments", new DiploMods(0, 0, 0.20, 0.20, 0))
            .Add(DiploSource.Resolution, "resolution_commerce_underdeveloped_system_utilization", new DiploMods(0, 0, 0.60, 0, 0))
            .Add(DiploSource.Resolution, "resolution_industry_environmental_ordinance_waivers", new DiploMods(0, 0, 0.80, 0, 0))
            .Add(DiploSource.Resolution, "resolution_customs_treaty", new DiploMods(0, 0, 0.25, 0, 0))
            .Add(DiploSource.Resolution, "resolution_emperor_imperial_bank", new DiploMods(0, 0, 0.40, 0, 0))
            .Add(DiploSource.Resolution, "resolution_galacticstudies_astral_studies_network", new DiploMods(0, 0, 0, 0.40, 0))
            .Add(DiploSource.Civic, "civic_galactic_sovereign", O(0.40))
            .Add(DiploSource.Tech, "tech_xeno_diplomacy", O(0.10))
            .Add(DiploSource.Megastructure, "interstellar_assembly_4", O(0.40))
            .Add(DiploSource.StaticModifier, "council_member", new DiploMods(0, 0, 0, 0, 0, 0.20))
            .Add(DiploSource.Relic, "r_ancient_sword", O(0.10))
            .Add(DiploSource.Relic, "r_other", O(0.05));
        var holdings = CountryHoldings.Empty with
        {
            Civics = ["civic_galactic_sovereign"],
            Perks = ["ap_galactic_force_projection", "ap_lord_of_war"],
            Traditions = ["tr_gigaengineering_mega_monuments"],
            Relics = ["r_ancient_sword", "r_other"],
            Timed = [new TimedModifier("galactic_market_founder", 1)],
        };
        var gc = new GalacticCommunity([0, 5], [0], [
            "resolution_mutualdefense_renegade_containment", "resolution_rulesofwar_guardian_angels",
            "resolution_commerce_underdeveloped_system_utilization", "resolution_industry_environmental_ordinance_waivers",
            "resolution_customs_treaty", "resolution_emperor_imperial_bank", "resolution_galacticstudies_astral_studies_network"]);
        var me = Me(holdings);
        var b = DiploCalculator.Compute(me, Snap(me, gc, new() { [0] = ["interstellar_assembly_4", "interstellar_assembly_4"] }),
            Lookup(catalog), DiploDefines.Vanilla, (s, k) => k);

        Assert.Equal(16121879.5, b.Fleet.Base, 0);
        Assert.Equal(2.15, b.Fleet.Multiplier, 6);
        Assert.Equal(34662041, b.Fleet.Total, 0);
        Assert.Equal(371933.7, b.Economy.Base, 0);
        Assert.Equal(3.40, b.Economy.Multiplier, 6);
        Assert.Equal(1.60, b.Tech.Multiplier, 6);
        // overall: sovereign .4 + xeno diplomacy .1 + 2 assemblies .8 + council .2 + one relic line .15 = 1.65
        Assert.Equal(2.65, b.OverallMultiplier, 6);
        Assert.Equal(2, b.Overall.Count(l => l.Name == "interstellar_assembly_4"));
        Assert.Single(b.Overall, l => l.Name == "From Relics");
        Assert.True(b.Approximate);
        Assert.Equal(b.Real, b.InGame);
    }

    [Fact]
    public void Resolutions_and_council_only_for_members_and_negative_total_shows_zero_in_game()
    {
        var catalog = new FakeCatalog()
            .Add(DiploSource.Resolution, "res_a", O(0.5))
            .Add(DiploSource.StaticModifier, "council_member", new DiploMods(0, 0, 0, 0, 0, 0.2))
            .Add(DiploSource.Civic, "civic_bad", new DiploMods(0, -3, 0, 0, 0));
        var me = Me(CountryHoldings.Empty with { Civics = ["civic_bad"] });
        var outsider = new GalacticCommunity([5], [5], ["res_a"]);
        var b = DiploCalculator.Compute(me, Snap(me, outsider), Lookup(catalog), DiploDefines.Vanilla, (s, k) => k);
        Assert.Empty(b.Overall);
        Assert.True(b.Real < 0);
        Assert.Equal(0, b.InGame);
    }
}

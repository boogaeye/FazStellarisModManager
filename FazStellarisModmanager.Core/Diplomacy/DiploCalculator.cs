using FazStellarisModmanager.Core.Saves;

namespace FazStellarisModmanager.Core.Diplomacy;

/// <summary>One tooltip line: a source and its bonus as a fraction (0.25 = +25%).</summary>
public sealed record DiploBonus(string Name, double Percent);

/// <summary>A base part: Input × Factor = Base, then × (1 + Σ bonuses) = Total.</summary>
public sealed record DiploPart(double Input, double Factor, IReadOnlyList<DiploBonus> Bonuses)
{
    public double Base => Input * Factor;
    public double Multiplier => 1 + Bonuses.Sum(b => b.Percent);
    public double Total => Base * Multiplier;
}

/// <summary>Diplomatic weight laid out like the game's tooltip. Pops use the middle of the happiness range.</summary>
public sealed record DiploBreakdown(DiploPart Fleet, DiploPart Pops, DiploPart Economy, DiploPart Tech, IReadOnlyList<DiploBonus> Overall, bool Approximate)
{
    public const string NotCalculated = "Not calculated: leader, councilor and delegate bonuses, faction bonuses and other scripted sources.";
    public double BaseTotal => Fleet.Total + Pops.Total + Economy.Total + Tech.Total;
    public double OverallMultiplier => 1 + Overall.Sum(b => b.Percent);
    public double Real => BaseTotal * OverallMultiplier;
    /// <summary>The game shows a negative diplomatic weight as 0.</summary>
    public double InGame => Math.Max(0, Real);
}

public static class DiploCalculator
{
    // A later resolution of the same chain (category = second segment of resolution_<category>_<name>) replaces the earlier ones.
    // Keys without that shape count individually. Order of the result follows the surviving resolutions' passing order.
    static IEnumerable<string> LatestPerCategory(IReadOnlyList<string> passed)
    {
        string? Category(string key)
        {
            var parts = key.Split('_');
            return parts.Length >= 3 && parts[0] == "resolution" ? parts[1] : null;
        }
        var last = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < passed.Count; i++)
            if (Category(passed[i]) is { } cat) last[cat] = i;
        for (var i = 0; i < passed.Count; i++)
            if (Category(passed[i]) is not { } cat || last[cat] == i) yield return passed[i];
    }

    /// <param name="lookup">The catalog's Get (injectable for tests).</param>
    /// <param name="name">Display name for a source key (localisation, or the key).</param>
    public static DiploBreakdown Compute(SaveCountry c, GameSnapshot snapshot, Func<DiploSource, string, DiploMods?> lookup,
        DiploDefines defines, Func<DiploSource, string, string> name)
    {
        var lines = new List<(string Name, DiploMods Mods)>();
        void Add(DiploSource s, string? key, double multiplier = 1)
        {
            if (key is null || lookup(s, key) is not { } m || m.IsZero) return;
            lines.Add((name(s, key), m.Scale(multiplier)));
        }

        var h = c.Holdings ?? CountryHoldings.Empty;
        foreach (var k in h.Civics) Add(DiploSource.Civic, k);
        Add(DiploSource.Civic, h.Origin);
        Add(DiploSource.Authority, h.Authority);
        foreach (var k in h.Traditions) Add(DiploSource.Tradition, k);
        foreach (var k in h.Perks) Add(DiploSource.Perk, k);
        foreach (var k in h.Policies) Add(DiploSource.Policy, k);
        foreach (var k in h.Edicts) Add(DiploSource.Edict, k);
        foreach (var k in c.Techs) Add(DiploSource.Tech, k);
        foreach (var t in h.Timed) Add(DiploSource.StaticModifier, t.Key, t.Multiplier);

        var relics = h.Relics.Select(r => lookup(DiploSource.Relic, r)).OfType<DiploMods>().Aggregate(DiploMods.Zero, (a, b) => a + b);
        if (!relics.IsZero) lines.Add(("From Relics", relics));

        if (snapshot.Megastructures?.GetValueOrDefault(c.Id) is { } megas)
            foreach (var m in megas) Add(DiploSource.Megastructure, m);

        if (snapshot.Community is { } gc && gc.Members.Contains(c.Id))
        {
            foreach (var r in LatestPerCategory(gc.PassedResolutions)) Add(DiploSource.Resolution, r);
            if (gc.Council.Contains(c.Id)) Add(DiploSource.StaticModifier, "council_member");
        }

        var onCouncil = snapshot.Community is { } council && council.Council.Contains(c.Id);
        List<DiploBonus> Pick(Func<DiploMods, double> part) =>
            lines.Where(l => part(l.Mods) != 0).Select(l => new DiploBonus(l.Name, part(l.Mods))).ToList();

        return new DiploBreakdown(
            new DiploPart(c.MilitaryPower, defines.Naval, Pick(m => m.Naval)),
            new DiploPart(c.Pops, defines.PopBase * (1 + defines.PopHappiness * 0.5), Pick(m => m.Pops)),
            new DiploPart(c.EconomyPower, defines.Economy, Pick(m => m.Economy)),
            new DiploPart(c.TechPower, defines.Technology, Pick(m => m.Tech)),
            Pick(m => m.Overall + (onCouncil ? m.Council : 0)),
            Approximate: true);
    }
}

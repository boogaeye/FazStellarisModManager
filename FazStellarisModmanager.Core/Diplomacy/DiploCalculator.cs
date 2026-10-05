using FazStellarisModmanager.Core.Saves;

namespace FazStellarisModmanager.Core.Diplomacy;

/// <summary>One tooltip line: a source and its bonus as a fraction (0.25 = +25%).</summary>
public sealed record DiploBonus(string Name, double Percent)
{
    /// <summary>A bonus as the game shows it: whole percent truncated toward zero, with its sign (0.425 → "+42%").</summary>
    public static string FormatPercent(double fraction)
    {
        // Rounded first so that 0.29 × 100 = 28.999… still shows 29.
        var whole = Math.Truncate(Math.Round(fraction * 100, 6));
        return (fraction < 0 ? "-" : "+") + Math.Abs(whole).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }
}

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
    public const string NotCalculated = "Not calculated: leader traits, triggered modifiers and other scripted sources; faction approval is not applied.";
    public double BaseTotal => Fleet.Total + Pops.Total + Economy.Total + Tech.Total;
    public double OverallMultiplier => 1 + Overall.Sum(b => b.Percent);
    public double Real => BaseTotal * OverallMultiplier;
    /// <summary>The game shows a negative diplomatic weight as 0.</summary>
    public double InGame => Math.Max(0, Real);
}

public static class DiploCalculator
{
    // Targeted resolutions are skipped. In a single-active category only the last passed resolution (possibly a repeal) is active.
    static IEnumerable<string> ActiveResolutions(IReadOnlyList<string> passed,
        Func<string, (string Category, bool MultipleActive)?>? categoryOf, Func<string, bool>? isTargeted)
    {
        var kept = passed.Where(r => isTargeted?.Invoke(r) != true).ToList();
        if (categoryOf is null) return kept;
        var last = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < kept.Count; i++)
            if (categoryOf(kept[i]) is { MultipleActive: false } c) last[c.Category] = i;
        return kept.Where((r, i) => categoryOf(r) is not { MultipleActive: false } c || last[c.Category] == i);
    }

    /// <param name="lookup">The catalog's Get (injectable for tests).</param>
    /// <param name="resolutionCategory">Category of a resolution type; without it every passed resolution counts.</param>
    /// <param name="isTargeted">True for target = yes resolutions, which are skipped.</param>
    /// <param name="name">Display name for a source key (localisation, or the key).</param>
    /// <param name="swaps">Tradition swaps of a tradition or perk (the catalog's Swaps); without it the base definitions count.</param>
    public static DiploBreakdown Compute(SaveCountry c, GameSnapshot snapshot, Func<DiploSource, string, DiploMods?> lookup,
        DiploDefines defines, Func<DiploSource, string, string> name,
        Func<string, (string Category, bool MultipleActive)?>? resolutionCategory = null, Func<string, bool>? isTargeted = null,
        Func<DiploSource, string, IReadOnlyList<TraditionSwap>>? swaps = null)
    {
        var gc = snapshot.Community;
        var member = gc is not null && gc.Members.Contains(c.Id);
        var leads = gc is not null && gc.Leader == c.Id;
        bool emperor = leads && gc!.Empire, custodian = leads && !gc!.Empire;

        var lines = new List<(string Name, DiploMods Mods)>();
        void Add(DiploSource s, string? key, double multiplier = 1)
        {
            if (key is null) return;
            var mods = lookup(s, key);
            if (swaps?.Invoke(s, key).FirstOrDefault(x => x.Applies(emperor, custodian)) is { } swap) mods = swap.Mods;
            if (mods is not { } m || m.IsZero) return;
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

        var roster = c.Roster ?? CountryRoster.Empty;
        if (member)
        {
            foreach (var r in ActiveResolutions(gc!.PassedResolutions, resolutionCategory, isTargeted)) Add(DiploSource.Resolution, r);
            if (gc.Council.Contains(c.Id)) Add(DiploSource.StaticModifier, "council_member");
            // Every member with a delegate has this static modifier; the game names its line "Delegate".
            if (roster.Delegate is not null && lookup(DiploSource.StaticModifier, "galactic_community_delegate") is { IsZero: false } d)
                lines.Add(("Delegate", d));
        }

        // Councilors: modifier × (leader skill + councilor_skill_add from every source so far and the founder species' traits).
        var skillAdd = lines.Sum(l => l.Mods.CouncilorSkill)
                       + roster.FounderTraits.Sum(t => lookup(DiploSource.SpeciesTrait, t)?.CouncilorSkill ?? 0);
        foreach (var councilor in roster.Councilors)
        {
            if (councilor.LeaderId is not { } id || roster.Leader(id) is not { } leader
                || lookup(DiploSource.Councilor, councilor.Type) is not { IsZero: false } m) continue;
            lines.Add((name(DiploSource.Councilor, councilor.Type), m.Scale(leader.Skill + skillAdd) with { Delegate = m.Delegate, CouncilorSkill = 0 }));
        }

        // Pop factions: country_modifier × support power, as one line (approval is not applied).
        var factions = roster.Factions.Select(f => lookup(DiploSource.PopFaction, f.Type)?.Scale(f.SupportPower)).OfType<DiploMods>()
            .Aggregate(DiploMods.Zero, (a, b) => a + b);
        if (!factions.IsZero) lines.Add(("From Factions", factions));

        var onCouncil = gc is not null && gc.Council.Contains(c.Id);
        var delegateSkill = roster.Delegate?.Skill ?? 0;
        List<DiploBonus> Pick(Func<DiploMods, double> part) =>
            lines.Where(l => part(l.Mods) != 0).Select(l => new DiploBonus(l.Name, part(l.Mods))).ToList();

        return new DiploBreakdown(
            new DiploPart(c.MilitaryPower, defines.Naval, Pick(m => m.Naval)),
            new DiploPart(c.Pops, defines.PopBase * (1 + defines.PopHappiness * 0.5), Pick(m => m.Pops)),
            new DiploPart(c.EconomyPower, defines.Economy, Pick(m => m.Economy)),
            new DiploPart(c.TechPower, defines.Technology, Pick(m => m.Tech)),
            Pick(m => m.Overall + (onCouncil ? m.Council : 0) + m.Delegate * delegateSkill),
            Approximate: true);
    }
}

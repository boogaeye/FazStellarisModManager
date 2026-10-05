using System.Globalization;
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Saves;

/// <summary>The NGameplay DIPLOMACY_WEIGHT_* defines the game uses to compute diplomatic weight.</summary>
public sealed record DiploDefines(double Base, double Naval, double Economy, double Technology, double PopBase, double PopHappiness)
{
    public static DiploDefines Vanilla { get; } = new(0, 0.025, 0.15, 0.1, 0.01, 2.0);

    /// <summary>Reads common/defines from the sources in load order (game first): files load by file name, the last value wins; missing values stay vanilla.</summary>
    public static DiploDefines Load(IReadOnlyList<ContentSource> sources)
    {
        var files = sources
            .SelectMany((s, i) => s.Files("common/defines", ".txt").Select(rel => (Source: s, Index: i, Rel: rel)))
            .OrderBy(f => Path.GetFileName(f.Rel), StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Index)
            .ToList();
        var d = Vanilla;
        foreach (var f in files)
        {
            PdxBlock gameplay;
            try
            {
                if (ParadoxScriptParser.Parse(f.Source.ReadText(f.Rel)).GetBlock("NGameplay") is not { } g) continue;
                gameplay = g;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                continue;
            }
            d = d with
            {
                Base = Get(gameplay, "DIPLOMACY_WEIGHT_BASE") ?? d.Base,
                Naval = Get(gameplay, "DIPLOMACY_WEIGHT_NAVAL_FACTOR") ?? d.Naval,
                Economy = Get(gameplay, "DIPLOMACY_WEIGHT_ECONOMY_FACTOR") ?? d.Economy,
                Technology = Get(gameplay, "DIPLOMACY_WEIGHT_TECHNOLOGY_FACTOR") ?? d.Technology,
                PopBase = Get(gameplay, "DIPLOMACY_WEIGHT_POP_BASE") ?? d.PopBase,
                PopHappiness = Get(gameplay, "DIPLOMACY_WEIGHT_POP_HAPPINESS") ?? d.PopHappiness,
            };
        }
        return d;
    }

    static double? Get(PdxBlock b, string key) =>
        b.GetString(key) is { } v && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}

/// <summary>Diplomatic weight before percentage bonuses. Pops give between PopBase and PopBase + PopHappiness each, depending on happiness.</summary>
public sealed record DiploEstimate(double Base, double Naval, double Economy, double Tech, double PopsMin, double PopsMax)
{
    public double Min => Base + Naval + Economy + Tech + PopsMin;
    public double Max => Base + Naval + Economy + Tech + PopsMax;
}

public static class DiploWeight
{
    public static DiploEstimate Estimate(SaveCountry c, DiploDefines d) => new(
        d.Base,
        new StatValue(c.MilitaryPower).Real * d.Naval,
        new StatValue(c.EconomyPower).Real * d.Economy,
        new StatValue(c.TechPower).Real * d.Technology,
        c.Pops * d.PopBase,
        c.Pops * (d.PopBase + d.PopHappiness));
}

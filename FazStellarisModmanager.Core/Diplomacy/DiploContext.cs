using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Diplomacy;

/// <summary>Catalog, defines and display names for one mod list; Enrich computes breakdowns for the save's player countries.</summary>
public sealed class DiploContext(ModifierCatalog catalog, DiploDefines defines, Localisation? names)
{
    /// <summary>Slow (seconds on a large mod list): call on a background thread.</summary>
    public static DiploContext Load(IReadOnlyList<ContentSource> sources)
    {
        var warnings = new List<string>();
        Localisation? loc = null;
        try { loc = Localisation.Load(sources, warnings); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        return new DiploContext(ModifierCatalog.Load(sources), DiploDefines.Load(sources), loc);
    }

    public GameSnapshot Enrich(GameSnapshot s)
    {
        var map = new Dictionary<int, DiploBreakdown>();
        foreach (var p in s.Players)
            if (s.Countries.FirstOrDefault(c => c.Id == p.CountryId) is { } c)
                map[c.Id] = DiploCalculator.Compute(c, s, catalog.Get, defines, Name, catalog.ResolutionCategory, catalog.IsTargetedResolution);
        return s with { Diplo = map };
    }

    string Name(DiploSource source, string key) =>
        names?.Get(key) ?? names?.Get("modifier_" + key) ?? key.Replace('_', ' ');
}

namespace FazStellarisModmanager.Core.Saves;

/// <summary>A human player in a save: their Steam name and the country they play.</summary>
public sealed record SavePlayer(string Name, int CountryId);

/// <summary>A country's flag parts as stored in the save (drawn in part 3).</summary>
public sealed record CountryFlag(string? IconCategory, string? IconFile, string? BackgroundCategory, string? BackgroundFile, IReadOnlyList<string> Colors);

/// <summary>A timed (static) modifier on a country; Multiplier scales its values (1 when absent).</summary>
public sealed record TimedModifier(string Key, double Multiplier);

/// <summary>What a country has that can carry modifiers.</summary>
public sealed record CountryHoldings(
    IReadOnlyList<string> Civics,
    string? Origin,
    string? Authority,
    IReadOnlyList<string> Traditions,
    IReadOnlyList<string> Perks,
    IReadOnlyList<string> Policies,
    IReadOnlyList<string> Edicts,
    IReadOnlyList<string> Relics,
    IReadOnlyList<TimedModifier> Timed)
{
    public static CountryHoldings Empty { get; } = new([], null, null, [], [], [], [], [], []);
}

/// <summary>The Galactic Community: member and council country ids, and the types of passed resolutions.</summary>
public sealed record GalacticCommunity(IReadOnlyList<int> Members, IReadOnlyList<int> Council, IReadOnlyList<string> PassedResolutions);

/// <summary>The parts of a save's country the Live Game tab uses. Numbers are as stored (full precision, not wrapped).</summary>
public sealed record SaveCountry(
    int Id,
    string? Type,
    string? NameKey,
    IReadOnlyDictionary<string, string> NameVariables,
    string? Adjective,
    CountryFlag? Flag,
    int VictoryRank,
    double VictoryScore,
    double MilitaryPower,
    double EconomyPower,
    double TechPower,
    double FleetSize,
    double EmpireSize,
    double Pops,
    double? CachedDiploWeight,
    IReadOnlyList<int> ContactedIds,
    IReadOnlyList<string> Techs,
    CountryHoldings? Holdings = null);

/// <summary>What the Live Game tab knows about one save.</summary>
public sealed record GameSnapshot(
    string SaveName,
    string Date,
    string Version,
    string SavePath,
    DateTime SavedUtc,
    IReadOnlyList<SavePlayer> Players,
    IReadOnlyList<SaveCountry> Countries,
    GalacticCommunity? Community = null,
    IReadOnlyDictionary<int, IReadOnlyList<string>>? Megastructures = null);

namespace FazStellarisModmanager.Core.Saves;

/// <summary>A human player in a save: their Steam name and the country they play.</summary>
public sealed record SavePlayer(string Name, int CountryId);

/// <summary>A country's flag parts as stored in the save (drawn in part 3).</summary>
public sealed record CountryFlag(string? IconCategory, string? IconFile, string? BackgroundCategory, string? BackgroundFile, IReadOnlyList<string> Colors);

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
    IReadOnlyList<string> Techs);

/// <summary>What the Live Game tab knows about one save.</summary>
public sealed record GameSnapshot(
    string SaveName,
    string Date,
    string Version,
    string SavePath,
    DateTime SavedUtc,
    IReadOnlyList<SavePlayer> Players,
    IReadOnlyList<SaveCountry> Countries);

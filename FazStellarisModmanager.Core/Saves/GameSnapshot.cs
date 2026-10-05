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

/// <summary>A leader: skill is level + bonus_skill_level; LocationType is location.type (galactic_community for the GC delegate).</summary>
public sealed record SaveLeader(long Id, int Level, int BonusSkillLevel, string? LocationType)
{
    public int Skill => Level + BonusSkillLevel;
}

/// <summary>A filled or empty council position: the councilor type and its leader, if any.</summary>
public sealed record SaveCouncilor(string Type, long? LeaderId);

/// <summary>A pop faction of a country.</summary>
public sealed record SaveFaction(string Type, double SupportPower, double Approval);

/// <summary>
/// The people of a country that carry diplomatic weight: its owned leaders (plus its councilors' leaders), council
/// positions, pop factions and the traits of its founder species. Read for player countries only.
/// </summary>
public sealed record CountryRoster(
    IReadOnlyList<SaveLeader> Leaders,
    IReadOnlyList<SaveCouncilor> Councilors,
    IReadOnlyList<SaveFaction> Factions,
    IReadOnlyList<string> FounderTraits)
{
    public static CountryRoster Empty { get; } = new([], [], [], []);

    /// <summary>The Galactic Community delegate: the leader located in the galactic community.</summary>
    public SaveLeader? Delegate => Leaders.FirstOrDefault(l => l.LocationType == "galactic_community");

    public SaveLeader? Leader(long id) => Leaders.FirstOrDefault(l => l.Id == id);
}

/// <summary>
/// The Galactic Community: member and council country ids, and the types of passed resolutions. Leader is the
/// custodian's or emperor's country (galactic_community.leader); Empire is true once the Galactic Imperium exists.
/// </summary>
public sealed record GalacticCommunity(IReadOnlyList<int> Members, IReadOnlyList<int> Council, IReadOnlyList<string> PassedResolutions,
    int? Leader = null, bool Empire = false);

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
    CountryHoldings? Holdings = null,
    CountryRoster? Roster = null,
    IReadOnlyList<string>? Flags = null,
    IReadOnlyList<string>? Ethics = null);

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
    IReadOnlyDictionary<int, IReadOnlyList<string>>? Megastructures = null,
    IReadOnlyDictionary<int, FazStellarisModmanager.Core.Diplomacy.DiploBreakdown>? Diplo = null,
    IReadOnlyList<string>? GlobalFlags = null,
    IReadOnlyList<string>? Dlcs = null);

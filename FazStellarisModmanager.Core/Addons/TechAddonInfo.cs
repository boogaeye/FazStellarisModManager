namespace FazStellarisModmanager.Core.Addons;

/// <summary>
/// One tech of common/technology as the current list loads it (the last definition of the key). <see cref="Area"/> is as written (null when
/// absent); <see cref="Tier"/> is null when absent or not a number after resolving variables. <see cref="Icon"/> is the icon name, the key
/// when the definition has none.
/// </summary>
public sealed record TechInfo(string Key, string Name, string? Area, int? Tier, bool IsRepeatable, string Icon);

/// <summary>A tech category of common/technology/category, with its localised name (else the key).</summary>
public sealed record TechCategoryInfo(string Key, string Name);

/// <summary>
/// A regular ethic of common/ethics (or gestalt consciousness), with its localised name (else the key) and the key of its fanatic variant
/// when that ethic exists. <see cref="IconUri"/> stays null until <see cref="TechAddonCatalog.LoadIcons"/>.
/// </summary>
public sealed record EthicInfo(string Key, string Name, string? FanaticKey, string? IconUri = null);

/// <summary>
/// A modifier a tech can give. <see cref="Category"/> is as the game's modifiers.log lists it (e.g. "Countries, AI Economy"), null when the
/// list didn't come from the log. <see cref="Name"/> is the localised name (mod_&lt;key&gt;), null when there is none.
/// </summary>
public sealed record ModifierInfo(string Key, string? Category, string? Name);

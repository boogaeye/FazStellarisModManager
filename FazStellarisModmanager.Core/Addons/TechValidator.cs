using System.Text.RegularExpressions;

namespace FazStellarisModmanager.Core.Addons;

public enum TechIssueLevel { Error, Warning }

/// <summary>A problem with a custom tech. <see cref="Field"/> is the name of the <see cref="CustomTech"/> property it is about.</summary>
public sealed record TechIssue(TechIssueLevel Level, string Field, string Message);

/// <summary>
/// Checks custom techs against the current game (<see cref="TechAddonCatalog"/>). Errors depend only on the mod files and the settings, so
/// every player of a session gets the same ones; a tech with an error is left out of the build. Warnings may depend on this PC (its
/// modifiers.log) and never change the build.
/// </summary>
public static class TechValidator
{
    public const string KeyPrefix = "tech_";
    public const int MaxTier = 5;

    /// <summary>The tier the game shows repeatable techs at.</summary>
    public const int RepeatableTier = 5;

    public static readonly IReadOnlyList<string> Areas = ["physics", "society", "engineering"];

    static readonly Regex KeyPattern = new("^[a-z0-9_]+$", RegexOptions.Compiled);

    // Every modifier the game lists is a plain word of letters, digits and _.
    static readonly Regex ModifierKeyPattern = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    // modifiers.log categories whose modifiers a country's modifiers reach (vanilla techs use all of these); others, such as Federations,
    // Star Systems or Galactic Community, belong to something an empire's techs don't change.
    static readonly HashSet<string> EmpireCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Countries", "Pops", "Planets", "Colony", "Leaders", "Armies", "Starbases", "Waystations", "Ships", "Military Ships",
        "Civilian Ships", "Science Ships", "Transport Ships", "Orbital Stations", "Space Stations", "Ship Design Stats", "Owned Ships",
        "Fleets", "Economic Units", "AI Economy", "Habitability", "Espionage", "Megastructures", "Deposits", "Pop Factions", "All",
    };

    /// <summary>
    /// Every issue of <paramref name="tech"/>, errors first, as one of <paramref name="all"/> (the settings' techs; found by reference, else
    /// by value; a tech that isn't in the list is checked against all of it). A prerequisite can be another custom tech only when that one
    /// is buildable itself.
    /// </summary>
    public static IReadOnlyList<TechIssue> Validate(CustomTech tech, IReadOnlyList<CustomTech> all, TechAddonCatalog catalog)
    {
        var self = IndexOf(tech, all);
        var issues = Errors(tech, all, self, catalog);
        var buildable = BuildableFlags(all, catalog);
        foreach (var p in tech.Prerequisites ?? [])
        {
            if (catalog.FindTech(p) is not null) continue;
            var custom = Others(all, self).Where(i => SameKey(all[i].Key, p)).ToList();
            if (custom.Count > 0 && !custom.Any(i => buildable[i]))
                issues.Add(Error(nameof(CustomTech.Prerequisites),
                    $"Prerequisite {all[custom[0]].Name} ({p}) isn't added itself: it has errors, or the prerequisites go round in a circle."));
        }
        issues.AddRange(Warnings(tech, catalog));
        return issues;
    }

    /// <summary>The techs without errors, in settings order: what Faz Addons builds.</summary>
    public static IReadOnlyList<CustomTech> Buildable(IReadOnlyList<CustomTech> all, TechAddonCatalog catalog)
    {
        var buildable = BuildableFlags(all, catalog);
        return all.Where((_, i) => buildable[i]).ToList();
    }

    /// <summary>
    /// False when every category of a modifiers.log category list (e.g. "Federations") is one an empire's techs don't reach; true for an
    /// unknown category (null).
    /// </summary>
    public static bool AppliesToEmpire(string? category) =>
        category is null || category.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(EmpireCategories.Contains);

    // A tech is buildable when it has no errors of its own and each prerequisite is a tech of the mod list or a buildable custom tech.
    // Starting from none and adding techs until nothing changes leaves out techs that require each other.
    static bool[] BuildableFlags(IReadOnlyList<CustomTech> all, TechAddonCatalog catalog)
    {
        var clean = all.Select((t, i) => Errors(t, all, i, catalog).Count == 0).ToArray();
        var buildable = new bool[all.Count];
        for (var changed = true; changed;)
        {
            changed = false;
            for (var i = 0; i < all.Count; i++)
            {
                if (buildable[i] || !clean[i]) continue;
                var self = i;
                if (!(all[i].Prerequisites ?? []).All(p => catalog.FindTech(p) is not null || Others(all, self).Any(j => buildable[j] && SameKey(all[j].Key, p))))
                    continue;
                buildable[i] = true;
                changed = true;
            }
        }
        return buildable;
    }

    // The errors that don't depend on whether other custom techs are buildable.
    static List<TechIssue> Errors(CustomTech t, IReadOnlyList<CustomTech> all, int self, TechAddonCatalog catalog)
    {
        var issues = new List<TechIssue>();
        void Add(string field, string message) => issues.Add(Error(field, message));

        if (t.Key.Length == 0)
        {
            Add(nameof(CustomTech.Key), "The key is empty.");
        }
        else
        {
            if (!KeyPattern.IsMatch(t.Key)) Add(nameof(CustomTech.Key), "The key may only contain lower-case letters, digits and _.");
            if (!t.Key.StartsWith(KeyPrefix, StringComparison.Ordinal)) Add(nameof(CustomTech.Key), $"The key must start with {KeyPrefix}.");
            if (catalog.FindTech(t.Key) is { } existing) Add(nameof(CustomTech.Key), $"The key is already used by {existing.Name} in the mod list.");
            if (Others(all, self).Any(i => SameKey(all[i].Key, t.Key))) Add(nameof(CustomTech.Key), "Another custom technology uses this key.");
        }
        if (string.IsNullOrWhiteSpace(t.Name)) Add(nameof(CustomTech.Name), "The name is empty.");
        if (!Areas.Contains(t.Area, StringComparer.Ordinal)) Add(nameof(CustomTech.Area), "The area must be physics, society or engineering.");
        if (catalog.FindCategory(t.Category) is null) Add(nameof(CustomTech.Category), $"Unknown category '{t.Category}'.");
        if (t.Tier is < 0 or > MaxTier) Add(nameof(CustomTech.Tier), $"The tier must be between 0 and {MaxTier}.");
        if (!catalog.HasIcon(t.Icon)) Add(nameof(CustomTech.Icon), t.Icon.Length == 0 ? "No icon is chosen." : $"Unknown icon '{t.Icon}'.");
        foreach (var p in t.Prerequisites ?? [])
            if (catalog.FindTech(p) is null && !Others(all, self).Any(i => SameKey(all[i].Key, p)))
                Add(nameof(CustomTech.Prerequisites), $"Unknown prerequisite '{p}': no technology of the mod list or custom technology has that key.");
        if (t.Cost <= 0) Add(nameof(CustomTech.Cost), "The cost must be above 0.");
        if (t.Repeatable && t.CostPerLevel < 0) Add(nameof(CustomTech.CostPerLevel), "The cost per level can't be negative.");
        if (t.Weight < 0) Add(nameof(CustomTech.Weight), "The weight can't be negative.");
        foreach (var e in t.Ethics ?? [])
            if (catalog.FindEthic(e) is null) Add(nameof(CustomTech.Ethics), $"Unknown ethic '{e}'.");
        foreach (var m in t.Modifiers ?? [])
        {
            if (!ModifierKeyPattern.IsMatch(m.Key))
                Add(nameof(CustomTech.Modifiers), m.Key.Length == 0 ? "An effect has no modifier." : $"'{m.Key}' isn't a valid modifier key.");
            else if (!double.IsFinite(m.Value))
                Add(nameof(CustomTech.Modifiers), $"The value of {m.Key} isn't a number.");
        }
        return issues;
    }

    static IEnumerable<TechIssue> Warnings(CustomTech t, TechAddonCatalog catalog)
    {
        var modifiers = t.Modifiers ?? [];
        if (modifiers.Count == 0) yield return Warning(nameof(CustomTech.Modifiers), "The technology has no effects.");
        foreach (var m in modifiers)
        {
            if (!ModifierKeyPattern.IsMatch(m.Key)) continue; // an error already
            var info = catalog.FindModifier(m.Key);
            if (info is null)
                yield return Warning(nameof(CustomTech.Modifiers), catalog.ModifiersFromLog
                    ? $"{m.Key} was not found in this PC's modifier list (modifiers.log)."
                    : $"{m.Key} couldn't be checked: this PC has no modifier list (modifiers.log).");
            else if (catalog.ModifiersFromLog && !AppliesToEmpire(info.Category))
                yield return Warning(nameof(CustomTech.Modifiers), $"{m.Key} ({info.Category}) may not apply to an empire.");
            if (Math.Round(m.Value, 4) == 0) yield return Warning(nameof(CustomTech.Modifiers), $"{m.Key} is 0, so it changes nothing.");
        }
        if (t.Repeatable && t.Tier < RepeatableTier)
            yield return Warning(nameof(CustomTech.Tier), $"The game shows repeatable technologies at tier {RepeatableTier}.");
    }

    static TechIssue Error(string field, string message) => new(TechIssueLevel.Error, field, message);

    static TechIssue Warning(string field, string message) => new(TechIssueLevel.Warning, field, message);

    static IEnumerable<int> Others(IReadOnlyList<CustomTech> all, int self) => Enumerable.Range(0, all.Count).Where(i => i != self);

    static bool SameKey(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static int IndexOf(CustomTech tech, IReadOnlyList<CustomTech> all)
    {
        for (var i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], tech)) return i;
        for (var i = 0; i < all.Count; i++)
            if (all[i] == tech) return i;
        return -1;
    }
}

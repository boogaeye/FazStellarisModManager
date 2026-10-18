using System.Globalization;
using System.Text;

namespace FazStellarisModmanager.Core.Addons;

/// <summary>A tech that can be a prerequisite: one of the game's (<see cref="Custom"/> false) or another custom tech. <see cref="Icon"/> is the icon name.</summary>
public sealed record TechChoice(string Key, string Name, string Icon, bool Custom);

/// <summary>What a search shows: at most the asked number of <see cref="Items"/>, out of <see cref="Total"/> matches.</summary>
public sealed record SearchResult<T>(IReadOnlyList<T> Items, int Total)
{
    /// <summary>The matches not shown.</summary>
    public int More => Total - Items.Count;
}

/// <summary>A tech effect as the game shows it: the signed value ("+1", "−10%") and the modifier's localised name (else its key).</summary>
public sealed record EffectText(string Value, string Name)
{
    public override string ToString() => Value + " " + Name;
}

/// <summary>What a tech requires, by name: the ethics (any of them) and the prerequisite techs.</summary>
public sealed record TechRequirements(IReadOnlyList<string> Ethics, IReadOnlyList<string> Techs)
{
    public bool IsEmpty => Ethics.Count == 0 && Techs.Count == 0;

    /// <summary>"Requires: Authoritarian or Militarist ethic · Galactic Administration"; empty when there are none.</summary>
    public string Text
    {
        get
        {
            var parts = new List<string>();
            if (Ethics.Count > 0) parts.Add(string.Join(" or ", Ethics) + " ethic");
            parts.AddRange(Techs);
            return parts.Count == 0 ? "" : "Requires: " + string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Display and edit helpers for the Technologies addon on the Addons tab. Pure: the page saves the edited settings through
/// <see cref="AddonService.Save"/>. Techs are edited by their index in the settings' list.
/// </summary>
public static class TechnologiesView
{
    /// <summary>What generated keys start with.</summary>
    public const string KeyPrefix = "tech_faz_";

    /// <summary>The name of a tech made with "New technology".</summary>
    public const string NewName = "New technology";

    /// <summary>The most icons the picker shows.</summary>
    public const int MaxIcons = 60;

    /// <summary>The most modifiers or techs a search shows.</summary>
    public const int MaxResults = 30;

    /// <summary>The level the cost hint is for.</summary>
    public const int HintLevel = 10;

    const string Minus = "−";

    // Generated keys use this when the name has no letters or digits of a-z and 0-9.
    const string FallbackSlug = "technology";

    // ---- Keys and names ----

    /// <summary>
    /// The name in lower case with accents removed and apostrophes dropped; every run of other characters than a-z and 0-9 becomes one _, and
    /// none starts or ends it ("Emperor's Will 2" gives "emperors_will_2"). Empty when nothing is left.
    /// </summary>
    public static string Slug(string name)
    {
        var text = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || c == (char)39 || c == (char)0x2019) continue;
            var keep = c is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (keep) text.Append(c);
            else if (text.Length > 0 && text[^1] != '_') text.Append('_');
        }
        return text.ToString().TrimEnd('_');
    }

    /// <summary>The key generated for a name: <see cref="KeyPrefix"/> plus its <see cref="Slug"/> (tech_faz_technology without one).</summary>
    public static string KeyFor(string name) => KeyPrefix + (Slug(name) is { Length: > 0 } slug ? slug : FallbackSlug);

    /// <summary>True while <paramref name="key"/> is the one generated for <paramref name="name"/>, possibly made unique (…_2, …_3): renaming then changes it too.</summary>
    public static bool IsAutoKey(string key, string name)
    {
        var generated = KeyFor(name);
        if (key == generated) return true;
        if (!key.StartsWith(generated + "_", StringComparison.Ordinal)) return false;
        var number = key[(generated.Length + 1)..];
        return number.Length is > 0 and < 10 && number[0] != '0' && number.All(char.IsAsciiDigit) && int.Parse(number, CultureInfo.InvariantCulture) >= 2;
    }

    /// <summary><paramref name="key"/>, else with _2, _3… added: the first that no tech of <paramref name="catalog"/> nor of <paramref name="others"/> uses (any case).</summary>
    public static string UniqueKey(string key, IEnumerable<CustomTech> others, TechAddonCatalog catalog)
    {
        var taken = others.Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Free(string k) => !taken.Contains(k) && catalog.FindTech(k) is null;
        if (Free(key)) return key;
        for (var n = 2; ; n++)
            if (Free(key + "_" + n.ToString(CultureInfo.InvariantCulture))) return key + "_" + n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The icon of a new tech: the first icon named repeatable_…, else the first icon (empty without icons).</summary>
    public static string DefaultIcon(TechAddonCatalog catalog) =>
        catalog.Icons.FirstOrDefault(i => i.StartsWith("repeatable_", StringComparison.OrdinalIgnoreCase)) ?? catalog.Icons.FirstOrDefault() ?? "";

    /// <summary>
    /// A tech with the defaults (the base game's repeatables'), named <see cref="NewName"/>, with a unique generated key and the
    /// <see cref="DefaultIcon"/>. Its category is statecraft, or the catalog's first one when the game has no statecraft.
    /// </summary>
    public static CustomTech NewTech(IReadOnlyList<CustomTech> all, TechAddonCatalog catalog)
    {
        var defaults = new CustomTech("", NewName, "", DefaultIcon(catalog));
        var category = catalog.FindCategory(defaults.Category) is null && catalog.Categories.Count > 0 ? catalog.Categories[0].Key : defaults.Category;
        return defaults with { Key = UniqueKey(KeyFor(NewName), all, catalog), Category = category };
    }

    /// <summary>The tech with another name; a generated key (see <see cref="IsAutoKey"/>) follows it, a key edited by hand is kept.</summary>
    public static CustomTech Rename(CustomTech tech, string name, IEnumerable<CustomTech> others, TechAddonCatalog catalog) =>
        IsAutoKey(tech.Key, tech.Name)
            ? tech with { Name = name, Key = UniqueKey(KeyFor(name), others, catalog) }
            : tech with { Name = name };

    // ---- Editing the settings ----

    /// <summary>The settings' custom techs (none when unset).</summary>
    public static IReadOnlyList<CustomTech> Techs(AddonSettings settings) => settings.Technologies?.Techs ?? [];

    /// <summary>A copy with the Technologies settings edited (their defaults when unset).</summary>
    public static AddonSettings EditTechnologies(AddonSettings settings, Func<TechnologiesSettings, TechnologiesSettings> edit) =>
        settings with { Technologies = edit(settings.Technologies ?? new TechnologiesSettings()) };

    /// <summary>A copy with <paramref name="tech"/> added last.</summary>
    public static AddonSettings AddTech(AddonSettings settings, CustomTech tech) =>
        EditTechnologies(settings, t => t with { Techs = [.. t.Techs ?? [], tech] });

    /// <summary>A copy with the tech at <paramref name="index"/> edited; the same settings when there is none.</summary>
    public static AddonSettings EditTech(AddonSettings settings, int index, Func<CustomTech, CustomTech> edit)
    {
        var techs = Techs(settings);
        if (index < 0 || index >= techs.Count) return settings;
        var list = techs.ToList();
        list[index] = edit(list[index]);
        return EditTechnologies(settings, t => t with { Techs = list });
    }

    /// <summary>A copy without the tech at <paramref name="index"/> (no techs left gives null); the same settings when there is none.</summary>
    public static AddonSettings RemoveTech(AddonSettings settings, int index)
    {
        var techs = Techs(settings);
        if (index < 0 || index >= techs.Count) return settings;
        var list = techs.Where((_, i) => i != index).ToList();
        return EditTechnologies(settings, t => t with { Techs = list.Count == 0 ? null : list });
    }

    /// <summary>The tech with the ethic added, or removed when it has it (no ethics left gives null).</summary>
    public static CustomTech ToggleEthic(CustomTech tech, string ethic)
    {
        var ethics = (tech.Ethics ?? []).ToList();
        if (ethics.RemoveAll(e => string.Equals(e, ethic, StringComparison.OrdinalIgnoreCase)) == 0) ethics.Add(ethic);
        return tech with { Ethics = ethics.Count == 0 ? null : ethics };
    }

    /// <summary>The tech with the prerequisite added last; the same tech when it has it already (any case).</summary>
    public static CustomTech AddPrerequisite(CustomTech tech, string key) =>
        (tech.Prerequisites ?? []).Contains(key, StringComparer.OrdinalIgnoreCase) ? tech : tech with { Prerequisites = [.. tech.Prerequisites ?? [], key] };

    /// <summary>The tech without the prerequisite (none left gives null).</summary>
    public static CustomTech RemovePrerequisite(CustomTech tech, string key)
    {
        var list = (tech.Prerequisites ?? []).Where(p => !string.Equals(p, key, StringComparison.OrdinalIgnoreCase)).ToList();
        return tech with { Prerequisites = list.Count == 0 ? null : list };
    }

    /// <summary>The tech with an effect added last.</summary>
    public static CustomTech AddModifier(CustomTech tech, string key, double value = 1) =>
        tech with { Modifiers = [.. tech.Modifiers ?? [], new TechModifier(key, value)] };

    /// <summary>The tech with the effect at <paramref name="index"/> replaced; the same tech when there is none.</summary>
    public static CustomTech SetModifier(CustomTech tech, int index, TechModifier modifier)
    {
        var list = (tech.Modifiers ?? []).ToList();
        if (index < 0 || index >= list.Count) return tech;
        list[index] = modifier;
        return tech with { Modifiers = list };
    }

    /// <summary>The tech without the effect at <paramref name="index"/> (none left gives null); the same tech when there is none.</summary>
    public static CustomTech RemoveModifier(CustomTech tech, int index)
    {
        var list = (tech.Modifiers ?? []).ToList();
        if (index < 0 || index >= list.Count) return tech;
        list.RemoveAt(index);
        return tech with { Modifiers = list.Count == 0 ? null : list };
    }

    // ---- Display ----

    /// <summary>"Physics", "Society" or "Engineering"; another area as written.</summary>
    public static string AreaName(string area) => area switch
    {
        "physics" => "Physics",
        "society" => "Society",
        "engineering" => "Engineering",
        _ => area,
    };

    /// <summary>The category's localised name, else the key.</summary>
    public static string CategoryName(string key, TechAddonCatalog catalog) => catalog.FindCategory(key)?.Name ?? key;

    /// <summary>"Society · Statecraft · Repeatable" or "Engineering · Industry · Tier 3".</summary>
    public static string Subtitle(CustomTech tech, TechAddonCatalog catalog) =>
        AreaName(tech.Area) + " · " + CategoryName(tech.Category, catalog) + " · "
        + (tech.Repeatable ? "Repeatable" : "Tier " + tech.Tier.ToString(CultureInfo.InvariantCulture));

    /// <summary>The research cost at a level: the cost plus the cost per level for each level after the first (repeatables only).</summary>
    public static long CostAt(CustomTech tech, int level) =>
        tech.Repeatable ? tech.Cost + (long)Math.Max(level - 1, 0) * tech.CostPerLevel : tech.Cost;

    /// <summary>A whole number with thousands separators ("50,000").</summary>
    public static string Thousands(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The cost as the research screen shows it: ("50,000", "research (+5,000 per level)"); the part in brackets only for repeatables that cost more per level.</summary>
    public static (string Amount, string Text) CostText(CustomTech tech) =>
        (Thousands(tech.Cost), tech.Repeatable && tech.CostPerLevel != 0
            ? "research (" + (tech.CostPerLevel > 0 ? "+" : Minus) + Thousands(Math.Abs((long)tech.CostPerLevel)) + " per level)"
            : "research");

    /// <summary>"Level 10 costs 95,000." for repeatables; null for other techs.</summary>
    public static string? LevelHint(CustomTech tech, int level = HintLevel) =>
        tech.Repeatable ? $"Level {level.ToString(CultureInfo.InvariantCulture)} costs {Thousands(CostAt(tech, level))}." : null;

    /// <summary>A number with its sign and up to 4 decimals ("+1", "−2.5"); "0" for zero.</summary>
    public static string Signed(double value)
    {
        var rounded = Math.Round(value, 4);
        if (rounded == 0) return "0";
        var text = Math.Abs(rounded).ToString("0.####", CultureInfo.InvariantCulture);
        return (rounded > 0 ? "+" : Minus) + text;
    }

    /// <summary>
    /// An effect as the game shows it: modifiers named …_mult are percentages ("+25%"), others plain numbers ("+1"); the name is the
    /// modifier's localised name, else its key.
    /// </summary>
    public static EffectText Effect(TechModifier modifier, TechAddonCatalog catalog)
    {
        var value = modifier.Key.EndsWith("_mult", StringComparison.OrdinalIgnoreCase)
            ? Percent(modifier.Value)
            : Signed(modifier.Value);
        return new EffectText(value, ModifierName(modifier.Key, catalog));
    }

    /// <summary>The modifier's localised name, else its key.</summary>
    public static string ModifierName(string key, TechAddonCatalog catalog) => catalog.FindModifier(key)?.Name is { Length: > 0 } name ? name : key;

    /// <summary>The first effect for a tech's card, with " / level" for repeatables; null without effects.</summary>
    public static string? FirstEffect(CustomTech tech, TechAddonCatalog catalog) =>
        tech.Modifiers is { Count: > 0 } modifiers ? Effect(modifiers[0], catalog) + (tech.Repeatable ? " / level" : "") : null;

    /// <summary>
    /// A prerequisite by key (any case): the game's tech, else another custom tech than the one at <paramref name="self"/> (its name, else
    /// its key); null when neither has that key.
    /// </summary>
    public static TechChoice? FindPrerequisite(string key, IReadOnlyList<CustomTech> all, int self, TechAddonCatalog catalog)
    {
        if (catalog.FindTech(key) is { } tech) return new TechChoice(tech.Key, tech.Name, tech.Icon, Custom: false);
        for (var i = 0; i < all.Count; i++)
            if (i != self && string.Equals(all[i].Key, key, StringComparison.OrdinalIgnoreCase)) return Choice(all[i]);
        return null;
    }

    /// <summary>The names of the ethics (else keys) and prerequisites (see <see cref="FindPrerequisite"/>; unknown ones by key) of the tech at <paramref name="self"/>.</summary>
    public static TechRequirements Requirements(CustomTech tech, IReadOnlyList<CustomTech> all, int self, TechAddonCatalog catalog) =>
        new((tech.Ethics ?? []).Select(e => catalog.FindEthic(e)?.Name ?? e).ToList(),
            (tech.Prerequisites ?? []).Select(p => FindPrerequisite(p, all, self, catalog)?.Name ?? p).ToList());

    /// <summary>The badge of a tech's card: red (Error) when it has errors, yellow (Warning) when it only has warnings, none without issues.</summary>
    public static TechIssueLevel? Badge(IReadOnlyList<TechIssue> issues) =>
        issues.Any(i => i.Level == TechIssueLevel.Error) ? TechIssueLevel.Error
        : issues.Count > 0 ? TechIssueLevel.Warning
        : null;

    // ---- Searches ----

    /// <summary>
    /// The icons whose name contains the search (any case; all for an empty search), in the catalog's order but with
    /// <paramref name="selected"/> first when it matches, at most <paramref name="max"/>.
    /// </summary>
    public static SearchResult<string> SearchIcons(TechAddonCatalog catalog, string? search, string? selected, int max = MaxIcons)
    {
        var text = search?.Trim() ?? "";
        var matches = catalog.Icons.Where(i => i.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        var first = matches.FindIndex(i => string.Equals(i, selected, StringComparison.OrdinalIgnoreCase));
        if (first > 0)
        {
            var icon = matches[first];
            matches.RemoveAt(first);
            matches.Insert(0, icon);
        }
        return new SearchResult<string>(matches.Take(max).ToList(), matches.Count);
    }

    /// <summary>
    /// The modifiers whose key or name holds every word of the search (words split at spaces and _, any case), at most <paramref name="max"/>;
    /// nothing for an empty search. Best first: the key or name equal to the search, then starting with it, then the rest; within each,
    /// modifiers that apply to an empire (see <see cref="TechValidator.AppliesToEmpire"/>) come first, then those with a localised name,
    /// in the catalog's order. A single pass, as modifiers.log can list over 100,000 modifiers.
    /// </summary>
    public static SearchResult<ModifierInfo> SearchModifiers(TechAddonCatalog catalog, string? search, int max = MaxResults)
    {
        var query = Query.Of(search);
        if (query is null || max <= 0) return new SearchResult<ModifierInfo>([], 0);

        // Bucket = rank, then not for an empire, then unnamed; each bucket keeps only the first max, which is all the result can use.
        var buckets = new List<ModifierInfo>[Query.Ranks * 4];
        var empire = new Dictionary<string, bool>(StringComparer.Ordinal); // per category string; the log has few
        var total = 0;
        foreach (var m in catalog.Modifiers)
        {
            var rank = query.Rank(m.Key, m.Name);
            if (rank < 0) continue;
            total++;
            var applies = m.Category is not { } category
                || (empire.TryGetValue(category, out var known) ? known : empire[category] = TechValidator.AppliesToEmpire(category));
            var bucket = (buckets[rank * 4 + (applies ? 0 : 2) + (m.Name is null ? 1 : 0)] ??= []);
            if (bucket.Count < max) bucket.Add(m);
        }
        var items = buckets.Where(b => b is not null).SelectMany(b => b).Take(max).ToList();
        return new SearchResult<ModifierInfo>(items, total);
    }

    /// <summary>
    /// The techs the tech at <paramref name="self"/> can require that match the search as in <see cref="SearchModifiers"/>: the game's techs,
    /// then the other custom techs (keys the game has are left out), without the ones it requires already; nothing for an empty search.
    /// </summary>
    public static SearchResult<TechChoice> SearchTechs(TechAddonCatalog catalog, IReadOnlyList<CustomTech> all, int self, string? search, int max = MaxResults)
    {
        var query = Query.Of(search);
        if (query is null) return new SearchResult<TechChoice>([], 0);
        var chosen = new HashSet<string>(self >= 0 && self < all.Count ? all[self].Prerequisites ?? [] : [], StringComparer.OrdinalIgnoreCase);
        var custom = all
            .Where((t, i) => i != self && t.Key.Length > 0 && catalog.FindTech(t.Key) is null)
            .Select(Choice);
        var matches = catalog.Techs.Select(t => new TechChoice(t.Key, t.Name, t.Icon, Custom: false))
            .Concat(custom)
            .Where(t => !chosen.Contains(t.Key))
            .DistinctBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .Select(t => (Tech: t, Rank: query.Rank(t.Key, t.Name)))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank)
            .Select(x => x.Tech)
            .ToList();
        return new SearchResult<TechChoice>(matches.Take(max).ToList(), matches.Count);
    }

    static TechChoice Choice(CustomTech tech) => new(tech.Key, tech.Name.Trim().Length > 0 ? tech.Name : tech.Key, tech.Icon, Custom: true);

    static string Percent(double fraction)
    {
        var percent = Math.Round(fraction * 100, 2);
        if (percent == 0) return "0%";
        return (percent > 0 ? "+" : Minus) + Math.Abs(percent).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }

    // A search of keys and names: every word must be in the key or the name.
    sealed class Query
    {
        /// <summary>How many ranks <see cref="Rank"/> gives to matches.</summary>
        public const int Ranks = 3;

        readonly string[] _words;
        readonly string _asKey;
        readonly string _asName;

        Query(string text)
        {
            _words = text.Split([' ', '_'], StringSplitOptions.RemoveEmptyEntries);
            _asKey = string.Join("_", _words);
            _asName = string.Join(" ", _words);
        }

        public static Query? Of(string? search) =>
            search?.Trim() is { Length: > 0 } text && new Query(text) is { _words.Length: > 0 } query ? query : null;

        // 0: the key or name is the search; 1: it starts with it; 2: holds every word; -1: no match.
        public int Rank(string key, string? name)
        {
            name ??= "";
            foreach (var word in _words)
                if (!key.Contains(word, StringComparison.OrdinalIgnoreCase) && !name.Contains(word, StringComparison.OrdinalIgnoreCase)) return -1;
            if (key.Equals(_asKey, StringComparison.OrdinalIgnoreCase) || name.Equals(_asName, StringComparison.OrdinalIgnoreCase)) return 0;
            if (key.StartsWith(_asKey, StringComparison.OrdinalIgnoreCase) || name.StartsWith(_asName, StringComparison.OrdinalIgnoreCase)) return 1;
            return 2;
        }
    }
}

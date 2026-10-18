using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Addons;

/// <summary>
/// What the Technologies addon reads from the current list: its techs (whose keys custom techs must not reuse, and which they can require),
/// the tech icons, categories and ethics, and the modifiers a tech can give. Built from the base game and the list's mods in load order,
/// never from Faz Addons itself. What <see cref="TechValidator"/> errors depend on (keys and icon file names) comes from the mod files
/// alone; names, ethic icons and the modifier list are extras that differ per PC. Immutable apart from the memo of loaded tech icons;
/// <see cref="LoadIcons"/> returns a copy with icons.
/// </summary>
public sealed class TechAddonCatalog
{
    public const string CategoryFolder = "common/technology/category";
    public const string EthicsFolder = "common/ethics";
    public const string EthicIconFolder = "gfx/interface/icons/ethics";
    public const string FanaticPrefix = "ethic_fanatic_";
    public const string GestaltEthic = "ethic_gestalt_consciousness";

    // modifiers.log lists one "- key, Category: …" line per modifier after this line.
    const string LogMarker = "Printing Modifier Definitions:";
    static readonly Regex LogLine = new(@"^- ([^\s,]+), Category: (.*)$", RegexOptions.Compiled);

    // Icon names a tech can use unquoted ("icon = <name>").
    static readonly Regex IconName = new("^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    readonly Dictionary<string, TechInfo> _techs;
    readonly HashSet<string> _icons;
    readonly Dictionary<string, TechCategoryInfo> _categories;
    readonly Dictionary<string, EthicInfo> _ethics;
    readonly Dictionary<string, ModifierInfo> _modifiers;
    readonly Func<string, string?>? _loadIcon;
    readonly ConcurrentDictionary<string, Lazy<string?>> _iconUris = new(StringComparer.OrdinalIgnoreCase);

    TechAddonCatalog(IReadOnlyList<TechInfo> techs, IReadOnlyList<string> icons, IReadOnlyList<TechCategoryInfo> categories,
        IReadOnlyList<EthicInfo> ethics, IReadOnlyList<ModifierInfo> modifiers, bool modifiersFromLog)
    {
        Techs = techs;
        Icons = icons;
        Categories = categories;
        Ethics = ethics;
        Modifiers = modifiers;
        ModifiersFromLog = modifiersFromLog;
        _techs = techs.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
        _icons = new HashSet<string>(icons, StringComparer.OrdinalIgnoreCase);
        _categories = categories.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
        _ethics = ethics.ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);
        _modifiers = modifiers.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);
    }

    // A copy with other ethics and an icon loader; everything else is shared.
    TechAddonCatalog(TechAddonCatalog from, IReadOnlyList<EthicInfo> ethics, Func<string, string?> loadIcon)
    {
        Techs = from.Techs;
        Icons = from.Icons;
        Categories = from.Categories;
        Ethics = ethics;
        Modifiers = from.Modifiers;
        ModifiersFromLog = from.ModifiersFromLog;
        _techs = from._techs;
        _icons = from._icons;
        _categories = from._categories;
        _ethics = ethics.ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);
        _modifiers = from._modifiers;
        _loadIcon = loadIcon;
    }

    public static TechAddonCatalog Empty { get; } = new([], [], [], [], [], false);

    /// <summary>Every tech, in load order (each where its key was first defined).</summary>
    public IReadOnlyList<TechInfo> Techs { get; }

    /// <summary>Names of the icons a tech can use: the .dds files directly in gfx/interface/icons/technologies of any source, sorted.</summary>
    public IReadOnlyList<string> Icons { get; }

    /// <summary>The tech categories, in load order.</summary>
    public IReadOnlyList<TechCategoryInfo> Categories { get; }

    /// <summary>The regular ethics and gestalt consciousness (not the fanatic variants), in load order.</summary>
    public IReadOnlyList<EthicInfo> Ethics { get; }

    /// <summary>The modifiers a tech can give, sorted by key: this PC's modifiers.log when it has one, else every localised modifier name.</summary>
    public IReadOnlyList<ModifierInfo> Modifiers { get; }

    /// <summary>True when <see cref="Modifiers"/> come from modifiers.log (complete, with categories).</summary>
    public bool ModifiersFromLog { get; }

    public TechInfo? FindTech(string key) => _techs.GetValueOrDefault(key);

    public bool HasIcon(string name) => _icons.Contains(name);

    public TechCategoryInfo? FindCategory(string key) => _categories.GetValueOrDefault(key);

    /// <summary>A regular ethic or gestalt consciousness; null for fanatic variants.</summary>
    public EthicInfo? FindEthic(string key) => _ethics.GetValueOrDefault(key);

    public ModifierInfo? FindModifier(string key) => _modifiers.GetValueOrDefault(key);

    /// <summary>Where the game writes its list of modifiers (every modifier of the mod list it last ran with).</summary>
    public static string ModifiersLogPath(string userDir) => Path.Combine(userDir, "logs", "script_documentation", "modifiers.log");

    /// <summary>
    /// Reads <paramref name="sources"/> (base game first, then mods in load order). Per file path the last source wins; the winning files
    /// load in file-name order and a later definition of a key wins. Techs come from common/technology/*.txt (not its subfolders), categories
    /// from common/technology/category, ethics from common/ethics. Names come from <paramref name="loc"/>, else the key. Modifiers come from
    /// <paramref name="modifiersLogPath"/> when it exists and lists any, else from the localisation's mod_&lt;key&gt; names. Unreadable files are
    /// skipped with a warning.
    /// </summary>
    public static TechAddonCatalog Build(IReadOnlyList<ContentSource> sources, Localisation? loc = null, string? modifiersLogPath = null,
        ICollection<string>? warnings = null, CancellationToken ct = default)
    {
        warnings ??= new List<string>();
        string Name(string key) => loc?.Get(key) is { Length: > 0 } name ? name : key;

        var techs = ReadTechs(sources, Name, warnings, ct);
        var icons = ReadIcons(sources);
        var categories = CommonDefinitions.Load(sources, CategoryFolder, warnings, ct).Keys.Select(k => new TechCategoryInfo(k, Name(k))).ToList();
        var ethics = ReadEthics(CommonDefinitions.Load(sources, EthicsFolder, warnings, ct), Name);
        var (modifiers, fromLog) = ReadModifiers(modifiersLogPath, loc, warnings, ct);
        return new TechAddonCatalog(techs, icons, categories, ethics, modifiers, fromLog);
    }

    /// <summary>
    /// A copy with each ethic's icon (gfx/interface/icons/ethics/&lt;key&gt;.dds, decoded now; null when missing) that loads tech icons on
    /// request (see <see cref="IconUri"/>) from <paramref name="sources"/>, which must stay open meanwhile: once they are disposed, icons not
    /// loaded yet come back null. Does IO, so never call it on the UI thread.
    /// </summary>
    public TechAddonCatalog LoadIcons(IReadOnlyList<ContentSource> sources, IconCache icons) =>
        new(this, Ethics.Select(e => e with { IconUri = icons.DataUri(sources, new IconRef($"{EthicIconFolder}/{e.Key}.dds")) }).ToList(),
            name => icons.DataUri(sources, name));

    /// <summary>
    /// PNG data URI of one of the <see cref="Icons"/>, or null (unknown, undecodable, or a catalog without <see cref="LoadIcons"/>). Kept once
    /// loaded; the first call per icon does IO, so never call it on the UI thread (see <see cref="IconUriAsync"/>).
    /// </summary>
    public string? IconUri(string name) =>
        _loadIcon is { } load && _icons.TryGetValue(name, out var icon)
            ? _iconUris.GetOrAdd(icon, n => new Lazy<string?>(() => load(n))).Value
            : null;

    /// <summary><see cref="IconUri"/> on the thread pool; completes at once when the icon was loaded before.</summary>
    public Task<string?> IconUriAsync(string name) =>
        _iconUris.TryGetValue(name, out var icon) && icon.IsValueCreated ? Task.FromResult(icon.Value) : Task.Run(() => IconUri(name));

    static List<TechInfo> ReadTechs(IReadOnlyList<ContentSource> sources, Func<string, string> name, ICollection<string> warnings, CancellationToken ct)
    {
        var definitions = new Dictionary<string, (TechDefinition Def, Dictionary<string, string> Locals)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, rel) in MarketCatalog.Winners(sources, TechDatabase.TechFolder).Where(f => IsDirectlyIn(f.Rel, TechDatabase.TechFolder)))
        {
            ct.ThrowIfCancellationRequested();
            if (MarketCatalog.TryRead(source, rel, warnings) is not { } text) continue;
            var locals = ScriptedVariables.Parse(text);
            foreach (var def in TechParser.Parse(text)) definitions[def.Key] = (def, locals);
        }

        // Global scripted variables are read only once a tier needs them.
        Dictionary<string, string>? globals = null;
        int? Tier(string? raw, Dictionary<string, string> locals)
        {
            if (raw is not null && raw.StartsWith('@')) raw = ScriptedVariables.Resolve(raw, locals, globals ??= MarketCatalog.ReadGlobals(sources, warnings, ct));
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tier) ? tier : null;
        }
        return definitions.Values
            .Select(d => new TechInfo(d.Def.Key, name(d.Def.Key), d.Def.Area, Tier(d.Def.Tier, d.Locals), d.Def.IsRepeatable, d.Def.Icon ?? d.Def.Key))
            .ToList();
    }

    // Icon names are file names, so a tech's icon is checked the same on every PC with the same mods.
    static List<string> ReadIcons(IReadOnlyList<ContentSource> sources)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var icons = new List<string>();
        foreach (var source in sources)
            foreach (var rel in source.Files(IconCache.IconFolder, ".dds"))
            {
                var name = Path.GetFileNameWithoutExtension(rel);
                if (IsDirectlyIn(rel, IconCache.IconFolder) && IconName.IsMatch(name) && seen.Add(name)) icons.Add(name);
            }
        icons.Sort(StringComparer.OrdinalIgnoreCase);
        return icons;
    }

    // A fanatic variant is the ethic's fanatic_variant when that ethic exists, else ethic_fanatic_<suffix> when that exists. Ethics named
    // ethic_fanatic_… or with a regular_variant are fanatic variants themselves.
    static List<EthicInfo> ReadEthics(Dictionary<string, PdxBlock> definitions, Func<string, string> name)
    {
        var keys = definitions.Keys.ToDictionary(k => k, StringComparer.OrdinalIgnoreCase);
        string? Existing(string? key) => key is not null && keys.TryGetValue(key, out var spelled) ? spelled : null;
        return definitions
            .Where(d => !d.Key.StartsWith(FanaticPrefix, StringComparison.OrdinalIgnoreCase) && d.Value.GetString("regular_variant") is null)
            .Select(d => new EthicInfo(d.Key, name(d.Key),
                Existing(d.Value.GetString("fanatic_variant"))
                ?? (d.Key.StartsWith("ethic_", StringComparison.OrdinalIgnoreCase) ? Existing(FanaticPrefix + d.Key["ethic_".Length..]) : null)))
            .ToList();
    }

    static (List<ModifierInfo> Modifiers, bool FromLog) ReadModifiers(string? logPath, Localisation? loc, ICollection<string> warnings, CancellationToken ct)
    {
        string? Name(string key) => loc?.Get("mod_" + key) is { Length: > 0 } name ? name : null;

        if (logPath is not null && File.Exists(logPath))
        {
            try
            {
                if (ReadLog(logPath, Name, ct) is { Count: > 0 } logged) return (logged, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"{logPath}: {ex.Message}");
            }
        }

        // Every localised modifier name (mod_<key>, without the mod_<key>_desc descriptions): the modifiers known to have a name.
        var named = new Dictionary<string, ModifierInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var locKey in loc?.Keys ?? [])
            if (locKey.Length > 4 && locKey.StartsWith("mod_", StringComparison.OrdinalIgnoreCase) && !locKey.EndsWith("_desc", StringComparison.OrdinalIgnoreCase))
                named[locKey[4..]] = new ModifierInfo(locKey[4..], null, Name(locKey[4..]));
        return (named.Values.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase).ToList(), false);
    }

    // The lines after the marker (all lines when there is none); a later line for a key wins. Categories share one string each.
    static List<ModifierInfo> ReadLog(string path, Func<string, string?> name, CancellationToken ct)
    {
        var lines = File.ReadAllLines(path);
        var start = Array.FindIndex(lines, l => l.Contains(LogMarker, StringComparison.Ordinal)) + 1;
        var categories = new Dictionary<string, string>(StringComparer.Ordinal);
        var modifiers = new Dictionary<string, ModifierInfo>(StringComparer.OrdinalIgnoreCase);
        for (var i = start; i < lines.Length; i++)
        {
            if (i % 4096 == 0) ct.ThrowIfCancellationRequested();
            var m = LogLine.Match(lines[i].TrimEnd());
            if (!m.Success) continue;
            var key = m.Groups[1].Value;
            var category = m.Groups[2].Value.Trim();
            if (!categories.TryGetValue(category, out var shared)) categories[category] = shared = category;
            modifiers[key] = new ModifierInfo(key, shared.Length == 0 ? null : shared, name(key));
        }
        return modifiers.Values.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // "common/technology/x.txt" is directly in common/technology; "common/technology/category/x.txt" is not.
    static bool IsDirectlyIn(string rel, string folder) => rel.LastIndexOf('/') == folder.Length;
}

using System.Globalization;
using System.Text;
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Addons;

/// <summary>
/// The Galactic Market as the current list loads it: the resources of the winning common/strategic_resources files and the market defines
/// (NEconomy, plus the NAI lists of resources the AI may buy and sell). Built from the base game and the list's mods in load order, never
/// from Faz Addons itself. Immutable; <see cref="LoadIcons"/> returns a copy with icons.
/// </summary>
public sealed class MarketCatalog
{
    public const string ResourcesFolder = "common/strategic_resources";
    public const string DefinesFolder = "common/defines";
    public const string ResourceIconFolder = "gfx/interface/icons/resources";

    /// <summary>Base price for a resource put on the market that has no positive price yet.</summary>
    public const int SuggestedPrice = 100;

    sealed record MarketDefines(int? MaxBaseAmounts, double? TradeFeeBase, double? TradeFeeMin, int? MinFluctuation, int? MaxFluctuation,
        IReadOnlyList<string> AiAllowedToBuy, IReadOnlyList<string> AiAllowedToSell);

    readonly MarketDefines _defines;
    readonly Dictionary<string, ResourceInfo> _byKey;
    readonly HashSet<string> _buy;
    readonly HashSet<string> _sell;

    MarketCatalog(IReadOnlyList<ResourceInfo> resources, IReadOnlyList<MarketFile> files, MarketDefines defines)
    {
        Resources = resources;
        Files = files;
        _defines = defines;
        _byKey = resources.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        _buy = new HashSet<string>(defines.AiAllowedToBuy, StringComparer.OrdinalIgnoreCase);
        _sell = new HashSet<string>(defines.AiAllowedToSell, StringComparer.OrdinalIgnoreCase);
    }

    public static MarketCatalog Empty { get; } = new([], [], new MarketDefines(null, null, null, null, null, [], []));

    /// <summary>Every resource, in the order the game loads their (last) definitions.</summary>
    public IReadOnlyList<ResourceInfo> Resources { get; }

    /// <summary>The winning common/strategic_resources files, in load order.</summary>
    public IReadOnlyList<MarketFile> Files { get; }

    public int? MaxBaseAmounts => _defines.MaxBaseAmounts;
    public double? TradeFeeBase => _defines.TradeFeeBase;
    public double? TradeFeeMin => _defines.TradeFeeMin;
    public int? MinFluctuation => _defines.MinFluctuation;
    public int? MaxFluctuation => _defines.MaxFluctuation;

    /// <summary>NAI.AI_ALLOWED_TO_BUY as the game loads it, in its order (empty when no source defines it).</summary>
    public IReadOnlyList<string> AiAllowedToBuy => _defines.AiAllowedToBuy;

    /// <summary>NAI.AI_ALLOWED_TO_SELL as the game loads it, in its order (empty when no source defines it).</summary>
    public IReadOnlyList<string> AiAllowedToSell => _defines.AiAllowedToSell;

    public ResourceInfo? Find(string key) => _byKey.GetValueOrDefault(key);

    public bool AiBuys(string key) => _buy.Contains(key);

    public bool AiSells(string key) => _sell.Contains(key);

    /// <summary>The AI both buys and sells the resource on the market.</summary>
    public bool AiTrades(string key) => AiBuys(key) && AiSells(key);

    /// <summary>Bundle size for a resource put on the market that has no positive one yet, after the base game's sizes per category.</summary>
    public static int SuggestedBundle(string? category) => category?.ToLowerInvariant() switch
    {
        "basic" => 100,
        "advanced" => 25,
        "strategic" => 10,
        "rare" => 5,
        _ => 10,
    };

    /// <summary>
    /// Reads the market from <paramref name="sources"/> (base game first, then mods in load order). Per file path the last source wins;
    /// the winning files then load in file-name order and a later definition wins (a resource key or a Namespace.Key define).
    /// Names come from <paramref name="loc"/>, else the key. Unreadable files are skipped with a warning.
    /// </summary>
    public static MarketCatalog Build(IReadOnlyList<ContentSource> sources, Localisation? loc = null, ICollection<string>? warnings = null,
        CancellationToken ct = default)
    {
        warnings ??= new List<string>();
        Dictionary<string, string>? globals = null;
        IReadOnlyDictionary<string, string> Globals() => globals ??= ReadGlobals(sources, warnings, ct);

        var files = new List<MarketFile>();
        var definitions = new Dictionary<string, (PdxBlock Block, MarketFile File, Dictionary<string, string> Locals)>(StringComparer.OrdinalIgnoreCase);
        var fileLists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var (source, rel) in Winners(sources, ResourcesFolder))
        {
            ct.ThrowIfCancellationRequested();
            byte[] bytes;
            try { bytes = ReadBytes(source, rel); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
            {
                warnings.Add($"{source.Name}: {rel}: {ex.Message}");
                continue;
            }
            var text = Decode(bytes);
            var locals = ScriptedVariables.Parse(text);
            var blocks = ParadoxScriptParser.Parse(text).Entries
                .Where(e => e.Value is PdxBlock && !e.Key.StartsWith('@'))
                .ToList();
            var file = new MarketFile(rel, source.Name, bytes, blocks.Select(e => e.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            files.Add(file);
            foreach (var e in blocks)
            {
                definitions[e.Key] = ((PdxBlock)e.Value, file, locals);
                order.RemoveAll(k => k.Equals(e.Key, StringComparison.OrdinalIgnoreCase));
                order.Add(e.Key);
                if (!fileLists.TryGetValue(e.Key, out var list)) fileLists[e.Key] = list = [];
                if (!list.Contains(rel, StringComparer.OrdinalIgnoreCase)) list.Add(rel);
            }
        }

        var resources = order.Select(key =>
        {
            var (block, file, locals) = definitions[key];
            // Global scripted variables are read only once a value needs them.
            string? Value(string name) =>
                block.GetString(name) is { } raw && raw.StartsWith('@') ? ScriptedVariables.Resolve(raw, locals, Globals()) : block.GetString(name);
            return new ResourceInfo(
                key,
                loc?.Get(key) is { Length: > 0 } name ? name : key,
                block.GetString("category"),
                Int(Value("market_amount")) ?? -1,
                Int(Value("market_price")) ?? -1,
                IsYes(block.GetString("tradable")),
                Int(Value("max")),
                IsYes(block.GetString("fixed_max_amount")),
                block.GetBlock("visibility_prerequisite")?.GetString("always") is { } always && always.Equals("no", StringComparison.OrdinalIgnoreCase),
                block.Entries.Any(e => e.Key.Equals("tradable_in_market", StringComparison.OrdinalIgnoreCase)),
                file.SourceName,
                file.Path,
                fileLists[key]);
        }).ToList();

        return new MarketCatalog(resources, files, ReadDefines(sources, warnings, ct));
    }

    /// <summary>
    /// A copy with each resource's icon: the sprite GFX_resource_&lt;key&gt; through <paramref name="icons"/>, else
    /// gfx/interface/icons/resources/&lt;key&gt;.dds; null when neither decodes. Does IO, so never call it on the UI thread.
    /// </summary>
    public MarketCatalog LoadIcons(IReadOnlyList<ContentSource> sources, SpriteIndex sprites, IconCache icons)
    {
        string? Icon(string key)
        {
            if (sprites.Find("GFX_resource_" + key) is { } sprite
                && icons.DataUri(sources, new IconRef(sprite.TextureFile, Math.Clamp(sprite.DefaultFrame ?? 1, 1, sprite.Frames), sprite.Frames)) is { } uri)
                return uri;
            return icons.DataUri(sources, new IconRef($"{ResourceIconFolder}/{key}.dds"));
        }
        return new MarketCatalog(Resources.Select(r => r with { IconUri = Icon(r.Key) }).ToList(), Files, _defines);
    }

    // The last value of each Namespace.Key; only files that mention a market define are parsed.
    static MarketDefines ReadDefines(IReadOnlyList<ContentSource> sources, ICollection<string> warnings, CancellationToken ct)
    {
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, rel) in Winners(sources, DefinesFolder))
        {
            ct.ThrowIfCancellationRequested();
            if (TryRead(source, rel, warnings) is not { } text) continue;
            if (!text.Contains("MARKET_", StringComparison.OrdinalIgnoreCase) && !text.Contains("AI_ALLOWED_TO_", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var ns in ParadoxScriptParser.Parse(text).Entries)
                if (ns.Value is PdxBlock block)
                    foreach (var e in block.Entries) values[ns.Key + "." + e.Key] = e.Value;
        }

        string? Str(string key) => values.GetValueOrDefault(key) as string;
        IReadOnlyList<string> List(string key) => values.GetValueOrDefault(key) is PdxBlock b ? b.StringItems.ToList() : [];
        return new MarketDefines(
            Int(Str("NEconomy.MARKET_MAX_BASE_AMOUNTS")),
            Double(Str("NEconomy.MARKET_TRADE_FEE_BASE")),
            Double(Str("NEconomy.MARKET_TRADE_FEE_MIN")),
            Int(Str("NEconomy.MARKET_MIN_FLUCTUATION_FROM_BASE_PRICE")),
            Int(Str("NEconomy.MARKET_MAX_FLUCTUATION_FROM_BASE_PRICE")),
            List("NAI.AI_ALLOWED_TO_BUY"),
            List("NAI.AI_ALLOWED_TO_SELL"));
    }

    static Dictionary<string, string> ReadGlobals(IReadOnlyList<ContentSource> sources, ICollection<string> warnings, CancellationToken ct)
    {
        var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, rel) in Winners(sources, "common/scripted_variables"))
        {
            ct.ThrowIfCancellationRequested();
            if (TryRead(source, rel, warnings) is { } text)
                foreach (var (k, v) in ScriptedVariables.Parse(text)) globals[k] = v;
        }
        return globals;
    }

    // Per relative path the last source wins; the winners load in file-name order.
    static IEnumerable<(ContentSource Source, string Rel)> Winners(IReadOnlyList<ContentSource> sources, string folder)
    {
        var files = new Dictionary<string, (ContentSource Source, string Rel)>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            foreach (var rel in source.Files(folder, ".txt"))
                files[rel] = (source, rel);
        return files.Values.OrderBy(f => f.Rel, TechDatabase.LoadOrder).ToList();
    }

    static string? TryRead(ContentSource source, string rel, ICollection<string> warnings)
    {
        try { return source.ReadText(rel); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
        {
            warnings.Add($"{source.Name}: {rel}: {ex.Message}");
            return null;
        }
    }

    static byte[] ReadBytes(ContentSource source, string rel)
    {
        using var stream = source.Open(rel);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    // As ContentSource.ReadText: UTF-8 unless a byte order mark says otherwise.
    static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    static bool IsYes(string? value) => string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    static int? Int(string? raw)
    {
        if (raw is null) return null;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        return Double(raw) is { } d && Math.Abs(d) < int.MaxValue ? (int)Math.Round(d) : null;
    }

    static double? Double(string? raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : null;
}

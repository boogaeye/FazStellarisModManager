using System.Text.Json.Serialization;

namespace FazStellarisModmanager.Core.Addons;

/// <summary>
/// The addons built into the generated Faz Addons mod; off by default. The addons are the <see cref="Marketplace"/> (null means its
/// defaults: on, nothing changed) and the custom <see cref="Technologies"/> (null means none). Throughout, a null value means "unchanged
/// from the game".
/// </summary>
public sealed record AddonSettings(bool Enabled = false, MarketplaceSettings? Marketplace = null, TechnologiesSettings? Technologies = null)
{
    /// <summary>
    /// True when Faz Addons may have something to build: addons are on, and the Marketplace sets values or Technologies holds techs. Apply
    /// builds it only then; values equal to the game's or techs with errors can still leave nothing to load (see <see cref="AddonOutput.HasChanges"/>).
    /// </summary>
    [JsonIgnore]
    public bool MayChangeGame => ActiveAddons.Count > 0;

    /// <summary>The names of the addons that may change the game (see <see cref="MayChangeGame"/>): "Marketplace", "Technologies"; none while addons are off.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> ActiveAddons
    {
        get
        {
            var names = new List<string>();
            if (!Enabled) return names;
            if (Marketplace is { Enabled: true, ChangeCount: > 0 }) names.Add("Marketplace");
            if (Technologies is { Enabled: true, Techs.Count: > 0 }) names.Add("Technologies");
            return names;
        }
    }

    /// <summary>A copy without the overrides that equal the game's current values (see <see cref="MarketplaceSettings.Normalize"/>); techs are kept as they are.</summary>
    public AddonSettings Normalize(MarketCatalog catalog) => Marketplace is null ? this : this with { Marketplace = Marketplace.Normalize(catalog) };
}

/// <summary>
/// Galactic Market changes: the market rules (NEconomy defines) and per-resource overrides keyed by resource key.
/// Compares by value, including the <see cref="Resources"/> entries (an empty map equals null).
/// </summary>
public sealed record MarketplaceSettings(
    bool Enabled = true,
    int? MaxBaseAmounts = null,          // NEconomy.MARKET_MAX_BASE_AMOUNTS
    double? TradeFeeBase = null,         // NEconomy.MARKET_TRADE_FEE_BASE
    double? TradeFeeMin = null,          // NEconomy.MARKET_TRADE_FEE_MIN
    int? MinFluctuation = null,          // NEconomy.MARKET_MIN_FLUCTUATION_FROM_BASE_PRICE
    int? MaxFluctuation = null,          // NEconomy.MARKET_MAX_FLUCTUATION_FROM_BASE_PRICE
    IReadOnlyDictionary<string, ResourceOverride>? Resources = null)
{
    /// <summary>How many values are set: each market rule plus each field of each resource override.</summary>
    [JsonIgnore]
    public int ChangeCount =>
        new object?[] { MaxBaseAmounts, TradeFeeBase, TradeFeeMin, MinFluctuation, MaxFluctuation }.Count(v => v is not null)
        + (Resources?.Values.Sum(r => r.ChangeCount) ?? 0);

    /// <summary>
    /// A copy without the values that change nothing in <paramref name="catalog"/>: market rules equal to the current defines (doubles
    /// compared at 4 decimals), and per resource: see <see cref="ResourceOverride.Normalize"/>. Overrides of resources the catalog doesn't
    /// have are kept, so a mod that is off for a while doesn't lose its settings. Empty overrides are dropped, and no overrides left gives
    /// null <see cref="Resources"/>.
    /// </summary>
    public MarketplaceSettings Normalize(MarketCatalog catalog)
    {
        Dictionary<string, ResourceOverride>? resources = null;
        foreach (var (key, value) in (Resources ?? new Dictionary<string, ResourceOverride>()).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var normalized = catalog.Find(key) is { } resource ? value.Normalize(resource, catalog) : value;
            if (normalized.IsEmpty) continue;
            (resources ??= new Dictionary<string, ResourceOverride>(StringComparer.OrdinalIgnoreCase))[key] = normalized;
        }
        return this with
        {
            MaxBaseAmounts = MaxBaseAmounts is not > 0 || MaxBaseAmounts == catalog.MaxBaseAmounts ? null : MaxBaseAmounts,
            TradeFeeBase = SameDouble(TradeFeeBase, catalog.TradeFeeBase) ? null : TradeFeeBase,
            TradeFeeMin = SameDouble(TradeFeeMin, catalog.TradeFeeMin) ? null : TradeFeeMin,
            MinFluctuation = MinFluctuation == catalog.MinFluctuation ? null : MinFluctuation,
            MaxFluctuation = MaxFluctuation == catalog.MaxFluctuation ? null : MaxFluctuation,
            Resources = resources,
        };
    }

    static bool SameDouble(double? value, double? game) =>
        value is { } v && game is { } g && Math.Round(v, 4) == Math.Round(g, 4);

    public bool Equals(MarketplaceSettings? other) =>
        other is not null && Enabled == other.Enabled && MaxBaseAmounts == other.MaxBaseAmounts && TradeFeeBase == other.TradeFeeBase
        && TradeFeeMin == other.TradeFeeMin && MinFluctuation == other.MinFluctuation && MaxFluctuation == other.MaxFluctuation
        && SameResources(Resources, other.Resources);

    public override int GetHashCode() =>
        HashCode.Combine(Enabled, MaxBaseAmounts, TradeFeeBase, TradeFeeMin, MinFluctuation, MaxFluctuation, Resources?.Count ?? 0);

    static bool SameResources(IReadOnlyDictionary<string, ResourceOverride>? a, IReadOnlyDictionary<string, ResourceOverride>? b)
    {
        if ((a?.Count ?? 0) != (b?.Count ?? 0)) return false;
        if (a is null || b is null) return true;
        foreach (var (key, value) in a)
            if (!b.TryGetValue(key, out var other) || value != other) return false;
        return true;
    }
}

/// <summary>Changes to one resource. <see cref="AiTrades"/> adds it to (or removes it from) both NAI lists of resources the AI may buy and sell.</summary>
public sealed record ResourceOverride(bool? OnMarket = null, int? MarketAmount = null, int? MarketPrice = null,
    bool? Tradable = null, bool? AiTrades = null, int? BaseStorage = null)
{
    [JsonIgnore]
    public bool IsEmpty => ChangeCount == 0;

    [JsonIgnore]
    public int ChangeCount => new object?[] { OnMarket, MarketAmount, MarketPrice, Tradable, AiTrades, BaseStorage }.Count(v => v is not null);

    /// <summary>
    /// A copy without the values that change nothing for <paramref name="resource"/>: anything equal to the current value; a bundle size or
    /// price that isn't positive, or for a resource that stays off the market; AI trades already in force (true when the AI both buys and
    /// sells it, false when it does neither); a base storage for a resource without <c>max</c>, or with <c>fixed_max_amount</c>, or negative.
    /// </summary>
    public ResourceOverride Normalize(ResourceInfo resource, MarketCatalog catalog)
    {
        var onMarket = OnMarket == resource.OnMarket ? null : OnMarket;
        var staysOn = onMarket ?? resource.OnMarket;
        var buys = catalog.AiBuys(resource.Key);
        var sells = catalog.AiSells(resource.Key);
        return new ResourceOverride(
            onMarket,
            staysOn && MarketAmount is > 0 && MarketAmount != resource.MarketAmount ? MarketAmount : null,
            staysOn && MarketPrice is > 0 && MarketPrice != resource.MarketPrice ? MarketPrice : null,
            Tradable == resource.Tradable ? null : Tradable,
            AiTrades switch
            {
                true when buys && sells => null,
                false when !buys && !sells => null,
                _ => AiTrades,
            },
            resource.Max is null || resource.FixedMax || BaseStorage is not >= 0 || BaseStorage == resource.Max ? null : BaseStorage);
    }
}

/// <summary>
/// Custom technologies Faz Addons adds, in the order they are listed and built. Compares by value, including the <see cref="Techs"/>
/// (an empty list equals null).
/// </summary>
public sealed record TechnologiesSettings(bool Enabled = true, IReadOnlyList<CustomTech>? Techs = null)
{
    public bool Equals(TechnologiesSettings? other) => other is not null && Enabled == other.Enabled && Items.Same(Techs, other.Techs);

    public override int GetHashCode() => HashCode.Combine(Enabled, Techs?.Count ?? 0);
}

/// <summary>
/// One custom technology; the defaults are the base game's repeatables'. <see cref="Ethics"/> are regular ethic keys (e.g.
/// ethic_authoritarian, whose fanatic variant counts too) or ethic_gestalt_consciousness, any of which allows it; <see cref="Prerequisites"/>
/// are tech keys; <see cref="Modifiers"/> apply per level on repeatables, and <see cref="CostPerLevel"/> only counts for them. Compares by
/// value, including the lists (in order; an empty list equals null). <see cref="TechValidator"/> says whether it can be built.
/// </summary>
public sealed record CustomTech(
    string Key, string Name, string Description, string Icon,
    string Area = "society", string Category = "statecraft", int Tier = 5,
    bool Repeatable = true, bool Rare = false,
    int Cost = 50000, int CostPerLevel = 5000, int Weight = 15,
    IReadOnlyList<string>? Ethics = null,
    IReadOnlyList<string>? Prerequisites = null,
    IReadOnlyList<TechModifier>? Modifiers = null)
{
    public bool Equals(CustomTech? other) =>
        other is not null && Key == other.Key && Name == other.Name && Description == other.Description && Icon == other.Icon
        && Area == other.Area && Category == other.Category && Tier == other.Tier && Repeatable == other.Repeatable && Rare == other.Rare
        && Cost == other.Cost && CostPerLevel == other.CostPerLevel && Weight == other.Weight
        && Items.Same(Ethics, other.Ethics) && Items.Same(Prerequisites, other.Prerequisites) && Items.Same(Modifiers, other.Modifiers);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key);
        hash.Add(Name);
        hash.Add(Description);
        hash.Add(Icon);
        hash.Add(Area);
        hash.Add(Category);
        hash.Add(Tier);
        hash.Add(Repeatable);
        hash.Add(Rare);
        hash.Add(Cost);
        hash.Add(CostPerLevel);
        hash.Add(Weight);
        hash.Add(Ethics?.Count ?? 0);
        hash.Add(Prerequisites?.Count ?? 0);
        hash.Add(Modifiers?.Count ?? 0);
        return hash.ToHashCode();
    }
}

/// <summary>One effect of a custom tech: modifier key and value (per level on repeatables).</summary>
public sealed record TechModifier(string Key, double Value);

// Value equality for the settings' lists: the same items in the same order, an empty list being the same as none.
file static class Items
{
    public static bool Same<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b)
    {
        if ((a?.Count ?? 0) != (b?.Count ?? 0)) return false;
        if (a is null || b is null) return true;
        for (var i = 0; i < a.Count; i++)
            if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
        return true;
    }
}

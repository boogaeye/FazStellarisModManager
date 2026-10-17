using System.Text.Json.Serialization;

namespace FazStellarisModmanager.Core.Addons;

/// <summary>
/// The addons built into the generated Faz Addons mod; off by default. In v1 the only addon is the <see cref="Marketplace"/> (null means its
/// defaults: on, nothing changed). Throughout, a null value means "unchanged from the game".
/// </summary>
public sealed record AddonSettings(bool Enabled = false, MarketplaceSettings? Marketplace = null)
{
    /// <summary>A copy without the overrides that equal the game's current values (see <see cref="MarketplaceSettings.Normalize"/>).</summary>
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

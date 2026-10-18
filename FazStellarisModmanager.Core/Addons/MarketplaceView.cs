using System.Globalization;

namespace FazStellarisModmanager.Core.Addons;

/// <summary>
/// A resource as the Addons tab shows it: the game's values with the Marketplace overrides applied (only those that change something, see
/// <see cref="ResourceOverride.Normalize"/>), and which values differ from the game. <see cref="Bundle"/> and <see cref="Price"/> are what
/// the build would write while on the market (an added resource takes the category bundle and the suggested price when it has none), else
/// the game's positive values or null. <see cref="BaseStorage"/> is null when the resource has no <c>max</c>.
/// <see cref="HasOverride"/>: settings are saved for it, even ones that change nothing (the reset removes them).
/// </summary>
public sealed record ResourceRow(
    ResourceInfo Resource,
    bool OnMarket,
    int? Bundle,
    int? Price,
    bool Tradable,
    bool AiTrades,
    int? BaseStorage,
    bool MarketChanged,
    bool BundleChanged,
    bool PriceChanged,
    bool TradableChanged,
    bool AiTradesChanged,
    bool BaseStorageChanged,
    bool HasOverride)
{
    public string Key => Resource.Key;

    /// <summary>Put on the market by the settings.</summary>
    public bool Added => OnMarket && !Resource.OnMarket;

    public bool Changed => MarketChanged || BundleChanged || PriceChanged || TradableChanged || AiTradesChanged || BaseStorageChanged;

    /// <summary>The most one trade can move: the bundle times NEconomy.MARKET_MAX_BASE_AMOUNTS; null off the market or without the define.</summary>
    public long? MaxPerTrade(int? maxBaseAmounts) => OnMarket && Bundle is { } bundle && maxBaseAmounts is { } max ? (long)bundle * max : null;
}

public enum ResourceFilter { All, OnMarket, OffMarket, Changed }

public sealed record ResourceGroup(string Name, IReadOnlyList<ResourceRow> Rows);

/// <summary>Display and edit helpers for the Marketplace on the Addons tab. Pure: the page saves the edited settings through <see cref="AddonService.Save"/>.</summary>
public static class MarketplaceView
{
    public const string HiddenGroup = "Hidden (internal)";

    /// <summary>The resource groups in display order; hidden resources go last, whatever their category.</summary>
    public static readonly IReadOnlyList<string> GroupOrder = ["Basic", "Advanced", "Strategic", "Rare", "Other", HiddenGroup];

    public static string GroupOf(ResourceInfo resource) => resource.Hidden ? HiddenGroup : resource.Category?.ToLowerInvariant() switch
    {
        "basic" => "Basic",
        "advanced" => "Advanced",
        "strategic" => "Strategic",
        "rare" => "Rare",
        _ => "Other",
    };

    /// <summary>Every resource of the catalog, in its order, with <paramref name="market"/>'s overrides applied.</summary>
    public static IReadOnlyList<ResourceRow> Rows(MarketCatalog catalog, MarketplaceSettings? market) =>
        catalog.Resources.Select(r => Row(catalog, r, market?.Resources?.GetValueOrDefault(r.Key))).ToList();

    public static ResourceRow Row(MarketCatalog catalog, ResourceInfo r, ResourceOverride? saved)
    {
        var o = saved?.Normalize(r, catalog) ?? new ResourceOverride();
        var onMarket = o.OnMarket ?? r.OnMarket;
        int? bundle = onMarket
            ? o.MarketAmount ?? (r.MarketAmount > 0 ? r.MarketAmount : MarketCatalog.SuggestedBundle(r.Category))
            : r.MarketAmount > 0 ? r.MarketAmount : null;
        int? price = onMarket
            ? o.MarketPrice ?? (r.MarketPrice > 0 ? r.MarketPrice : MarketCatalog.SuggestedPrice)
            : r.MarketPrice > 0 ? r.MarketPrice : null;
        return new ResourceRow(
            r,
            onMarket,
            bundle,
            price,
            o.Tradable ?? r.Tradable,
            o.AiTrades ?? catalog.AiTrades(r.Key),
            r.Max is null ? null : o.BaseStorage ?? r.Max,
            MarketChanged: o.OnMarket is not null,
            BundleChanged: onMarket && bundle != r.MarketAmount,
            PriceChanged: onMarket && price != r.MarketPrice,
            TradableChanged: o.Tradable is not null,
            AiTradesChanged: o.AiTrades is not null,
            BaseStorageChanged: o.BaseStorage is not null,
            HasOverride: saved is { IsEmpty: false });
    }

    /// <summary>Whether a row shows: hidden resources only with <paramref name="showHidden"/>, then the filter, then the search over name and key.</summary>
    public static bool Matches(ResourceRow row, ResourceFilter filter, bool showHidden, string? search)
    {
        if (row.Resource.Hidden && !showHidden) return false;
        var kept = filter switch
        {
            ResourceFilter.OnMarket => row.OnMarket,
            ResourceFilter.OffMarket => !row.OnMarket,
            ResourceFilter.Changed => row.Changed,
            _ => true,
        };
        var text = search?.Trim();
        return kept && (string.IsNullOrEmpty(text)
            || row.Resource.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || row.Key.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>How many rows a filter chip shows (without the search).</summary>
    public static int Count(IEnumerable<ResourceRow> rows, ResourceFilter filter, bool showHidden) =>
        rows.Count(r => Matches(r, filter, showHidden, null));

    /// <summary>The rows that show, by group in <see cref="GroupOrder"/> (empty groups left out), each keeping the catalog's order.</summary>
    public static IReadOnlyList<ResourceGroup> Group(IEnumerable<ResourceRow> rows, ResourceFilter filter, bool showHidden, string? search)
    {
        var shown = rows.Where(r => Matches(r, filter, showHidden, search)).ToList();
        return GroupOrder
            .Select(name => new ResourceGroup(name, shown.Where(r => GroupOf(r.Resource) == name).ToList()))
            .Where(g => g.Rows.Count > 0)
            .ToList();
    }

    /// <summary>NEconomy.MARKET_MAX_BASE_AMOUNTS as built: the setting when positive, else the game's (null when no source defines it).</summary>
    public static int? MaxBaseAmounts(MarketCatalog catalog, MarketplaceSettings? market) =>
        market?.MaxBaseAmounts is > 0 and var max ? max : catalog.MaxBaseAmounts;

    static readonly (string Key, string Label)[] PreviewResources = [("energy", "Basic"), ("alloys", "Alloys"), ("volatile_motes", "Strategic")];

    /// <summary>
    /// The max per trade of energy (Basic), alloys and volatile motes (Strategic) for <paramref name="maxBaseAmounts"/> bundles, for those
    /// on the market.
    /// </summary>
    public static IReadOnlyList<(string Label, long Amount)> TradePreview(IReadOnlyList<ResourceRow> rows, int maxBaseAmounts) =>
        PreviewResources
            .Select(p => (p.Label, Amount: rows.FirstOrDefault(r => r.Key.Equals(p.Key, StringComparison.OrdinalIgnoreCase))?.MaxPerTrade(maxBaseAmounts)))
            .Where(p => p.Amount is not null)
            .Select(p => (p.Label, p.Amount!.Value))
            .ToList();

    /// <summary>A copy with the Marketplace edited (its defaults when unset).</summary>
    public static AddonSettings EditMarket(AddonSettings settings, Func<MarketplaceSettings, MarketplaceSettings> edit) =>
        settings with { Marketplace = edit(settings.Marketplace ?? new MarketplaceSettings()) };

    /// <summary>A copy with one resource's override edited (key case-insensitive); an override left empty is removed, and no overrides left gives null.</summary>
    public static AddonSettings EditResource(AddonSettings settings, string key, Func<ResourceOverride, ResourceOverride> edit) =>
        EditMarket(settings, market =>
        {
            var resources = new Dictionary<string, ResourceOverride>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in market.Resources ?? new Dictionary<string, ResourceOverride>()) resources[k] = v;
            var next = edit(resources.GetValueOrDefault(key) ?? new ResourceOverride());
            if (next.IsEmpty) resources.Remove(key);
            else resources[key] = next;
            return market with { Resources = resources.Count == 0 ? null : resources };
        });

    /// <summary>A copy without the resource's override: back to the game's values.</summary>
    public static AddonSettings ResetResource(AddonSettings settings, string key) => EditResource(settings, key, _ => new ResourceOverride());

    /// <summary>A fraction (0.3) as a percent for an input ("30"), up to 2 decimals; "" for null.</summary>
    public static string Percent(double? fraction) =>
        fraction is { } f ? Math.Round(f * 100, 2).ToString("0.##", CultureInfo.InvariantCulture) : "";

    /// <summary>A percent typed into an input ("12.5") as a fraction (0.125) at 4 decimals; null unless a number from 0 to 100.</summary>
    public static double? FractionFromPercent(string? text) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) && percent is >= 0 and <= 100
            ? Math.Round(percent / 100, 4)
            : null;
}

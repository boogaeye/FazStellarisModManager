namespace FazStellarisModmanager.Core.Addons;

/// <summary>
/// One resource of common/strategic_resources as the current list loads it. <see cref="MarketAmount"/> (the bundle size) and
/// <see cref="MarketPrice"/> are -1 when absent, the game's default. <see cref="Max"/> is the base storage, null when absent.
/// <see cref="Hidden"/>: <c>visibility_prerequisite = { always = no }</c>; <see cref="Gated"/>: it has a <c>tradable_in_market</c> trigger.
/// <see cref="FileName"/> is the content-relative path of the winning definition's file, from the source <see cref="SourceName"/>;
/// <see cref="Files"/> lists every winning file that defines the key, in load order. <see cref="IconUri"/> stays null until
/// <see cref="MarketCatalog.LoadIcons"/>.
/// </summary>
public sealed record ResourceInfo(
    string Key,
    string Name,
    string? Category,
    int MarketAmount,
    int MarketPrice,
    bool Tradable,
    int? Max,
    bool FixedMax,
    bool Hidden,
    bool Gated,
    string SourceName,
    string FileName,
    IReadOnlyList<string> Files,
    string? IconUri = null)
{
    /// <summary>On the Galactic Market: the bundle size and the price are both positive.</summary>
    public bool OnMarket => MarketAmount > 0 && MarketPrice > 0;
}

/// <summary>A winning common/strategic_resources file: content-relative path, the source it comes from, its bytes as read, and the resource keys it defines (file order, distinct).</summary>
public sealed record MarketFile(string Path, string SourceName, byte[] Bytes, IReadOnlyList<string> Keys);

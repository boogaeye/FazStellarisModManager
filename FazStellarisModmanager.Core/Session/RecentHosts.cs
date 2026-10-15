namespace FazStellarisModmanager.Core.Session;

/// <summary>The join addresses the Session tab remembers: newest first, no duplicates (ignoring case), at most <see cref="Max"/>.</summary>
public static class RecentHosts
{
    public const int Max = 5;

    /// <summary>Moves <paramref name="address"/> (trimmed) to the front. A blank address leaves the list as it is.</summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string>? list, string address)
    {
        var items = (list ?? []).Select(x => x.Trim());
        if (address.Trim() is { Length: > 0 } a) items = items.Prepend(a);
        return items.Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(Max).ToList();
    }

    public static IReadOnlyList<string> Remove(IReadOnlyList<string>? list, string address)
    {
        var a = address.Trim();
        return (list ?? []).Where(x => !string.Equals(x.Trim(), a, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}

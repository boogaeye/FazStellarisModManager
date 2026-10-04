using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Diff;

/// <summary>Why two mods were paired.</summary>
public enum MatchKind { Key, WorkshopId, Name, Files }

/// <summary>
/// What makes two mods the same: key, Workshop id, normalised name and (when files are known) a fingerprint of all file
/// hashes. The fingerprint is computed lazily, at most once, and only when a rule asks for it.
/// </summary>
public sealed class ModIdentity
{
    static readonly ConditionalWeakTable<ModSnapshot, ModIdentity> Snapshots = new();

    readonly Lazy<string?> _fingerprint;

    /// <summary>An identity with a known fingerprint (or none).</summary>
    public ModIdentity(string key, string? workshopId, string name, string? fingerprint = null)
        : this(key, workshopId, name, () => fingerprint) { }

    /// <summary>An identity whose fingerprint is computed by <paramref name="fingerprintSource"/> on first use.</summary>
    public ModIdentity(string key, string? workshopId, string name, Func<string?> fingerprintSource)
    {
        Key = key;
        WorkshopId = workshopId;
        Name = name;
        _fingerprint = new Lazy<string?>(fingerprintSource);
    }

    public string Key { get; }
    public string? WorkshopId { get; }
    public string Name { get; }
    public string? Fingerprint => _fingerprint.Value;

    /// <summary>Cached per snapshot instance, so re-diffing the same snapshot never re-hashes its file list.</summary>
    public static ModIdentity Of(ModSnapshot s) => Snapshots.GetValue(s, static s =>
        new(s.Key, ModMatcher.WorkshopIdOf(s.Key, s.RemoteId), ModMatcher.NormalizeName(s.Name), () => ModMatcher.Fingerprint(s.Files)));

    public static ModIdentity Of(InstalledMod m) =>
        new(m.Key, ModMatcher.WorkshopIdOf(m.Key, m.RemoteId), ModMatcher.NormalizeName(m.Name));

    public static ModIdentity Of(ModListEntry e) =>
        new(e.Key, ModMatcher.WorkshopIdOf(e.Key, e.RemoteId), ModMatcher.NormalizeName(e.Name));
}

public sealed record ModPair<TTarget, TMine>(TTarget Target, TMine Mine, MatchKind Kind);

/// <summary>
/// Pairs target (host) mods with mine by identity, rule by rule: same key, same Workshop id, same normalised name (only when
/// neither side has a Workshop id), same file fingerprint. Each mod takes part in at most one pair; each rule only looks at
/// mods still unpaired; targets are taken in order and each pairs with the first unpaired mine mod with the same value.
/// Exception: when several unpaired mine mods share a target's name, one with the same fingerprint is preferred.
/// Fingerprints are only read for mods still unpaired at the files rule, or for such a name tie.
/// </summary>
public static class ModMatcher
{
    public static readonly MatchKind[] AllRules = [MatchKind.Key, MatchKind.WorkshopId, MatchKind.Name, MatchKind.Files];

    static readonly Regex Prefix = new(@"^\s*\([^)]*\)\s*", RegexOptions.Compiled);
    static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>The Workshop id of a ugc:&lt;id&gt; key, else of a positive numeric remote_file_id, else null.</summary>
    public static string? WorkshopIdOf(string key, string? remoteId)
    {
        if (ModKeys.WorkshopId(key) is { } fromKey) return fromKey > 0 ? fromKey.ToString(CultureInfo.InvariantCulture) : null;
        return ulong.TryParse(remoteId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>Lower-case, one leading "(…)" collection prefix removed, whitespace runs collapsed, trimmed.</summary>
    public static string NormalizeName(string? name) =>
        Spaces.Replace(Prefix.Replace(name ?? "", "", 1), " ").Trim().ToLowerInvariant();

    /// <summary>SHA-1 over the sorted "path|md5" lines (lower-case, / separators); null for an empty file list.</summary>
    public static string? Fingerprint(IReadOnlyCollection<ModFile> files)
    {
        if (files.Count == 0) return null;
        var lines = files
            .Select(f => (f.Path ?? "").Replace((char)92, '/').ToLowerInvariant() + "|" + (f.Md5 ?? "").ToLowerInvariant())
            .Order(StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(string.Join((char)10, lines))));
    }

    public static List<ModPair<TTarget, TMine>> Pair<TTarget, TMine>(IReadOnlyList<TTarget> targets, IReadOnlyList<TMine> mine,
        Func<TTarget, ModIdentity> targetIdentity, Func<TMine, ModIdentity> mineIdentity, IReadOnlyList<MatchKind>? rules = null)
    {
        var t = targets.Select(targetIdentity).ToList();
        var m = mine.Select(mineIdentity).ToList();
        var partner = new int[t.Count];
        Array.Fill(partner, -1);
        var kind = new MatchKind[t.Count];
        var used = new bool[m.Count];

        foreach (var rule in rules ?? AllRules)
        {
            // Nothing left on one side: skip the rule, so no value (above all no fingerprint) is computed.
            if (!partner.Contains(-1) || !used.Contains(false)) break;
            var available = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (var j = 0; j < m.Count; j++)
                if (!used[j] && Value(m[j], rule) is { } v)
                {
                    if (!available.TryGetValue(v, out var list)) available[v] = list = [];
                    list.Add(j);
                }
            for (var i = 0; i < t.Count; i++)
                if (partner[i] < 0 && Value(t[i], rule) is { } v && available.TryGetValue(v, out var list) && list.Count > 0)
                {
                    var at = rule == MatchKind.Name && list.Count > 1 ? SameFiles(t[i], list, m) : 0;
                    var j = list[at];
                    list.RemoveAt(at);
                    (used[j], partner[i], kind[i]) = (true, j, rule);
                }
        }

        var pairs = new List<ModPair<TTarget, TMine>>();
        for (var i = 0; i < t.Count; i++)
            if (partner[i] >= 0) pairs.Add(new ModPair<TTarget, TMine>(targets[i], mine[partner[i]], kind[i]));
        return pairs;
    }

    // Name tie: the first candidate with the target's fingerprint, else the first one.
    static int SameFiles(ModIdentity target, List<int> candidates, List<ModIdentity> mine)
    {
        if (target.Fingerprint is not { } fp) return 0;
        var at = candidates.FindIndex(j => string.Equals(mine[j].Fingerprint, fp, StringComparison.OrdinalIgnoreCase));
        return at < 0 ? 0 : at;
    }

    static string? Value(ModIdentity id, MatchKind rule) => rule switch
    {
        MatchKind.Key => id.Key,
        MatchKind.WorkshopId => id.WorkshopId,
        MatchKind.Name => id.WorkshopId is null && id.Name.Length > 0 ? id.Name : null,
        _ => id.Fingerprint,
    };
}

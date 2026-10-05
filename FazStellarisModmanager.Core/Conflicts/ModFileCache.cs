using System.Collections.Concurrent;

namespace FazStellarisModmanager.Core.Conflicts;

/// <summary>Remembers <see cref="ModFiles.List"/> per content path for the app run. Unreadable content gives an empty list that is not remembered. Thread-safe.</summary>
public sealed class ModFileCache
{
    readonly ConcurrentDictionary<string, IReadOnlyList<string>> _lists = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Get(string contentPath)
    {
        if (_lists.TryGetValue(contentPath, out var cached)) return cached;
        try
        {
            var list = ModFiles.List(contentPath);
            _lists[contentPath] = list;
            return list;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>Forgets every list (after a rescan, when mods may have changed on disk).</summary>
    public void Clear() => _lists.Clear();
}

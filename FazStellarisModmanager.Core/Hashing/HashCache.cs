using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using FazStellarisModmanager.Core.IO;

namespace FazStellarisModmanager.Core.Hashing;

internal sealed record HashCacheEntry(long Size, long MtimeTicks, string Md5);

/// <summary>Thread-safe MD5 cache keyed by full path; an entry is reused while size and mtime are unchanged.</summary>
public sealed class HashCache
{
    readonly ConcurrentDictionary<string, HashCacheEntry> _entries;
    readonly string? _file;
    int _misses;

    HashCache(string? file, ConcurrentDictionary<string, HashCacheEntry> entries)
    {
        _file = file;
        _entries = entries;
    }

    public static HashCache InMemory() => new(null, new(StringComparer.OrdinalIgnoreCase));

    public static HashCache Load(string file)
    {
        var entries = new ConcurrentDictionary<string, HashCacheEntry>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(file))
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, HashCacheEntry>>(File.ReadAllText(file));
                if (data is not null)
                    foreach (var (k, v) in data)
                        if (v?.Md5 is { Length: 32 }) entries[k] = v; // drop malformed entries from a hand-edited file
            }
            catch (JsonException)
            {
                // Corrupt cache: start empty. It is only a cache, so this is safe.
            }
            catch (IOException)
            {
                // I/O error: start empty. It is only a cache, so this is safe.
            }
            catch (UnauthorizedAccessException)
            {
                // Permission denied: start empty. It is only a cache, so this is safe.
            }
        }
        return new HashCache(file, entries);
    }

    public int Count => _entries.Count;

    /// <summary>Number of files actually hashed (cache misses) since this instance was created.</summary>
    public int Misses => _misses;

    /// <summary>Lower-case hex MD5 of the file.</summary>
    public string GetMd5(string absPath, out long size)
    {
        var info = new FileInfo(absPath);
        size = info.Length;
        var key = Path.GetFullPath(absPath);
        var mtime = info.LastWriteTimeUtc.Ticks;
        if (_entries.TryGetValue(key, out var e) && e.Size == size && e.MtimeTicks == mtime) return e.Md5;

        Interlocked.Increment(ref _misses);
        string md5;
        using (var fs = File.OpenRead(absPath)) md5 = Convert.ToHexStringLower(MD5.HashData(fs));
        _entries[key] = new HashCacheEntry(size, mtime, md5);
        return md5;
    }

    public void Save()
    {
        if (_file is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        AtomicFile.WriteAllText(_file, JsonSerializer.Serialize(new Dictionary<string, HashCacheEntry>(_entries)));
    }
}

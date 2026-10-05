using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace FazStellarisModmanager.Core.Library;

/// <summary>Small PNG icons for mods as data URIs. Each picture is shrunk once (disk cache keyed by picture and stamp) and remembered until <see cref="Refresh"/>. Never throws; failures give null.</summary>
public sealed class ModIconCache(string directory, Func<byte[], byte[]?> shrinkToPng)
{
    readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _memo = new(StringComparer.OrdinalIgnoreCase);

    readonly ConcurrentDictionary<string, string> _last = new(StringComparer.OrdinalIgnoreCase);

    static string MemoKey(InstalledMod mod) => mod.DescriptorRel + "|" + mod.ContentPath;

    /// <summary>The icon if it has already been loaded, else null. Does no IO (safe while rendering).</summary>
    public string? TryPeek(InstalledMod mod)
    {
        var key = MemoKey(mod);
        if (_memo.TryGetValue(key, out var lazy) && lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully && lazy.Value.Result is { } uri) return uri;
        return _last.GetValueOrDefault(key);
    }

    /// <summary>Forgets what was loaded so pictures are read again, but keeps the old icons visible (via <see cref="TryPeek"/>) until the new ones load.</summary>
    public void Refresh()
    {
        foreach (var (key, lazy) in _memo)
            if (lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully && lazy.Value.Result is { } uri) _last[key] = uri;
        _memo.Clear();
    }

    /// <summary>Loads the icon on the thread pool (once per mod per run).</summary>
    public async Task<string?> GetAsync(InstalledMod mod)
    {
        var key = MemoKey(mod);
        var lazy = _memo.GetOrAdd(key, _ => new Lazy<Task<string?>>(() => Task.Run(() => Load(mod))));
        var result = await lazy.Value;
        if (result is null) _memo.TryRemove(new KeyValuePair<string, Lazy<Task<string?>>>(key, lazy));   // let a later call retry
        return result;
    }

    /// <summary>Finds, shrinks (or reads from the disk cache) and encodes the icon. Does IO.</summary>
    public string? Load(InstalledMod mod)
    {
        try
        {
            var source = ModThumbnail.Find(mod);
            if (source is null) return null;
            var id = source.Id + "|" + ModThumbnail.Stamp(source);
            var path = Path.Combine(directory, Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(id))) + ".png");
            byte[]? png;
            if (File.Exists(path))
            {
                png = File.ReadAllBytes(path);
            }
            else
            {
                png = shrinkToPng(ModThumbnail.Read(source));
                if (png is null || png.Length == 0) return null;
                Write(path, png);
            }
            return "data:image/png;base64," + Convert.ToBase64String(png);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    // Atomic: other runs never see half a file.
    static void Write(string path, byte[] png)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, png);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

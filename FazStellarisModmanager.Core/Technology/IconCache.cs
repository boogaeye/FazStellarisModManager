using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>Finds tech icons across sources (last wins), converts DDS to PNG once, and serves data URIs.</summary>
public sealed class IconCache(string directory)
{
    public const string IconFolder = "gfx/interface/icons/technologies";

    readonly ConcurrentDictionary<string, string?> _memory = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"data:image/png;base64,…" for the icon, or null if no source has it or it can't be decoded.</summary>
    public string? DataUri(IReadOnlyList<ContentSource> sources, string iconKey)
    {
        if (iconKey.AsSpan().IndexOfAny('/', (char)92, ':') >= 0 || iconKey.Contains("..", StringComparison.Ordinal)) return null;
        var rel = $"{IconFolder}/{iconKey}.dds";
        try
        {
            var source = sources.LastOrDefault(s => s.Exists(rel));
            if (source is null) return null;
            var id = $"{source.Name}|{rel}|{source.Stamp(rel)}";
            return _memory.GetOrAdd(id, _ => Load(source, rel, id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            return null;
        }
    }

    string? Load(ContentSource source, string rel, string id)
    {
        var path = Path.Combine(directory, Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(id))) + ".png");
        try
        {
            byte[] png;
            if (File.Exists(path))
            {
                png = File.ReadAllBytes(path);
            }
            else
            {
                byte[] dds;
                using (var stream = source.Open(rel))
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    dds = buffer.ToArray();
                }
                var (w, h, rgba) = DdsDecoder.Decode(dds);
                png = PngEncoder.Encode(w, h, rgba);
                Directory.CreateDirectory(directory);
                var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(tmp, png);
                File.Move(tmp, path, overwrite: true);
            }
            return "data:image/png;base64," + Convert.ToBase64String(png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
        {
            return null;
        }
    }
}

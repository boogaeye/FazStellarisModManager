using System.Security.Cryptography;
using System.Text;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>Stateless decoder: finds tech icons across sources (last wins), converts DDS to PNG once (disk cache), returns data URIs. Does IO, so never call it on the UI thread.</summary>
public sealed class IconCache(string directory)
{
    public const string IconFolder = "gfx/interface/icons/technologies";

    /// <summary>"data:image/png;base64,…" for the icon, or null if no source has it or it can't be decoded.</summary>
    public string? DataUri(IReadOnlyList<ContentSource> sources, string iconKey)
    {
        if (iconKey.AsSpan().IndexOfAny('/', (char)92, ':') >= 0 || iconKey.Contains("..", StringComparison.Ordinal)) return null;
        var rel = $"{IconFolder}/{iconKey}.dds";
        try
        {
            for (var i = sources.Count - 1; i >= 0; i--)
            {
                var source = sources[i];
                if (!source.Exists(rel)) continue;
                var id = $"{source.Name}|{rel}|{source.Stamp(rel)}";
                return Load(source, rel, id);
            }
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Deletes cached PNGs not used (written or touched) within <paramref name="maxAge"/>. Errors are ignored.</summary>
    public void Prune(TimeSpan maxAge)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var file in Directory.EnumerateFiles(directory, "*.png"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
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
                try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { }
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
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}

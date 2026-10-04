using System.Security.Cryptography;
using System.Text;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>Stateless decoder: finds tech icons across sources (last wins), converts DDS to PNG once (disk cache), returns data URIs. Does IO, so never call it on the UI thread.</summary>
public sealed class IconCache(string directory)
{
    public const int MaxIconSize = 64;
    public const string IconFolder = "gfx/interface/icons/technologies";

    /// <summary>"data:image/png;base64,…" for the icon, or null if no source has it or it can't be decoded.</summary>
    public string? DataUri(IReadOnlyList<ContentSource> sources, string iconKey)
    {
        if (iconKey.AsSpan().IndexOfAny('/', (char)92, ':') >= 0 || iconKey.Contains("..", StringComparison.Ordinal)) return null;
        return DataUri(sources, IconResolver.ForTech(iconKey));
    }

    /// <summary>"data:image/png;base64,…" for the texture (cropped to its frame, scaled to fit <paramref name="maxSize"/>), or null when no source has it, the path is unsafe/not .dds, or it can't be decoded.</summary>
    public string? DataUri(IReadOnlyList<ContentSource> sources, IconRef icon, int maxSize = MaxIconSize)
    {
        var rel = icon.Path.Replace((char)92, '/');
        if (!rel.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || rel.StartsWith('/') || rel.Contains(':') || rel.Split('/').Contains("..")) return null;
        try
        {
            for (var i = sources.Count - 1; i >= 0; i--)
            {
                var source = sources[i];
                if (!source.Exists(rel)) continue;
                var id = $"{source.Name}|{icon.CacheId}|{source.Stamp(rel)}" + (maxSize == MaxIconSize ? "" : $"|{maxSize}");
                return Load(source, rel, icon, id, maxSize);
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
            var tmpCutoff = DateTime.UtcNow - TimeSpan.FromDays(1);
            foreach (var file in Directory.EnumerateFiles(directory, "*.png").Concat(Directory.EnumerateFiles(directory, "*.tmp")))
            {
                try
                {
                    var limit = file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ? tmpCutoff : cutoff;
                    if (File.GetLastWriteTimeUtc(file) < limit) File.Delete(file);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }

    string? Load(ContentSource source, string rel, IconRef icon, string id, int maxSize)
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
                if (icon.Frames > 1 && w >= icon.Frames) (w, h, rgba) = CropFrame(w, h, rgba, icon.Frame, icon.Frames);
                if (w > maxSize || h > maxSize) (w, h, rgba) = Downscale(w, h, rgba, maxSize);
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

    /// <summary>Frame f (1-based) of a horizontal strip of n frames: columns [(f-1)·w/n, f·w/n).</summary>
    static (int W, int H, byte[] Rgba) CropFrame(int w, int h, byte[] rgba, int frame, int frames)
    {
        var fw = w / frames;
        var x0 = (Math.Clamp(frame, 1, frames) - 1) * fw;
        var result = new byte[fw * h * 4];
        for (int y = 0; y < h; y++)
            Array.Copy(rgba, (y * w + x0) * 4, result, y * fw * 4, fw * 4);
        return (fw, h, result);
    }

    /// <summary>Box/area-averages RGBA down to fit within max x max, keeping the aspect ratio.</summary>
    static (int W, int H, byte[] Rgba) Downscale(int w, int h, byte[] rgba, int max)
    {
        var scale = Math.Min((double)max / w, (double)max / h);
        var w2 = Math.Max(1, (int)Math.Round(w * scale));
        var h2 = Math.Max(1, (int)Math.Round(h * scale));
        var result = new byte[w2 * h2 * 4];
        for (int y = 0; y < h2; y++)
        {
            int y0 = (int)((long)y * h / h2), y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * h / h2));
            for (int x = 0; x < w2; x++)
            {
                int x0 = (int)((long)x * w / w2), x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * w / w2));
                long r = 0, g = 0, b = 0, a = 0;
                var n = (y1 - y0) * (x1 - x0);
                for (int yy = y0; yy < y1; yy++)
                    for (int xx = x0; xx < x1; xx++)
                    {
                        var p = (yy * w + xx) * 4;
                        r += rgba[p]; g += rgba[p + 1]; b += rgba[p + 2]; a += rgba[p + 3];
                    }
                var o = (y * w2 + x) * 4;
                result[o] = (byte)(r / n); result[o + 1] = (byte)(g / n); result[o + 2] = (byte)(b / n); result[o + 3] = (byte)(a / n);
            }
        }
        return (w2, h2, result);
    }
}

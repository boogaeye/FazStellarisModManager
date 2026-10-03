using System.Globalization;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

public sealed record SpriteInfo(string Name, string TextureFile, int Frames);

/// <summary>GFX sprite name -> texture file (and sprite-sheet frame count), from interface/**/*.gfx. A later source's file at the same path replaces the earlier one; remaining files are read in load order, later wins per name.</summary>
public sealed class SpriteIndex
{
    readonly Dictionary<string, SpriteInfo> _sprites;

    SpriteIndex(Dictionary<string, SpriteInfo> sprites) => _sprites = sprites;

    public static SpriteIndex Empty { get; } = new(new Dictionary<string, SpriteInfo>(StringComparer.OrdinalIgnoreCase));

    public int Count => _sprites.Count;

    public SpriteInfo? Find(string name) => _sprites.TryGetValue(name, out var s) ? s : null;

    public static SpriteIndex Build(IReadOnlyList<ContentSource> sources, ICollection<string> warnings, CancellationToken ct = default)
    {
        var files = new Dictionary<string, (ContentSource Source, string Rel)>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            foreach (var rel in source.Files("interface", ".gfx"))
                files[rel] = (source, rel);

        var map = new Dictionary<string, SpriteInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, rel) in files.Values.OrderBy(f => f.Rel, TechDatabase.LoadOrder))
        {
            ct.ThrowIfCancellationRequested();
            string text;
            try { text = source.ReadText(rel); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
            {
                warnings.Add($"{source.Name}: {rel}: {ex.Message}");
                continue;
            }
            if (!text.Contains("spriteType", StringComparison.OrdinalIgnoreCase)) continue;
            Collect(ParadoxScriptParser.Parse(text), map);
        }
        return new SpriteIndex(map);
    }

    static void Collect(PdxBlock block, Dictionary<string, SpriteInfo> map)
    {
        foreach (var e in block.Entries)
        {
            if (e.Value is not PdxBlock b) continue;
            if (e.Key.Equals("spriteType", StringComparison.OrdinalIgnoreCase) || e.Key.Equals("frameAnimatedSpriteType", StringComparison.OrdinalIgnoreCase))
            {
                if (b.GetString("name") is { } name && b.GetString("texturefile") is { } texture)
                {
                    var frames = int.TryParse(b.GetString("noOfFrames"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1;
                    map[name] = new SpriteInfo(name, texture.Replace('\\', '/'), frames);
                }
            }
            else
            {
                Collect(b, map);
            }
        }
    }
}

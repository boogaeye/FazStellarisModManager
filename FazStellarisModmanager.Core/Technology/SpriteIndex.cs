using System.Globalization;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>A sprite. <see cref="DefaultFrame"/> is the 1-based default_frame of a sprite that references a sheet.</summary>
public sealed record SpriteInfo(string Name, string TextureFile, int Frames, int? DefaultFrame = null);

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

        var raw = new Dictionary<string, RawSprite>(StringComparer.OrdinalIgnoreCase);
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
            Collect(ParadoxScriptParser.Parse(text), raw);
        }
        return new SpriteIndex(Resolve(raw));
    }

    sealed record RawSprite(string Name, string? Texture, int Frames, string? SheetRef, int? DefaultFrame);

    const int MaxSheetHops = 3;

    static Dictionary<string, SpriteInfo> Resolve(Dictionary<string, RawSprite> raw)
    {
        var map = new Dictionary<string, SpriteInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in raw.Values)
        {
            var cur = entry;
            for (var hops = 0; cur.Texture is null && hops < MaxSheetHops; hops++)
            {
                if (cur.SheetRef is null || !raw.TryGetValue(cur.SheetRef, out var next)) break;
                cur = next;
            }
            if (cur.Texture is null) continue;
            map[entry.Name] = new SpriteInfo(entry.Name, cur.Texture, cur.Frames, entry.DefaultFrame);
        }
        return map;
    }

    static void Collect(PdxBlock block, Dictionary<string, RawSprite> map)
    {
        foreach (var e in block.Entries)
        {
            if (e.Value is not PdxBlock b) continue;
            if (e.Key.Equals("spriteType", StringComparison.OrdinalIgnoreCase) || e.Key.Equals("frameAnimatedSpriteType", StringComparison.OrdinalIgnoreCase))
            {
                if (b.GetString("name") is not { } name) continue;
                var texture = b.GetString("texturefile")?.Replace((char)92, '/');
                var sheet = b.GetString("sprite_sheet_sprite_type");
                if (texture is null && sheet is null) continue;
                var frames = int.TryParse(b.GetString("noOfFrames"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1;
                int? defaultFrame = int.TryParse(b.GetString("default_frame"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? Math.Max(d, 1) : null;
                map[name] = new RawSprite(name, texture, frames, sheet, defaultFrame);
            }
            else
            {
                Collect(b, map);
            }
        }
    }
}

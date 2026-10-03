namespace FazStellarisModmanager.Core.Technology;

/// <summary>A texture to show: relative path plus 1-based frame of a horizontal sprite sheet with <see cref="Frames"/> frames.</summary>
public sealed record IconRef(string Path, int Frame = 1, int Frames = 1)
{
    public string CacheId => Frames > 1 ? $"{Path}#{Frame}/{Frames}" : Path;
}

/// <summary>Turns icon fields (unlock objects) and modifier keys (stat bonuses) into texture references.</summary>
public static class IconResolver
{
    const string Icons = "gfx/interface/icons";

    public static IconRef ForTech(string iconKey) => new($"{IconCache.IconFolder}/{iconKey}.dds");

    /// <summary>
    /// 1) "GFX_…" -> sprite (frame = icon_frame, default 1, clamped). 2) a path ("/" or ".dds") as written.
    /// 3) a bare name -> sprite GFX_name, else icons/&lt;kind folder&gt;/name.dds. 4) no icon -> icons/&lt;kind folder&gt;/&lt;id&gt;.dds.
    /// </summary>
    public static IconRef? ForUnlock(string? icon, int? iconFrame, string kindFolder, string id, SpriteIndex sprites, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(icon)) return Existing($"{Icons}/{kindFolder}/{id}.dds", exists);
        icon = icon.Trim();
        if (icon.StartsWith("GFX_", StringComparison.OrdinalIgnoreCase)) return FromSprite(sprites.Find(icon), iconFrame);
        if (icon.Contains('/') || icon.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) return new IconRef(icon.Replace('\\', '/'));
        return FromSprite(sprites.Find("GFX_" + icon), iconFrame) ?? Existing($"{Icons}/{kindFolder}/{icon}.dds", exists);
    }

    /// <summary>icons/modifiers/mod_&lt;key&gt;.dds, or its _negative variant for a negative value when that file exists.</summary>
    public static IconRef? ForBonus(StatBonus bonus, Func<string, bool> exists) =>
        (bonus.IsNegative ? Existing($"{Icons}/modifiers/mod_{bonus.Key}_negative.dds", exists) : null)
        ?? Existing($"{Icons}/modifiers/mod_{bonus.Key}.dds", exists);

    static IconRef? FromSprite(SpriteInfo? sprite, int? frame) =>
        sprite is null ? null : new IconRef(sprite.TextureFile, Math.Clamp(frame ?? 1, 1, sprite.Frames), sprite.Frames);

    static IconRef? Existing(string path, Func<string, bool> exists) => exists(path) ? new IconRef(path) : null;
}

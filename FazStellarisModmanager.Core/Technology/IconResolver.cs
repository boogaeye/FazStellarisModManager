using System.Text.RegularExpressions;

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
    /// 3) a bare name -> sprite GFX_name, else icons/&lt;kind folder&gt;/name.dds. 4) no icon -> icons/&lt;kind folder&gt;/&lt;id&gt;.dds, else sprite GFX_&lt;id&gt;.
    /// </summary>
    public static IconRef? ForUnlock(string? icon, int? iconFrame, string kindFolder, string id, SpriteIndex sprites, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(icon)) return Existing($"{Icons}/{kindFolder}/{id}.dds", exists) ?? FromSprite(sprites.Find("GFX_" + id), iconFrame);
        icon = icon.Trim();
        if (icon.StartsWith("GFX_", StringComparison.OrdinalIgnoreCase)) return FromSprite(sprites.Find(icon), iconFrame);
        if (icon.Contains('/') || icon.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) return new IconRef(icon.Replace('\\', '/'));
        return FromSprite(sprites.Find("GFX_" + icon), iconFrame) ?? Existing($"{Icons}/{kindFolder}/{icon}.dds", exists);
    }

    static readonly Regex ShipFamily = new(
        @"^(?:shipsize|ship)_(?:offspring_)?.+?_(build_speed_mult|cost_mult|hull_mult|hull_add|damage_mult|armor_mult|shield_mult|speed_mult|evasion_add|upkeep_mult)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly string[] TagSpritePrefixes = ["GFX_text_", "GFX_resource_", "GFX_"];

    // Scope prefixes the game's modifier icons often leave out: country_influence_produces_mult uses mod_influence_produces_mult.
    static readonly string[] ScopePrefixes = ["country_", "planet_", "pop_", "species_"];

    static readonly Regex ResourceModifier = new(
        @"^(?:(?:country|planet|pop|species)_)?([a-z_]+?)_(?:produces|upkeep|cost)_(?:mult|add)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Modifier icon, first hit wins: negative variants (mod_&lt;key&gt;_negative, mod_negative_&lt;key&gt;) for a negative value; mod_&lt;key&gt;; mod_&lt;key&gt;_positive;
    /// the same without a scope prefix (country_, planet_, pop_, species_); the sprite named by the first inline tag of the modifier's name;
    /// icons/jobs/&lt;tag&gt;.dds for a job_ tag;
    /// the resource icon (GFX_resource_&lt;res&gt;) for resource production/upkeep modifiers; the general ship icon for per-ship-size modifiers.
    /// </summary>
    public static IconRef? ForBonus(StatBonus bonus, Func<string, bool> exists, SpriteIndex? sprites = null)
    {
        string Mod(string file) => $"{Icons}/modifiers/{file}.dds";
        var key = bonus.Key;
        if (bonus.IsNegative && (Existing(Mod($"mod_{key}_negative"), exists) ?? Existing(Mod($"mod_negative_{key}"), exists)) is { } neg) return neg;
        if ((Existing(Mod($"mod_{key}"), exists) ?? Existing(Mod($"mod_{key}_positive"), exists)) is { } direct) return direct;
        foreach (var scope in ScopePrefixes)
        {
            if (!key.StartsWith(scope, StringComparison.OrdinalIgnoreCase)) continue;
            var bare = key[scope.Length..];
            if (bonus.IsNegative && (Existing(Mod($"mod_{bare}_negative"), exists) ?? Existing(Mod($"mod_negative_{bare}"), exists)) is { } bareNeg)
                return bareNeg;
            if (Existing(Mod("mod_" + bare), exists) is { } unscoped) return unscoped;
        }

        if (bonus.IconTag is { Length: > 0 } tag && sprites is not null)
            foreach (var prefix in TagSpritePrefixes)
                if (FromExistingSprite(sprites.Find(prefix + tag), exists) is { } tagged) return tagged;
        // Job icons from [job.GetIcon] often have no sprite, only the file.
        if (bonus.IconTag is { Length: > 0 } jobTag && jobTag.StartsWith("job_", StringComparison.OrdinalIgnoreCase)
            && Existing($"{Icons}/jobs/{jobTag}.dds", exists) is { } job)
            return job;

        if (sprites is not null && ResourceModifier.Match(key) is { Success: true } r
            && FromExistingSprite(sprites.Find("GFX_resource_" + r.Groups[1].Value), exists) is { } resource)
            return resource;

        if (ShipFamily.Match(key) is { Success: true } m)
        {
            var stat = m.Groups[1].Value.ToLowerInvariant();
            string[] general = stat switch
            {
                "build_speed_mult" => ["mod_ship_build_speed_mult"],
                "cost_mult" => ["mod_ship_build_cost_mult", "mod_ship_cost_mult"],
                _ => ["mod_ship_" + stat],
            };
            foreach (var g in general)
                if (Existing(Mod(g), exists) is { } icon) return icon;
        }
        return null;
    }

    static IconRef? FromExistingSprite(SpriteInfo? sprite, Func<string, bool> exists) =>
        sprite is not null && exists(sprite.TextureFile) ? FromSprite(sprite, null) : null;

    static IconRef? FromSprite(SpriteInfo? sprite, int? frame) =>
        sprite is null ? null : new IconRef(sprite.TextureFile, Math.Clamp(frame ?? sprite.DefaultFrame ?? 1, 1, sprite.Frames), sprite.Frames);

    static IconRef? Existing(string path, Func<string, bool> exists) => exists(path) ? new IconRef(path) : null;
}

using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class SpriteIconTests
{
    static SpriteIndex Sprites(TempDir tmp)
    {
        tmp.Write("g/interface/a.gfx", """
            spriteTypes = {
            	spriteType = { name = "GFX_a" texturefile = "gfx/a.dds" }
            	spriteType = { name = "GFX_sheet" textureFile = "gfx/sheet.dds" noOfFrames = 3 }
            	frameAnimatedSpriteType = { name = "GFX_anim" texturefile = "gfx/anim.dds" noOfFrames = 2 }
            	spriteType = { name = "GFX_building_capital" texturefile = "gfx/sprites/capital.dds" }
            }
            """);
        tmp.Write("m/interface/b.gfx", "spriteTypes = { spriteType = { name = \"GFX_a\" texturefile = \"gfx/mod_a.dds\" } }");
        using var g = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        using var m = ContentSource.FromPath("Mod", Path.Combine(tmp.Path, "m"));
        var warnings = new List<string>();
        var index = SpriteIndex.Build([g, m], warnings);
        Assert.Empty(warnings);
        return index;
    }

    [Fact]
    public void Indexes_sprites_case_insensitively_with_frames_and_later_sources_winning()
    {
        using var tmp = new TempDir();

        var s = Sprites(tmp);

        Assert.Equal(4, s.Count);
        Assert.Equal("gfx/mod_a.dds", s.Find("gfx_a")!.TextureFile);
        Assert.Equal(("gfx/sheet.dds", 3), (s.Find("GFX_sheet")!.TextureFile, s.Find("GFX_sheet")!.Frames));
        Assert.Equal(2, s.Find("GFX_anim")!.Frames);
        Assert.Equal(1, s.Find("GFX_a")!.Frames);
        Assert.Null(s.Find("GFX_missing"));
    }

    [Fact]
    public void A_later_file_at_the_same_path_replaces_the_earlier_one()
    {
        using var tmp = new TempDir();
        tmp.Write("g/interface/a.gfx", "spriteTypes = { spriteType = { name = \"GFX_old\" texturefile = \"gfx/old.dds\" } }");
        tmp.Write("m/interface/a.gfx", "spriteTypes = { spriteType = { name = \"GFX_new\" texturefile = \"gfx/new.dds\" } }");
        using var g = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        using var m = ContentSource.FromPath("Mod", Path.Combine(tmp.Path, "m"));

        var index = SpriteIndex.Build([g, m], new List<string>());

        Assert.Null(index.Find("GFX_old"));
        Assert.NotNull(index.Find("GFX_new"));
    }

    [Fact]
    public void Falls_back_to_a_GFX_sprite_named_after_the_id()
    {
        using var tmp = new TempDir();
        tmp.Write("g/interface/a.gfx", "spriteTypes = { spriteType = { name = \"GFX_module_x\" texturefile = \"gfx/mx.dds\" } }");
        using var g = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        var index = SpriteIndex.Build([g], new List<string>());

        Assert.Equal(new IconRef("gfx/mx.dds"), IconResolver.ForUnlock(null, null, "starbase_modules", "module_x", index, _ => false));
    }

    [Fact]
    public void Resolves_unlock_icons_by_each_rule()
    {
        using var tmp = new TempDir();
        var s = Sprites(tmp);
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gfx/interface/icons/buildings/building_lab.dds",
            "gfx/interface/icons/decisions/decision_resources.dds",
        };
        bool Exists(string p) => existing.Contains(p);

        Assert.Equal(new IconRef("gfx/sheet.dds", 2, 3), IconResolver.ForUnlock("GFX_sheet", 2, "x", "id", s, Exists));
        Assert.Equal(new IconRef("gfx/sheet.dds", 1, 3), IconResolver.ForUnlock("GFX_sheet", null, "x", "id", s, Exists));
        Assert.Equal(new IconRef("gfx/sheet.dds", 3, 3), IconResolver.ForUnlock("GFX_sheet", 9, "x", "id", s, Exists));
        Assert.Null(IconResolver.ForUnlock("GFX_missing", null, "x", "id", s, Exists));
        Assert.Equal(new IconRef("gfx/interface/icons/traits/t.dds"), IconResolver.ForUnlock("gfx/interface/icons/traits/t.dds", null, "traits", "t", s, Exists));
        Assert.Equal(new IconRef("gfx/sprites/capital.dds"), IconResolver.ForUnlock("building_capital", null, "buildings", "b", s, Exists));
        Assert.Equal(new IconRef("gfx/interface/icons/decisions/decision_resources.dds"), IconResolver.ForUnlock("decision_resources", null, "decisions", "d", s, Exists));
        Assert.Equal(new IconRef("gfx/interface/icons/buildings/building_lab.dds"), IconResolver.ForUnlock(null, null, "buildings", "building_lab", s, Exists));
        Assert.Null(IconResolver.ForUnlock(null, null, "buildings", "building_none", s, Exists));
    }

    [Fact]
    public void Resolves_bonus_and_tech_icons()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gfx/interface/icons/modifiers/mod_a_mult.dds",
            "gfx/interface/icons/modifiers/mod_a_mult_negative.dds",
            "gfx/interface/icons/modifiers/mod_b_add.dds",
        };
        bool Exists(string p) => existing.Contains(p);
        static StatBonus B(string key, bool negative) => new(key, "1", key, "+1", negative);

        Assert.Equal(new IconRef("gfx/interface/icons/modifiers/mod_a_mult_negative.dds"), IconResolver.ForBonus(B("a_mult", true), Exists));
        Assert.Equal(new IconRef("gfx/interface/icons/modifiers/mod_a_mult.dds"), IconResolver.ForBonus(B("a_mult", false), Exists));
        Assert.Equal(new IconRef("gfx/interface/icons/modifiers/mod_b_add.dds"), IconResolver.ForBonus(B("b_add", true), Exists));
        Assert.Null(IconResolver.ForBonus(B("c_mult", false), Exists));
        Assert.Equal(new IconRef("gfx/interface/icons/technologies/tech_a.dds"), IconResolver.ForTech("tech_a"));
        Assert.Equal("gfx/sheet.dds#2/3", new IconRef("gfx/sheet.dds", 2, 3).CacheId);
        Assert.Equal("gfx/a.dds", new IconRef("gfx/a.dds").CacheId);
    }
}

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

    static SpriteIndex SheetIndex(TempDir tmp)
    {
        tmp.Write("g/interface/a_refs.gfx", """
            spriteTypes = {
            	spriteType = { name = "GFX_ship_size_mauler_stage_2" sprite_sheet_sprite_type = "GFX_ship_sizes" default_frame = 16 }
            	spriteType = { name = "GFX_zero" sprite_sheet_sprite_type = "GFX_ship_sizes" default_frame = 0 }
            	spriteType = { name = "GFX_dangling" sprite_sheet_sprite_type = "GFX_nowhere" default_frame = 2 }
            	spriteType = { name = "GFX_loop_a" sprite_sheet_sprite_type = "GFX_loop_b" }
            	spriteType = { name = "GFX_loop_b" sprite_sheet_sprite_type = "GFX_loop_a" }
            }
            """);
        tmp.Write("g/interface/z_sheets.gfx", "spriteTypes = { spriteType = { name = \"GFX_ship_sizes\" textureFile = \"gfx/interface/icons/ship_sizes.dds\" noOfFrames = 29 } }");
        using var g = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        return SpriteIndex.Build([g], new List<string>());
    }

    [Fact]
    public void Sprite_sheet_references_resolve_regardless_of_order_or_file()
    {
        using var tmp = new TempDir();
        var s = SheetIndex(tmp);

        var r = s.Find("GFX_ship_size_mauler_stage_2")!;
        Assert.Equal(("gfx/interface/icons/ship_sizes.dds", 29, 16), (r.TextureFile, r.Frames, r.DefaultFrame));
        Assert.Equal(1, s.Find("GFX_zero")!.DefaultFrame);
        Assert.Null(s.Find("GFX_dangling"));
        Assert.Null(s.Find("GFX_loop_a"));
        Assert.Null(s.Find("GFX_ship_sizes")!.DefaultFrame);
    }

    [Fact]
    public void Unlock_icons_use_the_default_frame_unless_icon_frame_overrides()
    {
        using var tmp = new TempDir();
        var s = SheetIndex(tmp);

        Assert.Equal(new IconRef("gfx/interface/icons/ship_sizes.dds", 16, 29), IconResolver.ForUnlock("ship_size_mauler_stage_2", null, "ship_sizes", "x", s, _ => false));
        Assert.Equal(new IconRef("gfx/interface/icons/ship_sizes.dds", 3, 29), IconResolver.ForUnlock("ship_size_mauler_stage_2", 3, "ship_sizes", "x", s, _ => false));
    }

    const string Mod = "gfx/interface/icons/modifiers/";

    static IconRef? Bonus(string key, bool negative, string[] files, SpriteIndex? sprites = null, string? tag = null) =>
        IconResolver.ForBonus(new StatBonus(key, "1", key, "+1", negative, tag), new HashSet<string>(files, StringComparer.OrdinalIgnoreCase).Contains, sprites);

    [Fact]
    public void Bonus_icons_try_name_variants_in_order()
    {
        Assert.Equal(new IconRef(Mod + "mod_k_negative.dds"), Bonus("k", true, [Mod + "mod_k_negative.dds", Mod + "mod_negative_k.dds", Mod + "mod_k.dds"]));
        Assert.Equal(new IconRef(Mod + "mod_negative_k.dds"), Bonus("k", true, [Mod + "mod_negative_k.dds", Mod + "mod_k.dds"]));
        Assert.Equal(new IconRef(Mod + "mod_k.dds"), Bonus("k", true, [Mod + "mod_k.dds", Mod + "mod_k_positive.dds"]));
        Assert.Equal(new IconRef(Mod + "mod_k_positive.dds"), Bonus("k", false, [Mod + "mod_k_positive.dds"]));
        Assert.Null(Bonus("k", false, [Mod + "mod_negative_k.dds"]));
    }

    [Fact]
    public void Bonus_icons_fall_back_to_the_inline_tag_sprite()
    {
        using var tmp = new TempDir();
        tmp.Write("g/interface/a.gfx", """
            spriteTypes = {
            	spriteType = { name = "GFX_text_food" texturefile = "gfx/text_food.dds" }
            	spriteType = { name = "GFX_resource_minerals" texturefile = "gfx/res_min.dds" }
            	spriteType = { name = "GFX_zz" texturefile = "gfx/zz.dds" }
            	spriteType = { name = "GFX_gone" texturefile = "gfx/gone.dds" }
            	spriteType = { name = "GFX_ship_sizes" textureFile = "gfx/sizes.dds" noOfFrames = 4 }
            	spriteType = { name = "GFX_text_sheet" sprite_sheet_sprite_type = "GFX_ship_sizes" default_frame = 3 }
            }
            """);
        using var g = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        var s = SpriteIndex.Build([g], new List<string>());
        string[] files = ["gfx/text_food.dds", "gfx/res_min.dds", "gfx/zz.dds", "gfx/sizes.dds"];

        Assert.Equal(new IconRef("gfx/text_food.dds"), Bonus("k", false, files, s, "food"));
        Assert.Equal(new IconRef("gfx/res_min.dds"), Bonus("k", false, files, s, "minerals"));
        Assert.Equal(new IconRef("gfx/zz.dds"), Bonus("k", false, files, s, "zz"));
        Assert.Equal(new IconRef("gfx/sizes.dds", 3, 4), Bonus("k", false, files, s, "sheet"));
        Assert.Null(Bonus("k", false, files, s, "gone"));
        Assert.Null(Bonus("k", false, files, s, "unknown"));
        Assert.Null(Bonus("k", false, files, null, "food"));
    }

    [Fact]
    public void Per_ship_size_bonuses_fall_back_to_the_general_ship_icon()
    {
        Assert.Equal(new IconRef(Mod + "mod_ship_build_speed_mult.dds"), Bonus("shipsize_corvette_build_speed_mult", false, [Mod + "mod_ship_build_speed_mult.dds"]));
        Assert.Equal(new IconRef(Mod + "mod_ship_cost_mult.dds"), Bonus("shipsize_corvette_cost_mult", false, [Mod + "mod_ship_cost_mult.dds"]));
        Assert.Equal(new IconRef(Mod + "mod_ship_build_cost_mult.dds"), Bonus("ship_offspring_x_cost_mult", false, [Mod + "mod_ship_cost_mult.dds", Mod + "mod_ship_build_cost_mult.dds"]));
        Assert.Equal(new IconRef(Mod + "mod_ship_hull_mult.dds"), Bonus("shipsize_battleship_hull_mult", false, [Mod + "mod_ship_hull_mult.dds"]));
        Assert.Null(Bonus("shipsize_battleship_hull_mult", false, []));
        Assert.Null(Bonus("shipsize_battleship_other", false, [Mod + "mod_ship_other.dds"]));
    }
}

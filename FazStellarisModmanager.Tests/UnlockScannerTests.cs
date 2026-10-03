using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class UnlockScannerTests
{
    static TechDatabase Build(TempDir tmp)
    {
        tmp.Write("g/common/technology/00_t.txt", "tech_lasers_2 = { area = physics }\ntech_x = { area = physics }\ntech_lasers_3 = { area = physics prerequisites = { \"tech_lasers_2\" } }\n");
        tmp.Write("g/common/component_templates/00_w.txt", """
            weapon_component_template = {
            	key = "SMALL_BLUE_LASER"
            	icon = "GFX_ship_part_laser_1"
            	icon_frame = 2
            	prerequisites = { "tech_lasers_2" }
            }
            weapon_component_template = {
            	key = "OTHER"
            	prerequisites = { "tech_other" }
            }
            """);
        tmp.Write("g/common/buildings/00_b.txt", "building_lab = { prerequisites = { \"tech_lasers_2\" \"tech_x\" } }\nbuilding_plain = { potential = { always = yes } }\n");
        tmp.Write("g/common/starbase_modules/00_s.txt", "module_x = { show_in_tech = \"tech_lasers_2\" }\n");
        tmp.Write("g/common/inline_scripts/x.txt", "y = { prerequisites = { \"tech_lasers_2\" } }\n");
        tmp.Write("g/common/zones/00_z.txt", "zone_cap = { prerequisites = { \"tech_lasers_2\" } }\n");
        tmp.Write("g/common/my_custom_things/00.txt", "thing = { prerequisites = { \"tech_lasers_2\" } }\n");
        tmp.Write("g/interface/ships.gfx", "spriteTypes = { spriteType = { name = \"GFX_ship_part_laser_1\" texturefile = \"gfx/l.dds\" noOfFrames = 3 } }\n");
        tmp.Write("g/localisation/english/c_l_english.yml", "l_english:\n SMALL_BLUE_LASER:0 \"Small Blue Laser\"\n building_lab:0 \"Research Lab\"\n");
        tmp.Write("m/common/buildings/zz_b.txt", "building_lab = { prerequisites = { \"tech_x\" } }\n");
        using var g = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        using var m = ContentSource.FromPath("Mod", Path.Combine(tmp.Path, "m"));
        return TechDatabase.Build([g, m]);
    }

    [Fact]
    public void Finds_unlocks_by_prerequisites_show_in_tech_and_key_with_overrides()
    {
        using var tmp = new TempDir();

        var db = Build(tmp);

        var lasers = db.Unlocks("tech_lasers_2");
        Assert.Equal(new[]
        {
            ("My custom things", "thing"),
            ("Ship components", "SMALL_BLUE_LASER"),
            ("Starbase modules", "module_x"),
            ("Zones", "zone_cap"),
        }, lasers.Select(u => (u.Kind, u.Id)));
        var laser = lasers.Single(u => u.Id == "SMALL_BLUE_LASER");
        Assert.Equal(("Small Blue Laser", "component_templates", "GFX_ship_part_laser_1", (int?)2, "Base game"),
            (laser.Name, laser.KindFolder, laser.Icon, laser.IconFrame, laser.Source.SourceName));

        var lab = Assert.Single(db.Unlocks("TECH_X"));
        Assert.Equal(("Research Lab", "Mod", "common/buildings/zz_b.txt"), (lab.Name, lab.Source.SourceName, lab.Source.File));

        Assert.Empty(db.Unlocks("tech_nothing"));
        Assert.Equal(6, db.AllUnlocks.Count); // thing, SMALL_BLUE_LASER, OTHER, module_x, zone_cap, building_lab
        Assert.Equal(1, db.Sprites.Count);
    }

    [Fact]
    public void A_mod_file_redefining_an_object_without_the_keyword_clears_the_old_unlock()
    {
        using var tmp = new TempDir();
        tmp.Write("g/common/technology/00_t.txt", "tech_a = { area = physics }");
        tmp.Write("g/common/buildings/00_b.txt", "building_x = { prerequisites = { \"tech_a\" } }");
        tmp.Write("m/common/buildings/zz_b.txt", "building_x = { cost = 5 }");
        using var g = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        using var m = ContentSource.FromPath("Mod", Path.Combine(tmp.Path, "m"));

        var db = TechDatabase.Build([g, m]);

        Assert.Empty(db.Unlocks("tech_a"));
        Assert.DoesNotContain(db.AllUnlocks, u => u.Id == "building_x");
    }

    [Fact]
    public void Names_use_folder_prefixes()
    {
        using var tmp = new TempDir();
        tmp.Write("g/common/technology/00_t.txt", "tech_a = { area = physics }");
        tmp.Write("g/common/edicts/00_e.txt", "test_edict = { prerequisites = { \"tech_a\" } }");
        tmp.Write("g/common/starbase_modules/00_s.txt", "mod_y = { prerequisites = { \"tech_a\" } }");
        tmp.Write("g/localisation/english/c_l_english.yml", "l_english:\n edict_test_edict:0 \"Test Edict\"\n sm_mod_y:0 \"Module Y\"\n");
        using var g = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);

        var db = TechDatabase.Build([g]);

        var names = db.Unlocks("tech_a").ToDictionary(u => u.Id, u => u.Name);
        Assert.Equal("Test Edict", names["test_edict"]);
        Assert.Equal("Module Y", names["mod_y"]);
    }

    [Fact]
    public void Policy_options_with_prerequisites_become_their_own_unlocks()
    {
        using var tmp = new TempDir();
        tmp.Write("g/common/technology/00_t.txt", "tech_a = { area = physics }");
        tmp.Write("g/common/policies/00_p.txt", "policy_p = { option = { name = \"opt_a\" prerequisites = { \"tech_a\" } } option = { name = \"opt_b\" } }");
        using var g = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);

        var db = TechDatabase.Build([g]);

        var u = Assert.Single(db.Unlocks("tech_a"));
        Assert.Equal(("opt_a", "Policies", "policies"), (u.Id, u.Kind, u.KindFolder));
    }

    [Theory]
    [InlineData("component_templates", "Ship components")]
    [InlineData("bypass", "Bypasses")]
    [InlineData("some_new_thing", "Some new thing")]
    public void Names_kinds(string folder, string expected) =>
        Assert.Equal(expected, UnlockScanner.KindName(folder));
}

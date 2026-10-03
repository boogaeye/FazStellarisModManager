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

    [Theory]
    [InlineData("component_templates", "Ship components")]
    [InlineData("bypass", "Bypasses")]
    [InlineData("some_new_thing", "Some new thing")]
    public void Names_kinds(string folder, string expected) =>
        Assert.Equal(expected, UnlockScanner.KindName(folder));
}

using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class TechDetailsTests
{
    const string Techs = """
        tech_x = {
        	area = physics
        	weight = @w
        	gateway = energy_weapons
        	modifier = { army_damage_mult = 0.05 ship_speed_mult = -0.1 country_admin_cap_add = 2 weird_flag = yes }
        	feature_flags = { unlocks_auto_research some_flag }
        	prereqfor_desc = { ship = { title = "T_SHIP" desc = "T_SHIP_DESC" } custom = { title = t_custom } }
        	technology_swap = { name = tech_y inherit_effects = yes trigger = { country_uses_bio_ships = yes } }
        	weight_modifier = { modifier = { factor = 1.25 has_ethic = ethic_militarist } }
        	ai_weight = { weight = 2 }
        	potential = { always = yes }
        }
        tech_y = { area = physics }
        """;

    const string Loc = "l_english:\n MOD_ARMY_DAMAGE_MULT:0 \"Army Damage\"\n mod_ship_speed_mult:0 \"Ship Speed\"\n T_SHIP:0 \"Science Ship\"\n T_SHIP_DESC:0 \"Build science ships\"\n t_custom:0 \"Particle storm\"\n ethic_militarist:0 \"Militarist\"\n";

    static TechDatabase Build(TempDir tmp)
    {
        tmp.Write("g/common/technology/00_t.txt", Techs);
        tmp.Write("g/common/scripted_variables/00_v.txt", "@w = 95\n");
        tmp.Write("g/localisation/english/t_l_english.yml", Loc);
        using var source = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        return TechDatabase.Build([source]);
    }

    [Fact]
    public void Formats_stat_bonuses()
    {
        using var tmp = new TempDir();

        var bonuses = Build(tmp).Techs["tech_x"].Details.Bonuses;

        Assert.Equal(new[]
        {
            ("army_damage_mult", "+5%", "Army Damage", false),
            ("ship_speed_mult", "-10%", "Ship Speed", true),
            ("country_admin_cap_add", "+2", "country_admin_cap_add", false),
            ("weird_flag", "yes", "weird_flag", false),
        }, bonuses.Select(b => (b.Key, b.Display, b.Name, b.IsNegative)));
    }

    [Fact]
    public void Reads_unlock_text_flags_gateway_swaps_and_weight()
    {
        using var tmp = new TempDir();

        var d = Build(tmp).Techs["tech_x"].Details;

        Assert.Equal(new[] { ("ship", "Science Ship", (string?)"Build science ships"), ("custom", "Particle storm", null) },
            d.CustomUnlocks.Select(c => (c.Kind, c.Title, c.Description)));
        Assert.Equal(new[] { "unlocks_auto_research", "some_flag" }, d.FeatureFlags);
        Assert.Equal("energy_weapons", d.Gateway);
        Assert.Equal("95", d.Weight);
        var swap = Assert.Single(d.Swaps);
        Assert.Equal(("tech_y", "country_uses_bio_ships = yes", true), (swap.Name, swap.TriggerScript, swap.InheritsEffects));
    }

    [Fact]
    public void Prints_weights_conditions_and_raw_definition()
    {
        using var tmp = new TempDir();

        var d = Build(tmp).Techs["tech_x"].Details;

        Assert.Equal("modifier = {\n    factor = 1.25\n    has_ethic = ethic_militarist   # Militarist\n}", d.WeightModifierScript);
        Assert.Equal("weight = 2", d.AiWeightScript);
        Assert.Equal("always = yes", d.PotentialScript);
        Assert.StartsWith("tech_x = {\n    area = physics\n    weight = @w\n", d.RawScript);
    }

    [Fact]
    public void A_plain_tech_has_empty_details()
    {
        using var tmp = new TempDir();

        var d = Build(tmp).Techs["tech_y"].Details;

        Assert.Empty(d.Bonuses);
        Assert.Empty(d.CustomUnlocks);
        Assert.Null(d.Weight);
        Assert.Null(d.PotentialScript);
        Assert.Equal("tech_y = {\n    area = physics\n}", d.RawScript);
    }

    [Fact]
    public void Formats_resolved_tiny_and_zero_bonuses_and_drops_empty_scripts()
    {
        using var tmp = new TempDir();
        tmp.Write("g/common/technology/00_t.txt", "tech_z = { area = physics potential = { } modifier = { x_mult = @b y_mult = 0.00001 z_add = 0 n_add = nan } }");
        tmp.Write("g/common/scripted_variables/00_v.txt", "@b = 0.1\n");
        using var source = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);

        var d = TechDatabase.Build([source]).Techs["tech_z"].Details;

        Assert.Equal(new[] { "+10%", "+0.001%", "0", "nan" }, d.Bonuses.Select(b => b.Display));
        Assert.Null(d.PotentialScript);
    }

    [Fact]
    public void Tooltip_modifier_entries_are_not_bonuses()
    {
        using var tmp = new TempDir();
        tmp.Write("g/common/technology/00_t.txt", "tech_z = { area = physics modifier = { army_damage_mult = 0.05 custom_tooltip = tt_key } technology_swap = { name = s trigger = { } } }");
        tmp.Write("g/localisation/english/t_l_english.yml", "l_english:\n tt_key:0 \"Something special\"\n");
        using var source = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);

        var d = TechDatabase.Build([source]).Techs["tech_z"].Details;

        Assert.Single(d.Bonuses);
        var cu = Assert.Single(d.CustomUnlocks);
        Assert.Equal(("tooltip", "Something special", (string?)null), (cu.Kind, cu.Title, cu.Description));
        Assert.Null(Assert.Single(d.Swaps).TriggerScript);
    }

    [Fact]
    public void Annotations_use_a_cache_and_skip_stop_words()
    {
        var loc = new Localisation();
        loc.AddText("l_english:\n always:0 \"Always\"\n foo:0 \"Foo Name\"\n");
        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var wrapper = FazStellarisModmanager.Core.Descriptors.ParadoxScriptParser.Parse("t = { potential = { x = always y = foo } }").GetBlock("t")!;

        var d = TechDetailsBuilder.Build("t", wrapper, loc, new Dictionary<string, string>(), new Dictionary<string, string>(), cache);

        Assert.Equal("x = always\ny = foo   # Foo Name", d.PotentialScript);
        Assert.True(cache.ContainsKey("foo"));
        Assert.False(cache.ContainsKey("always"));
    }
}

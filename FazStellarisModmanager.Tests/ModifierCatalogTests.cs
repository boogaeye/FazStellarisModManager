using FazStellarisModmanager.Core.Diplomacy;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ModifierCatalogTests
{
    [Fact]
    public void Reads_each_category_container_options_and_variables()
    {
        using var t = new TempDir();
        t.Write("game/common/scripted_variables/00_vars.txt", "@global_dw = 0.4");
        t.Write("game/common/traditions/00_t.txt", """
            @local_dw = 0.2
            tr_politics_finish = { modifier = { diplo_weight_mult = @local_dw } possible = { diplo_weight_mult = 9 } }
            tr_nothing = { modifier = { unity_produces_mult = 0.1 } }
            """);
        t.Write("game/common/governments/civics/00_c.txt", "civic_galactic_sovereign = { modifier = { diplo_weight_mult = @global_dw } }");
        t.Write("game/common/policies/00_p.txt", """
            diplomatic_stance = {
                option = { name = "diplo_stance_condescending_authority_advanced" modifier = { diplo_weight_mult = 0.5 } }
                option = { name = "diplo_stance_cooperative" modifier = { diplo_weight_mult = 0.1 } }
            }
            """);
        t.Write("game/common/static_modifiers/00_s.txt", "council_member = { diplo_weight_mult = 0.2 }  galactic_market_founder = { diplo_weight_economy_mult = 0.15 }");
        t.Write("game/common/megastructures/00_m.txt", "interstellar_assembly_4 = { country_modifier = { diplo_weight_mult = 0.4 } }");
        t.Write("game/common/resolutions/00_r.txt", "resolution_mutualdefense_renegade_containment = { modifier = { diplo_weight_naval_mult = 1 } }");
        t.Write("game/common/relics/00_r.txt", "r_ancient_sword = { passive_modifier = { diplo_weight_mult = 0.1 } }");
        using var game = ContentSource.FromPath("game", Path.Combine(t.Path, "game"), isBaseGame: true);

        var c = ModifierCatalog.Load([game]);
        Assert.Equal(0.2, c.Get(DiploSource.Tradition, "tr_politics_finish")!.Overall, 6);
        Assert.Null(c.Get(DiploSource.Tradition, "tr_nothing"));
        Assert.Equal(0.4, c.Get(DiploSource.Civic, "civic_galactic_sovereign")!.Overall, 6);
        Assert.Equal(0.5, c.Get(DiploSource.Policy, "diplo_stance_condescending_authority_advanced")!.Overall, 6);
        Assert.Equal(0.2, c.Get(DiploSource.StaticModifier, "council_member")!.Overall, 6);
        Assert.Equal(0.15, c.Get(DiploSource.StaticModifier, "galactic_market_founder")!.Economy, 6);
        Assert.Equal(0.4, c.Get(DiploSource.Megastructure, "interstellar_assembly_4")!.Overall, 6);
        Assert.Equal(1, c.Get(DiploSource.Resolution, "resolution_mutualdefense_renegade_containment")!.Naval, 6);
        Assert.Equal(0.1, c.Get(DiploSource.Relic, "r_ancient_sword")!.Overall, 6);
    }

    [Fact]
    public void Later_definition_wins_even_when_it_removes_the_bonus()
    {
        using var t = new TempDir();
        t.Write("game/common/edicts/00_e.txt", "diplomatic_grants = { modifier = { diplo_weight_mult = 0.1 } }  other = { modifier = { diplo_weight_mult = 0.3 } }");
        t.Write("mod/common/edicts/zz_e.txt", "diplomatic_grants = { modifier = { diplo_weight_mult = 0.25 } }  other = { modifier = { } }");
        using var game = ContentSource.FromPath("game", Path.Combine(t.Path, "game"), isBaseGame: true);
        using var mod = ContentSource.FromPath("mod", Path.Combine(t.Path, "mod"));
        var c = ModifierCatalog.Load([game, mod]);
        Assert.Equal(0.25, c.Get(DiploSource.Edict, "diplomatic_grants")!.Overall, 6);
        Assert.Null(c.Get(DiploSource.Edict, "other"));
    }

    [Fact]
    public void Static_modifier_references_count_times_their_value()
    {
        using var t = new TempDir();
        t.Write("game/common/static_modifiers/00_s.txt", "res_naval_weight = { diplo_weight_naval_mult = 0.25 }");
        t.Write("game/common/resolutions/00_r.txt", "res_a = { modifier = { res_naval_weight = 4 } }");
        using var game = ContentSource.FromPath("game", Path.Combine(t.Path, "game"), isBaseGame: true);
        var c = ModifierCatalog.Load([game]);
        Assert.Equal(1.0, c.Get(DiploSource.Resolution, "res_a")!.Naval, 6);
    }

    [Fact]
    public void Inline_scripts_are_expanded_in_both_forms()
    {
        using var t = new TempDir();
        t.Write("game/common/policies/00_p.txt", """
            diplomatic_stance = { inline_script = "x/stances" }
            other = { inline_script = { script = x/param_stance NAME = stance_b VALUE = 0.3 } }
            """);
        t.Write("game/common/inline_scripts/x/stances.txt", "option = { name = \"stance_a\" modifier = { diplo_weight_mult = 0.5 } }");
        t.Write("game/common/inline_scripts/x/param_stance.txt", "option = { name = \"$NAME$\" modifier = { diplo_weight_mult = $VALUE$ } }");
        using var game = ContentSource.FromPath("game", Path.Combine(t.Path, "game"), isBaseGame: true);
        var c = ModifierCatalog.Load([game]);
        Assert.Equal(0.5, c.Get(DiploSource.Policy, "stance_a")!.Overall, 6);
        Assert.Equal(0.3, c.Get(DiploSource.Policy, "stance_b")!.Overall, 6);
    }

    [Fact]
    public void Relic_triggered_country_modifier_counts_and_potential_is_ignored()
    {
        using var t = new TempDir();
        t.Write("game/common/relics/00_r.txt", "r_x = { triggered_country_modifier = { potential = { diplo_weight_mult = 9 } diplo_weight_mult = 0.1 } }");
        using var game = ContentSource.FromPath("game", Path.Combine(t.Path, "game"), isBaseGame: true);
        Assert.Equal(0.1, ModifierCatalog.Load([game]).Get(DiploSource.Relic, "r_x")!.Overall, 6);
    }

    [Fact]
    public void Council_weight_is_read()
    {
        using var t = new TempDir();
        t.Write("game/common/static_modifiers/00_s.txt", "council_member = { diplo_weight_council_mult = 0.2 }");
        using var game = ContentSource.FromPath("game", Path.Combine(t.Path, "game"), isBaseGame: true);
        Assert.Equal(0.2, ModifierCatalog.Load([game]).Get(DiploSource.StaticModifier, "council_member")!.Council, 6);
    }

    [Fact]
    public void Reads_resolution_categories_and_targeted_resolutions()
    {
        using var t = new TempDir();
        t.Write("game/common/resolution_categories/00.txt", """
            resolution_category_commerce = { resolution_types = { "res_c1" "res_c_repeal" } }
            resolution_category_special = { multiple_active_resolutions = yes resolution_types = { "res_s1" } }
            """);
        t.Write("game/common/resolutions/00.txt", """
            res_c1 = { modifier = { diplo_weight_mult = 0.1 } }
            res_t = { target = yes modifier = { diplo_weight_mult = 0.1 } }
            """);
        using var game = ContentSource.FromPath("game", Path.Combine(t.Path, "game"), isBaseGame: true);
        var c = ModifierCatalog.Load([game]);
        Assert.Equal(("resolution_category_commerce", false), c.ResolutionCategory("res_c_repeal"));
        Assert.Equal(("resolution_category_special", true), c.ResolutionCategory("res_s1"));
        Assert.Null(c.ResolutionCategory("res_other"));
        Assert.True(c.IsTargetedResolution("res_t"));
        Assert.False(c.IsTargetedResolution("res_c1"));
    }
}

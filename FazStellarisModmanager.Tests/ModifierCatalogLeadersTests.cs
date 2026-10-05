using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Diplomacy;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ModifierCatalogLeadersTests
{
    static ModifierCatalog Load(TempDir t)
    {
        using var game = ContentSource.FromPath("game", Path.Combine(t.Path, "game"), isBaseGame: true);
        return ModifierCatalog.Load([game]);
    }

    [Fact]
    public void Reads_delegate_and_councilor_skill_modifiers()
    {
        using var t = new TempDir();
        t.Write("game/common/static_modifiers/00_s.txt", """
            galactic_community_delegate = { diplo_weight_delegate_mult = 0.10 }
            prototype_vir_core_modifier = { councilor_skill_add = 4 }
            """);
        t.Write("game/common/traditions/00_t.txt", "tr_politics_gravitas = { modifier = { resolutions_cost_mult = -0.25 diplo_weight_delegate_mult = 0.025 } }");
        var c = Load(t);
        Assert.Equal(0.10, c.Get(DiploSource.StaticModifier, "galactic_community_delegate")!.Delegate, 6);
        Assert.Equal(4, c.Get(DiploSource.StaticModifier, "prototype_vir_core_modifier")!.CouncilorSkill, 6);
        Assert.Equal(0.025, c.Get(DiploSource.Tradition, "tr_politics_gravitas")!.Delegate, 6);
        Assert.Equal(0, c.Get(DiploSource.Tradition, "tr_politics_gravitas")!.Overall, 6);
    }

    [Fact]
    public void Reads_pop_factions_councilors_and_species_traits()
    {
        using var t = new TempDir();
        t.Write("game/common/pop_faction_types/00_f.txt", """
            inline_script = { script = pop_faction_types/the_masquerade ETHIC = militarist }
            the_curtain_materialist = { country_modifier = { espionage_operation_speed_mult = 0.08 } }
            """);
        t.Write("game/common/inline_scripts/pop_faction_types/the_masquerade.txt", """
            the_masquerade_$ETHIC$ = {
                support_weight_exponent = 0.7
                country_modifier = { diplo_weight_mult = 0.003 }
                modifier = { diplo_weight_mult = 9 }
            }
            """);
        t.Write("game/common/governments/councilors/00_c.txt", """
            councilor_secret_societies = { modifier = { add_base_country_intel = 1 diplo_weight_mult = 0.01 } ai_hiring_weight = { diplo_weight_mult = 9 } }
            """);
        t.Write("game/common/traits/00_t.txt", "trait_uncanny_intuition = { cost = 3 modifier = { councilor_skill_add = 2 } }");
        var c = Load(t);
        Assert.Equal(0.003, c.Get(DiploSource.PopFaction, "the_masquerade_militarist")!.Overall, 6);
        Assert.Null(c.Get(DiploSource.PopFaction, "the_curtain_materialist"));
        Assert.Equal(0.01, c.Get(DiploSource.Councilor, "councilor_secret_societies")!.Overall, 6);
        Assert.Equal(2, c.Get(DiploSource.SpeciesTrait, "trait_uncanny_intuition")!.CouncilorSkill, 6);
    }

    [Fact]
    public void Evaluates_expressions_with_scripted_variables()
    {
        using var t = new TempDir();
        t.Write("game/common/scripted_variables/00_v.txt", """
            @sartek_utopian_legacy_mod_active = 1
            @two = 2
            """);
        t.Write("game/common/ascension_perks/00_p.txt", """
            @local = 0.5
            ap_shared_destiny = { modifier = { envoys_add = @[ 2 + ( 1 * sartek_utopian_mod_active ) ] diplo_weight_mult = @[ 0.2 * sartek_utopian_legacy_mod_active ] } }
            ap_b = { modifier = { diplo_weight_mult = @[(1 - 0.5) * two / 4 + local] } }
            ap_c = { modifier = { diplo_weight_mult = @[ -0.1 * ( two - 1 ) ] } }
            ap_unknown = { modifier = { diplo_weight_mult = @[ 0.2 * not_defined ] } }
            # a comment with @[ 1 ] in it
            ap_quoted = { desc = "uses @[ 1 + 1 ] inside" modifier = { diplo_weight_mult = 0.4 } }
            """);
        var c = Load(t);
        Assert.Equal(0.2, c.Get(DiploSource.Perk, "ap_shared_destiny")!.Overall, 6);
        Assert.Equal(0.75, c.Get(DiploSource.Perk, "ap_b")!.Overall, 6);
        Assert.Equal(-0.1, c.Get(DiploSource.Perk, "ap_c")!.Overall, 6);
        Assert.Null(c.Get(DiploSource.Perk, "ap_unknown"));
        Assert.Equal(0.4, c.Get(DiploSource.Perk, "ap_quoted")!.Overall, 6);
    }

    [Fact]
    public void Inline_scripts_with_quoted_code_and_commented_examples_do_not_break_parsing()
    {
        // The shape of Sartek's toggled-code scripts: a parameter holding quoted code, and an example call in comments.
        using var t = new TempDir();
        t.Write("game/common/scripted_variables/00_v.txt", "@legacy = 1  @wreaths = 0");
        t.Write("game/common/ascension_perks/00_p.txt", """
            ap_before = {
                possible = {
                    inline_script = {
                        script = sartek/sartek_toggled_code
                        code = "
                            custom_tooltip = {
                                fail_text = \"must_know_about_fe\"
                                OR = { any_country = { is_fallen_empire = yes } }
                            }
                            "

                        toggle = @wreaths
                    }
                }
                modifier = { diplo_weight_mult = 0.3 }
            }
            ap_shared_destiny = {
                modifier = {
                    diplo_weight_mult = @[ 0.2 * legacy ]
                }
                ai_weight = {
                    factor = 5
                    modifier = {
                        factor = @[ 1 + ( 1 * wreaths ) ]
                        inline_script = {
                            script = sartek/sartek_toggled_code
                            code = "
                                has_valid_civic = civic_achaemenid_admin
                                "
                            toggle = @wreaths
                        }
                    }
                }
            }
            ap_after = { modifier = { diplo_weight_mult = 0.1 } }
            """);
        t.Write("game/common/inline_scripts/sartek/sartek_toggled_code.txt", """
            # Toggled code script
            inline_script = {
                script = sartek/parts/switch
                file = sartek/parts/toggled_code_case_
                # this is equal to ceil( x^2 / (x^2+1) )
                value = @[ (-1 * ((-1 * (($toggle$*$toggle$) / (($toggle$*$toggle$)+1))) - ((((-1 * (($toggle$*$toggle$) / (($toggle$*$toggle$)+1))) % 1) + 1) % 1))) ]

                params = "code = \"$code$\"" # this is fine don't worry about it :)
            }

            # example use:

            # inline_script = {
            #   script = sartek/sartek_toggled_code
            #   code = "
            #       # code here will only be included if the toggle value below is != 0
            #   "
            #   toggle = @some_mod_presence_scripted_variable
            # }
            """);
        t.Write("game/common/inline_scripts/sartek/parts/switch.txt", """
            inline_script = {
                script = $file$$value$
                $params$
            }
            """);
        t.Write("game/common/inline_scripts/sartek/parts/toggled_code_case_1.txt", "$code$");
        t.Write("game/common/inline_scripts/sartek/parts/toggled_code_case_0.txt", "# no-op");
        var c = Load(t);
        Assert.Equal(0.3, c.Get(DiploSource.Perk, "ap_before")!.Overall, 6);
        Assert.Equal(0.2, c.Get(DiploSource.Perk, "ap_shared_destiny")!.Overall, 6);
        Assert.Equal(0.1, c.Get(DiploSource.Perk, "ap_after")!.Overall, 6);
    }

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("10 / 4 - 1", 1.5)]
    [InlineData("-2 * -3", 6)]
    [InlineData("@x * 2", 8)]
    [InlineData("x", 4)]
    public void Expression_evaluator(string expression, double expected)
    {
        var vars = new Dictionary<string, string> { ["@x"] = "4" };
        Assert.Equal(expected, ModifierCatalog.Evaluate(expression, n => vars.GetValueOrDefault(n))!.Value, 6);
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("unknown * 2")]
    [InlineData("1 / 0")]
    [InlineData("")]
    public void Expression_evaluator_rejects_bad_input(string expression) =>
        Assert.Null(ModifierCatalog.Evaluate(expression, _ => null));

    const string Gravitas = """
        tr_politics_adopt = {
            modifier = { envoys_add = 2 }
            inline_script = MECR/traditions/MECR_swap_tr_politics_adopt
        }
        tr_politics_gravitas = {
            modifier = { resolutions_cost_mult = -0.20 diplo_weight_delegate_mult = 0.025 }
            inline_script = MECR/traditions/MECR_swap_tr_politics_gravitas
        }
        """;

    const string AdoptSwap = """
        tradition_swap = {
            name = tr_politics_adopt_advanced
            inherit_name = yes
            modifier = { country_official_cap_add = 1 envoys_add = 2 diplo_weight_delegate_mult = 0.025 }
            trigger = { OR = { is_galactic_custodian = yes is_galactic_emperor = yes } }
            weight = { factor = 10000 }
        }
        """;

    const string GravitasSwap = """
        tradition_swap = {
            name = tr_politics_gravitas_custodian
            trigger = { is_galactic_custodian = yes }
            modifier = { diplo_weight_delegate_mult = 0.025 diplo_weight_mult = 0.10 }
        }
        tradition_swap = {
            name = tr_politics_gravitas_emperor
            trigger = { is_galactic_emperor = yes }
            modifier = { resolutions_cost_mult = -0.20 diplo_weight_delegate_mult = 0.025 }
        }
        """;

    static ModifierCatalog LoadSwaps()
    {
        using var t = new TempDir();
        t.Write("game/common/traditions/00_politics.txt", Gravitas);
        t.Write("game/common/inline_scripts/MECR/traditions/MECR_swap_tr_politics_adopt.txt", AdoptSwap);
        t.Write("game/common/inline_scripts/MECR/traditions/MECR_swap_tr_politics_gravitas.txt", GravitasSwap);
        t.Write("game/common/ascension_perks/00_p.txt", """
            ap_shared_destiny = {
                modifier = { diplo_weight_mult = 0.2 }
                tradition_swap = { trigger = { is_nomadic = yes } modifier = { envoys_add = 2 } }
            }
            """);
        return Load(t);
    }

    [Fact]
    public void Reads_tradition_swaps_from_inline_scripts()
    {
        var c = LoadSwaps();
        Assert.Null(c.Get(DiploSource.Tradition, "tr_politics_adopt"));
        var adopt = Assert.Single(c.Swaps(DiploSource.Tradition, "tr_politics_adopt"));
        Assert.Equal("tr_politics_adopt_advanced", adopt.Name);
        Assert.Equal(0.025, adopt.Mods.Delegate, 6);
        Assert.Equal(2, c.Swaps(DiploSource.Tradition, "tr_politics_gravitas").Count);
        Assert.Empty(c.Swaps(DiploSource.Tradition, "tr_other"));
    }

    [Fact]
    public void Swap_triggers_follow_the_galactic_role()
    {
        var c = LoadSwaps();
        var adopt = c.Swaps(DiploSource.Tradition, "tr_politics_adopt").Single();
        Assert.True(adopt.Applies(emperor: true, custodian: false));
        Assert.True(adopt.Applies(emperor: false, custodian: true));
        Assert.False(adopt.Applies(emperor: false, custodian: false));

        var gravitas = c.Swaps(DiploSource.Tradition, "tr_politics_gravitas");
        Assert.Equal("tr_politics_gravitas_emperor", c.Resolve(DiploSource.Tradition, "tr_politics_gravitas", emperor: true, custodian: false).Swap?.Name);
        Assert.Equal(0.10, c.Resolve(DiploSource.Tradition, "tr_politics_gravitas", emperor: false, custodian: true).Mods!.Overall, 6);
        var plain = c.Resolve(DiploSource.Tradition, "tr_politics_gravitas", emperor: false, custodian: false);
        Assert.Null(plain.Swap);
        Assert.Equal(0.025, plain.Mods!.Delegate, 6);
        Assert.Equal(2, gravitas.Count);
    }

    [Fact]
    public void Unsupported_swap_triggers_never_apply()
    {
        var c = LoadSwaps();
        var swap = c.Swaps(DiploSource.Perk, "ap_shared_destiny").Single();
        Assert.False(swap.Applies(emperor: true, custodian: true));
        Assert.Equal(0.2, c.Resolve(DiploSource.Perk, "ap_shared_destiny", true, false).Mods!.Overall, 6);
    }

    [Theory]
    [InlineData("NOT = { is_galactic_emperor = yes }", false, true)]
    [InlineData("is_galactic_emperor = no", false, true)]
    [InlineData("NOR = { is_galactic_emperor = yes is_galactic_custodian = yes }", false, true)]
    [InlineData("AND = { is_galactic_emperor = yes NOT = { is_galactic_custodian = yes } }", true, false)]
    [InlineData("OR = { is_galactic_custodian = yes has_global_flag = x }", false, false)]
    public void Swap_trigger_forms(string trigger, bool emperorApplies, bool neitherApplies)
    {
        var t = ParadoxScriptParser.Parse(trigger);
        Assert.Equal(emperorApplies, TraditionSwap.Evaluate(t, emperor: true, custodian: false) == true);
        Assert.Equal(neitherApplies, TraditionSwap.Evaluate(t, emperor: false, custodian: false) == true);
    }
}

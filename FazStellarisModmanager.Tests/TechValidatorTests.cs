using FazStellarisModmanager.Core.Addons;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class TechValidatorTests
{
    const string Log = """
        Printing Modifier Definitions:
        - divided_patrongage_max_subjects, Category: Countries
        - country_unity_produces_mult, Category: Countries, AI Economy
        - ship_speed_mult, Category: Ships, AI Economy
        - pop_happiness, Category: Pops
        - federation_cohesion_add, Category: Federations
        """;

    // The game as Apply reads it (no localisation, no log), with names, and with names and this PC's modifier list.
    static readonly TechAddonCatalog Files = Build(loc: false, log: false);
    static readonly TechAddonCatalog NoLog = Build(loc: true, log: false);
    static readonly TechAddonCatalog Game = Build(loc: true, log: true);

    static TechAddonCatalog Build(bool loc, bool log)
    {
        var dds = DdsBuilder.Bgra32(1, 1, (1, 2, 3, 255));
        using var f = new MarketFixture()
            .Base("common/technology/00_soc_tech.txt", """
                tech_galactic_administration = { area = society tier = 3 category = { statecraft } }
                tech_existing = { area = physics tier = 1 category = { particles } }
                """)
            .Base("common/technology/category/00_category.txt", "statecraft = { } particles = { }")
            .Base("common/ethics/00_ethics.txt", """
                ethic_fanatic_authoritarian = { regular_variant = ethic_authoritarian }
                ethic_authoritarian = { fanatic_variant = ethic_fanatic_authoritarian }
                ethic_gestalt_consciousness = { }
                """)
            .Bytes("Base game", "gfx/interface/icons/technologies/tech_icon.dds", dds)
            .Bytes("Base game", "gfx/interface/icons/technologies/tech_galactic_administration.dds", dds);
        var names = new Localisation();
        names.AddText(MarketFixture.LfText("""
            l_english:
             tech_galactic_administration:0 "Galactic Administration"
             mod_divided_patrongage_max_subjects:0 "Subjects Exempt from Divided Patronage"
             mod_pop_happiness:0 "Happiness"
            """));
        return f.TechCatalog(loc ? names : null, log ? MarketFixture.LfText(Log) : null);
    }

    static CustomTech Valid(string key = "tech_faz_patronage") => new(key, "Patronage", "More subjects.", "tech_icon",
        Ethics: ["ethic_authoritarian"], Prerequisites: ["tech_galactic_administration"],
        Modifiers: [new TechModifier("divided_patrongage_max_subjects", 1)]);

    static IReadOnlyList<TechIssue> Issues(CustomTech tech, TechAddonCatalog? catalog = null, params CustomTech[] others) =>
        TechValidator.Validate(tech, [tech, .. others], catalog ?? Game);

    // Exactly one error, on the field, and the tech is left out of the build, whatever the catalog was built with.
    static void AssertError(CustomTech tech, string field, string message, params CustomTech[] others)
    {
        foreach (var catalog in new[] { Game, NoLog, Files })
        {
            var error = Assert.Single(Issues(tech, catalog, others), i => i.Level == TechIssueLevel.Error);
            Assert.Equal(field, error.Field);
            Assert.Contains(message, error.Message);
            Assert.DoesNotContain(TechValidator.Buildable([tech, .. others], catalog), t => ReferenceEquals(t, tech));
        }
    }

    static TechIssue Warning(CustomTech tech, TechAddonCatalog? catalog = null) =>
        Assert.Single(Issues(tech, catalog), i => i.Level == TechIssueLevel.Warning);

    [Fact]
    public void A_valid_tech_has_no_issues_and_is_built()
    {
        Assert.Empty(Issues(Valid()));
        Assert.Empty(Issues(Valid(), NoLog));
        Assert.Equal([Valid()], TechValidator.Buildable([Valid()], Files));
    }

    [Fact]
    public void An_empty_key_is_an_error() => AssertError(Valid(""), "Key", "empty");

    [Theory]
    [InlineData("tech_Faz")]
    [InlineData("tech_faz x")]
    [InlineData("tech_faz-x")]
    [InlineData("tech_faz{")]
    public void A_key_with_other_characters_is_an_error(string key) => AssertError(Valid(key), "Key", "lower-case letters, digits and _");

    [Fact]
    public void A_key_without_the_tech_prefix_is_an_error() => AssertError(Valid("faz_patronage"), "Key", "start with tech_");

    [Fact]
    public void A_key_of_a_tech_in_the_mod_list_is_an_error()
    {
        AssertError(Valid("tech_existing"), "Key", "already used by");
        Assert.Contains("Galactic Administration", Assert.Single(Issues(Valid("tech_galactic_administration"))).Message);
    }

    [Fact]
    public void A_key_used_by_another_custom_tech_is_an_error_for_both()
    {
        var first = Valid();
        var second = Valid() with { Name = "Second" };

        AssertError(first, "Key", "Another custom technology", second);
        Assert.Contains(TechValidator.Validate(second, [first, second], Game), i => i.Level == TechIssueLevel.Error && i.Field == "Key");
        Assert.Empty(TechValidator.Buildable([first, second], Game));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void An_empty_name_is_an_error(string name) => AssertError(Valid() with { Name = name }, "Name", "name is empty");

    [Theory]
    [InlineData("military")]
    [InlineData("Society")]
    [InlineData("")]
    public void An_unknown_area_is_an_error(string area) => AssertError(Valid() with { Area = area }, "Area", "physics, society or engineering");

    [Theory]
    [InlineData("physics")]
    [InlineData("engineering")]
    public void The_three_areas_are_valid(string area) => Assert.Empty(Issues(Valid() with { Area = area }));

    [Fact]
    public void An_unknown_category_is_an_error() => AssertError(Valid() with { Category = "biology" }, "Category", "biology");

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void A_tier_outside_0_to_5_is_an_error(int tier) =>
        AssertError(Valid() with { Tier = tier, Repeatable = false }, "Tier", "between 0 and 5");

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Tiers_0_and_5_are_valid(int tier) => Assert.Empty(Issues(Valid() with { Tier = tier, Repeatable = false }));

    [Fact]
    public void An_unknown_icon_is_an_error()
    {
        AssertError(Valid() with { Icon = "tech_missing" }, "Icon", "tech_missing");
        AssertError(Valid() with { Icon = "" }, "Icon", "No icon");
        Assert.Empty(Issues(Valid() with { Icon = "TECH_GALACTIC_ADMINISTRATION" }));
    }

    [Fact]
    public void An_unknown_prerequisite_is_an_error()
    {
        AssertError(Valid() with { Prerequisites = ["tech_galactic_administration", "tech_missing"] }, "Prerequisites", "tech_missing");
        AssertError(Valid() with { Prerequisites = ["tech_faz_patronage"] }, "Prerequisites", "tech_faz_patronage");
    }

    [Fact]
    public void A_prerequisite_on_another_valid_custom_tech_is_accepted_wherever_it_is_listed()
    {
        var later = Valid("tech_faz_later") with { Prerequisites = ["tech_faz_patronage"] };

        Assert.Empty(TechValidator.Validate(later, [later, Valid()], Game));
        Assert.Equal([later, Valid()], TechValidator.Buildable([later, Valid()], Files));
    }

    [Fact]
    public void A_prerequisite_on_a_custom_tech_with_errors_is_an_error_down_the_chain()
    {
        var broken = Valid("tech_faz_broken") with { Cost = 0 };
        var middle = Valid("tech_faz_middle") with { Prerequisites = ["tech_faz_broken"] };
        var last = Valid("tech_faz_last") with { Prerequisites = ["tech_faz_middle", "tech_existing"] };

        AssertError(middle, "Prerequisites", "tech_faz_broken", broken, last);
        AssertError(last, "Prerequisites", "tech_faz_middle", broken, middle);
        Assert.Equal([Valid()], TechValidator.Buildable([broken, middle, Valid(), last], Game));
    }

    [Fact]
    public void Custom_techs_requiring_each_other_are_errors()
    {
        var a = Valid("tech_faz_a") with { Prerequisites = ["tech_faz_b"] };
        var b = Valid("tech_faz_b") with { Prerequisites = ["tech_faz_a"] };

        AssertError(a, "Prerequisites", "circle", b);
        AssertError(b, "Prerequisites", "circle", a);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_cost_of_0_or_less_is_an_error(int cost) => AssertError(Valid() with { Cost = cost }, "Cost", "above 0");

    [Fact]
    public void A_negative_cost_per_level_is_an_error_for_repeatables_only()
    {
        AssertError(Valid() with { CostPerLevel = -1 }, "CostPerLevel", "negative");
        Assert.Empty(Issues(Valid() with { CostPerLevel = -1, Repeatable = false }));
        Assert.Empty(Issues(Valid() with { CostPerLevel = 0 }));
    }

    [Fact]
    public void A_negative_weight_is_an_error()
    {
        AssertError(Valid() with { Weight = -1 }, "Weight", "negative");
        Assert.Empty(Issues(Valid() with { Weight = 0 }));
    }

    [Fact]
    public void An_unknown_ethic_is_an_error()
    {
        AssertError(Valid() with { Ethics = ["ethic_authoritarian", "ethic_xenophile"] }, "Ethics", "ethic_xenophile");
        AssertError(Valid() with { Ethics = ["ethic_fanatic_authoritarian"] }, "Ethics", "ethic_fanatic_authoritarian");
        Assert.Empty(Issues(Valid() with { Ethics = ["ethic_gestalt_consciousness", "ethic_authoritarian"] }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("pop happiness")]
    [InlineData("pop_happiness = 1 }")]
    public void A_modifier_key_that_isnt_a_script_key_is_an_error(string key) =>
        AssertError(Valid() with { Modifiers = [new TechModifier(key, 1)] }, "Modifiers", "modifier");

    [Fact]
    public void A_modifier_value_that_isnt_a_number_is_an_error() =>
        AssertError(Valid() with { Modifiers = [new TechModifier("pop_happiness", double.NaN)] }, "Modifiers", "pop_happiness");

    [Fact]
    public void A_modifier_missing_from_the_log_is_a_warning()
    {
        var tech = Valid() with { Modifiers = [new TechModifier("mod_from_nowhere", 1)] };

        var warning = Warning(tech);

        Assert.Equal("Modifiers", warning.Field);
        Assert.Contains("not found in this PC's modifier list", warning.Message);
        Assert.Contains("mod_from_nowhere", warning.Message);
        Assert.Equal([tech], TechValidator.Buildable([tech], Game));
    }

    [Fact]
    public void Without_a_log_a_modifier_that_isnt_known_couldnt_be_checked()
    {
        Assert.Contains("couldn't be checked", Warning(Valid() with { Modifiers = [new TechModifier("mod_from_nowhere", 1)] }, NoLog).Message);
        Assert.Empty(Issues(Valid() with { Modifiers = [new TechModifier("pop_happiness", 1)] }, NoLog));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.00001)]
    public void A_modifier_value_of_0_is_a_warning(double value)
    {
        var warning = Warning(Valid() with { Modifiers = [new TechModifier("pop_happiness", value)] });

        Assert.Equal("Modifiers", warning.Field);
        Assert.Contains("0", warning.Message);
    }

    [Fact]
    public void A_tech_without_effects_is_a_warning()
    {
        var tech = Valid() with { Modifiers = null };

        Assert.Equal("Modifiers", Warning(tech).Field);
        Assert.Contains("no effects", Warning(tech with { Modifiers = [] }).Message);
        Assert.Equal([tech], TechValidator.Buildable([tech], Game));
    }

    [Fact]
    public void A_repeatable_below_tier_5_is_a_warning()
    {
        var warning = Warning(Valid() with { Tier = 3 });

        Assert.Equal("Tier", warning.Field);
        Assert.Contains("tier 5", warning.Message);
        Assert.Empty(Issues(Valid() with { Tier = 3, Repeatable = false }));
    }

    [Fact]
    public void A_modifier_of_a_scope_that_isnt_an_empire_is_a_warning()
    {
        var warning = Warning(Valid() with { Modifiers = [new TechModifier("federation_cohesion_add", 1)] });

        Assert.Equal("Modifiers", warning.Field);
        Assert.Contains("may not apply to an empire", warning.Message);
        Assert.Contains("Federations", warning.Message);
        Assert.Empty(Issues(Valid() with { Modifiers = [new TechModifier("ship_speed_mult", 0.1), new TechModifier("country_unity_produces_mult", 0.1)] }));
    }

    [Theory]
    [InlineData("Countries", true)]
    [InlineData("Pops", true)]
    [InlineData("Economic Units, AI Economy", true)]
    [InlineData("Orbital Stations, Space Stations, Military Ships", true)]
    [InlineData("Fleets, Countries", true)]
    [InlineData(null, true)]
    [InlineData("Federations", false)]
    [InlineData("Star Systems", false)]
    [InlineData("Galactic Community", false)]
    public void Modifier_categories_that_apply_to_an_empire(string? category, bool applies) =>
        Assert.Equal(applies, TechValidator.AppliesToEmpire(category));

    [Fact]
    public void Errors_come_before_warnings_and_every_problem_is_listed()
    {
        var tech = Valid("faz") with { Name = "", Cost = 0, Tier = 3, Modifiers = null };

        var issues = Issues(tech);

        Assert.Equal(["Key", "Name", "Cost", "Modifiers", "Tier"], issues.Select(i => i.Field));
        Assert.Equal([TechIssueLevel.Error, TechIssueLevel.Error, TechIssueLevel.Error, TechIssueLevel.Warning, TechIssueLevel.Warning],
            issues.Select(i => i.Level));
    }

    [Fact]
    public void Buildable_keeps_the_settings_order_and_ignores_warnings()
    {
        var first = Valid("tech_faz_first") with { Modifiers = null };
        var broken = Valid("tech_faz_broken") with { Category = "nope" };
        var third = Valid("tech_faz_third") with { Prerequisites = ["tech_faz_first"], Tier = 1 };
        var fourth = Valid("tech_faz_fourth") with { Modifiers = [new TechModifier("mod_from_nowhere", 0)] };

        Assert.Equal([first, third, fourth], TechValidator.Buildable([first, broken, third, fourth], Game));
        Assert.Empty(TechValidator.Buildable([], Game));
    }

    [Fact]
    public void Errors_do_not_depend_on_localisation_or_the_modifier_list()
    {
        CustomTech[] all =
        [
            Valid("tech_faz_a") with { Modifiers = [new TechModifier("mod_from_nowhere", 1)] },
            Valid("tech_faz_b") with { Modifiers = [new TechModifier("federation_cohesion_add", 0)] },
            Valid("tech_existing"),
            Valid("tech_faz_c") with { Prerequisites = ["tech_faz_a", "tech_existing"] },
        ];

        var built = TechValidator.Buildable(all, Game);

        Assert.Equal(["tech_faz_a", "tech_faz_b", "tech_faz_c"], built.Select(t => t.Key));
        Assert.Equal(built, TechValidator.Buildable(all, NoLog));
        Assert.Equal(built, TechValidator.Buildable(all, Files));
    }

    [Fact]
    public void A_tech_not_in_the_list_is_checked_against_all_of_it()
    {
        var edited = Valid() with { Name = "Edited" };

        Assert.Contains(TechValidator.Validate(edited, [Valid()], Game), i => i.Field == "Key" && i.Level == TechIssueLevel.Error);
        Assert.Empty(TechValidator.Validate(edited, [Valid("tech_faz_other")], Game));
    }
}

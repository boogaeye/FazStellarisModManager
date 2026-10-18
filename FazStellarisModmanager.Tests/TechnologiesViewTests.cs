using FazStellarisModmanager.Core.Addons;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class TechnologiesViewTests
{
    const string Log = """
        Printing Modifier Definitions:
        - divided_patrongage_max_subjects, Category: Countries
        - country_subject_integration_speed_mult, Category: Countries
        - country_subject_trust_cap_add, Category: Countries
        - subject_tribute_mult, Category: Countries
        - federation_cohesion_add, Category: Federations
        - council_seat_add, Category: Galactic Community
        - pop_happiness, Category: Pops
        - aaa_zeal_mult, Category: Countries
        - zzz_zeal_mult, Category: Countries
        """;

    static readonly TechAddonCatalog Game = Build(repeatableIcon: true);

    static TechAddonCatalog Build(bool repeatableIcon, string categories = "particles = { } statecraft = { } industry = { }")
    {
        var dds = DdsBuilder.Bgra32(1, 1, (1, 2, 3, 255));
        var f = new MarketFixture()
            .Base("common/technology/00_soc_tech.txt", """
                tech_galactic_administration = { area = society tier = 3 category = { statecraft } icon = tech_galactic_administration }
                tech_galactic_bureaucracy = { area = society tier = 4 category = { statecraft } }
                tech_faz_new_technology = { area = physics tier = 1 category = { particles } }
                """)
            .Base("common/technology/category/00_category.txt", categories)
            .Base("common/ethics/00_ethics.txt", """
                ethic_fanatic_authoritarian = { regular_variant = ethic_authoritarian }
                ethic_authoritarian = { fanatic_variant = ethic_fanatic_authoritarian }
                ethic_militarist = { }
                ethic_gestalt_consciousness = { }
                """)
            .Bytes("Base game", "gfx/interface/icons/technologies/tech_galactic_administration.dds", dds)
            .Bytes("Base game", "gfx/interface/icons/technologies/aaa_first.dds", dds);
        if (repeatableIcon) f.Bytes("Base game", "gfx/interface/icons/technologies/repeatable_diplomacy_tech.dds", dds);
        using (f)
        {
            var names = new Localisation();
            names.AddText(MarketFixture.LfText("""
                l_english:
                 tech_galactic_administration:0 "Galactic Administration"
                 tech_galactic_bureaucracy:0 "Galactic Bureaucracy"
                 statecraft:0 "Statecraft"
                 industry:0 "Industry"
                 ethic_authoritarian:0 "Authoritarian"
                 ethic_militarist:0 "Militarist"
                 ethic_gestalt_consciousness:0 "Gestalt Consciousness"
                 mod_divided_patrongage_max_subjects:0 "Subjects Exempt from Divided Patronage"
                 mod_country_subject_integration_speed_mult:0 "Subject Integration Speed"
                 mod_country_subject_trust_cap_add:0 "Subject Trust Cap"
                 mod_pop_happiness:0 "Happiness"
                 mod_zzz_zeal_mult:0 "More Zeal"
                """));
            return f.TechCatalog(names, MarketFixture.LfText(Log));
        }
    }

    static CustomTech Patronage(string key = "tech_faz_imperial_patronage_doctrine") => new(key, "Imperial Patronage Doctrine",
        "Our administrators learn to keep more subjects content under one banner.", "repeatable_diplomacy_tech",
        Ethics: ["ethic_authoritarian"], Prerequisites: ["tech_galactic_administration"],
        Modifiers: [new TechModifier("divided_patrongage_max_subjects", 1)]);

    static CustomTech Plain(string key, string name = "Plain") => new(key, name, "", "aaa_first", Area: "engineering", Category: "industry",
        Tier: 3, Repeatable: false, Cost: 12000);

    // Keys and names

    [Theory]
    [InlineData("Imperial Patronage Doctrine", "imperial_patronage_doctrine")]
    [InlineData("  Über-Tech   2! ", "uber_tech_2")]
    [InlineData("Emperor's Will", "emperors_will")]
    [InlineData("Emperor’s Will", "emperors_will")]
    [InlineData("__x__y__", "x_y")]
    [InlineData("Технология", "")]
    [InlineData("", "")]
    public void Slug_keeps_lower_case_letters_digits_and_single_underscores(string name, string slug) =>
        Assert.Equal(slug, TechnologiesView.Slug(name));

    [Fact]
    public void The_key_of_a_name_is_tech_faz_and_its_slug()
    {
        Assert.Equal("tech_faz_imperial_patronage_doctrine", TechnologiesView.KeyFor("Imperial Patronage Doctrine"));
        Assert.Equal("tech_faz_technology", TechnologiesView.KeyFor("!!!"));
    }

    [Theory]
    [InlineData("tech_faz_new_technology", "New technology", true)]
    [InlineData("tech_faz_new_technology_2", "New technology", true)]
    [InlineData("tech_faz_new_technology_17", "New technology", true)]
    [InlineData("tech_faz_new_technology_1", "New technology", false)]
    [InlineData("tech_faz_new_technology_x", "New technology", false)]
    [InlineData("tech_faz_my_own_key", "New technology", false)]
    [InlineData("tech_faz_technology", "", true)]
    public void A_key_is_automatic_while_it_is_the_key_of_the_name(string key, string name, bool auto) =>
        Assert.Equal(auto, TechnologiesView.IsAutoKey(key, name));

    [Fact]
    public void A_unique_key_skips_the_keys_of_other_custom_techs_and_of_the_game()
    {
        Assert.Equal("tech_faz_patronage", TechnologiesView.UniqueKey("tech_faz_patronage", [], Game));
        Assert.Equal("tech_faz_patronage_2", TechnologiesView.UniqueKey("tech_faz_patronage", [Plain("tech_faz_patronage")], Game));
        Assert.Equal("tech_faz_patronage_3",
            TechnologiesView.UniqueKey("tech_faz_patronage", [Plain("TECH_FAZ_PATRONAGE"), Plain("tech_faz_patronage_2")], Game));
        Assert.Equal("tech_faz_new_technology_2", TechnologiesView.UniqueKey("tech_faz_new_technology", [], Game)); // the game has it
    }

    [Fact]
    public void A_new_tech_has_the_defaults_a_unique_key_and_a_repeatable_icon()
    {
        var tech = TechnologiesView.NewTech([], Game);

        Assert.Equal(new CustomTech("tech_faz_new_technology_2", "New technology", "", "repeatable_diplomacy_tech"), tech);
        var second = TechnologiesView.NewTech([tech], Game);
        Assert.Equal("tech_faz_new_technology_3", second.Key);
    }

    [Fact]
    public void A_new_tech_takes_the_first_icon_without_a_repeatable_one_and_the_first_category_without_statecraft()
    {
        Assert.Equal("aaa_first", TechnologiesView.DefaultIcon(Build(repeatableIcon: false)));
        Assert.Equal("", TechnologiesView.DefaultIcon(TechAddonCatalog.Empty));
        Assert.Equal("statecraft", TechnologiesView.NewTech([], TechAddonCatalog.Empty).Category);
        Assert.Equal("industry", TechnologiesView.NewTech([], Build(repeatableIcon: true, categories: "industry = { } particles = { }")).Category);
        Assert.Equal("tech_faz_new_technology", TechnologiesView.NewTech([], TechAddonCatalog.Empty).Key);
    }

    [Fact]
    public void Renaming_updates_an_automatic_key_and_keeps_one_edited_by_hand()
    {
        var tech = TechnologiesView.NewTech([], Game);

        var renamed = TechnologiesView.Rename(tech, "Imperial Patronage", [], Game);
        Assert.Equal(("Imperial Patronage", "tech_faz_imperial_patronage"), (renamed.Name, renamed.Key));

        var clash = TechnologiesView.Rename(tech, "Imperial Patronage", [renamed], Game);
        Assert.Equal("tech_faz_imperial_patronage_2", clash.Key);

        var edited = TechnologiesView.Rename(renamed with { Key = "tech_faz_mine" }, "Other", [], Game);
        Assert.Equal(("Other", "tech_faz_mine"), (edited.Name, edited.Key));
    }

    // Editing the settings

    [Fact]
    public void Techs_are_added_edited_and_removed_by_index()
    {
        var settings = TechnologiesView.AddTech(new AddonSettings(), Patronage());
        settings = TechnologiesView.AddTech(settings, Plain("tech_faz_plain"));
        Assert.Equal(["tech_faz_imperial_patronage_doctrine", "tech_faz_plain"], TechnologiesView.Techs(settings).Select(t => t.Key));
        Assert.True(settings.Technologies!.Enabled);

        settings = TechnologiesView.EditTech(settings, 1, t => t with { Weight = 3 });
        Assert.Equal(3, TechnologiesView.Techs(settings)[1].Weight);

        settings = TechnologiesView.RemoveTech(settings, 0);
        Assert.Equal(["tech_faz_plain"], TechnologiesView.Techs(settings).Select(t => t.Key));
        settings = TechnologiesView.RemoveTech(settings, 0);
        Assert.Null(settings.Technologies!.Techs);
        Assert.Empty(TechnologiesView.Techs(new AddonSettings()));
    }

    [Fact]
    public void An_index_out_of_range_changes_nothing()
    {
        var settings = TechnologiesView.AddTech(new AddonSettings(), Patronage());
        Assert.Same(settings, TechnologiesView.EditTech(settings, 3, t => t with { Weight = 3 }));
        Assert.Same(settings, TechnologiesView.RemoveTech(settings, -1));
    }

    [Fact]
    public void Ethics_toggle_and_prerequisites_and_effects_are_added_and_removed()
    {
        var tech = Patronage();

        Assert.Equal(["ethic_authoritarian", "ethic_militarist"], TechnologiesView.ToggleEthic(tech, "ethic_militarist").Ethics);
        Assert.Null(TechnologiesView.ToggleEthic(tech, "ethic_authoritarian").Ethics);

        var two = TechnologiesView.AddPrerequisite(tech, "tech_galactic_bureaucracy");
        Assert.Equal(["tech_galactic_administration", "tech_galactic_bureaucracy"], two.Prerequisites);
        Assert.Same(two, TechnologiesView.AddPrerequisite(two, "TECH_GALACTIC_BUREAUCRACY"));
        Assert.Null(TechnologiesView.RemovePrerequisite(tech, "tech_galactic_administration").Prerequisites);

        var effects = TechnologiesView.AddModifier(tech, "pop_happiness");
        Assert.Equal([new TechModifier("divided_patrongage_max_subjects", 1), new TechModifier("pop_happiness", 1)], effects.Modifiers);
        effects = TechnologiesView.SetModifier(effects, 1, new TechModifier("pop_happiness", 0.05));
        Assert.Equal(0.05, effects.Modifiers![1].Value);
        Assert.Equal([new TechModifier("pop_happiness", 0.05)], TechnologiesView.RemoveModifier(effects, 0).Modifiers);
        Assert.Null(TechnologiesView.RemoveModifier(tech, 0).Modifiers);
        Assert.Same(tech, TechnologiesView.RemoveModifier(tech, 5));
    }

    // Display

    [Fact]
    public void The_subtitle_shows_area_category_and_repeatable_or_tier()
    {
        Assert.Equal("Society · Statecraft · Repeatable", TechnologiesView.Subtitle(Patronage(), Game));
        Assert.Equal("Engineering · Industry · Tier 3", TechnologiesView.Subtitle(Plain("tech_faz_plain"), Game));
        Assert.Equal("space · unknown_cat · Tier 3", TechnologiesView.Subtitle(Plain("x") with { Area = "space", Category = "unknown_cat" }, Game));
    }

    [Fact]
    public void Cost_at_a_level_adds_the_cost_per_level_for_repeatables()
    {
        Assert.Equal(50000, TechnologiesView.CostAt(Patronage(), 1));
        Assert.Equal(95000, TechnologiesView.CostAt(Patronage(), 10));
        Assert.Equal(12000, TechnologiesView.CostAt(Plain("x"), 10));
        Assert.Equal(2147483647L + 9L * 2147483647L, TechnologiesView.CostAt(Patronage() with { Cost = int.MaxValue, CostPerLevel = int.MaxValue }, 10));
    }

    [Fact]
    public void Cost_text_shows_the_cost_and_the_cost_per_level()
    {
        Assert.Equal(("50,000", "research (+5,000 per level)"), TechnologiesView.CostText(Patronage()));
        Assert.Equal(("50,000", "research"), TechnologiesView.CostText(Patronage() with { CostPerLevel = 0 }));
        Assert.Equal(("12,000", "research"), TechnologiesView.CostText(Plain("x") with { CostPerLevel = 700 }));
        Assert.Equal("Level 10 costs 95,000.", TechnologiesView.LevelHint(Patronage()));
        Assert.Null(TechnologiesView.LevelHint(Plain("x")));
    }

    [Theory]
    [InlineData(1, "+1")]
    [InlineData(0.05, "+0.05")]
    [InlineData(-2.5, "−2.5")]
    [InlineData(0.00001, "0")]
    [InlineData(-0.00001, "0")]
    [InlineData(1.23456, "+1.2346")]
    public void Signed_numbers_have_a_sign_and_up_to_4_decimals(double value, string text) =>
        Assert.Equal(text, TechnologiesView.Signed(value));

    [Fact]
    public void An_effect_shows_its_signed_value_and_localised_name()
    {
        Assert.Equal(new EffectText("+1", "Subjects Exempt from Divided Patronage"),
            TechnologiesView.Effect(new TechModifier("divided_patrongage_max_subjects", 1), Game));
        Assert.Equal(new EffectText("+25%", "Subject Integration Speed"),
            TechnologiesView.Effect(new TechModifier("country_subject_integration_speed_mult", 0.25), Game));
        Assert.Equal(new EffectText("−10%", "subject_tribute_mult"), TechnologiesView.Effect(new TechModifier("subject_tribute_mult", -0.1), Game));
        Assert.Equal(new EffectText("+2", "unknown_mod"), TechnologiesView.Effect(new TechModifier("unknown_mod", 2), Game));
        Assert.Equal("+1 Subjects Exempt from Divided Patronage",
            TechnologiesView.Effect(new TechModifier("divided_patrongage_max_subjects", 1), Game).ToString());
    }

    [Fact]
    public void The_first_effect_says_per_level_for_repeatables()
    {
        Assert.Equal("+1 Subjects Exempt from Divided Patronage / level", TechnologiesView.FirstEffect(Patronage(), Game));
        Assert.Equal("+3 Happiness", TechnologiesView.FirstEffect(Plain("x") with { Modifiers = [new TechModifier("pop_happiness", 3)] }, Game));
        Assert.Null(TechnologiesView.FirstEffect(Plain("x"), Game));
    }

    [Fact]
    public void Requirements_name_the_ethics_and_the_prerequisites()
    {
        var other = Plain("tech_faz_plain", "Plain Tech");
        var tech = Patronage() with
        {
            Ethics = ["ethic_authoritarian", "ethic_militarist"],
            Prerequisites = ["tech_galactic_administration", "tech_faz_plain", "tech_unknown"],
        };

        var r = TechnologiesView.Requirements(tech, [tech, other], 0, Game);

        Assert.Equal(["Authoritarian", "Militarist"], r.Ethics);
        Assert.Equal(["Galactic Administration", "Plain Tech", "tech_unknown"], r.Techs);
        Assert.Equal("Requires: Authoritarian or Militarist ethic · Galactic Administration · Plain Tech · tech_unknown", r.Text);
        Assert.Equal("Requires: Authoritarian ethic · Galactic Administration", TechnologiesView.Requirements(Patronage(), [Patronage()], 0, Game).Text);
        Assert.Equal("", TechnologiesView.Requirements(Plain("x"), [Plain("x")], 0, Game).Text);
        Assert.True(TechnologiesView.Requirements(Plain("x"), [Plain("x")], 0, Game).IsEmpty);
    }

    [Fact]
    public void A_prerequisite_is_a_game_tech_or_another_custom_tech()
    {
        var mine = Plain("tech_faz_plain", "Plain Tech");
        Assert.Equal(new TechChoice("tech_galactic_administration", "Galactic Administration", "tech_galactic_administration", Custom: false),
            TechnologiesView.FindPrerequisite("tech_galactic_administration", [mine], -1, Game));
        Assert.Equal(new TechChoice("tech_faz_plain", "Plain Tech", "aaa_first", Custom: true),
            TechnologiesView.FindPrerequisite("TECH_FAZ_PLAIN", [mine], -1, Game));
        Assert.Null(TechnologiesView.FindPrerequisite("tech_faz_plain", [mine], 0, Game)); // itself
        Assert.Null(TechnologiesView.FindPrerequisite("tech_nope", [mine], -1, Game));
    }

    [Fact]
    public void The_badge_is_red_for_errors_and_yellow_for_warnings_only()
    {
        var error = new TechIssue(TechIssueLevel.Error, "Key", "x");
        var warning = new TechIssue(TechIssueLevel.Warning, "Modifiers", "y");
        Assert.Equal(TechIssueLevel.Error, TechnologiesView.Badge([warning, error]));
        Assert.Equal(TechIssueLevel.Warning, TechnologiesView.Badge([warning]));
        Assert.Null(TechnologiesView.Badge([]));
    }

    // Searches

    [Fact]
    public void Icon_search_matches_names_shows_the_selected_one_first_and_counts_all()
    {
        var all = TechnologiesView.SearchIcons(Game, "", "repeatable_diplomacy_tech");
        Assert.Equal(["repeatable_diplomacy_tech", "aaa_first", "tech_galactic_administration"], all.Items);
        Assert.Equal(3, all.Total);

        var found = TechnologiesView.SearchIcons(Game, " GALACTIC ", "repeatable_diplomacy_tech");
        Assert.Equal(["tech_galactic_administration"], found.Items);

        var capped = TechnologiesView.SearchIcons(Game, "", null, max: 2);
        Assert.Equal(["aaa_first", "repeatable_diplomacy_tech"], capped.Items);
        Assert.Equal((3, 1), (capped.Total, capped.More));
    }

    [Fact]
    public void Modifier_search_matches_name_and_key_words_with_the_best_matches_first()
    {
        var subject = TechnologiesView.SearchModifiers(Game, "subject");
        Assert.Equal(["country_subject_integration_speed_mult", "country_subject_trust_cap_add", "divided_patrongage_max_subjects", "subject_tribute_mult"],
            subject.Items.Select(m => m.Key));
        Assert.Equal(["pop_happiness", "country_subject_integration_speed_mult"], TechnologiesView.SearchModifiers(Game, "p").Items.Select(m => m.Key).Take(2));

        Assert.Equal(["country_subject_trust_cap_add"], TechnologiesView.SearchModifiers(Game, "trust cap").Items.Select(m => m.Key));
        Assert.Equal(["country_subject_trust_cap_add"], TechnologiesView.SearchModifiers(Game, "subject_trust").Items.Select(m => m.Key));
        Assert.Equal("pop_happiness", TechnologiesView.SearchModifiers(Game, "happiness").Items[0].Key);
        Assert.Equal("divided_patrongage_max_subjects", TechnologiesView.SearchModifiers(Game, "divided_patrongage_max_subjects").Items[0].Key);

        var capped = TechnologiesView.SearchModifiers(Game, "subject", max: 1);
        Assert.Equal((1, 4, 3), (capped.Items.Count, capped.Total, capped.More));
        Assert.Empty(TechnologiesView.SearchModifiers(Game, " ").Items);
    }

    [Fact]
    public void Modifier_search_puts_named_modifiers_before_unnamed_ones_that_match_as_well()
    {
        Assert.Equal(["zzz_zeal_mult", "aaa_zeal_mult"], TechnologiesView.SearchModifiers(Game, "zeal").Items.Select(m => m.Key));
        var capped = TechnologiesView.SearchModifiers(Game, "zeal", max: 1);
        Assert.Equal(["zzz_zeal_mult"], capped.Items.Select(m => m.Key));
        Assert.Equal(2, capped.Total);
    }

    [Fact]
    public void Modifier_search_puts_modifiers_that_apply_to_an_empire_first()
    {
        var found = TechnologiesView.SearchModifiers(Game, "add");
        Assert.Equal(["country_subject_trust_cap_add", "council_seat_add", "federation_cohesion_add"], found.Items.Select(m => m.Key));
    }

    [Fact]
    public void Tech_search_offers_game_techs_and_other_custom_techs_not_chosen_yet()
    {
        var tech = Patronage();
        var other = Plain("tech_faz_galactic_plain", "Galactic Plain");

        var found = TechnologiesView.SearchTechs(Game, [tech, other], 0, "galactic");

        Assert.Equal(["tech_galactic_bureaucracy", "tech_faz_galactic_plain"], found.Items.Select(t => t.Key));
        Assert.Equal([false, true], found.Items.Select(t => t.Custom));
        Assert.Equal(["tech_faz_imperial_patronage_doctrine"], TechnologiesView.SearchTechs(Game, [tech, other], 1, "patronage").Items.Select(t => t.Key));
        Assert.Empty(TechnologiesView.SearchTechs(Game, [tech, other], 0, "patronage").Items); // not itself
        Assert.Empty(TechnologiesView.SearchTechs(Game, [tech], 0, "").Items);
    }

    // The Mods tab's Faz Addons row

    [Fact]
    public void Active_addons_name_the_addons_that_may_change_the_game()
    {
        var market = new MarketplaceSettings(MaxBaseAmounts: 500);
        var techs = new TechnologiesSettings(Techs: [Patronage()]);

        Assert.Equal(["Marketplace", "Technologies"], new AddonSettings(true, market, techs).ActiveAddons);
        Assert.Equal(["Technologies"], new AddonSettings(true, new MarketplaceSettings(), techs).ActiveAddons);
        Assert.Equal(["Marketplace"], new AddonSettings(true, market, techs with { Enabled = false }).ActiveAddons);
        Assert.Empty(new AddonSettings(false, market, techs).ActiveAddons);
        Assert.Empty(new AddonSettings(true, null, new TechnologiesSettings()).ActiveAddons);
        Assert.True(new AddonSettings(true, null, techs).MayChangeGame);
    }
}

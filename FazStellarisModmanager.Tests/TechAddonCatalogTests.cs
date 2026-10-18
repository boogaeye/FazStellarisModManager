using FazStellarisModmanager.Core.Addons;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class TechAddonCatalogTests
{
    const string SocTech = "common/technology/00_soc_tech.txt";

    static MarketFixture Techs() => new MarketFixture()
        .Base("common/scripted_variables/00_vars.txt", "@repeatableTechTier = 5")
        .Base(SocTech, """
            tech_galactic_administration = { area = society tier = 3 category = { statecraft } }
            tech_old = { area = physics tier = 1 category = { particles } }
            """)
        .Base("common/technology/00_repeatable.txt", """
            @three = 3
            tech_repeatable_unity = {
            	area = society
            	tier = @repeatableTechTier
            	category = { statecraft }
            	levels = -1
            	icon = tech_unity_icon
            }
            tech_local_tier = { area = physics tier = @three category = { particles } levels = 1 }
            """)
        .Base("common/technology/category/00_category.txt", "statecraft = { icon = \"gfx/x.dds\" }")
        .Base("common/technology/tier/00_tier.txt", "0 = { } 1 = { previously_unlocked = 0 }")
        // Mod A's copy of the base file replaces it whole, so tech_old is gone; its zz file redefines a repeatable.
        .Mod("Mod A", SocTech, "tech_galactic_administration = { area = society tier = 2 category = { statecraft } }")
        .Mod("Mod A", "common/technology/zz_moda_tech.txt", "tech_repeatable_unity = { area = engineering tier = 4 category = { industry } }");

    static Localisation Loc(string text)
    {
        var loc = new Localisation();
        loc.AddText(MarketFixture.LfText(text));
        return loc;
    }

    [Fact]
    public void Techs_come_from_the_winning_files_and_the_last_definition_wins()
    {
        using var f = Techs();

        var c = f.TechCatalog();

        // In load order (by file name), each where it was first defined.
        Assert.Equal(["tech_repeatable_unity", "tech_local_tier", "tech_galactic_administration"], c.Techs.Select(t => t.Key));
        Assert.Null(c.FindTech("tech_old"));
        Assert.Null(c.FindTech("statecraft"));
        Assert.Null(c.FindTech("0"));
        Assert.Equal(2, c.FindTech("TECH_GALACTIC_ADMINISTRATION")!.Tier);
        var unity = c.FindTech("tech_repeatable_unity")!;
        Assert.Equal(("engineering", 4, false, "tech_repeatable_unity"), (unity.Area, unity.Tier, unity.IsRepeatable, unity.Icon));
        Assert.Empty(f.Warnings);
    }

    [Fact]
    public void Tier_variables_resolve_and_repeatables_and_icons_are_read()
    {
        using var f = new MarketFixture()
            .Base("common/scripted_variables/00_vars.txt", "@repeatableTechTier = 5")
            .Base("common/technology/00_repeatable.txt", """
                @three = 3
                tech_repeatable_unity = { area = society tier = @repeatableTechTier levels = -1 icon = tech_unity_icon }
                tech_local_tier = { area = physics tier = @three levels = 1 }
                tech_no_tier = { tier = @missing }
                """);

        var c = f.TechCatalog();

        var unity = c.FindTech("tech_repeatable_unity")!;
        Assert.Equal(("society", 5, true, "tech_unity_icon"), (unity.Area, unity.Tier, unity.IsRepeatable, unity.Icon));
        Assert.Equal((3, false), (c.FindTech("tech_local_tier")!.Tier, c.FindTech("tech_local_tier")!.IsRepeatable));
        Assert.Null(c.FindTech("tech_no_tier")!.Tier);
        Assert.Null(c.FindTech("tech_no_tier")!.Area);
    }

    [Fact]
    public void Tech_names_come_from_localisation_with_the_key_as_fallback()
    {
        using var f = Techs();

        var c = f.TechCatalog(Loc("""
            l_english:
             tech_galactic_administration:0 "§YGalactic Administration§!"
            """));

        Assert.Equal("Galactic Administration", c.FindTech("tech_galactic_administration")!.Name);
        Assert.Equal("tech_repeatable_unity", c.FindTech("tech_repeatable_unity")!.Name);
    }

    [Fact]
    public void Icons_are_the_technology_icon_files_of_every_source()
    {
        var dds = DdsBuilder.Bgra32(1, 1, (1, 2, 3, 255));
        using var f = new MarketFixture()
            .Bytes("Base game", "gfx/interface/icons/technologies/tech_b.dds", dds)
            .Bytes("Base game", "gfx/interface/icons/technologies/tech_a.dds", dds)
            .Bytes("Base game", "gfx/interface/icons/technologies/categories/category_x.dds", dds)
            .Bytes("Base game", "gfx/interface/icons/technologies/bad name.dds", dds)
            .Bytes("Base game", "gfx/interface/icons/technologies/tech_png.png", dds)
            .Bytes("Mod A", "gfx/interface/icons/technologies/tech_c.dds", dds)
            .Bytes("Mod A", "gfx/interface/icons/technologies/TECH_A.dds", dds);

        var c = f.TechCatalog();

        Assert.Equal(["tech_a", "tech_b", "tech_c"], c.Icons);
        Assert.True(c.HasIcon("TECH_B"));
        Assert.False(c.HasIcon("category_x"));
        Assert.False(c.HasIcon("bad name"));
        Assert.False(c.HasIcon("tech_png"));
        Assert.False(c.HasIcon(""));
    }

    [Fact]
    public void Categories_come_from_the_winning_files_with_names()
    {
        using var f = new MarketFixture()
            .Base("common/technology/category/00_category.txt", "materials = { } statecraft = { }")
            .Mod("Mod A", "common/technology/category/00_category.txt", "statecraft = { } biology = { }")
            .Mod("Mod B", "common/technology/category/01_more.txt", "psionics = { }");

        var c = f.TechCatalog(Loc("""
            l_english:
             statecraft:0 "Statecraft"
            """));

        Assert.Equal(new[] { ("statecraft", "Statecraft"), ("biology", "biology"), ("psionics", "psionics") }, c.Categories.Select(x => (x.Key, x.Name)));
        Assert.Null(c.FindCategory("materials"));
        Assert.Equal("biology", c.FindCategory("BIOLOGY")!.Key);
    }

    [Fact]
    public void Ethics_are_the_regular_ones_and_gestalt_with_their_fanatic_variants()
    {
        using var f = new MarketFixture()
            .Base("common/ethics/00_ethics.txt", """
                ethic_fanatic_authoritarian = { regular_variant = ethic_authoritarian }
                ethic_authoritarian = { fanatic_variant = ethic_fanatic_authoritarian }
                ethic_militarist = { cost = 1 }
                ethic_fanatic_militarist = { cost = 2 }
                ethic_pacifist = { cost = 1 }
                ethic_spiritualist = { fanatic_variant = ethic_missing }
                ethic_gestalt_consciousness = { cost = 3 }
                """)
            .Mod("Mod A", "common/ethics/zz_purist.txt", """
                ethic_purist = { fanatic_variant = ethic_zealous_purist }
                ethic_zealous_purist = { regular_variant = ethic_purist }
                """);

        var c = f.TechCatalog(Loc("""
            l_english:
             ethic_authoritarian:1 "Authoritarian"
            """));

        Assert.Equal(new (string, string?)[]
        {
            ("ethic_authoritarian", "ethic_fanatic_authoritarian"),
            ("ethic_militarist", "ethic_fanatic_militarist"),
            ("ethic_pacifist", null),
            ("ethic_spiritualist", null),
            ("ethic_gestalt_consciousness", null),
            ("ethic_purist", "ethic_zealous_purist"),
        }, c.Ethics.Select(e => (e.Key, e.FanaticKey)));
        Assert.Equal("Authoritarian", c.FindEthic("ethic_authoritarian")!.Name);
        Assert.Equal("ethic_pacifist", c.FindEthic("ethic_pacifist")!.Name);
        Assert.Null(c.FindEthic("ethic_fanatic_authoritarian"));
        Assert.Null(c.FindEthic("ethic_zealous_purist"));
        Assert.All(c.Ethics, e => Assert.Null(e.IconUri));
    }

    const string Log = """
        [17:13:11][modifier.cpp:2274]:
         == MODIFIER DOCUMENTATION ==
        Note on Modifier Categories:
        -- These are used internally as a tag to suggest where they are intended to be used.
        - not_a_modifier, Category: Header

        Printing Modifier Definitions:
        - pop_happiness, Category: Pops
        - divided_patrongage_max_subjects, Category: Countries
        - country_energy_produces_mult, Category: Economic Units, AI Economy
        a line that is not a modifier
        """;

    [Fact]
    public void Modifiers_come_from_the_log_with_localised_names()
    {
        using var f = new MarketFixture();

        var c = f.TechCatalog(Loc("""
            l_english:
             mod_DIVIDED_patrongage_max_subjects: "Subjects Exempt from Divided Patronage"
             mod_pop_happiness:0 "§YHappiness§!"
             mod_not_in_the_log:0 "Elsewhere"
            """), MarketFixture.LfText(Log));

        Assert.True(c.ModifiersFromLog);
        Assert.Equal(new (string, string?, string?)[]
        {
            ("country_energy_produces_mult", "Economic Units, AI Economy", null),
            ("divided_patrongage_max_subjects", "Countries", "Subjects Exempt from Divided Patronage"),
            ("pop_happiness", "Pops", "Happiness"),
        }, c.Modifiers.Select(m => (m.Key, m.Category, m.Name)));
        Assert.Null(c.FindModifier("not_a_modifier"));
        Assert.Null(c.FindModifier("not_in_the_log"));
        Assert.Equal("Countries", c.FindModifier("Divided_Patrongage_Max_Subjects")!.Category);
    }

    [Fact]
    public void A_log_with_crlf_line_endings_is_read_too()
    {
        using var f = new MarketFixture();

        var c = f.TechCatalog(modifiersLog: MarketFixture.LfText(Log).Replace(MarketFixture.Lf, MarketFixture.Crlf));

        Assert.Equal(["country_energy_produces_mult", "divided_patrongage_max_subjects", "pop_happiness"], c.Modifiers.Select(m => m.Key));
        Assert.Equal("Economic Units, AI Economy", c.FindModifier("country_energy_produces_mult")!.Category);
    }

    static Localisation ModLoc() => Loc("""
        l_english:
         mod_pop_happiness:0 "Happiness"
         mod_pop_happiness_desc:0 "How happy pops are."
         mod_country_unity_produces_mult:0 "Monthly Unity"
         pop_happiness:0 "Not a modifier name"
        """);

    [Fact]
    public void Without_a_log_modifiers_come_from_the_localised_modifier_names()
    {
        using var f = new MarketFixture();

        var c = f.TechCatalog(ModLoc());

        Assert.False(c.ModifiersFromLog);
        Assert.Equal(new (string, string?, string?)[]
        {
            ("country_unity_produces_mult", null, "Monthly Unity"),
            ("pop_happiness", null, "Happiness"),
        }, c.Modifiers.Select(m => (m.Key, m.Category, m.Name)));
    }

    [Fact]
    public void A_missing_or_empty_log_falls_back_to_the_localised_names()
    {
        using var f = new MarketFixture();
        var missing = Path.Combine(f.TempPath, "no", "modifiers.log");

        var c = TechAddonCatalog.Build(f.Sources, ModLoc(), missing, f.Warnings);
        var empty = f.TechCatalog(ModLoc(), "Printing Modifier Definitions:" + MarketFixture.Lf);
        var nothing = f.TechCatalog();

        Assert.False(c.ModifiersFromLog);
        Assert.Equal(["country_unity_produces_mult", "pop_happiness"], c.Modifiers.Select(m => m.Key));
        Assert.False(empty.ModifiersFromLog);
        Assert.Equal(2, empty.Modifiers.Count);
        Assert.False(nothing.ModifiersFromLog);
        Assert.Empty(nothing.Modifiers);
        Assert.Empty(f.Warnings);
    }

    [Fact]
    public void The_modifiers_log_is_under_the_user_folder() =>
        Assert.Equal(Path.Combine(@"C:\user", "logs", "script_documentation", "modifiers.log"), TechAddonCatalog.ModifiersLogPath(@"C:\user"));

    [Fact]
    public async Task LoadIcons_decodes_ethic_icons_and_loads_tech_icons_on_request()
    {
        using var f = new MarketFixture()
            .Base("common/ethics/00_ethics.txt", "ethic_militarist = { } ethic_pacifist = { }")
            .Bytes("Base game", "gfx/interface/icons/ethics/ethic_militarist.dds", DdsBuilder.Bgra32(1, 1, (255, 0, 0, 255)))
            .Bytes("Base game", "gfx/interface/icons/technologies/tech_a.dds", DdsBuilder.Bgra32(1, 1, (0, 255, 0, 255)))
            .Bytes("Mod A", "gfx/interface/icons/technologies/tech_b.dds", DdsBuilder.Bgra32(1, 1, (0, 0, 255, 255)));
        var catalog = f.TechCatalog();

        var withIcons = catalog.LoadIcons(f.Sources, new IconCache(Path.Combine(f.TempPath, "cache")));

        Assert.StartsWith("data:image/png;base64,", withIcons.FindEthic("ethic_militarist")!.IconUri);
        Assert.Null(withIcons.FindEthic("ethic_pacifist")!.IconUri);
        Assert.Null(catalog.FindEthic("ethic_militarist")!.IconUri);
        Assert.Null(catalog.IconUri("tech_a"));
        var a = withIcons.IconUri("tech_a");
        Assert.StartsWith("data:image/png;base64,", a);
        Assert.Same(a, withIcons.IconUri("TECH_A"));
        Assert.StartsWith("data:image/png;base64,", await withIcons.IconUriAsync("tech_b"));
        Assert.True(withIcons.IconUriAsync("tech_b").IsCompletedSuccessfully);
        Assert.NotEqual(a, withIcons.IconUri("tech_b"));
        Assert.Null(withIcons.IconUri("tech_missing"));
        Assert.Null(await withIcons.IconUriAsync("tech_missing"));
        Assert.Equal(catalog.Icons, withIcons.Icons);
    }

    [Fact]
    public void Empty_sources_give_an_empty_catalog()
    {
        using var f = new MarketFixture();

        var c = f.TechCatalog();

        Assert.Empty(c.Techs);
        Assert.Empty(c.Icons);
        Assert.Empty(c.Categories);
        Assert.Empty(c.Ethics);
        Assert.Empty(TechAddonCatalog.Empty.Techs);
        Assert.Empty(TechAddonCatalog.Empty.Modifiers);
        Assert.False(TechAddonCatalog.Empty.ModifiersFromLog);
    }
}

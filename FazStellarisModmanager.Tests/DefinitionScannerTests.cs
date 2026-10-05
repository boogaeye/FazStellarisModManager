using FazStellarisModmanager.Core.Conflicts;

namespace FazStellarisModmanager.Tests;

public class DefinitionScannerTests
{
    static string L(params string[] lines) => string.Join((char)10, lines);

    [Fact]
    public void Top_level_keys_only()
    {
        var text = L(
            "# comment = { }",
            "@cost = 5",
            "building_a = {",
            "    potential = { has_tech = x }",
            "    desc = \"has { braces } and # hash\"",
            "}",
            "building_b = { cost = { minerals >= 3 } }",
            "simple = yes");
        Assert.Equal(["@cost", "building_a", "building_b", "simple"], DefinitionScanner.Names(text, DefinitionKind.TopLevel));
    }

    [Fact]
    public void Defines_are_category_dot_key()
    {
        var text = L("NGameplay = {", "  MARKET_FEE = 0.3", "  LIST = { 1 2 3 }", "}", "NAI = { X = 1 }");
        Assert.Equal(["NGameplay.MARKET_FEE", "NGameplay.LIST", "NAI.X"], DefinitionScanner.Names(text, DefinitionKind.Defines));
    }

    [Fact]
    public void Events_are_ids()
    {
        var text = L(
            "namespace = gme",
            "country_event = {",
            "    id = gme.1",
            "    option = { id = not_this }",
            "}",
            "event = { id = \"gme.2\" hide_window = yes }",
            "planet_event = { is_triggered_only = yes id = gme.3 }",
            "something_else = { id = nope }");
        Assert.Equal(["gme.1", "gme.2", "gme.3"], DefinitionScanner.Names(text, DefinitionKind.Events));
    }

    [Fact]
    public void Unbalanced_braces_do_not_throw()
    {
        Assert.Equal(["a"], DefinitionScanner.Names("a = { } } }", DefinitionKind.TopLevel));
        Assert.Equal(["a"], DefinitionScanner.Names("a = { b = {", DefinitionKind.TopLevel));
    }

    [Theory]
    [InlineData("common/buildings/00_buildings.txt", "common/buildings", DefinitionKind.TopLevel)]
    [InlineData("Common/Defines/zz_defines.txt", "common/defines", DefinitionKind.Defines)]
    [InlineData("events/gme_events.txt", "events", DefinitionKind.Events)]
    [InlineData("common/buildings/sub/x.txt", "common/buildings", DefinitionKind.TopLevel)]
    public void Classify_scanned_files(string path, string folder, DefinitionKind kind)
    {
        Assert.Equal((folder, kind), DefinitionRules.Classify(path));
    }

    [Theory]
    [InlineData("common/on_actions/x.txt")]
    [InlineData("common/inline_scripts/a/b.txt")]
    [InlineData("common/buildings/readme.md")]
    [InlineData("common/x.txt")]
    [InlineData("localisation/english/x.yml")]
    [InlineData("descriptor.mod")]
    public void Classify_skips_other_files(string path) => Assert.Null(DefinitionRules.Classify(path));

    [Fact]
    public void Rules()
    {
        Assert.Equal(OverrideRule.LastWins, DefinitionRules.RuleFor("common/buildings"));
        Assert.Equal(OverrideRule.LastWins, DefinitionRules.RuleFor("common/defines"));
        Assert.Equal(OverrideRule.FirstWins, DefinitionRules.RuleFor("common/scripted_variables"));
        Assert.Equal(OverrideRule.Duplicated, DefinitionRules.RuleFor("common/strategic_resources"));
        Assert.Equal(OverrideRule.Unknown, DefinitionRules.RuleFor("events"));
    }
}

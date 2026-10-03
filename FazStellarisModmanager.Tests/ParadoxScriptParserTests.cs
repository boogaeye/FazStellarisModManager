using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Tests;

public class ParadoxScriptParserTests
{
    [Fact]
    public void Parses_key_values_blocks_and_bare_items()
    {
        var text = (char)0xFEFF + "name=\"My Mod\"\n# comment\ntags={\n\t\"Balance\"\n\t\"Gameplay\"\n}\nsupported_version=\"v4.*\"\nversion = 1.2\n";

        var b = ParadoxScriptParser.Parse(text);

        Assert.Equal("My Mod", b.GetString("name"));
        Assert.Equal("v4.*", b.GetString("supported_version"));
        Assert.Equal("1.2", b.GetString("version"));
        Assert.Equal(new[] { "Balance", "Gameplay" }, b.GetBlock("tags")!.StringItems);
    }

    [Fact]
    public void Parses_nested_blocks_and_comparison_operators()
    {
        var b = ParadoxScriptParser.Parse("tech_a = { cost = 100 weight_modifier = { factor = 2 } potential = { years_passed >= 5 } }");

        var tech = b.GetBlock("tech_a")!;
        Assert.Equal("100", tech.GetString("cost"));
        Assert.Equal("2", tech.GetBlock("weight_modifier")!.GetString("factor"));
        var cond = tech.GetBlock("potential")!.Entries.Single();
        Assert.Equal(("years_passed", ">=", "5"), (cond.Key, cond.Op, (string)cond.Value));
    }

    [Fact]
    public void Later_duplicate_key_wins_and_stray_braces_are_ignored()
    {
        var b = ParadoxScriptParser.Parse("name=\"a\"\n}\nname=\"b\"");

        Assert.Equal("b", b.GetString("name"));
    }

    [Fact]
    public void Handles_escaped_quotes_and_case_insensitive_keys()
    {
        var b = ParadoxScriptParser.Parse("Name=\"The \\\"Best\\\" Mod\"");

        Assert.Equal("The \"Best\" Mod", b.GetString("name"));
    }

    [Fact]
    public void Empty_input_gives_empty_block()
    {
        var b = ParadoxScriptParser.Parse("");

        Assert.Empty(b.Entries);
        Assert.Empty(b.Items);
        Assert.Null(b.GetString("name"));
    }
}

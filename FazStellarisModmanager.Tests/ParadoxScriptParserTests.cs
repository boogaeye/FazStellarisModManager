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

    [Fact]
    public void Parses_question_equals_operator()
    {
        var e = ParadoxScriptParser.Parse("owner ?= { is_ai = no }").Entries.Single();

        Assert.Equal(("owner", "?="), (e.Key, e.Op));
        Assert.Equal("no", ((PdxBlock)e.Value).GetString("is_ai"));
        var e2 = ParadoxScriptParser.Parse("a?=b").Entries.Single();
        Assert.Equal(("a", "?=", "b"), (e2.Key, e2.Op, (string)e2.Value));
    }

    [Fact]
    public void Lone_question_mark_stays_in_word()
    {
        Assert.Equal("a?b", ParadoxScriptParser.Parse("k = a?b").GetString("k"));
    }

    [Fact]
    public void Trailing_backslash_before_closing_quote_is_literal()
    {
        var b = ParadoxScriptParser.Parse("path=\"C:\\mods\\\"\nname=\"x\"");

        Assert.Equal("C:\\mods\\", b.GetString("path"));
        Assert.Equal("x", b.GetString("name"));
    }

    [Fact]
    public void Double_backslashes_are_kept_verbatim()
    {
        var b = ParadoxScriptParser.Parse("path=\"\\\\server\\share\"");

        Assert.Equal("\\\\server\\share", b.GetString("path"));
    }

    [Fact]
    public void Extreme_nesting_does_not_overflow()
    {
        var b = ParadoxScriptParser.Parse(new string('{', 100_000));

        Assert.NotNull(b);
    }

    [Fact]
    public void Unterminated_quote_takes_rest_of_input()
    {
        Assert.Equal("abc", ParadoxScriptParser.Parse("name=\"abc").GetString("name"));
    }

    [Fact]
    public void Key_with_operator_at_end_of_file_gives_no_entry()
    {
        Assert.Empty(ParadoxScriptParser.Parse("key =").Entries);
    }

    [Fact]
    public void Missing_value_before_close_brace_is_recovered()
    {
        Assert.Equal("1", ParadoxScriptParser.Parse("a = }\nb = 1").GetString("b"));
    }

    [Fact]
    public void Unclosed_block_keeps_its_content()
    {
        Assert.Equal("1", ParadoxScriptParser.Parse("x = { y = 1").GetBlock("x")!.GetString("y"));
    }

    [Fact]
    public void Anonymous_nested_blocks_become_items()
    {
        var list = ParadoxScriptParser.Parse("list = { {1 2} {3} }").GetBlock("list")!;

        var blocks = list.Items.OfType<PdxBlock>().ToList();
        Assert.Equal(2, blocks.Count);
        Assert.Equal(new[] { "1", "2" }, blocks[0].StringItems);
        Assert.Equal(new[] { "3" }, blocks[1].StringItems);
    }

    [Fact]
    public void Parses_not_equal_and_less_equal_operators()
    {
        var b = ParadoxScriptParser.Parse("a != 1 b <= 2");

        Assert.Equal(new[] { "!=", "<=" }, b.Entries.Select(e => e.Op));
    }

    [Fact]
    public void Hash_inside_quotes_is_not_a_comment()
    {
        Assert.Equal("a # b", ParadoxScriptParser.Parse("name=\"a # b\"").GetString("name"));
    }
}

using FazStellarisModmanager.Core.Addons;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ScriptTextEditorTests
{
    static readonly string Lf = MarketFixture.Lf;
    static readonly string Crlf = MarketFixture.Crlf;
    static readonly string Tab = ((char)9).ToString();
    static readonly string Q = ((char)34).ToString();

    [Fact]
    public void Finds_top_level_blocks_past_comments_quotes_and_nesting()
    {
        var text = MarketFixture.LfText("""
            # energy = { in a comment }
            wrapper = { energy = { nested = yes } }
            energy = {
            	name = "a } brace"
            	sub = { x = 1 }
            }
            "energy" = { second = yes }
            """);

        var blocks = ScriptTextEditor.FindBlocks(text, "ENERGY");

        Assert.Equal(2, blocks.Count);
        var first = text[blocks[0].Open..(blocks[0].Close + 1)];
        Assert.Equal("{" + Lf + Tab + "name = " + Q + "a } brace" + Q + Lf + Tab + "sub = { x = 1 }" + Lf + "}", first);
        Assert.Equal("{ second = yes }", text[blocks[1].Open..(blocks[1].Close + 1)]);
        Assert.Empty(ScriptTextEditor.FindBlocks(text, "nested"));
    }

    [Fact]
    public void An_escaped_quote_does_not_end_the_string()
    {
        var bs = ((char)92).ToString();
        var text = "a = { desc = " + Q + "say " + bs + Q + "}" + bs + Q + " now" + Q + " x = 1 }";

        Assert.Equal(text.Replace("x = 1", "x = 2"), ScriptTextEditor.SetValue(text, "a", "x", "2"));
    }

    [Fact]
    public void Replaces_only_direct_entries_of_the_block()
    {
        var text = "minerals = { max = 15000 dynamic_capacity = { max = 5 } } other = { max = 7 }";

        Assert.Equal("minerals = { max = 30000 dynamic_capacity = { max = 5 } } other = { max = 7 }",
            ScriptTextEditor.SetValue(text, "minerals", "max", "30000"));
    }

    [Fact]
    public void Replaces_every_occurrence_in_every_block_with_the_key()
    {
        var text = "a = { x = 1 x=2 } b = { x = 3 } a = { y = 1 }";

        Assert.Equal("a = { x = 9 x=9 } b = { x = 3 } a = {" + Lf + Tab + "x = 9 y = 1 }", ScriptTextEditor.SetValue(text, "a", "x", "9"));
    }

    [Fact]
    public void Replaces_quoted_and_block_values()
    {
        var text = "a = { tags = { x y } name = " + Q + "old" + Q + " }";

        var edited = ScriptTextEditor.SetValue(ScriptTextEditor.SetValue(text, "a", "tags", "yes"), "a", "name", "new");

        Assert.Equal("a = { tags = yes name = new }", edited);
    }

    [Fact]
    public void Inserts_a_missing_key_after_the_brace_with_the_files_line_ending()
    {
        var text = "# x" + Crlf + "a = {" + Crlf + Tab + "x = 1" + Crlf + "}" + Crlf;

        Assert.Equal("# x" + Crlf + "a = {" + Crlf + Tab + "y = 2" + Crlf + Tab + "x = 1" + Crlf + "}" + Crlf,
            ScriptTextEditor.SetValue(text, "a", "y", "2"));
    }

    [Fact]
    public void A_missing_block_leaves_the_text_unchanged()
    {
        var text = "a = { x = 1 }";

        Assert.Same(text, ScriptTextEditor.SetValue(text, "b", "x", "2"));
    }

    [Theory]
    [InlineData("crlf-first", true)]
    [InlineData("lf-first", false)]
    [InlineData("none", false)]
    public void Line_ending_comes_from_the_first_line_break(string kind, bool crlf)
    {
        var text = kind switch
        {
            "crlf-first" => "x" + Crlf + "y" + Lf,
            "lf-first" => "x" + Lf + "y" + Crlf,
            _ => "x",
        };

        Assert.Equal(crlf ? Crlf : Lf, ScriptTextEditor.LineEnding(text));
    }
}

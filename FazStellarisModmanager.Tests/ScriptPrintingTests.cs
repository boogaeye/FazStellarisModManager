using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Tests;

public class ScriptPrintingTests
{
    [Fact]
    public void Prints_indented_script_with_annotations()
    {
        var block = ParadoxScriptParser.Parse("modifier = { factor = 1.25 has_ethic = ethic_militarist }\nyears_passed >= 5\ntags = { a b }\nempty = { }");

        var text = PdxScriptPrinter.Print(block, v => v == "ethic_militarist" ? "Militarist" : null);

        Assert.Equal(
            "modifier = {\n    factor = 1.25\n    has_ethic = ethic_militarist   # Militarist\n}\nyears_passed >= 5\ntags = {\n    a\n    b\n}\nempty = { }",
            text);
    }

    [Fact]
    public void Prints_a_named_block_and_quotes_values_with_spaces()
    {
        var text = PdxScriptPrinter.PrintNamed("t", ParadoxScriptParser.Parse("a = \"two words\" b = { { x } }"));

        Assert.Equal("t = {\n    a = \"two words\"\n    b = {\n        {\n            x\n        }\n    }\n}", text);
    }

    [Fact]
    public void Annotation_equal_to_the_value_is_omitted() =>
        Assert.Equal("a = b", PdxScriptPrinter.Print(ParadoxScriptParser.Parse("a = b"), v => "B"));

    [Fact]
    public void Highlights_key_operator_value_and_comment()
    {
        var tokens = ScriptHighlighter.Line("    has_ethic = ethic_militarist   # Militarist");

        Assert.Equal(new[]
        {
            ("    ", ScriptTokenKind.Plain), ("has_ethic", ScriptTokenKind.Key), (" ", ScriptTokenKind.Plain),
            ("=", ScriptTokenKind.Operator), (" ", ScriptTokenKind.Plain), ("ethic_militarist   ", ScriptTokenKind.Value),
            ("# Militarist", ScriptTokenKind.Comment),
        }, tokens.Select(t => (t.Text, t.Kind)));
    }

    [Theory]
    [InlineData("}", "}", ScriptTokenKind.Plain)]
    [InlineData("    a", "    a", ScriptTokenKind.Value)]
    public void Highlights_braces_and_bare_items(string line, string text, ScriptTokenKind kind) =>
        Assert.Equal((text, kind), ScriptHighlighter.Line(line).Select(t => (t.Text, t.Kind)).Single());

    [Fact]
    public void Highlights_comparison_and_block_openers()
    {
        Assert.Equal(new[] { "years_passed", " ", ">=", " ", "5" }, ScriptHighlighter.Line("years_passed >= 5").Select(t => t.Text));
        Assert.Equal(ScriptTokenKind.Plain, ScriptHighlighter.Line("modifier = {").Last().Kind);
    }

    [Fact]
    public void Quotes_values_with_special_characters_and_highlighting_ignores_hash_in_quotes()
    {
        var text = PdxScriptPrinter.Print(ParadoxScriptParser.Parse("a = \"x # y\""));

        Assert.Equal("a = \"x # y\"", text);
        Assert.DoesNotContain(ScriptHighlighter.Line(text), t => t.Kind == ScriptTokenKind.Comment);
    }

    [Fact]
    public void Escapes_quotes_inside_quoted_values() =>
        Assert.Equal("a = \"x\\\"y=z\"", PdxScriptPrinter.Print(Single("x\"y=z")));

    static PdxBlock Single(string value)
    {
        var b = new PdxBlock();
        b.Entries.Add(new PdxEntry("a", "=", value));
        return b;
    }

    [Fact]
    public void Highlights_optional_assignment_operator() =>
        Assert.Contains(ScriptHighlighter.Line("a ?= b"), t => t is { Text: "?=", Kind: ScriptTokenKind.Operator });

    [Fact]
    public void Annotations_are_single_line() =>
        Assert.Equal("a = b   # line1 line2", PdxScriptPrinter.Print(ParadoxScriptParser.Parse("a = b"), v => "line1\nline2"));
}

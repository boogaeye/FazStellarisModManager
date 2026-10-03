using System.Text;

namespace FazStellarisModmanager.Core.Descriptors;

/// <summary>
/// Turns parsed Paradox script back into readable text: 4 spaces per level, "key op value" lines, bare items on their own lines.
/// The parser stores entries and bare items separately, so a block prints its entries first, then its items.
/// </summary>
public static class PdxScriptPrinter
{
    /// <param name="annotate">Optional: a display name for a string value, appended as "   # name" (skipped when null or equal to the value).</param>
    public static string Print(PdxBlock block, Func<string, string?>? annotate = null)
    {
        var sb = new StringBuilder();
        WriteBody(sb, block, 0, annotate);
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Prints "key = { … }" for a whole named block.</summary>
    public static string PrintNamed(string key, PdxBlock block, Func<string, string?>? annotate = null)
    {
        var sb = new StringBuilder();
        WriteEntry(sb, new PdxEntry(key, "=", block), 0, annotate);
        return sb.ToString().TrimEnd('\n');
    }

    static void WriteBody(StringBuilder sb, PdxBlock block, int depth, Func<string, string?>? annotate)
    {
        foreach (var e in block.Entries) WriteEntry(sb, e, depth, annotate);
        foreach (var item in block.Items)
        {
            Indent(sb, depth);
            if (item is PdxBlock child) WriteBlock(sb, child, depth, annotate);
            else sb.Append(Value((string)item)).Append(Note((string)item, annotate)).Append('\n');
        }
    }

    static void WriteEntry(StringBuilder sb, PdxEntry e, int depth, Func<string, string?>? annotate)
    {
        Indent(sb, depth);
        sb.Append(e.Key).Append(' ').Append(e.Op).Append(' ');
        if (e.Value is PdxBlock b) WriteBlock(sb, b, depth, annotate);
        else sb.Append(Value((string)e.Value)).Append(Note((string)e.Value, annotate)).Append('\n');
    }

    static void WriteBlock(StringBuilder sb, PdxBlock b, int depth, Func<string, string?>? annotate)
    {
        if (b.Entries.Count == 0 && b.Items.Count == 0)
        {
            sb.Append("{ }\n");
            return;
        }
        sb.Append("{\n");
        WriteBody(sb, b, depth + 1, annotate);
        Indent(sb, depth);
        sb.Append("}\n");
    }

    static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 4);

    static string Value(string s) => s.Length == 0 || s.Any(char.IsWhiteSpace) ? "\"" + s + "\"" : s;

    static string Note(string value, Func<string, string?>? annotate)
    {
        if (annotate is null) return "";
        var name = annotate(value);
        return string.IsNullOrEmpty(name) || string.Equals(name, value, StringComparison.OrdinalIgnoreCase) ? "" : "   # " + name;
    }
}

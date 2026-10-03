using System.Text;

namespace FazStellarisModmanager.Core.Descriptors;

/// <summary>A "key op value" entry. Value is either a string or a nested <see cref="PdxBlock"/>.</summary>
public sealed record PdxEntry(string Key, string Op, object Value);

/// <summary>A { ... } block (or the whole file): keyed entries plus bare items such as tag lists.</summary>
public sealed class PdxBlock
{
    public List<PdxEntry> Entries { get; } = [];

    /// <summary>Bare values inside the block: strings or anonymous nested blocks.</summary>
    public List<object> Items { get; } = [];

    public IEnumerable<string> StringItems => Items.OfType<string>();

    /// <summary>Last string value for the key (later definitions win, as in Paradox scripts).</summary>
    public string? GetString(string key) =>
        Entries.LastOrDefault(e => e.Value is string && e.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value as string;

    public PdxBlock? GetBlock(string key) =>
        Entries.LastOrDefault(e => e.Value is PdxBlock && e.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value as PdxBlock;
}

/// <summary>Lenient parser for Paradox script (mod descriptors, .dlc files, common/*.txt).</summary>
public static class ParadoxScriptParser
{
    enum Kind { Word, Quoted, Open, Close, Op }

    readonly record struct Token(Kind Kind, string Text);

    public static PdxBlock Parse(string text)
    {
        var tokens = Tokenize(text);
        int pos = 0;
        return ParseBlock(tokens, ref pos, depth: 0);
    }

    const int MaxDepth = 256; // third-party input: bound recursion

    static PdxBlock ParseBlock(List<Token> t, ref int pos, int depth)
    {
        var block = new PdxBlock();
        int skipped = 0; // too-deep '{' flattened into this block; their '}' must not close it
        while (pos < t.Count)
        {
            var tok = t[pos];
            if (tok.Kind == Kind.Close)
            {
                pos++;
                if (skipped > 0) { skipped--; continue; }
                if (depth == 0) continue; // stray '}' at top level: ignore
                return block;
            }
            if (tok.Kind == Kind.Open)
            {
                pos++;
                if (depth >= MaxDepth) skipped++;
                else block.Items.Add(ParseBlock(t, ref pos, depth + 1));
                continue;
            }
            if (tok.Kind == Kind.Op) { pos++; continue; } // stray operator

            if (pos + 1 < t.Count && t[pos + 1].Kind == Kind.Op)
            {
                var op = t[pos + 1].Text;
                pos += 2;
                if (pos >= t.Count) break;
                var v = t[pos];
                if (v.Kind == Kind.Open)
                {
                    pos++;
                    if (depth >= MaxDepth) skipped++;
                    else block.Entries.Add(new PdxEntry(tok.Text, op, ParseBlock(t, ref pos, depth + 1)));
                }
                else if (v.Kind is Kind.Word or Kind.Quoted)
                {
                    pos++;
                    block.Entries.Add(new PdxEntry(tok.Text, op, v.Text));
                }
                // otherwise malformed ("a = }"): leave the '}' for the loop to handle
                continue;
            }

            pos++;
            block.Items.Add(tok.Text);
        }
        return block;
    }

    /// <summary>True if the quote at q is followed only by spaces/tabs then a line break or end of input (so a preceding backslash is literal, e.g. a path).</summary>
    static bool QuoteEndsLine(string s, int q)
    {
        int j = q + 1;
        while (j < s.Length && (s[j] is ' ' or '\t')) j++;
        return j >= s.Length || (s[j] is '\n' or '\r');
    }

    static List<Token> Tokenize(string s)
    {
        var list = new List<Token>();
        int i = s.Length > 0 && s[0] == (char)0xFEFF ? 1 : 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#') { while (i < s.Length && s[i] != '\n') i++; continue; }
            if (c == '{') { list.Add(new Token(Kind.Open, "{")); i++; continue; }
            if (c == '}') { list.Add(new Token(Kind.Close, "}")); i++; continue; }
            if (c is '=' or '<' or '>' or '!' || (c == '?' && i + 1 < s.Length && s[i + 1] == '='))
            {
                if (i + 1 < s.Length && s[i + 1] == '=') { list.Add(new Token(Kind.Op, s.Substring(i, 2))); i += 2; }
                else { list.Add(new Token(Kind.Op, c.ToString())); i++; }
                continue;
            }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] == '"' && !QuoteEndsLine(s, i + 1)) { sb.Append('"'); i += 2; continue; }
                    sb.Append(s[i]);
                    i++;
                }
                i++; // closing quote
                list.Add(new Token(Kind.Quoted, sb.ToString()));
                continue;
            }
            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not ('{' or '}' or '=' or '<' or '>' or '!' or '"' or '#')
                   && !(s[i] == '?' && i + 1 < s.Length && s[i + 1] == '=')) i++;
            list.Add(new Token(Kind.Word, s[start..i]));
        }
        return list;
    }
}

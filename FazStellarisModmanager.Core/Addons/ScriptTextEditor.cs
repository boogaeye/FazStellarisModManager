using System.Text;

namespace FazStellarisModmanager.Core.Addons;

/// <summary>
/// Minimal in-place edits of Paradox script text: only the edited value tokens change, and comments, spacing and line endings stay exactly as
/// they were. Lexing follows <see cref="Descriptors.ParadoxScriptParser"/>: quotes (with its escaped-quote rule) and # comments are respected.
/// </summary>
public static class ScriptTextEditor
{
    enum Kind { Word, Quoted, Open, Close, Op }

    // [Start, End) in the text.
    readonly record struct Token(Kind Kind, int Start, int End);

    static readonly string Lf = ((char)10).ToString();
    static readonly string Crlf = (char)13 + Lf;
    static readonly string Tab = ((char)9).ToString();

    /// <summary>The text's line ending, from its first line break: CRLF or LF (LF when there is none).</summary>
    public static string LineEnding(string text)
    {
        var i = text.IndexOf((char)10);
        return i > 0 && text[i - 1] == (char)13 ? Crlf : Lf;
    }

    /// <summary>Positions of the '{' and its matching '}' of every top-level "key = { … }" block, in file order. An unclosed block ends at the text's length.</summary>
    public static IReadOnlyList<(int Open, int Close)> FindBlocks(string text, string key)
    {
        var tokens = Tokenize(text);
        return Blocks(text, tokens, key).Select(b => (tokens[b.Open].Start, b.Close < tokens.Count ? tokens[b.Close].Start : text.Length)).ToList();
    }

    /// <summary>
    /// Sets "name = value" in every top-level block named <paramref name="blockKey"/>: replaces the value (a token, or a whole { } block) of each
    /// of the block's direct "name" entries, or, when it has none, inserts the line ending, a tab and "name = value" right after the '{'.
    /// Returns the same instance when no block has that name.
    /// </summary>
    public static string SetValue(string text, string blockKey, string name, string value)
    {
        var tokens = Tokenize(text);
        var blocks = Blocks(text, tokens, blockKey);
        if (blocks.Count == 0) return text;

        // Edit from the end backwards so earlier positions stay valid.
        var sb = new StringBuilder(text);
        for (var b = blocks.Count - 1; b >= 0; b--)
        {
            var (open, close) = blocks[b];
            var spans = ValueSpans(text, tokens, open, close, name);
            if (spans.Count == 0) sb.Insert(tokens[open].End, LineEnding(text) + Tab + name + " = " + value);
            for (var s = spans.Count - 1; s >= 0; s--)
                sb.Remove(spans[s].Start, spans[s].End - spans[s].Start).Insert(spans[s].Start, value);
        }
        return sb.ToString();
    }

    // Token indexes of the '{' and matching '}' (tokens.Count when unclosed) of every top-level "key op {" block.
    static List<(int Open, int Close)> Blocks(string text, List<Token> t, string key)
    {
        var result = new List<(int, int)>();
        var depth = 0;
        for (var i = 0; i < t.Count; i++)
        {
            var kind = t[i].Kind;
            if (kind == Kind.Open) { depth++; continue; }
            if (kind == Kind.Close) { if (depth > 0) depth--; continue; } // a stray '}' at top level is ignored, as the parser does
            if (depth != 0 || kind is not (Kind.Word or Kind.Quoted)) continue;
            if (i + 2 >= t.Count || t[i + 1].Kind != Kind.Op || t[i + 2].Kind != Kind.Open) continue;
            if (!Text(text, t[i]).Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            var close = Matching(t, i + 2);
            result.Add((i + 2, close));
            i = close;
        }
        return result;
    }

    // Spans of the values of the block's direct "name op value" entries; nested blocks are skipped.
    static List<(int Start, int End)> ValueSpans(string text, List<Token> t, int open, int close, string name)
    {
        var spans = new List<(int, int)>();
        for (var i = open + 1; i < close; i++)
        {
            if (t[i].Kind == Kind.Open)
            {
                i = Matching(t, i);
                continue;
            }
            if (t[i].Kind is not (Kind.Word or Kind.Quoted) || i + 2 >= close || t[i + 1].Kind != Kind.Op) continue;
            var v = i + 2;
            var matches = Text(text, t[i]).Equals(name, StringComparison.OrdinalIgnoreCase);
            if (t[v].Kind == Kind.Open)
            {
                var end = Matching(t, v);
                if (matches) spans.Add((t[v].Start, end < t.Count ? t[end].End : text.Length));
                i = end;
            }
            else if (t[v].Kind is Kind.Word or Kind.Quoted)
            {
                if (matches) spans.Add((t[v].Start, t[v].End));
                i = v;
            }
            // otherwise malformed ("a = }" or "a = ="): move on
        }
        return spans;
    }

    // Index of the '}' closing the '{' at open, or t.Count when it is never closed.
    static int Matching(List<Token> t, int open)
    {
        var depth = 0;
        for (var i = open; i < t.Count; i++)
        {
            if (t[i].Kind == Kind.Open) depth++;
            else if (t[i].Kind == Kind.Close && --depth == 0) return i;
        }
        return t.Count;
    }

    // A word as written, or a quoted string without its quotes (an unclosed one runs to the end).
    static string Text(string text, Token token)
    {
        if (token.Kind != Kind.Quoted) return text[token.Start..token.End];
        var end = token.End > token.Start + 1 && text[token.End - 1] == '"' ? token.End - 1 : token.End;
        return text[(token.Start + 1)..end];
    }

    /// <summary>True if the quote at q is followed only by spaces/tabs then a line break or end of input (so a preceding backslash is literal, e.g. a path).</summary>
    static bool QuoteEndsLine(string s, int q)
    {
        int j = q + 1;
        while (j < s.Length && (s[j] == ' ' || s[j] == (char)9)) j++;
        return j >= s.Length || s[j] == (char)10 || s[j] == (char)13;
    }

    static List<Token> Tokenize(string s)
    {
        var list = new List<Token>();
        int i = s.Length > 0 && s[0] == (char)0xFEFF ? 1 : 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#') { while (i < s.Length && s[i] != (char)10) i++; continue; }
            if (c == '{') { list.Add(new Token(Kind.Open, i, i + 1)); i++; continue; }
            if (c == '}') { list.Add(new Token(Kind.Close, i, i + 1)); i++; continue; }
            if (c is '=' or '<' or '>' or '!' || (c == '?' && i + 1 < s.Length && s[i + 1] == '='))
            {
                var length = i + 1 < s.Length && s[i + 1] == '=' ? 2 : 1;
                list.Add(new Token(Kind.Op, i, i + length));
                i += length;
                continue;
            }
            if (c == '"')
            {
                var start = i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == (char)92 && i + 1 < s.Length && s[i + 1] == '"' && !QuoteEndsLine(s, i + 1)) { i += 2; continue; }
                    i++;
                }
                i = Math.Min(i + 1, s.Length); // closing quote
                list.Add(new Token(Kind.Quoted, start, i));
                continue;
            }
            int wordStart = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not ('{' or '}' or '=' or '<' or '>' or '!' or '"' or '#')
                   && !(s[i] == '?' && i + 1 < s.Length && s[i + 1] == '=')) i++;
            list.Add(new Token(Kind.Word, wordStart, i));
        }
        return list;
    }
}

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
        return ParseBlock(tokens, ref pos, topLevel: true);
    }

    static PdxBlock ParseBlock(List<Token> t, ref int pos, bool topLevel)
    {
        var block = new PdxBlock();
        while (pos < t.Count)
        {
            var tok = t[pos];
            if (tok.Kind == Kind.Close)
            {
                pos++;
                if (topLevel) continue; // stray '}' at top level: ignore
                return block;
            }
            if (tok.Kind == Kind.Open)
            {
                pos++;
                block.Items.Add(ParseBlock(t, ref pos, topLevel: false));
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
                    block.Entries.Add(new PdxEntry(tok.Text, op, ParseBlock(t, ref pos, topLevel: false)));
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

    static List<Token> Tokenize(string s)
    {
        var list = new List<Token>();
        int i = s.Length > 0 && s[0] == '﻿' ? 1 : 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#') { while (i < s.Length && s[i] != '\n') i++; continue; }
            if (c == '{') { list.Add(new Token(Kind.Open, "{")); i++; continue; }
            if (c == '}') { list.Add(new Token(Kind.Close, "}")); i++; continue; }
            if (c is '=' or '<' or '>' or '!')
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
                    if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] is '"' or '\\') { sb.Append(s[i + 1]); i += 2; continue; }
                    sb.Append(s[i]);
                    i++;
                }
                i++; // closing quote
                list.Add(new Token(Kind.Quoted, sb.ToString()));
                continue;
            }
            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not ('{' or '}' or '=' or '<' or '>' or '!' or '"' or '#')) i++;
            list.Add(new Token(Kind.Word, s[start..i]));
        }
        return list;
    }
}

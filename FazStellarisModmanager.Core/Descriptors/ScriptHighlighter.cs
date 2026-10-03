using System.Text.RegularExpressions;

namespace FazStellarisModmanager.Core.Descriptors;

public enum ScriptTokenKind { Plain, Key, Operator, Value, Comment }

public sealed record ScriptToken(string Text, ScriptTokenKind Kind);

/// <summary>Splits one line printed by <see cref="PdxScriptPrinter"/> into parts the UI colours.</summary>
public static class ScriptHighlighter
{
    static readonly Regex Assignment = new(@"^(\s*)([^\s=<>!{}]+)(\s*)(>=|<=|!=|==|=|<|>)(\s*)(.*)$", RegexOptions.Compiled);

    public static List<ScriptToken> Line(string line)
    {
        var tokens = new List<ScriptToken>();
        var hash = line.IndexOf('#');
        var code = hash >= 0 ? line[..hash] : line;
        var m = Assignment.Match(code);
        if (m.Success)
        {
            Add(tokens, m.Groups[1].Value, ScriptTokenKind.Plain);
            Add(tokens, m.Groups[2].Value, ScriptTokenKind.Key);
            Add(tokens, m.Groups[3].Value, ScriptTokenKind.Plain);
            Add(tokens, m.Groups[4].Value, ScriptTokenKind.Operator);
            Add(tokens, m.Groups[5].Value, ScriptTokenKind.Plain);
            var rest = m.Groups[6].Value;
            Add(tokens, rest, rest.Trim() is "{" or "{ }" ? ScriptTokenKind.Plain : ScriptTokenKind.Value);
        }
        else
        {
            Add(tokens, code, code.Trim() is "" or "{" or "}" or "{ }" ? ScriptTokenKind.Plain : ScriptTokenKind.Value);
        }
        if (hash >= 0) Add(tokens, line[hash..], ScriptTokenKind.Comment);
        return tokens;
    }

    static void Add(List<ScriptToken> tokens, string text, ScriptTokenKind kind)
    {
        if (text.Length > 0) tokens.Add(new ScriptToken(text, kind));
    }
}

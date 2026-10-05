namespace FazStellarisModmanager.Core.Conflicts;

public enum DefinitionKind
{
    /// <summary>Each top-level "name = …" is a definition (common/&lt;type&gt;).</summary>
    TopLevel,
    /// <summary>"NCategory.Key" for each key inside a top-level block (common/defines).</summary>
    Defines,
    /// <summary>The id of each top-level "…event = { id = x }" block (events).</summary>
    Events,
    /// <summary>The "key" value inside each top-level block, whatever the block keyword is (section_templates, component_templates, ...).</summary>
    KeyField,
    /// <summary>The "name" value inside each top-level block, whatever the block keyword is (global_ship_designs, scripted_loc, ...).</summary>
    NameField,
    /// <summary>Top-level "@name = value" (common/scripted_variables). Elsewhere @variables are file-local.</summary>
    Variables,
}

/// <summary>Finds the names a Paradox script file defines without building a parse tree. Tolerates broken files.</summary>
public static class DefinitionScanner
{
    public static List<string> Names(string text, DefinitionKind kind)
    {
        var names = new List<string>();
        var depth = 0;
        string? prevWord = null;   // the previous token, when it was a word
        string? key0 = null;       // the key just assigned at depth 0
        string? block0 = null;     // the key of the depth-0 block we are inside
        var expectName = false;
        var field = FieldFor(kind);

        foreach (var (token, isWord) in Tokens(text))
        {
            if (isWord)
            {
                if (expectName) names.Add(token);
                expectName = false;
                prevWord = token;
                continue;
            }

            expectName = false;
            if (token == "=" && prevWord is not null)
            {
                if (depth == 0)
                {
                    key0 = prevWord;
                    if (kind == DefinitionKind.TopLevel && !prevWord.StartsWith('@')) names.Add(prevWord);
                    else if (kind == DefinitionKind.Variables && prevWord.StartsWith('@')) names.Add(prevWord);
                }
                else if (depth == 1 && block0 is not null)
                {
                    if (kind == DefinitionKind.Defines) names.Add(block0 + "." + prevWord);
                    else if (field is not null && prevWord.Equals(field, StringComparison.OrdinalIgnoreCase)
                             && (kind != DefinitionKind.Events || block0.EndsWith("event", StringComparison.OrdinalIgnoreCase))) expectName = true;
                }
            }
            else if (token == "{")
            {
                if (depth == 0) { block0 = key0; key0 = null; }
                depth++;
            }
            else if (token == "}")
            {
                if (depth > 0) depth--;
                if (depth == 0) block0 = null;
            }
            prevWord = null;
        }
        return names;
    }

    // The field inside a top-level block that holds the definition name, for the kinds that name definitions that way.
    static string? FieldFor(DefinitionKind kind) => kind switch
    {
        DefinitionKind.Events => "id",
        DefinitionKind.KeyField => "key",
        DefinitionKind.NameField => "name",
        _ => null,
    };

    // Words (bare or quoted, quotes removed) and the symbols { } = and comparison operators. "#" starts a comment.
    static IEnumerable<(string Token, bool IsWord)> Tokens(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#')
            {
                while (i < text.Length && text[i] != (char)10) i++;
                continue;
            }
            if (c == '"')
            {
                var end = text.IndexOf('"', i + 1);
                if (end < 0) end = text.Length;
                yield return (text[(i + 1)..end], true);
                i = end + 1;
                continue;
            }
            if (c is '{' or '}')
            {
                yield return (c.ToString(), false);
                i++;
                continue;
            }
            if (c is '=' or '<' or '>' or '!' or '?')
            {
                var j = i + 1;
                if (j < text.Length && text[j] == '=') j++;
                var op = text[i..j];
                yield return (op is "=" or "==" or "?=" ? "=" : op, false);
                i = j;
                continue;
            }
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '=' or '#' or '"' or '<' or '>' or '!' or '?')) i++;
            yield return (text[start..i], true);
        }
    }
}

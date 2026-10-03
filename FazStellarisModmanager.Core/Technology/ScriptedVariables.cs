using System.Text.RegularExpressions;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>Paradox "@name = value" variables, file-local or from common/scripted_variables.</summary>
public static class ScriptedVariables
{
    static readonly Regex Definition = new(@"^\s*@([A-Za-z0-9_]+)\s*=\s*([^\s#]+)", RegexOptions.Multiline);

    public static Dictionary<string, string> Parse(string text)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Definition.Matches(text)) vars[m.Groups[1].Value] = m.Groups[2].Value;
        return vars;
    }

    /// <summary>Follows @references (local first, then global, at most 5 hops). Unresolvable values are returned as written; inline math as "(formula)".</summary>
    public static string Resolve(string value, IReadOnlyDictionary<string, string> local, IReadOnlyDictionary<string, string> global)
    {
        if (value.StartsWith("@[", StringComparison.Ordinal)) return "(formula)";
        var current = value;
        for (int hop = 0; hop < 5 && current.StartsWith('@'); hop++)
        {
            if (current.StartsWith("@[", StringComparison.Ordinal)) return "(formula)";
            var name = current[1..];
            if (local.TryGetValue(name, out var next) || global.TryGetValue(name, out next)) current = next;
            else return current;
        }
        return current.StartsWith("@[", StringComparison.Ordinal) ? "(formula)" : current;
    }
}

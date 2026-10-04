using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>Short readable wording of a trigger block, e.g. "Spiritualist, not Worker co-op" or "Civic: Imperial Cult".</summary>
public static class TriggerSummary
{
    // Scope plumbing that says nothing about the empire.
    static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "is_scope_valid", "exists", "is_scope_type", "always",
    };

    static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["is_gestalt"] = "Gestalt",
        ["is_hive_empire"] = "Hive mind",
        ["is_machine_empire"] = "Machine empire",
        ["is_megacorp"] = "Megacorp",
        ["is_worker_coop_empire"] = "Worker co-op",
        ["has_civic"] = "Civic",
        ["has_valid_civic"] = "Civic",
    };

    // How many scripted triggers deep "x = yes" is expanded into x's own conditions.
    const int MaxScriptedDepth = 3;

    sealed record Context(Func<string, string?> Name, Func<string, PdxBlock?>? Scripted);

    /// <summary>
    /// "always" for a missing or empty trigger. <paramref name="name"/> localises values such as civic keys. <paramref name="scripted"/>
    /// returns a scripted trigger's body (common/scripted_triggers), so "bureaucrat_is_priest = yes" reads as the conditions inside it.
    /// </summary>
    public static string Describe(PdxBlock? trigger, Func<string, string?> name, Func<string, PdxBlock?>? scripted = null)
    {
        var parts = trigger is null ? [] : Block(trigger, false, new Context(name, scripted), 0);
        return parts.Count == 0 ? "always" : string.Join(", ", parts);
    }

    static List<string> Block(PdxBlock block, bool negate, Context c, int depth)
    {
        var parts = new List<string>();
        foreach (var e in block.Entries)
            foreach (var p in Entry(e, negate, c, depth))
                if (!parts.Contains(p)) parts.Add(p);
        return parts;
    }

    // "a or b": one alternative per entry, each worded with the given negation.
    static List<string> AnyOf(PdxBlock block, bool negate, Context c, int depth)
    {
        var alternatives = block.Entries
            .Select(x => string.Join(", ", Entry(x, negate, c, depth)))
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();
        return alternatives.Count == 0 ? [] : [string.Join(" or ", alternatives)];
    }

    static List<string> Entry(PdxEntry e, bool negate, Context c, int depth)
    {
        // always = no switches a branch off entirely.
        if (e.Key.Equals("always", StringComparison.OrdinalIgnoreCase) && e.Value is string a
            && a.Equals("no", StringComparison.OrdinalIgnoreCase) != negate)
            return ["never"];
        if (Ignored.Contains(e.Key)) return [];
        if (e.Value is PdxBlock b)
        {
            switch (e.Key.ToLowerInvariant())
            {
                case "not":
                case "nor":
                    return Block(b, !negate, c, depth);
                case "or":
                case "nand":
                    // NAND = NOT AND = OR of negations. A negated OR is an AND of negations (De Morgan).
                    var childNegate = e.Key.Equals("nand", StringComparison.OrdinalIgnoreCase) != negate;
                    return negate ? Block(b, childNegate, c, depth) : AnyOf(b, childNegate, c, depth);
                default:
                    // AND, scope changes (owner = { … }), limits: the conditions inside still apply.
                    // Negated, an AND becomes "not a or not b" (De Morgan).
                    return negate ? AnyOf(b, true, c, depth) : Block(b, false, c, depth);
            }
        }

        var value = (string)e.Value;
        string label;
        if (value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            var positive = value.Equals("yes", StringComparison.OrdinalIgnoreCase) != negate;
            // Well-known checks keep their short name even though some (is_gestalt) are scripted triggers too.
            if (depth < MaxScriptedDepth && !Names.ContainsKey(e.Key) && c.Scripted?.Invoke(e.Key) is { } body
                && (positive ? Block(body, false, c, depth + 1) : AnyOf(body, true, c, depth + 1)) is { Count: > 0 } expanded)
                return expanded;
            return [positive ? Friendly(e.Key) : "not " + Friendly(e.Key)];
        }
        if (e.Op == "=" && (e.Key.StartsWith("has_", StringComparison.OrdinalIgnoreCase) || e.Key.StartsWith("is_", StringComparison.OrdinalIgnoreCase)))
            label = $"{Friendly(e.Key)}: {(c.Name(value) is { Length: > 0 and <= 60 } n ? n : value)}";
        else
            label = $"{e.Key} {e.Op} {value}";
        return [negate ? "not " + label : label];
    }

    static string Friendly(string key)
    {
        if (Names.TryGetValue(key, out var known)) return known;
        var s = key;
        if (s.StartsWith("has_", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        else if (s.StartsWith("is_", StringComparison.OrdinalIgnoreCase)) s = s[3..];
        s = s.Replace('_', ' ');
        return s.Length == 0 ? key : char.ToUpperInvariant(s[0]) + s[1..];
    }
}

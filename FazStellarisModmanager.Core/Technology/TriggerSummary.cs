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

    /// <summary>"always" for a missing or empty trigger. <paramref name="name"/> localises values such as civic keys.</summary>
    public static string Describe(PdxBlock? trigger, Func<string, string?> name)
    {
        var parts = trigger is null ? [] : Block(trigger, false, name);
        return parts.Count == 0 ? "always" : string.Join(", ", parts);
    }

    static List<string> Block(PdxBlock block, bool negate, Func<string, string?> name)
    {
        var parts = new List<string>();
        foreach (var e in block.Entries)
            foreach (var p in Entry(e, negate, name))
                if (!parts.Contains(p)) parts.Add(p);
        return parts;
    }

    static List<string> Entry(PdxEntry e, bool negate, Func<string, string?> name)
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
                    return Block(b, !negate, name);
                case "or":
                case "nand":
                    // NAND = NOT AND = OR of negations. A negated OR is an AND of negations (De Morgan).
                    var childNegate = e.Key.Equals("nand", StringComparison.OrdinalIgnoreCase) != negate;
                    if (negate) return Block(b, childNegate, name);
                    var alternatives = b.Entries
                        .Select(x => string.Join(", ", Entry(x, childNegate, name)))
                        .Where(s => s.Length > 0)
                        .Distinct()
                        .ToList();
                    return alternatives.Count == 0 ? [] : [string.Join(" or ", alternatives)];
                default:
                    // AND, scope changes (owner = { … }), limits: the conditions inside still apply.
                    // Negated, an AND becomes "not a or not b" (De Morgan).
                    if (!negate) return Block(b, false, name);
                    var negated = b.Entries
                        .Select(x => string.Join(", ", Entry(x, true, name)))
                        .Where(s => s.Length > 0)
                        .Distinct()
                        .ToList();
                    return negated.Count == 0 ? [] : [string.Join(" or ", negated)];
            }
        }

        var value = (string)e.Value;
        string label;
        if (value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            var positive = value.Equals("yes", StringComparison.OrdinalIgnoreCase) != negate;
            return [positive ? Friendly(e.Key) : "not " + Friendly(e.Key)];
        }
        if (e.Op == "=" && (e.Key.StartsWith("has_", StringComparison.OrdinalIgnoreCase) || e.Key.StartsWith("is_", StringComparison.OrdinalIgnoreCase)))
            label = $"{Friendly(e.Key)}: {(name(value) is { Length: > 0 and <= 60 } n ? n : value)}";
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

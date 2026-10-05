using System.Globalization;
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Saves;

namespace FazStellarisModmanager.Core.Conditions;

public enum Truth { True, False, Unknown }

/// <summary>One condition line with its result; Children for blocks (AND/OR/NOT/…, scripted triggers, unknown scopes).</summary>
/// <summary>Note: extra context for the line, e.g. how many years until a year condition becomes true.</summary>
public sealed record ConditionNode(string Text, Truth Result, IReadOnlyList<ConditionNode> Children, string? Note = null);

/// <summary>What the viewer's empire has, from live data. Sets are case-insensitive.</summary>
public sealed record EmpireFacts(IReadOnlySet<string> Techs, IReadOnlySet<string> Flags, IReadOnlySet<string> GlobalFlags,
    IReadOnlySet<string> Perks, IReadOnlySet<string> Traditions, IReadOnlySet<string> Civics, IReadOnlySet<string> Ethics,
    string? Origin, string? Authority, string? CountryType, IReadOnlySet<string> Dlcs, bool IsPlayer,
    int? YearsPassed = null, int? MidGameStart = null, int? EndGameStart = null)
{
    /// <summary>Stellaris games start on 2200.01.01.</summary>
    public const int GameStartYear = 2200;

    /// <summary>Whole years since the game start for a save date like "2387.09.17", or null if it can't be read.</summary>
    public static int? YearsSinceStart(string? date) =>
        date is not null && date.Split('.') is { Length: >= 1 } parts && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year - GameStartYear
            : null;

    static HashSet<string> Set(IEnumerable<string>? items) => new(items ?? [], StringComparer.OrdinalIgnoreCase);

    public static EmpireFacts From(SaveCountry c, GameSnapshot s, bool isPlayer) => new(
        Set(c.Techs), Set(c.Flags), Set(s.GlobalFlags), Set(c.Holdings?.Perks), Set(c.Holdings?.Traditions), Set(c.Holdings?.Civics),
        Set(c.Ethics), c.Holdings?.Origin, c.Holdings?.Authority, c.Type, Set(s.Dlcs), isPlayer,
        YearsSinceStart(s.Date), s.Galaxy?.MidGameStart, s.Galaxy?.EndGameStart);
}

/// <summary>
/// Three-valued (Kleene) evaluation of a country trigger block against an empire's facts. Anything it can't decide (scope changes,
/// comparisons, unknown triggers, scripted triggers with parameters) is Unknown.
/// </summary>
public sealed class ConditionEvaluator(Func<string, PdxBlock?> scriptedTrigger)
{
    const int MaxScriptedDepth = 8;

    static readonly HashSet<string> TooltipText = new(StringComparer.OrdinalIgnoreCase) { "fail_text", "success_text", "text" };

    /// <summary>The root is the implicit AND of all entries.</summary>
    public ConditionNode Evaluate(PdxBlock block, EmpireFacts facts)
    {
        var children = Entries(block, facts, 0);
        return new ConditionNode("All of", And(children.Select(c => c.Result)), children);
    }

    List<ConditionNode> Entries(PdxBlock block, EmpireFacts facts, int depth) =>
        block.Entries.Select(e => Entry(e, facts, depth)).ToList();

    ConditionNode Entry(PdxEntry e, EmpireFacts facts, int depth)
    {
        if (e.Value is PdxBlock b) return Block(e.Key, b, facts, depth);
        var value = (string)e.Value;
        var text = $"{e.Key} {e.Op} {value}";
        var result = Leaf(e.Key, e.Op, value, facts, depth, out var children, out var note);
        return new ConditionNode(text, result, children, note);
    }

    ConditionNode Block(string key, PdxBlock b, EmpireFacts facts, int depth)
    {
        switch (key.ToLowerInvariant())
        {
            case "and":
            case "hidden_trigger":
            {
                var c = Entries(b, facts, depth);
                return new ConditionNode(key, And(c.Select(x => x.Result)), c);
            }
            case "or":
            {
                var c = Entries(b, facts, depth);
                return new ConditionNode(key, Or(c.Select(x => x.Result)), c);
            }
            case "not":
            case "nor":
            {
                var c = Entries(b, facts, depth);
                return new ConditionNode(key, Not(Or(c.Select(x => x.Result))), c);
            }
            case "nand":
            {
                var c = Entries(b, facts, depth);
                return new ConditionNode(key, Not(And(c.Select(x => x.Result))), c);
            }
            case "custom_tooltip":
            {
                var c = b.Entries.Where(x => !TooltipText.Contains(x.Key)).Select(x => Entry(x, facts, depth)).ToList();
                return new ConditionNode(key, c.Count == 0 ? Truth.Unknown : And(c.Select(x => x.Result)), c);
            }
            default:
                // A scope change (or anything else we don't model): its contents aren't about this empire, so they are shown, not judged.
                return new ConditionNode(key, Truth.Unknown, UnknownEntries(b));
        }
    }

    static List<ConditionNode> UnknownEntries(PdxBlock b) =>
        b.Entries.Select(e => e.Value is PdxBlock inner
            ? new ConditionNode(e.Key, Truth.Unknown, UnknownEntries(inner))
            : new ConditionNode($"{e.Key} {e.Op} {e.Value}", Truth.Unknown, [])).ToList();

    Truth Leaf(string key, string op, string value, EmpireFacts f, int depth, out IReadOnlyList<ConditionNode> children, out string? note)
    {
        children = [];
        note = null;
        if (IsYearTrigger(key))
        {
            if (YearValue(key, f) is not int y || f.YearsPassed is not int passed
                || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return Truth.Unknown;
            var result = Compare(y, op, n);
            note = YearNote(y, op, n, result, passed);
            return result;
        }
        if (op != "=") return Truth.Unknown;
        var yes = value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        var no = value.Equals("no", StringComparison.OrdinalIgnoreCase);

        Truth Flag(bool state) => yes ? Of(state) : no ? Of(!state) : Truth.Unknown;
        // A value that is a script variable (@x), a scope reference (event_target:x, root) or a parameter ($X$) can't be judged.
        Truth Key(bool state) => yes || no || value.StartsWith('@') || value.Contains('$') || value.Contains(':') ? Truth.Unknown : Of(state);
        static bool Is(string? a, string b) => a is not null && a.Equals(b, StringComparison.OrdinalIgnoreCase);

        switch (key.ToLowerInvariant())
        {
            case "always": return Flag(true);
            case "has_technology": return Key(f.Techs.Contains(value));
            case "has_country_flag": return Key(f.Flags.Contains(value));
            case "has_global_flag": return Key(f.GlobalFlags.Contains(value));
            case "has_ascension_perk": return Key(f.Perks.Contains(value));
            case "has_tradition": return Key(f.Traditions.Contains(value));
            case "has_ethic": return Key(f.Ethics.Contains(value));
            case "has_civic":
            case "has_valid_civic": return Key(f.Civics.Contains(value));
            case "has_origin": return Key(Is(f.Origin, value));
            case "has_authority": return Key(Is(f.Authority, value));
            case "is_country_type": return Key(Is(f.CountryType, value));
            case "is_fallen_empire": return Flag(Is(f.CountryType, "fallen_empire") || Is(f.CountryType, "awakened_fallen_empire"));
            case "is_regular_empire": return Flag(Is(f.CountryType, "default"));
            case "is_machine_empire": return Flag(Is(f.Authority, "auth_machine_intelligence"));
            case "is_hive_empire": return Flag(Is(f.Authority, "auth_hive_mind"));
            case "is_gestalt": return Flag(Is(f.Authority, "auth_machine_intelligence") || Is(f.Authority, "auth_hive_mind"));
            case "host_has_dlc": return Key(f.Dlcs.Contains(value));
            case "is_ai": return Flag(!f.IsPlayer);
        }

        // A scripted trigger called as "name = yes/no" is judged by its body.
        if ((yes || no) && scriptedTrigger(key) is { } body)
        {
            if (depth >= MaxScriptedDepth || HasParameter(body)) return Truth.Unknown;
            var inner = Entries(body, f, depth + 1);
            children = inner;
            var result = And(inner.Select(x => x.Result));
            return no ? Not(result) : result;
        }
        return Truth.Unknown;
    }

    static bool IsYearTrigger(string key) =>
        key.Equals("years_passed", StringComparison.OrdinalIgnoreCase) || key.Equals("mid_game_years_passed", StringComparison.OrdinalIgnoreCase)
        || key.Equals("end_game_years_passed", StringComparison.OrdinalIgnoreCase);

    // years_passed counts from the game start; the mid/end game variants from the galaxy setup's start year (negative before it).
    static int? YearValue(string key, EmpireFacts f) => key.ToLowerInvariant() switch
    {
        "years_passed" => f.YearsPassed,
        "mid_game_years_passed" => f.YearsPassed - f.MidGameStart,
        "end_game_years_passed" => f.YearsPassed - f.EndGameStart,
        _ => null,
    };

    /// <summary>
    /// "now 87 · true in 13 years (2400)" for a condition that time will make true, "now 187 · no longer possible" for one it never
    /// will again (time only moves forward), and just "now N" when it already holds.
    /// </summary>
    public static string YearNote(int value, string op, double target, Truth result, int yearsPassed)
    {
        var now = "now " + value.ToString(CultureInfo.InvariantCulture);
        if (result == Truth.True) return now;
        int? wait = op switch
        {
            ">=" or "=" or "==" => (int)Math.Ceiling(target) - value,
            ">" => (int)Math.Floor(target) + 1 - value,
            _ => null,
        };
        if (wait is int w && w > 0)
        {
            var year = EmpireFacts.GameStartYear + yearsPassed + w;
            return $"{now} · true in {w} year{(w == 1 ? "" : "s")} ({year.ToString(CultureInfo.InvariantCulture)})";
        }
        return $"{now} · no longer possible";
    }

    static Truth Compare(double a, string op, double b) => op switch
    {
        "=" or "==" => Of(a == b),
        ">" => Of(a > b),
        ">=" => Of(a >= b),
        "<" => Of(a < b),
        "<=" => Of(a <= b),
        "!=" => Of(a != b),
        _ => Truth.Unknown,
    };

    static bool HasParameter(PdxBlock b) =>
        b.Entries.Any(e => e.Key.Contains('$') || e.Value is string s && s.Contains('$') || e.Value is PdxBlock inner && HasParameter(inner))
        || b.Items.Any(i => i is string s && s.Contains('$') || i is PdxBlock inner && HasParameter(inner));

    static Truth Of(bool b) => b ? Truth.True : Truth.False;

    static Truth Not(Truth t) => t switch { Truth.True => Truth.False, Truth.False => Truth.True, _ => Truth.Unknown };

    static Truth And(IEnumerable<Truth> items)
    {
        var unknown = false;
        foreach (var t in items)
        {
            if (t == Truth.False) return Truth.False;
            if (t == Truth.Unknown) unknown = true;
        }
        return unknown ? Truth.Unknown : Truth.True;
    }

    static Truth Or(IEnumerable<Truth> items)
    {
        var unknown = false;
        foreach (var t in items)
        {
            if (t == Truth.True) return Truth.True;
            if (t == Truth.Unknown) unknown = true;
        }
        return unknown ? Truth.Unknown : Truth.False;
    }
}

using System.Globalization;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>A tech grant found in an effect block, before it is placed in an event part.</summary>
public sealed record FoundGrant(string Tech, GrantKind Kind, double? Progress, string? Condition, string? Via);

/// <summary>
/// Finds give_technology / add_tech_progress / add_research_option in effect blocks. Follows scripted effects (Via = the
/// outermost effect called) and inline scripts, words if/else_if limits with <paramref name="describe"/> and switch cases, and skips
/// trigger blocks, tooltips (display only) and the effect block of create_country (it runs for the new country).
/// </summary>
public sealed class GrantFinder(ScriptLibrary library, Func<PdxBlock?, string> describe)
{
    const int MaxDepth = 6;

    public static readonly string[] EffectNames = ["give_technology", "add_tech_progress", "add_research_option"];

    static readonly HashSet<string> TriggerBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "limit", "trigger", "exclusive_trigger", "allow", "potential", "ai_chance", "weight", "modifier", "tooltip",
    };

    static readonly IReadOnlyDictionary<string, string> NoParameters = new Dictionary<string, string>();

    public List<FoundGrant> Find(PdxBlock effects)
    {
        var found = new List<FoundGrant>();
        Walk(effects, [], null, 0, [], found);
        return found;
    }

    void Walk(PdxBlock block, List<string> conditions, string? via, int depth, List<string> calls, List<FoundGrant> found)
    {
        foreach (var e in block.Entries)
        {
            var key = e.Key;
            if (TriggerBlocks.Contains(key)) continue;
            if (Is(key, "give_technology")) { Add(TechOf(e.Value), GrantKind.Gives, null); continue; }
            if (Is(key, "add_research_option")) { Add(TechOf(e.Value), GrantKind.ResearchOption, null); continue; }
            if (Is(key, "add_tech_progress")) { Add(TechOf(e.Value), GrantKind.Progress, ProgressOf(e.Value)); continue; }
            if (Is(key, "inline_script"))
            {
                if (depth < MaxDepth && InlineBody(e.Value) is { } body) Walk(body, conditions, via, depth + 1, calls, found);
                continue;
            }
            if (e.Value is PdxBlock b)
            {
                if (Is(key, "if")) Walk(b, With(conditions, describe(b.GetBlock("limit"))), via, depth, calls, found);
                else if (Is(key, "else_if")) Walk(b, With(conditions, Otherwise(describe(b.GetBlock("limit")))), via, depth, calls, found);
                else if (Is(key, "else")) Walk(b, With(conditions, "otherwise"), via, depth, calls, found);
                else if (Is(key, "random_list") || Is(key, "locked_random_list"))
                {
                    foreach (var choice in b.Entries)
                        if (choice.Value is PdxBlock cb) Walk(cb, With(conditions, "by chance"), via, depth, calls, found);
                }
                else if (Is(key, "switch") || Is(key, "inverted_switch"))
                {
                    var trigger = b.GetString("trigger") ?? "?";
                    var not = Is(key, "inverted_switch") ? "not " : "";
                    foreach (var c in b.Entries)
                        if (c.Value is PdxBlock cb && !Is(c.Key, "trigger"))
                            Walk(cb, With(conditions, Is(c.Key, "default") ? "otherwise" : $"{not}{trigger} = {c.Key}"), via, depth, calls, found);
                }
                else if (Is(key, "create_country"))
                {
                    // The effect block runs in the scope of the newly created country, not the player's.
                    var rest = new PdxBlock();
                    rest.Entries.AddRange(b.Entries.Where(c => !Is(c.Key, "effect")));
                    Walk(rest, conditions, via, depth, calls, found);
                }
                else if (library.Effects.TryGetValue(key, out var effect)) Call(key, effect, Parameters(b));
                else Walk(b, conditions, via, depth, calls, found);
            }
            else if (e.Value is string s && !s.Equals("no", StringComparison.OrdinalIgnoreCase) && library.Effects.TryGetValue(key, out var effect))
            {
                Call(key, effect, NoParameters);
            }
        }

        void Add(string? tech, GrantKind kind, double? progress)
        {
            if (string.IsNullOrWhiteSpace(tech)) return;
            found.Add(new FoundGrant(tech, kind, progress, conditions.Count == 0 ? null : string.Join("; ", conditions), via));
        }

        void Call(string name, PdxBlock effect, IReadOnlyDictionary<string, string> parameters)
        {
            if (depth >= MaxDepth || calls.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
            calls.Add(name);
            Walk(ScriptLibrary.Substitute(effect, parameters), conditions, via ?? name, depth + 1, calls, found);
            calls.RemoveAt(calls.Count - 1);
        }
    }

    // "always" (a limit that is always true or missing) adds nothing to the conditions.
    static List<string> With(List<string> conditions, string condition) =>
        condition == "always" ? conditions : [.. conditions, condition];

    // An else_if runs only when the previous if failed.
    static string Otherwise(string condition) => condition == "always" ? "otherwise" : "otherwise, " + condition;

    static bool Is(string key, string name) => key.Equals(name, StringComparison.OrdinalIgnoreCase);

    static string? TechOf(object value) => value is PdxBlock b ? b.GetString("tech") : (string)value;

    static double? ProgressOf(object value) =>
        value is PdxBlock b && double.TryParse(b.GetString("progress"), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null;

    static IReadOnlyDictionary<string, string> Parameters(PdxBlock block) =>
        block.Entries.Where(e => e.Value is string).GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (string)g.Last().Value, StringComparer.OrdinalIgnoreCase);

    PdxBlock? InlineBody(object value) => value switch
    {
        string path => library.Inline(path, NoParameters),
        PdxBlock b when b.GetString("script") is { } path => library.Inline(path,
            b.Entries.Where(e => e.Value is string && !Is(e.Key, "script")).GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (string)g.Last().Value, StringComparer.OrdinalIgnoreCase)),
        _ => null,
    };
}

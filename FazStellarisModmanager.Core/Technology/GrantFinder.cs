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

    readonly EffectWalker _walker = new(library, MaxDepth, walkCreateCountryEffect: false);

    public List<FoundGrant> Find(PdxBlock effects)
    {
        var found = new List<FoundGrant>();
        _walker.Walk(effects, (e, place) =>
        {
            var key = e.Key;
            if (Is(key, "give_technology")) Add(TechOf(e.Value), GrantKind.Gives, null);
            else if (Is(key, "add_research_option")) Add(TechOf(e.Value), GrantKind.ResearchOption, null);
            else if (Is(key, "add_tech_progress")) Add(TechOf(e.Value), GrantKind.Progress, ProgressOf(e.Value));
            else return false;
            return true;

            void Add(string? tech, GrantKind kind, double? progress)
            {
                if (string.IsNullOrWhiteSpace(tech)) return;
                var conditions = Conditions(place);
                found.Add(new FoundGrant(tech, kind, progress, conditions.Count == 0 ? null : string.Join("; ", conditions), place.Via));
            }
        });
        return found;
    }

    // Scope limits and random blocks are not shown for grants; "always" adds nothing.
    List<string> Conditions(EffectPlace place)
    {
        var conditions = new List<string>();
        foreach (var g in place.Guards)
        {
            var text = g.Kind switch
            {
                GuardKind.If => describe(g.Limit),
                GuardKind.ElseIf => Otherwise(describe(g.Limit)),
                GuardKind.Else => "otherwise",
                GuardKind.RandomList => "by chance",
                GuardKind.Switch => g.SwitchText,
                _ => null,
            };
            if (text is not null && text != "always") conditions.Add(text);
        }
        return conditions;
    }

    // An else_if runs only when the previous if failed.
    internal static string Otherwise(string condition) => condition == "always" ? "otherwise" : "otherwise, " + condition;

    static bool Is(string key, string name) => key.Equals(name, StringComparison.OrdinalIgnoreCase);

    static string? TechOf(object value) => value is PdxBlock b ? b.GetString("tech") : (string)value;

    static double? ProgressOf(object value) =>
        value is PdxBlock b && double.TryParse(b.GetString("progress"), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null;
}

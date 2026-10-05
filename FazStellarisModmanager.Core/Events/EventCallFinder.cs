using System.Globalization;
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Events;

/// <summary>
/// Finds event-firing effects (<c>country_event = { id = … days = … random = … scopes = { … } }</c>, any <c>*_event</c> or
/// <c>event</c> key, and the short <c>ship_event = id</c> form) in effect blocks, through scripted effects and inline scripts, with
/// their enclosing conditions, weighted picks and chances.
/// </summary>
internal sealed class EventCallFinder(ScriptLibrary library, Func<PdxBlock?, string> describe)
{
    /// <summary>How many scripted effects / inline scripts deep calls are followed.</summary>
    const int MaxDepth = 5;

    readonly EffectWalker _walker = new(library, MaxDepth, walkCreateCountryEffect: true);

    /// <summary>The calls in <paramref name="effects"/>; each is <paramref name="caller"/> with the call's own fields set.</summary>
    public List<EventCall> Find(PdxBlock effects, EventCall caller)
    {
        var calls = new List<EventCall>();
        _walker.Walk(effects, (e, place) =>
        {
            if (!EventScripts.IsEventKey(e.Key)) return false;
            var block = e.Value as PdxBlock;
            var id = block is not null ? block.GetString("id") : e.Value is string s && IsEventId(s) ? s : null;
            if (id is null) return false;
            calls.Add(Make(caller, e.Key, id, block, place));
            return true;
        });
        return calls;
    }

    /// <summary>"namespace.123".</summary>
    internal static bool IsEventId(string s)
    {
        var dot = s.LastIndexOf('.');
        return dot > 0 && dot < s.Length - 1 && s[(dot + 1)..].All(char.IsAsciiDigit) && !s[..dot].Any(c => char.IsWhiteSpace(c) || c is '$' or '@' or '=');
    }

    EventCall Make(EventCall caller, string effect, string id, PdxBlock? block, EffectPlace place)
    {
        var (min, max) = Days(block);
        var conditions = new List<CallCondition>();
        var all = new PdxBlock();
        WeightedPick? pick = null;
        double? chance = null;
        foreach (var g in place.Guards)
        {
            switch (g.Kind)
            {
                case GuardKind.If:
                case GuardKind.Limit:
                    if (describe(g.Limit) is var text && text != "always") Add(text, g);
                    break;
                case GuardKind.ElseIf:
                    Add(GrantFinder.Otherwise(describe(g.Limit)), g);
                    break;
                case GuardKind.Else:
                    Add("otherwise", g);
                    break;
                case GuardKind.Switch:
                    Add(g.SwitchText ?? "?", g);
                    break;
                case GuardKind.RandomList:
                    pick = Pick(g.RandomList!, g.Branch, g.Scopes);
                    var total = pick.Branches.All(b => b.Weight is not null) ? pick.Branches.Sum(b => b.Weight!.Value) : (double?)null;
                    conditions.Add(new CallCondition(total is { } t
                        ? $"by chance: weight {pick.Branch.WeightText} of {Format(t)}"
                        : $"by chance: weight {pick.Branch.WeightText}", null));
                    break;
                case GuardKind.Random:
                    if (Number(g.Chance) is { } c)
                    {
                        chance = (chance ?? 100) * c / 100;
                        conditions.Add(new CallCondition($"{Format(c)} % chance", null));
                    }
                    else conditions.Add(new CallCondition($"random chance ({g.Chance ?? "?"})", null));
                    break;
            }
        }

        return caller with
        {
            TargetId = id,
            Effect = effect,
            DaysMin = min,
            DaysMax = max,
            Scopes = block?.GetBlock("scopes") is { } scopes ? PdxScriptPrinter.Print(scopes) : null,
            Conditions = conditions,
            ConditionBlock = all.Entries.Count == 0 ? null : all,
            Weight = pick,
            Chance = chance,
            Via = place.Via,
        };

        void Add(string text, EffectGuard g)
        {
            var trigger = g.Trigger is { Entries.Count: > 0 } t ? Wrap(t, g.Scopes) : null;
            conditions.Add(new CallCondition(g.Scopes.Length > 0 ? $"{g.Scopes[^1]}: {text}" : text, trigger));
            if (trigger is not null) all.Entries.AddRange(trigger.Entries);
        }
    }

    /// <summary>A random_list as a weighted pick; modifier triggers are wrapped in the scopes around the list.</summary>
    WeightedPick Pick(PdxBlock list, int entryIndex, IReadOnlyList<string> scopes)
    {
        var branches = new List<WeightBranch>();
        var index = 0;
        for (var i = 0; i < list.Entries.Count; i++)
        {
            if (list.Entries[i].Value is not PdxBlock branch) continue;
            if (i == entryIndex) index = branches.Count;
            var modifiers = branch.Entries
                .Where(x => x.Key.Equals("modifier", StringComparison.OrdinalIgnoreCase) && x.Value is PdxBlock)
                .Select(x => Modifier((PdxBlock)x.Value, scopes))
                .ToList();
            branches.Add(new WeightBranch(list.Entries[i].Key, Number(list.Entries[i].Key), modifiers, null));
        }
        return new WeightedPick(branches, index);
    }

    WeightModifier Modifier(PdxBlock m, IReadOnlyList<string> scopes)
    {
        var trigger = new PdxBlock();
        trigger.Entries.AddRange(m.Entries.Where(x => !x.Key.Equals("factor", StringComparison.OrdinalIgnoreCase) && !x.Key.Equals("add", StringComparison.OrdinalIgnoreCase)));
        trigger.Items.AddRange(m.Items);
        var text = describe(trigger);
        return new WeightModifier(Number(m.GetString("factor")), Number(m.GetString("add")), Wrap(trigger, scopes),
            scopes.Count > 0 ? $"{scopes[^1]}: {text}" : text);
    }

    /// <summary><c>a = { b = { trigger } }</c> for scopes [a, b]; the trigger itself when there are none.</summary>
    static PdxBlock Wrap(PdxBlock trigger, IReadOnlyList<string> scopes)
    {
        var block = trigger;
        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            var outer = new PdxBlock();
            outer.Entries.Add(new PdxEntry(scopes[i], "=", block));
            block = outer;
        }
        return block;
    }

    /// <summary>(days, days + random); (0, 0) without days; nulls when a value is not a number.</summary>
    static (int? Min, int? Max) Days(PdxBlock? call)
    {
        var daysText = call?.GetString("days");
        var randomText = call?.GetString("random");
        int? days = daysText is null ? 0 : int.TryParse(daysText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : null;
        int? random = randomText is null ? 0 : int.TryParse(randomText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : null;
        return days is null ? (null, null) : (days, random is null ? null : days + random);
    }

    internal static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    internal static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

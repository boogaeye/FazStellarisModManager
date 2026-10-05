using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Events;

/// <summary>What fires an event: another event, an on_action (a root) or a common/ object such as a decision (a root).</summary>
public enum CallerKind { Event, OnAction, Object }

/// <summary>
/// Where the call is: an event's immediate, option or after block; an object's effect blocks; an on_action's
/// <c>events = { … }</c> or <c>random_events = { weight = id … }</c>.
/// </summary>
public enum CallPart { Immediate, Option, After, Effect, OnActionEvents, OnActionRandom }

/// <summary>How an uncalled-or-called event gets fired, for the "How it's triggered" panel.</summary>
public enum EventOrigin
{
    /// <summary>Something in the loaded files fires it (see <see cref="EventGraph.CallersOf"/>).</summary>
    Called,
    /// <summary>Nothing calls it and it is not is_triggered_only: the game polls it (pulse or mean time to happen).</summary>
    FiresOnItsOwn,
    /// <summary>Nothing calls it and it is is_triggered_only: fired by code or by a mod that isn't loaded.</summary>
    NoKnownCaller,
}

/// <summary>
/// One condition a call depends on: readable <see cref="Text"/> and, when it can be checked, the <see cref="Trigger"/> to give to the
/// ConditionEvaluator (already wrapped in the scope changes around it, e.g. <c>random_country = { … }</c>, which the evaluator
/// shows as undecided). Weighted-pick and random-chance conditions have no trigger.
/// </summary>
public sealed record CallCondition(string Text, PdxBlock? Trigger)
{
    public string? Script => Trigger is null ? null : PdxScriptPrinter.Print(Trigger);
}

/// <summary>
/// <c>modifier = { factor = X  triggers… }</c> (or <c>add = X</c>) of a weighted branch. An empty trigger always applies.
/// </summary>
public sealed record WeightModifier(double? Factor, double? Add, PdxBlock Trigger, string Text)
{
    public string Script => PdxScriptPrinter.Print(Trigger);
}

/// <summary>
/// One branch of a weighted pick: its base weight (null when it is not a number, e.g. a parameter), its modifiers, and for
/// on_action random_events the event it fires (null for "0", nothing).
/// </summary>
public sealed record WeightBranch(string WeightText, double? Weight, IReadOnlyList<WeightModifier> Modifiers, string? EventId);

/// <summary>A random_list (or on_action random_events) with every branch; <see cref="Index"/> is the branch the call is in.</summary>
public sealed record WeightedPick(IReadOnlyList<WeightBranch> Branches, int Index)
{
    public WeightBranch Branch => Branches[Index];

    /// <summary>The branch's chance from base weights only (0–1), or null when a weight is not a number.</summary>
    public double? BaseChance
    {
        get
        {
            if (Branches.Any(b => b.Weight is null)) return null;
            var total = Branches.Sum(b => b.Weight!.Value);
            return total <= 0 ? 0 : Branch.Weight!.Value / total;
        }
    }
}

/// <summary>A call that fires <see cref="TargetId"/>, with everything known about when and how.</summary>
public sealed record EventCall
{
    public required string TargetId { get; init; }
    /// <summary>The firing effect as written, e.g. country_event; or "on_action".</summary>
    public required string Effect { get; init; }

    public required CallerKind CallerKind { get; init; }
    /// <summary>Event id, on_action key or object id.</summary>
    public required string CallerId { get; init; }
    /// <summary>Event title (or id), on_action key, or the object's localised name.</summary>
    public required string CallerName { get; init; }
    /// <summary>"Event", "On action", or the object's kind (e.g. "Decisions", "Special projects").</summary>
    public required string CallerKindName { get; init; }
    /// <summary>For objects, the common/ folder (e.g. "decisions"); null otherwise.</summary>
    public string? CallerFolder { get; init; }

    public required CallPart Part { get; init; }
    public int? OptionIndex { get; init; }
    public string? OptionName { get; init; }
    /// <summary>For objects, the top-level block the call is in (e.g. on_success, effect, stage).</summary>
    public string? Block { get; init; }

    /// <summary>Delay in days: min = days, max = days + random. 0/0 when fired immediately; null when not a number.</summary>
    public int? DaysMin { get; init; }
    public int? DaysMax { get; init; }
    /// <summary>The <c>scopes = { … }</c> passed along, as script; null when none.</summary>
    public string? Scopes { get; init; }

    /// <summary>Enclosing if / else_if / else / limit / switch conditions, outermost first, plus weighted-pick and chance notes.</summary>
    public IReadOnlyList<CallCondition> Conditions { get; init; } = [];
    /// <summary>All checkable conditions as one trigger block (implicit AND), or null when there are none.</summary>
    public PdxBlock? ConditionBlock { get; init; }
    /// <summary>The innermost weighted pick the call is in (random_list branch, or on_action random_events).</summary>
    public WeightedPick? Weight { get; init; }
    /// <summary>Percent chance from enclosing <c>random = { chance = N }</c> blocks (multiplied), or null.</summary>
    public double? Chance { get; init; }
    /// <summary>The outermost scripted effect the call was found through, or null.</summary>
    public string? Via { get; init; }
    /// <summary>The caller's file.</summary>
    public required TechSourceRef Source { get; init; }

    /// <summary>Readable conditions joined with "; ", or null.</summary>
    public string? Condition => Conditions.Count == 0 ? null : string.Join("; ", Conditions.Select(c => c.Text));
    public string? ConditionScript => ConditionBlock is null ? null : PdxScriptPrinter.Print(ConditionBlock);
    /// <summary>On actions and objects are roots of the caller tree.</summary>
    public bool IsRoot => CallerKind != CallerKind.Event;

    /// <summary>"immediately", "after 90 days", "after 90–120 days" or "after a delay".</summary>
    public string Delay => (DaysMin, DaysMax) switch
    {
        (null, _) or (_, null) => "after a delay",
        (0, 0) => "immediately",
        var (min, max) when min == max => $"after {min} days",
        var (min, max) => $"after {min}–{max} days",
    };

    /// <summary>"option “Name”", "when the event fires", "after an option", "events", "random events" or the object's block.</summary>
    public string Where => Part switch
    {
        CallPart.Option => OptionName is null ? $"option {(OptionIndex ?? 0) + 1}" : $"option “{OptionName}”",
        CallPart.Immediate => "when the event fires",
        CallPart.After => "after an option",
        CallPart.OnActionEvents => "events",
        CallPart.OnActionRandom => "random events",
        _ => Block ?? "effect",
    };
}

/// <summary>An option of an event: its name, its trigger (exclusive_trigger or trigger) and allow, and the events it fires.</summary>
public sealed record EventOptionInfo(int Index, string Name, PdxBlock? Trigger, string? TriggerText, PdxBlock? Allow, string? AllowText,
    IReadOnlyList<EventCall> Calls)
{
    public string? TriggerScript => Trigger is null ? null : PdxScriptPrinter.Print(Trigger);
    public string? AllowScript => Allow is null ? null : PdxScriptPrinter.Print(Allow);
}

/// <summary>An event with its texts, conditions, options and the calls it makes (base = id inheritance applied).</summary>
public sealed record EventInfo
{
    public required string Id { get; init; }
    /// <summary>country_event, ship_event, …</summary>
    public required string Type { get; init; }
    /// <summary>Localised title, or null when the event has none.</summary>
    public string? Title { get; init; }
    public string? Description { get; init; }
    public bool DescriptionVaries { get; init; }
    /// <summary>Sprite name of the picture (for TechTreeService.EventPictureAsync), or null.</summary>
    public string? Picture { get; init; }
    public bool PictureVaries { get; init; }
    /// <summary>hide_window = yes.</summary>
    public bool Hidden { get; init; }
    /// <summary>is_triggered_only = yes.</summary>
    public bool TriggeredOnly { get; init; }
    /// <summary>Has a mean_time_to_happen block.</summary>
    public bool HasMtth { get; init; }
    public PdxBlock? Trigger { get; init; }
    public string? TriggerText { get; init; }
    public PdxBlock? MeanTimeToHappen { get; init; }
    public IReadOnlyList<EventOptionInfo> Options { get; init; } = [];
    public IReadOnlyList<EventCall> ImmediateCalls { get; init; } = [];
    public IReadOnlyList<EventCall> AfterCalls { get; init; } = [];
    /// <summary>The <c>base = id</c> it inherits from, or null.</summary>
    public string? BaseId { get; init; }
    public required TechSourceRef Source { get; init; }

    /// <summary>The id up to its last dot ("toxoids" for toxoids.7270).</summary>
    public string Namespace => EventGraph.NamespaceOf(Id);
    public string? TriggerScript => Trigger is null ? null : PdxScriptPrinter.Print(Trigger);
    public string? MtthScript => MeanTimeToHappen is null ? null : PdxScriptPrinter.Print(MeanTimeToHappen);

    /// <summary>The title, or "(no title)" plus the first option's text.</summary>
    public string ListTitle => Title ?? (Options.Count > 0 ? $"(no title) {Options[0].Name}" : "(no title)");

    /// <summary>Every call the event makes: immediate, then options in order, then after.</summary>
    public IEnumerable<EventCall> AllCalls => ImmediateCalls.Concat(Options.SelectMany(o => o.Calls)).Concat(AfterCalls);
}

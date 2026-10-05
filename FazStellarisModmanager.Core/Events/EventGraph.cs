using FazStellarisModmanager.Core.Conditions;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Events;

/// <summary>
/// Every event of a mod list and every known call between events, on_actions and common/ objects. Immutable; built by
/// <see cref="EventGraphScanner.Build"/>.
/// </summary>
public sealed class EventGraph
{
    readonly IReadOnlyDictionary<string, IReadOnlyList<EventCall>> _callers;

    public EventGraph(IReadOnlyDictionary<string, EventInfo> events, IReadOnlyList<EventCall> calls,
        IReadOnlyDictionary<string, PdxBlock> scriptedTriggers, IReadOnlyList<string> warnings)
    {
        Events = events;
        Calls = calls;
        Warnings = warnings;
        Conditions = new ConditionEvaluator(n => scriptedTriggers.TryGetValue(n, out var body) ? body : null);
        _callers = calls.GroupBy(c => c.TargetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EventCall>)g
                .OrderBy(c => c.CallerKind)
                .ThenBy(c => c.CallerId, IdOrder)
                .ToList(), StringComparer.OrdinalIgnoreCase);
        Namespaces = events.Values.Select(e => e.Namespace).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        SourceNames = events.Values.Select(e => e.Source.SourceName).Distinct().ToList();
    }

    public static EventGraph Empty { get; } = new(new Dictionary<string, EventInfo>(), [], new Dictionary<string, PdxBlock>(), []);

    /// <summary>Event id (case-insensitive) to event.</summary>
    public IReadOnlyDictionary<string, EventInfo> Events { get; }

    /// <summary>Every call found, including calls of events that are not defined in the loaded files.</summary>
    public IReadOnlyList<EventCall> Calls { get; }

    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Evaluates trigger blocks (event triggers, option triggers, call conditions, weight modifiers) with the mod list's scripted triggers.</summary>
    public ConditionEvaluator Conditions { get; }

    /// <summary>Event namespaces (the id up to its last dot), sorted.</summary>
    public IReadOnlyList<string> Namespaces { get; }

    /// <summary>Sources that define at least one event, in load order.</summary>
    public IReadOnlyList<string> SourceNames { get; }

    public EventInfo? Event(string id) => Events.TryGetValue(id, out var e) ? e : null;

    /// <summary>The direct callers of an event: events first, then on_actions, then objects, each by id.</summary>
    public IReadOnlyList<EventCall> CallersOf(string id) => _callers.TryGetValue(id, out var list) ? list : [];

    public EventOrigin Origin(string id) =>
        CallersOf(id).Count > 0 ? EventOrigin.Called
        : Event(id) is { TriggeredOnly: false } ? EventOrigin.FiresOnItsOwn
        : EventOrigin.NoKnownCaller;

    /// <summary>
    /// Events whose id, title or description contains <paramref name="text"/> (all when blank), optionally of one namespace and one
    /// source. Exact id first, then id prefix, title, id and description matches; each group in id order.
    /// </summary>
    public IEnumerable<EventInfo> Search(string? text, string? ns = null, string? source = null)
    {
        var s = text?.Trim() ?? "";
        return Events.Values
            .Where(e => (ns is null || e.Namespace.Equals(ns, StringComparison.OrdinalIgnoreCase))
                && (source is null || e.Source.SourceName.Equals(source, StringComparison.OrdinalIgnoreCase)))
            .Select(e => (Event: e, Rank: Rank(e, s)))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Event.Id, IdOrder)
            .Select(x => x.Event);
    }

    static int Rank(EventInfo e, string s)
    {
        if (s.Length == 0) return 0;
        if (e.Id.Equals(s, StringComparison.OrdinalIgnoreCase)) return 0;
        if (e.Id.StartsWith(s, StringComparison.OrdinalIgnoreCase)) return 1;
        if (e.Title?.Contains(s, StringComparison.OrdinalIgnoreCase) == true) return 2;
        if (e.Id.Contains(s, StringComparison.OrdinalIgnoreCase)) return 3;
        if (e.Description?.Contains(s, StringComparison.OrdinalIgnoreCase) == true) return 4;
        return -1;
    }

    public static string NamespaceOf(string id)
    {
        var dot = id.LastIndexOf('.');
        return dot > 0 ? id[..dot] : id;
    }

    /// <summary>Namespace (case-insensitive), then the number after the last dot numerically ("x.9" before "x.10").</summary>
    public static IComparer<string> IdOrder { get; } = Comparer<string>.Create((a, b) =>
    {
        var byNamespace = StringComparer.OrdinalIgnoreCase.Compare(NamespaceOf(a), NamespaceOf(b));
        if (byNamespace != 0) return byNamespace;
        var na = long.TryParse(a[(a.LastIndexOf('.') + 1)..], out var x) ? x : long.MaxValue;
        var nb = long.TryParse(b[(b.LastIndexOf('.') + 1)..], out var y) ? y : long.MaxValue;
        return na != nb ? na.CompareTo(nb) : StringComparer.OrdinalIgnoreCase.Compare(a, b);
    });
}

using System.Collections.Concurrent;
using System.Collections.Immutable;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>What encloses an effect: an if/else_if/else branch, a scope's limit, a switch case, a random_list branch or a random block.</summary>
internal enum GuardKind { If, ElseIf, Else, Limit, Switch, RandomList, Random }

/// <summary>
/// One enclosing condition of an effect. <see cref="Limit"/> is the block's own limit (if, else_if, limit). <see cref="Trigger"/> is
/// what must hold for the effect to run, in the scope of <see cref="Scopes"/> (null for random_list and random): an else_if adds
/// <c>NOR = { previous limits }</c>, an else is only that, a switch case is <c>trigger = case</c>.
/// </summary>
internal sealed record EffectGuard(GuardKind Kind, PdxBlock? Limit, PdxBlock? Trigger, string? SwitchText, ImmutableArray<string> Scopes,
    PdxBlock? RandomList = null, int Branch = -1, string? Chance = null);

/// <summary>Where the walker is: enclosing guards, the outermost scripted effect followed (null when none) and the scope changes.</summary>
internal sealed record EffectPlace(ImmutableArray<EffectGuard> Guards, string? Via, ImmutableArray<string> Scopes)
{
    public static EffectPlace Root { get; } = new([], null, []);
}

/// <summary>
/// Walks an effect block the way the game runs it, for finders that look for particular effects (tech grants, event calls). Follows
/// scripted effects (with parameters; cycle-safe) and inline scripts, records if/else_if/else, limits, switch cases, random_list
/// branches and random blocks as guards, and skips trigger blocks and tooltips (display only). The visitor sees every other entry
/// first and returns true when it handled it (the walker then does not go inside). With <paramref name="relevantEffects"/>, other
/// scripted effects are not followed (they cannot contain what the visitor looks for; see <see cref="ScriptFiles.Relevant"/>).
/// With <paramref name="expandOnce"/>, each scripted effect (with the same parameters) is followed only the first time it is met in
/// one walk: libraries that call effects from many branches (counters, arrays) otherwise multiply the work exponentially.
/// With <paramref name="cacheable"/> (true exactly for the entries the visitor handles), what a scripted effect contains is found
/// once per walker (for each set of parameters, with up to maxDepth - 1 further levels) and reused wherever it is called.
/// With <paramref name="budget"/>, a walk stops following scripted effects and inline scripts once it has looked at that many
/// entries (some mods generate effect libraries whose expansion would take seconds per event); <see cref="Walk"/> then returns false.
/// </summary>
internal sealed class EffectWalker(ScriptLibrary library, int maxDepth, bool walkCreateCountryEffect, IReadOnlySet<string>? relevantEffects = null,
    bool expandOnce = false, Func<PdxEntry, bool>? cacheable = null, int budget = int.MaxValue)
{
    // The scripted effects being followed (for cycles), with expandOnce those already followed, and the work done so far.
    sealed class WalkState(HashSet<string>? expanded, bool useCache)
    {
        public List<string> Stack { get; } = [];
        public HashSet<string>? Expanded { get; } = expanded;
        public bool UseCache { get; } = useCache;
        public long Visited { get; set; }
        public bool Truncated { get; set; }
    }

    sealed record Expansion(List<(PdxEntry Entry, EffectPlace Place)> Found, bool Truncated, long Visited);

    // Scripted effect + parameters to the handled entries inside it, placed relative to the call.
    readonly ConcurrentDictionary<string, Expansion> _expansions = new(StringComparer.OrdinalIgnoreCase);

    public delegate bool Visitor(PdxEntry entry, EffectPlace place);

    static readonly HashSet<string> TriggerBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "limit", "trigger", "exclusive_trigger", "allow", "potential", "ai_chance", "weight", "modifier", "tooltip",
    };

    // Blocks that run their contents in the same scope.
    static readonly HashSet<string> SameScope = new(StringComparer.OrdinalIgnoreCase)
    {
        "hidden_effect", "while", "custom_tooltip", "random",
    };

    static readonly IReadOnlyDictionary<string, string> NoParameters = new Dictionary<string, string>();

    // Parsed inline scripts by path and parameters, for this walker's lifetime (the blocks are only read).
    readonly ConcurrentDictionary<string, PdxBlock?> _inline = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Blocks that hold triggers (or display-only tooltips), never effects that run.</summary>
    public static bool IsTriggerBlock(string key) => TriggerBlocks.Contains(key);

    /// <summary>Walks the effects; false when the budget ran out and some scripted effects or inline scripts were not followed.</summary>
    public bool Walk(PdxBlock effects, Visitor visit)
    {
        var state = NewState(cacheable is not null);
        Walk(effects, EffectPlace.Root, 0, state, visit);
        return !state.Truncated;
    }

    // Whether the walk may still follow scripted effects and inline scripts.
    bool MayFollow(WalkState state)
    {
        if (state.Visited <= budget) return true;
        state.Truncated = true;
        return false;
    }

    WalkState NewState(bool useCache) => new(expandOnce ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null, useCache);

    void Walk(PdxBlock block, EffectPlace place, int depth, WalkState calls, Visitor visit)
    {
        // The limits of the if / else_if chain so far, for the conditions of later else_if and else branches.
        var chain = new List<PdxBlock>();
        calls.Visited += block.Entries.Count;
        foreach (var e in block.Entries)
        {
            var key = e.Key;
            var isIf = Is(key, "if");
            var isElse = Is(key, "else_if") || Is(key, "else");
            if (!isIf && !isElse) chain.Clear();
            if (TriggerBlocks.Contains(key) || visit(e, place)) continue;
            if (Is(key, "inline_script"))
            {
                if (depth < maxDepth && MayFollow(calls) && InlineBody(e.Value) is { } body) Walk(body, place, depth + 1, calls, visit);
                continue;
            }
            if (e.Value is PdxBlock b)
            {
                if (isIf)
                {
                    chain.Clear();
                    var limit = b.GetBlock("limit");
                    if (limit is not null) chain.Add(limit);
                    Walk(b, With(place, new EffectGuard(GuardKind.If, limit, limit, null, place.Scopes)), depth, calls, visit);
                }
                else if (Is(key, "else_if"))
                {
                    var limit = b.GetBlock("limit");
                    var trigger = Previous(chain);
                    if (limit is not null) trigger.Entries.AddRange(limit.Entries);
                    if (limit is not null) chain.Add(limit);
                    Walk(b, With(place, new EffectGuard(GuardKind.ElseIf, limit, trigger, null, place.Scopes)), depth, calls, visit);
                }
                else if (Is(key, "else"))
                {
                    Walk(b, With(place, new EffectGuard(GuardKind.Else, null, Previous(chain), null, place.Scopes)), depth, calls, visit);
                    chain.Clear();
                }
                else if (Is(key, "random_list") || Is(key, "locked_random_list"))
                {
                    for (var i = 0; i < b.Entries.Count; i++)
                        if (b.Entries[i].Value is PdxBlock cb)
                            Walk(cb, With(place, new EffectGuard(GuardKind.RandomList, null, null, null, place.Scopes, b, i)), depth, calls, visit);
                }
                else if (Is(key, "switch") || Is(key, "inverted_switch"))
                    Switch(key, b, place, depth, calls, visit);
                else if (Is(key, "create_country"))
                {
                    // The effect block runs in the scope of the newly created country, not the caller's.
                    var rest = new PdxBlock();
                    rest.Entries.AddRange(b.Entries.Where(c => !Is(c.Key, "effect")));
                    Walk(rest, place, depth, calls, visit);
                    if (walkCreateCountryEffect)
                        foreach (var effect in b.Entries.Where(c => Is(c.Key, "effect") && c.Value is PdxBlock))
                            Walk((PdxBlock)effect.Value, place with { Scopes = place.Scopes.Add(key) }, depth, calls, visit);
                }
                else if (library.Effects.TryGetValue(key, out var scripted)) Call(key, scripted, Parameters(b));
                else
                {
                    var inner = SameScope.Contains(key) ? place : place with { Scopes = place.Scopes.Add(key) };
                    if (Is(key, "random")) inner = With(inner, new EffectGuard(GuardKind.Random, null, null, null, inner.Scopes, Chance: b.GetString("chance")));
                    if (b.GetBlock("limit") is { } limit) inner = With(inner, new EffectGuard(GuardKind.Limit, limit, limit, null, inner.Scopes));
                    Walk(b, inner, depth, calls, visit);
                }
            }
            else if (e.Value is string s && !s.Equals("no", StringComparison.OrdinalIgnoreCase) && library.Effects.TryGetValue(key, out var scripted))
                Call(key, scripted, NoParameters);
        }

        void Call(string name, PdxBlock effect, IReadOnlyDictionary<string, string> parameters)
        {
            if (depth >= maxDepth || calls.Stack.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
            if (relevantEffects is not null && !relevantEffects.Contains(name)) return;
            if (!MayFollow(calls)) return;
            var key = name + string.Concat(parameters.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => $"|{p.Key}={p.Value}"));
            if (calls.Expanded is { } expanded && !expanded.Add(key)) return;
            if (calls.UseCache)
            {
                // Finding what the effect contains counts towards this walk's budget; reusing it is cheap.
                var computed = false;
                var expansion = _expansions.GetOrAdd(key, _ =>
                {
                    computed = true;
                    return Expand(name, Body());
                });
                if (computed) calls.Visited += expansion.Visited;
                if (expansion.Truncated) calls.Truncated = true;
                foreach (var (entry, relative) in expansion.Found)
                    visit(entry, Reroot(place, relative, name));
                return;
            }
            calls.Stack.Add(name);
            Walk(Body(), place with { Via = place.Via ?? name }, depth + 1, calls, visit);
            calls.Stack.RemoveAt(calls.Stack.Count - 1);

            PdxBlock Body() => library.HasParameters(name) ? ScriptLibrary.Substitute(effect, parameters) : effect;
        }
    }

    // The handled entries of a scripted effect's body, walked without the cache (so a cycle cannot recurse into it).
    Expansion Expand(string name, PdxBlock body)
    {
        var found = new List<(PdxEntry Entry, EffectPlace Place)>();
        var state = NewState(useCache: false);
        state.Stack.Add(name);
        Walk(body, EffectPlace.Root, 1, state, (e, p) =>
        {
            if (!cacheable!(e)) return false;
            found.Add((e, p));
            return true;
        });
        return new Expansion(found, state.Truncated, state.Visited);
    }

    // A place found inside a scripted effect, moved under the place of the call: outer guards first, scopes prefixed.
    static EffectPlace Reroot(EffectPlace outer, EffectPlace inner, string name)
    {
        var guards = outer.Scopes.IsEmpty ? inner.Guards : inner.Guards.Select(g => g with { Scopes = outer.Scopes.AddRange(g.Scopes) }).ToImmutableArray();
        return new EffectPlace(outer.Guards.AddRange(guards), outer.Via ?? name, outer.Scopes.AddRange(inner.Scopes));
    }

    void Switch(string key, PdxBlock b, EffectPlace place, int depth, WalkState calls, Visitor visit)
    {
        var trigger = b.GetString("trigger") ?? "?";
        var inverted = Is(key, "inverted_switch");
        var not = inverted ? "not " : "";
        var cases = b.Entries.Where(c => c.Value is PdxBlock && !Is(c.Key, "trigger") && !Is(c.Key, "default")).Select(c => c.Key).ToList();
        foreach (var c in b.Entries)
        {
            if (c.Value is not PdxBlock cb || Is(c.Key, "trigger")) continue;
            var isDefault = Is(c.Key, "default");
            var condition = new PdxBlock();
            if (isDefault)
            {
                // Otherwise: no case matched (for an inverted switch: every case matched).
                var others = new PdxBlock();
                others.Entries.AddRange(cases.Select(x => new PdxEntry(trigger, "=", x)));
                condition.Entries.Add(new PdxEntry(inverted ? "AND" : "NOR", "=", others));
            }
            else if (inverted) condition.Entries.Add(new PdxEntry("NOT", "=", Single(trigger, c.Key)));
            else condition.Entries.Add(new PdxEntry(trigger, "=", c.Key));
            var text = isDefault ? "otherwise" : $"{not}{trigger} = {c.Key}";
            Walk(cb, With(place, new EffectGuard(GuardKind.Switch, null, condition, text, place.Scopes)), depth, calls, visit);
        }
    }

    static PdxBlock Single(string key, string value)
    {
        var block = new PdxBlock();
        block.Entries.Add(new PdxEntry(key, "=", value));
        return block;
    }

    // NOR = { AND = { limit1 } AND = { limit2 } … }: none of the earlier branches ran.
    static PdxBlock Previous(List<PdxBlock> chain)
    {
        var block = new PdxBlock();
        if (chain.Count == 0) return block;
        var any = new PdxBlock();
        any.Entries.AddRange(chain.Select(l => new PdxEntry("AND", "=", l)));
        block.Entries.Add(new PdxEntry("NOR", "=", any));
        return block;
    }

    static EffectPlace With(EffectPlace place, EffectGuard guard) => place with { Guards = place.Guards.Add(guard) };

    static bool Is(string key, string name) => key.Equals(name, StringComparison.OrdinalIgnoreCase);

    static IReadOnlyDictionary<string, string> Parameters(PdxBlock block) =>
        block.Entries.Where(e => e.Value is string).GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (string)g.Last().Value, StringComparer.OrdinalIgnoreCase);

    PdxBlock? InlineBody(object value) => value switch
    {
        string path => Inline(path, NoParameters),
        PdxBlock b when b.GetString("script") is { } path => Inline(path, Parameters(WithoutScript(b))),
        _ => null,
    };

    PdxBlock? Inline(string path, IReadOnlyDictionary<string, string> parameters) =>
        _inline.GetOrAdd(path + string.Concat(parameters.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => $"|{p.Key}={p.Value}")),
            _ => library.Inline(path, parameters));

    static PdxBlock WithoutScript(PdxBlock b)
    {
        var copy = new PdxBlock();
        copy.Entries.AddRange(b.Entries.Where(e => !Is(e.Key, "script")));
        return copy;
    }
}

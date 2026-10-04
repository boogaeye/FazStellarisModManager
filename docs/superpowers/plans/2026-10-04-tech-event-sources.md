# Tech Event Sources Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show for each technology every event and game object that grants it, researches it partly, or offers it as a research option. Events open in an in-game-style event browser that marks the granting option(s).

**Architecture:** Core gains:
- `ScriptLibrary`: scripted effects and inline scripts, with parameter substitution.
- `GrantFinder`: walks effect blocks.
- `GrantScanner`: events (first definition wins) and common/ objects, producing a `GrantIndex`.
- `TechGrants.cs`: records plus `GrantText`.

`TechDatabase` exposes `GrantSources(tech)` and `Event(id)`. `IconCache` gets a max-size parameter so event pictures load at 450 px through `TechTreeService.EventPictureAsync`. The app adds an "Obtained from" sidebar section and an `EventBrowser` modal.

**Tech Stack:** .NET 10, C#, WPF + BlazorWebView, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-04-tech-event-sources-design.md`

**Conventions for every task:**
- **Build and test** with `-c Release --artifacts-path <scratch>/art`. A running copy of the app may lock the normal bin folders. Never kill FazStellarisModmanager.exe.
- **Backslash escapes:** never write `\n`, `\t` or `\u` inside C# string literals; the tools mangle them. Use `(char)10` or raw string literals (`"""…"""`).
- **Razor:** a bare `<text>` element with attributes inside an `@if`/`@foreach` code block fails with RZ1023. Wrap SVG text in `<g>`. A plain `<text>@x</text>` without attributes is fine.
- **Branch:** `feature/tech-event-sources`. Before each commit, check `git branch --show-current`; if the branch changed, stop and report.
- **Commits:** end every commit message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

## File structure

| File | Responsibility |
|---|---|
| Create `FazStellarisModmanager.Core/Technology/TechGrants.cs` | `GrantKind`, `EventPart`, `TechGrant`, `GrantSource`, `EventOption`, `GameEvent`, `GrantText` |
| Create `FazStellarisModmanager.Core/Technology/ScriptLibrary.cs` | scripted effects + inline scripts, `$PARAM$` substitution |
| Create `FazStellarisModmanager.Core/Technology/GrantFinder.cs` | `FoundGrant`, walks effect blocks |
| Create `FazStellarisModmanager.Core/Technology/GrantScanner.cs` | `GrantIndex`, event scan, object scan, pre-filter |
| Modify `FazStellarisModmanager.Core/Technology/TechDatabase.cs` | build the index; `GrantSources`, `Event` |
| Modify `FazStellarisModmanager.Core/Technology/IconCache.cs` | `maxSize` parameter |
| Modify `FazStellarisModmanager.Core/Technology/TechTreeService.cs` | `EventPictureAsync` |
| Create `FazStellarisModmanager/Components/EventBrowser.razor` | modal browser with the in-game-style window |
| Modify `FazStellarisModmanager/Components/TechSidebar.razor` | "Obtained from" section |
| Modify `FazStellarisModmanager/wwwroot/css/site.css` | styles |
| Tests: `GrantFinderTests.cs`, `GrantTextTests.cs`, `GrantScannerTests.cs`, additions to `IconCacheTests.cs` and `TechDatabaseTests.cs` | |

---

### Task 1: Records, `GrantText`, `ScriptLibrary`, `GrantFinder`

**Files:**
- Create: `FazStellarisModmanager.Core/Technology/TechGrants.cs`, `ScriptLibrary.cs`, `GrantFinder.cs`
- Test: `FazStellarisModmanager.Tests/GrantFinderTests.cs`, `FazStellarisModmanager.Tests/GrantTextTests.cs`

- [ ] **Step 1: Write the failing tests**

`GrantFinderTests.cs`:

```csharp
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Tests;

public class GrantFinderTests
{
    static GrantFinder Finder(ScriptLibrary? library = null) =>
        new(library ?? new ScriptLibrary(), b => TriggerSummary.Describe(b, _ => null));

    static PdxBlock P(string text) => ParadoxScriptParser.Parse(text);

    [Fact]
    public void Finds_the_three_effects_in_their_forms()
    {
        var found = Finder().Find(P("""
            give_technology = { tech = tech_a message = no }
            add_research_option = tech_b
            add_research_option = { tech = tech_c }
            add_tech_progress = { tech = tech_d progress = 0.25 }
            add_tech_progress = { tech = tech_e progress = @var }
            """));

        Assert.Equal(
            [("tech_a", GrantKind.Gives, (double?)null), ("tech_b", GrantKind.ResearchOption, null), ("tech_c", GrantKind.ResearchOption, null),
             ("tech_d", GrantKind.Progress, 0.25), ("tech_e", GrantKind.Progress, null)],
            found.Select(f => (f.Tech, f.Kind, f.Progress)));
        Assert.All(found, f => Assert.Equal((null, null), (f.Condition, f.Via)));
    }

    [Fact]
    public void Conditions_come_from_if_else_and_random_lists_and_trigger_blocks_are_skipped()
    {
        var found = Finder().Find(P("""
            owner = {
                if = { limit = { has_civic = civic_x } give_technology = { tech = tech_a } }
                else_if = { limit = { is_gestalt = yes } add_research_option = tech_b }
                else = {
                    random_list = {
                        50 = { add_tech_progress = { tech = tech_c progress = 0.5 } modifier = { factor = 2 give_technology = { tech = tech_never } } }
                    }
                }
                IF = { limit = { always = yes } add_research_option = tech_d }
            }
            limit = { give_technology = { tech = tech_in_trigger } }
            ai_chance = { factor = 1 }
            """));

        Assert.Equal(
            [("tech_a", "Civic: civic_x"), ("tech_b", "Gestalt"), ("tech_c", "otherwise; by chance"), ("tech_d", (string?)null)],
            found.Select(f => (f.Tech, f.Condition)));
    }

    [Fact]
    public void Scripted_effects_are_followed_with_parameters_and_cycles_stop()
    {
        var library = new ScriptLibrary();
        library.AddEffects("""
            grant_it = { give_technology = { tech = $TECH$ } }
            reward = { if = { limit = { always = yes } grant_it = { TECH = tech_a } } }
            default_tech = { add_research_option = $TECH|tech_d$ }
            loop_a = { loop_b = yes }
            loop_b = { loop_a = yes give_technology = { tech = tech_loop } }
            off = { give_technology = { tech = tech_off } }
            """);

        var found = Finder(library).Find(P("reward = yes default_tech = yes loop_a = yes off = no"));

        Assert.Equal(
            [("tech_a", GrantKind.Gives, "reward"), ("tech_d", GrantKind.ResearchOption, "default_tech"), ("tech_loop", GrantKind.Gives, "loop_a")],
            found.Select(f => (f.Tech, f.Kind, f.Via)));
    }

    [Fact]
    public void Inline_scripts_are_expanded_with_parameters()
    {
        var library = new ScriptLibrary();
        library.AddInlineScript("events/grant", "give_technology = { tech = $TECH$ }");

        var found = Finder(library).Find(P("""
            inline_script = { script = events/grant TECH = tech_a }
            inline_script = "events/grant"
            inline_script = missing/path
            """));

        Assert.Equal(["tech_a", "$TECH$"], found.Select(f => f.Tech));
        Assert.All(found, f => Assert.Null(f.Via));
    }

    [Fact]
    public void Substitution_uses_values_then_defaults_and_keeps_unknown_parameters()
    {
        var parameters = new Dictionary<string, string> { ["X"] = "1" };

        Assert.Equal("a = 1 b = def c = $Z$", ScriptLibrary.Substitute("a = $X$ b = $Y|def$ c = $Z$", parameters));
        var block = ScriptLibrary.Substitute(P("key_$X$ = { v = $X$ }"), parameters);
        Assert.Equal("1", block.GetBlock("key_1")!.GetString("v"));
    }
}
```

`GrantTextTests.cs`:

```csharp
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Tests;

public class GrantTextTests
{
    static TechGrant G(GrantKind kind, double? progress = null, EventPart part = EventPart.Option, int? option = 0, string? condition = null, string? via = null) =>
        new(kind, progress, part, option, condition, via);

    static readonly TechSourceRef Src = new("Base game", true, "events/x.txt");

    static GrantSource Event(params TechGrant[] grants) => new("Event", GrantSource.EventsFolder, "x.1", "X", Src, grants);

    [Fact]
    public void Badges_summarise_the_grants()
    {
        Assert.Equal(("Gives", "give"), (GrantText.Badge([G(GrantKind.Gives), G(GrantKind.Progress, 0.5)]), GrantText.BadgeClass([G(GrantKind.Gives)])));
        Assert.Equal(("+25 %", "prog"), (GrantText.Badge([G(GrantKind.Progress, 0.25)]), GrantText.BadgeClass([G(GrantKind.Progress, 0.25)])));
        Assert.Equal("+12.5 %", GrantText.Badge([G(GrantKind.Progress, 0.125)]));
        Assert.Equal(("Option +20 %", "mix"),
            (GrantText.Badge([G(GrantKind.ResearchOption), G(GrantKind.Progress, 0.2)]), GrantText.BadgeClass([G(GrantKind.ResearchOption), G(GrantKind.Progress, 0.2)])));
        Assert.Equal(("Research option", "opt"), (GrantText.Badge([G(GrantKind.ResearchOption)]), GrantText.BadgeClass([G(GrantKind.ResearchOption)])));
        Assert.Equal("+?", GrantText.Badge([G(GrantKind.Progress)]));
    }

    [Fact]
    public void Where_names_the_option_or_the_part()
    {
        var ev = new GameEvent("x.1", "country_event", "X", null, false, null, false, false,
            [new EventOption("Accept", null), new EventOption("Refuse", null)], Src);

        Assert.Equal("option “Refuse”", GrantText.Where(Event(G(GrantKind.Gives, option: 1)), ev));
        Assert.Equal("option 2", GrantText.Where(Event(G(GrantKind.Gives, option: 1)), null));
        Assert.Equal("when the event fires", GrantText.Where(Event(G(GrantKind.Gives, part: EventPart.Immediate, option: null)), ev));
        Assert.Equal("after any option", GrantText.Where(Event(G(GrantKind.Gives, part: EventPart.After, option: null)), ev));
        Assert.Equal("option “Accept”", GrantText.Where(Event(G(GrantKind.Gives), G(GrantKind.Progress, 0.1)), ev));
        Assert.Equal("2 places", GrantText.Where(Event(G(GrantKind.Gives), G(GrantKind.Gives, part: EventPart.After, option: null)), ev));
        Assert.Equal("", GrantText.Where(new GrantSource("Traditions", "traditions", "tr_x", "X", Src, [G(GrantKind.Gives)]), null));
    }

    [Fact]
    public void Effect_lines_include_how_and_when()
    {
        Assert.Equal("Gives Psionic Theory (via small_artifact_reward) — when: Civic: X",
            GrantText.Effect(G(GrantKind.Gives, condition: "Civic: X", via: "small_artifact_reward"), "Psionic Theory"));
        Assert.Equal("+25 % Psionic Theory", GrantText.Effect(G(GrantKind.Progress, 0.25), "Psionic Theory"));
        Assert.Equal("Research option: Psionic Theory", GrantText.Effect(G(GrantKind.ResearchOption), "Psionic Theory"));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art> --filter "GrantFinderTests|GrantTextTests"`
Expected: build errors, because `GrantFinder`, `ScriptLibrary`, `GrantText` and the other types don't exist yet.

- [ ] **Step 3: Implement `TechGrants.cs`**

```csharp
using System.Globalization;

namespace FazStellarisModmanager.Core.Technology;

public enum GrantKind { Gives, Progress, ResearchOption }

/// <summary>Where in an event a grant runs. Grants of common/ objects (traditions, perks, …) use Immediate.</summary>
public enum EventPart { Immediate, Option, After }

/// <summary>One tech-granting effect. Progress is the add_tech_progress fraction (0.25 = 25 %), null when not a number or not progress.</summary>
public sealed record TechGrant(GrantKind Kind, double? Progress, EventPart Part, int? OptionIndex, string? Condition, string? Via);

/// <summary>Something that grants a tech: an event (KindFolder "events") or a common/ object.</summary>
public sealed record GrantSource(string Kind, string KindFolder, string Id, string Name, TechSourceRef Source, IReadOnlyList<TechGrant> Grants)
{
    public const string EventsFolder = "events";
    public bool IsEvent => KindFolder == EventsFolder;
}

public sealed record EventOption(string Name, string? Condition);

/// <summary>An event that grants a tech, as shown in the event window. Varies = the game picks the text or picture by conditions; the first is kept.</summary>
public sealed record GameEvent(string Id, string Type, string Title, string? Description, bool DescriptionVaries,
    string? Picture, bool PictureVaries, bool Hidden, IReadOnlyList<EventOption> Options, TechSourceRef Source);

/// <summary>Short texts for grants, shared by the sidebar and the event window.</summary>
public static class GrantText
{
    public static string Percent(double? progress) =>
        progress is { } p ? "+" + Math.Round(p * 100, 1).ToString("0.#", CultureInfo.InvariantCulture) + " %" : "+?";

    /// <summary>"Gives" wins; otherwise progress and/or research option, e.g. "Option +25 %".</summary>
    public static string Badge(IReadOnlyList<TechGrant> grants)
    {
        if (grants.Any(g => g.Kind == GrantKind.Gives)) return "Gives";
        var progress = grants.FirstOrDefault(g => g.Kind == GrantKind.Progress);
        var option = grants.Any(g => g.Kind == GrantKind.ResearchOption);
        if (progress is not null) return option ? "Option " + Percent(progress.Progress) : Percent(progress.Progress);
        return "Research option";
    }

    /// <summary>give, prog, mix (option + progress) or opt; matches the badge colours in site.css.</summary>
    public static string BadgeClass(IReadOnlyList<TechGrant> grants)
    {
        if (grants.Any(g => g.Kind == GrantKind.Gives)) return "give";
        var progress = grants.Any(g => g.Kind == GrantKind.Progress);
        var option = grants.Any(g => g.Kind == GrantKind.ResearchOption);
        return progress ? (option ? "mix" : "prog") : "opt";
    }

    /// <summary>Where in an event the grants are: one place in words, or "N places". Empty for non-events.</summary>
    public static string Where(GrantSource source, GameEvent? ev)
    {
        if (!source.IsEvent) return "";
        var places = source.Grants.Select(g => (g.Part, g.OptionIndex)).Distinct().ToList();
        if (places.Count != 1) return $"{places.Count} places";
        var (part, index) = places[0];
        return part switch
        {
            EventPart.Immediate => "when the event fires",
            EventPart.After => "after any option",
            _ => index is { } i && ev is not null && i < ev.Options.Count ? $"option “{ev.Options[i].Name}”" : $"option {(index ?? 0) + 1}",
        };
    }

    /// <summary>"Gives Psionic Theory", "+25 % Psionic Theory" or "Research option: Psionic Theory", plus " (via effect)" and " — when: condition".</summary>
    public static string Effect(TechGrant grant, string techName)
    {
        var text = grant.Kind switch
        {
            GrantKind.Gives => "Gives " + techName,
            GrantKind.Progress => Percent(grant.Progress) + " " + techName,
            _ => "Research option: " + techName,
        };
        if (grant.Via is not null) text += $" (via {grant.Via})";
        if (grant.Condition is not null) text += " — when: " + grant.Condition;
        return text;
    }
}
```

- [ ] **Step 4: Implement `ScriptLibrary.cs`**

```csharp
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>
/// Scripted effects (common/scripted_effects, last definition per name wins) and inline scripts (common/inline_scripts,
/// raw text by path without .txt), with the game's $PARAM$ / $PARAM|default$ substitution.
/// </summary>
public sealed class ScriptLibrary
{
    const string InlineFolder = "common/inline_scripts";

    static readonly Regex Param = new(@"\$([A-Za-z0-9_]+)(?:\|([^$]*))?\$", RegexOptions.Compiled);

    readonly Dictionary<string, PdxBlock> _effects = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _inline = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, PdxBlock> Effects => _effects;

    public static ScriptLibrary Load(IReadOnlyList<ContentSource> sources, ICollection<string> warnings, CancellationToken ct = default)
    {
        var library = new ScriptLibrary();
        foreach (var (name, body) in CommonDefinitions.Load(sources, "common/scripted_effects", warnings, ct)) library._effects[name] = body;

        var files = new Dictionary<string, (ContentSource Source, string Rel)>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            foreach (var rel in source.Files(InlineFolder, ".txt"))
                files[rel] = (source, rel);
        foreach (var (source, rel) in files.Values)
        {
            ct.ThrowIfCancellationRequested();
            try { library.AddInlineScript(rel[(InlineFolder.Length + 1)..^4], source.ReadText(rel)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
            {
                warnings.Add($"{source.Name}: {rel}: {ex.Message}");
            }
        }
        return library;
    }

    /// <summary>Adds (or replaces) the scripted effects defined in <paramref name="text"/>.</summary>
    public void AddEffects(string text)
    {
        foreach (var e in ParadoxScriptParser.Parse(text).Entries)
            if (e.Value is PdxBlock b) _effects[e.Key] = b;
    }

    /// <summary>Adds an inline script; <paramref name="path"/> is relative to common/inline_scripts, without .txt.</summary>
    public void AddInlineScript(string path, string text) => _inline[Normalize(path)] = text;

    /// <summary>The inline script with parameters substituted, parsed; null when there is no such script.</summary>
    public PdxBlock? Inline(string path, IReadOnlyDictionary<string, string> parameters) =>
        _inline.TryGetValue(Normalize(path), out var text) ? ParadoxScriptParser.Parse(Substitute(text, parameters)) : null;

    static string Normalize(string path) => path.Trim().Trim('"').Replace((char)92, '/');

    public static string Substitute(string text, IReadOnlyDictionary<string, string> parameters) =>
        !text.Contains('$') ? text
        : Param.Replace(text, m => parameters.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Groups[2].Success ? m.Groups[2].Value : m.Value);

    /// <summary>A copy of <paramref name="block"/> with parameters substituted in keys, values and items.</summary>
    public static PdxBlock Substitute(PdxBlock block, IReadOnlyDictionary<string, string> parameters)
    {
        var copy = new PdxBlock();
        foreach (var e in block.Entries)
            copy.Entries.Add(new PdxEntry(Substitute(e.Key, parameters), e.Op,
                e.Value is PdxBlock b ? Substitute(b, parameters) : Substitute((string)e.Value, parameters)));
        foreach (var item in block.Items)
            copy.Items.Add(item is PdxBlock b ? Substitute(b, parameters) : Substitute((string)item, parameters));
        return copy;
    }
}
```

- [ ] **Step 5: Implement `GrantFinder.cs`**

```csharp
using System.Globalization;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>A tech grant found in an effect block, before it is placed in an event part.</summary>
public sealed record FoundGrant(string Tech, GrantKind Kind, double? Progress, string? Condition, string? Via);

/// <summary>
/// Finds give_technology / add_tech_progress / add_research_option in effect blocks. Follows scripted effects (Via = the
/// outermost effect called) and inline scripts, words if/else_if limits with <paramref name="describe"/>, and skips trigger blocks.
/// </summary>
public sealed class GrantFinder(ScriptLibrary library, Func<PdxBlock?, string> describe)
{
    const int MaxDepth = 6;

    public static readonly string[] EffectNames = ["give_technology", "add_tech_progress", "add_research_option"];

    static readonly HashSet<string> TriggerBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "limit", "trigger", "exclusive_trigger", "allow", "potential", "ai_chance", "weight", "modifier",
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
                if (Is(key, "if") || Is(key, "else_if")) Walk(b, With(conditions, describe(b.GetBlock("limit"))), via, depth, calls, found);
                else if (Is(key, "else")) Walk(b, With(conditions, "otherwise"), via, depth, calls, found);
                else if (Is(key, "random_list"))
                {
                    foreach (var choice in b.Entries)
                        if (choice.Value is PdxBlock cb) Walk(cb, With(conditions, "by chance"), via, depth, calls, found);
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
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art> --filter "GrantFinderTests|GrantTextTests"`
Expected: all pass. Then run the whole suite; everything should still pass.

- [ ] **Step 7: Commit**

```bash
git add FazStellarisModmanager.Core/Technology/TechGrants.cs FazStellarisModmanager.Core/Technology/ScriptLibrary.cs FazStellarisModmanager.Core/Technology/GrantFinder.cs FazStellarisModmanager.Tests/GrantFinderTests.cs FazStellarisModmanager.Tests/GrantTextTests.cs
git commit -m "Tech grants: find give_technology / tech progress / research options through scripted effects and inline scripts"
```

---

### Task 2: `GrantScanner` (events and common/ objects)

**Files:**
- Create: `FazStellarisModmanager.Core/Technology/GrantScanner.cs`
- Test: `FazStellarisModmanager.Tests/GrantScannerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class GrantScannerTests
{
    const string BaseEvents = """
        namespace = test
        country_event = {
            id = test.1
            title = test.1.name
            desc = test.1.desc
            picture = GFX_evt_one
            immediate = { add_research_option = tech_a }
            option = { name = test.1.a give_technology = { tech = tech_a } }
            option = { name = test.1.b trigger = { is_gestalt = yes } }
        }
        fleet_event = {
            id = test.2
            title = { trigger = { always = yes } text = test.2.name }
            desc = { trigger = { always = yes } text = test.2.desc }
            desc = test.2.desc
            picture = { trigger = { always = yes } picture = GFX_evt_two }
            hide_window = yes
            option = { name = OK }
            after = { if = { limit = { is_gestalt = no } add_tech_progress = { tech = tech_a progress = 0.25 } } }
        }
        country_event = {
            id = test.3
            title = test.3.name
            inline_script = { script = events/tech_option TECH = tech_b }
            option = { name = test.3.a give_technology = { tech = tech_unknown } }
        }
        country_event = { id = test.4 title = test.4.name option = { name = test.4.a } }
        country_event = { id = test.5 title = test.5.name option = { name = test.5.a } }
        """;

    sealed record Setup(TempDir Tmp, List<ContentSource> Sources, Localisation Loc) : IDisposable
    {
        public void Dispose()
        {
            foreach (var s in Sources) s.Dispose();
            Tmp.Dispose();
        }
    }

    static Setup Create()
    {
        var tmp = new TempDir();
        tmp.Write("g/events/test_events.txt", BaseEvents);
        tmp.Write("g/common/inline_scripts/events/tech_option.txt", "option = { name = pick_$TECH$ give_technology = { tech = $TECH$ } }");
        tmp.Write("g/common/traditions/00_traditions.txt", "tr_x = { on_enabled = { give_technology = { tech = tech_b } } }");
        tmp.Write("g/localisation/english/t_l_english.yml",
            "l_english:" + (char)10 + " test.1.name: \"First Contact\"" + (char)10 + " test.1.desc: \"Hello [Root.GetName]\"" + (char)10
            + " test.1.a: \"Accept\"" + (char)10 + " test.1.b: \"Refuse\"" + (char)10 + " test.2.name: \"Second\"" + (char)10 + " tr_x: \"Tradition X\"" + (char)10);
        // Events: the FIRST definition of an id wins. "!override" sorts before "test_events", "zz_late" after.
        tmp.Write("m/events/!override.txt", "country_event = { id = test.4 title = test.4.name option = { name = mod.a add_research_option = tech_b } }");
        tmp.Write("m/events/zz_late.txt", "country_event = { id = test.1 option = { give_technology = { tech = tech_b } } }");
        tmp.Write("m/common/ascension_perks/mod_perks.txt", "ap_mod = { on_enabled = { add_tech_progress = { tech = tech_b progress = 1 } } }");
        var sources = new List<ContentSource>
        {
            ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true),
            ContentSource.FromPath("Mod", Path.Combine(tmp.Path, "m")),
        };
        return new Setup(tmp, sources, Localisation.Load(sources, new List<string>()));
    }

    static GrantIndex Scan(Setup s)
    {
        var warnings = new List<string>();
        var techs = new HashSet<string>(["tech_a", "tech_b"], StringComparer.OrdinalIgnoreCase);
        var index = GrantScanner.Scan(s.Sources, s.Loc, ScriptLibrary.Load(s.Sources, warnings), k => techs.TryGetValue(k, out var t) ? t : null, warnings);
        Assert.Empty(warnings);
        return index;
    }

    [Fact]
    public void Events_record_where_each_grant_happens()
    {
        using var s = Create();
        var index = Scan(s);

        var sources = index.For("TECH_A");
        Assert.Equal(["test.1", "test.2"], sources.Select(x => x.Id));
        Assert.All(sources, x => Assert.True(x.IsEvent));
        Assert.Equal(
            [(GrantKind.ResearchOption, EventPart.Immediate, (int?)null), (GrantKind.Gives, EventPart.Option, 0)],
            sources[0].Grants.Select(g => (g.Kind, g.Part, g.OptionIndex)));
        var after = Assert.Single(sources[1].Grants);
        Assert.Equal((GrantKind.Progress, 0.25, EventPart.After, "not Gestalt"), (after.Kind, after.Progress!.Value, after.Part, after.Condition));
    }

    [Fact]
    public void Event_windows_get_title_text_picture_and_options()
    {
        using var s = Create();
        var index = Scan(s);

        var first = index.Event("test.1")!;
        Assert.Equal(("country_event", "First Contact", "Hello [Root.GetName]", false, "GFX_evt_one", false, false),
            (first.Type, first.Title, first.Description, first.DescriptionVaries, first.Picture, first.PictureVaries, first.Hidden));
        Assert.Equal([new EventOption("Accept", null), new EventOption("Refuse", "Gestalt")], first.Options);
        Assert.Equal(("Base game", "events/test_events.txt"), (first.Source.SourceName, first.Source.File));

        var second = index.Event("test.2")!;
        Assert.Equal(("Second", "test.2.desc", true, "GFX_evt_two", true, true),
            (second.Title, second.Description, second.DescriptionVaries, second.Picture, second.PictureVaries, second.Hidden));
        Assert.Equal(["OK"], second.Options.Select(o => o.Name));
        Assert.Null(index.Event("test.5"));
    }

    [Fact]
    public void Injected_options_mods_first_definition_and_other_sources()
    {
        using var s = Create();
        var index = Scan(s);

        var sources = index.For("tech_b");
        Assert.Equal(["test.3", "test.4", "ap_mod", "tr_x"], sources.Select(x => x.Id));
        Assert.Equal(["pick_tech_b", "test.3.a"], index.Event("test.3")!.Options.Select(o => o.Name));
        Assert.Equal((EventPart.Option, 0), (sources[0].Grants[0].Part, sources[0].Grants[0].OptionIndex!.Value));
        Assert.Equal("Mod", index.Event("test.4")!.Source.SourceName);
        Assert.DoesNotContain(index.For("tech_b"), x => x.Id == "test.1");
        Assert.Equal(("Ascension perks", "ascension_perks", "ap_mod", 1.0), (sources[2].Kind, sources[2].KindFolder, sources[2].Name, sources[2].Grants[0].Progress!.Value));
        Assert.Equal(("Traditions", "Tradition X", GrantKind.Gives), (sources[3].Kind, sources[3].Name, sources[3].Grants[0].Kind));
        Assert.Empty(index.For("tech_unknown"));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art> --filter GrantScannerTests`
Expected: build errors, because `GrantScanner` and `GrantIndex` don't exist yet.

- [ ] **Step 3: Implement `GrantScanner.cs`**

```csharp
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>Tech -> sources that grant it (events first, then other sources, each by name), plus the events among them.</summary>
public sealed class GrantIndex
{
    readonly IReadOnlyDictionary<string, IReadOnlyList<GrantSource>> _byTech;
    readonly IReadOnlyDictionary<string, GameEvent> _events;

    public GrantIndex(IReadOnlyDictionary<string, IReadOnlyList<GrantSource>> byTech, IReadOnlyDictionary<string, GameEvent> events)
    {
        _byTech = byTech;
        _events = events;
    }

    public static GrantIndex Empty { get; } = new(new Dictionary<string, IReadOnlyList<GrantSource>>(), new Dictionary<string, GameEvent>());

    public IReadOnlyList<GrantSource> For(string tech) => _byTech.TryGetValue(tech, out var list) ? list : [];

    public GameEvent? Event(string id) => _events.TryGetValue(id, out var e) ? e : null;

    public int EventCount => _events.Count;
}

/// <summary>
/// Scans events/ (first definition of an event id wins) and common/ objects (last (folder, id) wins) for tech grants.
/// Files at the same path in a later source replace earlier ones; files are read in load order. Base-game files are only
/// parsed when they mention a grant effect, inline_script or a scripted effect that (transitively) mentions one.
/// </summary>
public static class GrantScanner
{
    static readonly HashSet<string> ExcludedCommon = new(StringComparer.OrdinalIgnoreCase)
    {
        "technology", "scripted_effects", "inline_scripts", "scripted_triggers", "scripted_variables", "script_values",
        "defines", "pop_jobs", "random_names", "name_lists", "on_actions",
    };

    /// <param name="techKey">Maps a tech key to the database's spelling, or null when there is no such tech.</param>
    public static GrantIndex Scan(IReadOnlyList<ContentSource> sources, Localisation loc, ScriptLibrary library,
        Func<string, string?> techKey, ICollection<string> warnings, CancellationToken ct = default)
    {
        string Describe(PdxBlock? b) => TriggerSummary.Describe(b, loc.Get, n => loc.ScriptedTriggers.TryGetValue(n, out var t) ? t : null);
        var finder = new GrantFinder(library, Describe);
        var prefilter = Prefilter(library);

        var byTech = new Dictionary<string, List<GrantSource>>(StringComparer.OrdinalIgnoreCase);
        void AddSource(PendingSource source)
        {
            foreach (var tech in source.Grants.Select(g => g.Tech).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!byTech.TryGetValue(tech, out var list)) byTech[tech] = list = [];
                list.Add(new GrantSource(source.Kind, source.KindFolder, source.Id, source.Name, source.Source,
                    source.Grants.Where(g => g.Tech.Equals(tech, StringComparison.OrdinalIgnoreCase)).Select(g => g.Grant).ToList()));
            }
        }

        // Events: first definition per id wins.
        var events = new Dictionary<string, GameEvent>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ParseAll(sources, "events", f => true, prefilter, warnings, ct))
            foreach (var e in file.Root.Entries)
            {
                if (e.Value is not PdxBlock block || !IsEventKey(e.Key) || block.GetString("id") is not { } id || !seen.Add(id)) continue;
                var analysed = AnalyseEvent(e.Key, id, block, file.Src, loc, library, finder, Describe, techKey);
                if (analysed is null) continue;
                events[id] = analysed.Value.Event;
                AddSource(new PendingSource("Event", GrantSource.EventsFolder, id, analysed.Value.Event.Title, file.Src, analysed.Value.Grants));
            }

        // common/ objects: last (folder, id) wins.
        var objects = new Dictionary<string, PendingSource?>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ParseAll(sources, "common", rel => rel.Split('/') is { Length: >= 3 } p && !ExcludedCommon.Contains(p[1]), prefilter, warnings, ct))
        {
            var folder = file.Src.File.Split('/')[1];
            foreach (var e in file.Root.Entries)
            {
                if (e.Value is not PdxBlock block || e.Key.StartsWith('@')) continue;
                var id = block.GetString("key") ?? e.Key;
                var grants = Place(finder.Find(block), EventPart.Immediate, null, techKey);
                objects[folder + "|" + id] = grants.Count == 0 ? null
                    : new PendingSource(UnlockScanner.KindName(folder), folder, id, loc.Get(id) ?? id, file.Src, grants);
            }
        }
        foreach (var o in objects.Values)
            if (o is not null) AddSource(o);

        return new GrantIndex(
            byTech.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<GrantSource>)kv.Value
                .OrderBy(s => s.IsEvent ? 0 : 1)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .ToList(), StringComparer.OrdinalIgnoreCase),
            events);
    }

    sealed record TechGrantFor(string Tech, TechGrant Grant);

    sealed record PendingSource(string Kind, string KindFolder, string Id, string Name, TechSourceRef Source, List<TechGrantFor> Grants);

    sealed record ParsedFile(TechSourceRef Src, PdxBlock Root);

    static bool IsEventKey(string key) => key.Equals("event", StringComparison.OrdinalIgnoreCase) || key.EndsWith("_event", StringComparison.OrdinalIgnoreCase);

    static List<TechGrantFor> Place(List<FoundGrant> found, EventPart part, int? option, Func<string, string?> techKey) =>
        found.Select(f => techKey(f.Tech) is { } tech ? new TechGrantFor(tech, new TechGrant(f.Kind, f.Progress, part, option, f.Condition, f.Via)) : null)
            .OfType<TechGrantFor>()
            .ToList();

    static (GameEvent Event, List<TechGrantFor> Grants)? AnalyseEvent(string type, string id, PdxBlock block, TechSourceRef src,
        Localisation loc, ScriptLibrary library, GrantFinder finder, Func<PdxBlock?, string> describe, Func<string, string?> techKey)
    {
        block = ExpandInline(block, library, 0);
        var options = block.Entries.Where(e => e.Key.Equals("option", StringComparison.OrdinalIgnoreCase) && e.Value is PdxBlock)
            .Select(e => (PdxBlock)e.Value).ToList();
        var grants = new List<TechGrantFor>();
        if (block.GetBlock("immediate") is { } immediate) grants.AddRange(Place(finder.Find(immediate), EventPart.Immediate, null, techKey));
        for (var i = 0; i < options.Count; i++) grants.AddRange(Place(finder.Find(options[i]), EventPart.Option, i, techKey));
        if (block.GetBlock("after") is { } after) grants.AddRange(Place(finder.Find(after), EventPart.After, null, techKey));
        if (grants.Count == 0) return null;

        var titles = Entries(block, "title");
        var descs = Entries(block, "desc");
        var pictures = Entries(block, "picture");
        var ev = new GameEvent(
            id,
            type,
            titles.Count > 0 ? FirstText(titles[0].Value, loc) ?? id : id,
            descs.Count > 0 ? FirstText(descs[0].Value, loc) : null,
            descs.Count > 1 || descs.Any(d => d.Value is PdxBlock),
            pictures.Count > 0 ? (pictures[0].Value is PdxBlock pb ? pb.GetString("picture") : (string)pictures[0].Value) : null,
            pictures.Count > 1 || pictures.Any(p => p.Value is PdxBlock),
            string.Equals(block.GetString("hide_window"), "yes", StringComparison.OrdinalIgnoreCase),
            options.Select((o, i) => new EventOption(
                Entries(o, "name") is { Count: > 0 } names ? FirstText(names[0].Value, loc) ?? $"Option {i + 1}" : $"Option {i + 1}",
                (o.GetBlock("exclusive_trigger") ?? o.GetBlock("trigger")) is { } t && describe(t) is var c && c != "always" ? c : null)).ToList(),
            src);
        return (ev, grants);
    }

    static List<PdxEntry> Entries(PdxBlock block, string key) =>
        block.Entries.Where(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>A key's localised text, or the first "text" found depth-first in a block (first_valid, random_valid, nested desc…).</summary>
    static string? FirstText(object value, Localisation loc)
    {
        if (value is string key) return loc.Get(key) ?? key;
        var block = (PdxBlock)value;
        if (block.GetString("text") is { } text) return loc.Get(text) ?? text;
        foreach (var e in block.Entries)
            if (e.Value is PdxBlock child && !e.Key.Equals("trigger", StringComparison.OrdinalIgnoreCase) && FirstText(child, loc) is { } found)
                return found;
        return null;
    }

    /// <summary>Replaces event-level inline_script entries by the script's entries (parameters substituted), recursively.</summary>
    static PdxBlock ExpandInline(PdxBlock block, ScriptLibrary library, int depth)
    {
        if (depth > 5 || !block.Entries.Any(e => e.Key.Equals("inline_script", StringComparison.OrdinalIgnoreCase))) return block;
        var copy = new PdxBlock();
        foreach (var e in block.Entries)
        {
            if (!e.Key.Equals("inline_script", StringComparison.OrdinalIgnoreCase)) { copy.Entries.Add(e); continue; }
            var body = e.Value switch
            {
                string path => library.Inline(path, new Dictionary<string, string>()),
                PdxBlock b when b.GetString("script") is { } path => library.Inline(path,
                    b.Entries.Where(x => x.Value is string && !x.Key.Equals("script", StringComparison.OrdinalIgnoreCase))
                        .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => (string)g.Last().Value, StringComparer.OrdinalIgnoreCase)),
                _ => null,
            };
            if (body is not null) copy.Entries.AddRange(ExpandInline(body, library, depth + 1).Entries);
        }
        copy.Items.AddRange(block.Items);
        return copy;
    }

    /// <summary>Matches text that may grant a tech: the effect names, inline_script, or a scripted effect that (transitively) mentions them.</summary>
    static Regex Prefilter(ScriptLibrary library)
    {
        var words = library.Effects.ToDictionary(kv => kv.Key,
            kv => new HashSet<string>(Regex.Matches(PdxScriptPrinter.Print(kv.Value), "[A-Za-z0-9_]+").Select(m => m.Value), StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var granting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, w) in words)
            if (GrantFinder.EffectNames.Any(w.Contains) || w.Contains("inline_script")) granting.Add(name);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (name, w) in words)
                if (!granting.Contains(name) && w.Any(granting.Contains)) { granting.Add(name); changed = true; }
        }
        var names = GrantFinder.EffectNames.Append("inline_script").Concat(granting).Select(Regex.Escape);
        return new Regex(@"\b(?:" + string.Join("|", names) + @")\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    /// <summary>Winning files of a top folder (per-path override), parsed in parallel, returned in load order. Base files must match the prefilter.</summary>
    static List<ParsedFile> ParseAll(IReadOnlyList<ContentSource> sources, string folder, Func<string, bool> include, Regex prefilter,
        ICollection<string> warnings, CancellationToken ct)
    {
        var files = new Dictionary<string, (ContentSource Source, string Rel)>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            foreach (var rel in source.Files(folder, ".txt"))
                if (include(rel)) files[rel] = (source, rel);

        var ordered = files.Values.OrderBy(f => f.Rel, TechDatabase.LoadOrder).ToArray();
        var parsed = new ParsedFile?[ordered.Length];
        var warned = new ConcurrentQueue<string>();
        Parallel.ForEach(Enumerable.Range(0, ordered.Length), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, i =>
        {
            var (source, rel) = ordered[i];
            string text;
            try { text = source.ReadText(rel); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
            {
                warned.Enqueue($"{source.Name}: {rel}: {ex.Message}");
                return;
            }
            if (source.IsBaseGame && !prefilter.IsMatch(text)) return;
            parsed[i] = new ParsedFile(new TechSourceRef(source.Name, source.IsBaseGame, rel), ParadoxScriptParser.Parse(text));
        });
        ct.ThrowIfCancellationRequested();
        foreach (var w in warned) warnings.Add(w);
        return parsed.OfType<ParsedFile>().ToList();
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art> --filter GrantScannerTests`
Expected: 3 passed. Then run the whole suite.

- [ ] **Step 5: Commit**

```bash
git add FazStellarisModmanager.Core/Technology/GrantScanner.cs FazStellarisModmanager.Tests/GrantScannerTests.cs
git commit -m "Tech grants: scan events (first definition wins) and common objects into a grant index"
```

---

### Task 3: Database, icon size and event pictures

**Files:**
- Modify: `FazStellarisModmanager.Core/Technology/TechDatabase.cs`, `IconCache.cs`, `TechTreeService.cs`
- Test: add to `FazStellarisModmanager.Tests/IconCacheTests.cs` and `FazStellarisModmanager.Tests/TechDatabaseTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `IconCacheTests`:

```csharp
    [Fact]
    public void A_larger_max_size_keeps_event_pictures_big()
    {
        using var tmp = new TempDir();
        var pixels = new (byte R, byte G, byte B, byte A)[200 * 100];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (10, 20, 30, 255);
        using var s = Source(tmp, "base", DdsBuilder.Bgra32(200, 100, pixels));
        var cache = new IconCache(Path.Combine(tmp.Path, "cache"));
        static (int, int) Size(string uri)
        {
            var png = Convert.FromBase64String(uri["data:image/png;base64,".Length..]);
            static int BE(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
            return (BE(png, 16), BE(png, 20));
        }

        Assert.Equal((200, 100), Size(cache.DataUri([s], new IconRef(IconRel), 512)!));
        Assert.Equal((64, 32), Size(cache.DataUri([s], new IconRef(IconRel))!));
    }
```

Add to `TechDatabaseTests`. Use the file's existing helpers; if it has none that fit, build the sources inline as shown:

```csharp
    [Fact]
    public void Grant_sources_and_events_are_part_of_the_database()
    {
        using var tmp = new TempDir();
        tmp.Write("g/common/technology/00_t.txt", "tech_a = { area = physics tier = 1 }\n");
        tmp.Write("g/events/e.txt", "country_event = { id = e.1 title = e.1.name option = { name = OK give_technology = { tech = TECH_A } } }\n");
        using var source = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);

        var db = TechDatabase.Build([source]);

        var grant = Assert.Single(db.GrantSources("tech_a"));
        Assert.Equal(("e.1", true), (grant.Id, grant.IsEvent));
        Assert.Equal("e.1.name", db.Event("e.1")!.Title);
        Assert.Empty(db.GrantSources("tech_missing"));
        Assert.Null(db.Event("nope"));
    }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art> --filter "IconCacheTests|TechDatabaseTests"`
Expected: build errors: no `DataUri` overload with 3 arguments, and no `GrantSources` / `Event` on `TechDatabase`.

- [ ] **Step 3: `IconCache.cs`.** Give the `IconRef` overload a max size, used for the decode and the cache key.

```csharp
    /// <summary>"data:image/png;base64,…" for the texture (cropped to its frame, scaled to fit <paramref name="maxSize"/>), or null when no source has it, the path is unsafe/not .dds, or it can't be decoded.</summary>
    public string? DataUri(IReadOnlyList<ContentSource> sources, IconRef icon, int maxSize = MaxIconSize)
    {
        var rel = icon.Path.Replace((char)92, '/');
        if (!rel.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || rel.StartsWith('/') || rel.Contains(':') || rel.Split('/').Contains("..")) return null;
        try
        {
            for (var i = sources.Count - 1; i >= 0; i--)
            {
                var source = sources[i];
                if (!source.Exists(rel)) continue;
                var id = $"{source.Name}|{icon.CacheId}|{source.Stamp(rel)}" + (maxSize == MaxIconSize ? "" : $"|{maxSize}");
                return Load(source, rel, icon, id, maxSize);
            }
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
```

Change `Load` to take `int maxSize` as its last parameter. In it, replace `if (w > MaxIconSize || h > MaxIconSize) (w, h, rgba) = Downscale(w, h, rgba, MaxIconSize);` with `if (w > maxSize || h > maxSize) (w, h, rgba) = Downscale(w, h, rgba, maxSize);`. Also change `const int MaxIconSize = 64;` to `public const int MaxIconSize = 64;`.

- [ ] **Step 4: `TechDatabase.cs`**
- Add a constructor parameter `GrantIndex grants` at the end. Store it in `readonly GrantIndex _grants;`.
- Add these members next to `Unlocks`:

```csharp
    /// <summary>Events and other game objects that give, progress or offer the tech: events first, then others, each by name.</summary>
    public IReadOnlyList<GrantSource> GrantSources(string techKey) => _grants.For(techKey);

    /// <summary>An event that grants some tech, by id; null for other events.</summary>
    public GameEvent? Event(string id) => _grants.Event(id);
```

- In `Build`, the `techs` dictionary is filled first. Right after that loop, and before `var dependents = …`, add:

```csharp
        progress?.Report("Finding events and other sources that grant technologies…");
        var library = ScriptLibrary.Load(sources, warnings, ct);
        var grants = GrantScanner.Scan(sources, loc, library, key => techs.TryGetValue(key, out var t) ? t.Key : null, warnings, ct);
```

- Pass `grants` to the constructor call at the end: `…, unlocks, sprites, grants)`.

- [ ] **Step 5: `TechTreeService.cs`**
- In `TechTree`, add the property `internal ConcurrentDictionary<string, Task<string?>> Pictures { get; } = new(StringComparer.OrdinalIgnoreCase);`.
- Add to `TechTreeService`:

```csharp
    /// <summary>Largest side, in pixels, of event pictures (the game's are 450 x 150).</summary>
    public const int EventPictureSize = 512;

    /// <summary>PNG data URI of an event's picture, decoded once per tree on the thread pool; null when the sprite or texture is missing.</summary>
    public Task<string?> EventPictureAsync(GameEvent ev)
    {
        if (_current is not { } tree || ev.Picture is not { } sprite) return Task.FromResult<string?>(null);
        return tree.Pictures.GetOrAdd(sprite, name => Task.Run(() =>
        {
            if (tree.Database.Sprites.Find(name) is not { } info) return null;
            var icon = new IconRef(info.TextureFile, Math.Clamp(info.DefaultFrame ?? 1, 1, info.Frames), info.Frames);
            return _icons.DataUri(tree.Sources, icon, EventPictureSize);
        }));
    }
```

- [ ] **Step 6: Run all tests**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art>`
Expected: all pass, including the 2 new tests.

- [ ] **Step 7: Commit**

```bash
git add FazStellarisModmanager.Core/Technology/TechDatabase.cs FazStellarisModmanager.Core/Technology/IconCache.cs FazStellarisModmanager.Core/Technology/TechTreeService.cs FazStellarisModmanager.Tests/IconCacheTests.cs FazStellarisModmanager.Tests/TechDatabaseTests.cs
git commit -m "Tech: grant sources and events in the database; full-size event pictures"
```

---

### Task 4: UI — "Obtained from" and the event browser

**Files:**
- Create: `FazStellarisModmanager/Components/EventBrowser.razor`
- Modify: `FazStellarisModmanager/Components/TechSidebar.razor`, `FazStellarisModmanager/wwwroot/css/site.css`

- [ ] **Step 1: Create `Components/EventBrowser.razor`**

```razor
@using System.Text.RegularExpressions

<div class="modal-backdrop" @onclick="Close">
    <div class="evbrowser" @onclick:stopPropagation @ref="dialog" tabindex="-1" @onkeydown="Key"
         role="dialog" aria-modal="true" aria-label="@($"Sources of {Tech.Name}")">
        <div class="evlist">
            <div class="evlist-head">
                <b>@Tech.Name</b>
                <button type="button" class="x" title="Close" @onclick="Close">✕</button>
            </div>
            <div class="muted small">Obtained from (@Sources.Count)</div>
            @for (var i = 0; i < Sources.Count; i++)
            {
                var s = Sources[i];
                var index = i;
                <div class="gsrc @(i == Index ? "on" : "") @(s.IsEvent ? "" : "static")" @onclick="() => Select(index)">
                    <span class="gbadge @GrantText.BadgeClass(s.Grants)">@GrantText.Badge(s.Grants)</span>
                    <div>@(s.IsEvent ? s.Name : $"{s.Kind}: {s.Name}") <span class="muted small">@s.Id</span></div>
                </div>
            }
        </div>

        @if (Current is { } src && Database.Event(src.Id) is { } ev)
        {
            var immediate = src.Grants.Where(g => g.Part == EventPart.Immediate).ToList();
            var after = src.Grants.Where(g => g.Part == EventPart.After).ToList();
            <div class="evwin">
                <div class="evtitle">
                    @ev.Title
                    <span class="evid">@ev.Id · @ev.Type.Replace('_', ' ') · @ev.Source.SourceName@(ev.Hidden ? " · hidden event (no window in game)" : "")</span>
                </div>
                @if (picture is not null)
                {
                    <img class="evpic" src="@picture" alt="" />
                }
                else
                {
                    <div class="evpic placeholder">@(ev.Picture is null ? "No picture" : pictureLoading ? "Loading picture…" : ev.Picture)</div>
                }
                @if (ev.Picture is not null && ev.PictureVaries)
                {
                    <div class="muted small evnote">The picture depends on conditions; showing the first.</div>
                }
                <div class="evdesc">
                    @if (ev.Description is null)
                    {
                        <span class="muted">No description.</span>
                    }
                    else
                    {
                        foreach (var part in Commands.Split(ev.Description))
                        {
                            if (part.Length > 1 && part[0] == '[' && part[^1] == ']')
                            {
                                <span class="evcmd">@part</span>
                            }
                            else
                            {
                                <text>@part</text>
                            }
                        }
                    }
                    @if (ev.DescriptionVaries)
                    {
                        <div class="muted small">(the text depends on conditions; showing the first)</div>
                    }
                </div>
                @if (immediate.Count > 0)
                {
                    <div class="evstrip">When the event fires: @string.Join("; ", immediate.Select(Effect))</div>
                }
                <div class="evopts">
                    @for (var i = 0; i < ev.Options.Count; i++)
                    {
                        var option = ev.Options[i];
                        var optionIndex = i;
                        var mine = src.Grants.Where(g => g.Part == EventPart.Option && g.OptionIndex == optionIndex).ToList();
                        <div class="evopt @(mine.Count > 0 ? "gives" : "")">
                            @if (option.Condition is not null)
                            {
                                <span class="cnd">Only if: @option.Condition</span>
                            }
                            @foreach (var g in mine)
                            {
                                <span class="gtag">★ @Effect(g)</span>
                            }
                            @option.Name
                        </div>
                    }
                </div>
                @if (after.Count > 0)
                {
                    <div class="evstrip">After any option: @string.Join("; ", after.Select(Effect))</div>
                }
                <div class="evnav">
                    <button type="button" class="link" disabled="@(Step(-1) is null)" @onclick="() => Go(-1)">◀ Previous</button>
                    <span class="muted">Event @(EventIndexes.IndexOf(Index) + 1) of @EventIndexes.Count</span>
                    <button type="button" class="link" disabled="@(Step(1) is null)" @onclick="() => Go(1)">Next ▶</button>
                </div>
            </div>
        }
    </div>
</div>

@code {
    [Parameter, EditorRequired] public Tech Tech { get; set; } = default!;
    [Parameter, EditorRequired] public TechDatabase Database { get; set; } = default!;
    [Parameter, EditorRequired] public TechTreeService Tree { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyList<GrantSource> Sources { get; set; } = [];
    [Parameter] public int Index { get; set; }
    [Parameter] public EventCallback<int> IndexChanged { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    static readonly Regex Commands = new(@"(\[[^\[\]]+\])", RegexOptions.Compiled);

    ElementReference dialog;
    string? picture;
    bool pictureLoading;
    string? shownEvent;
    bool focused;

    GrantSource? Current => Index >= 0 && Index < Sources.Count ? Sources[Index] : null;

    List<int> EventIndexes => Enumerable.Range(0, Sources.Count).Where(i => Sources[i].IsEvent).ToList();

    string Effect(TechGrant g) => GrantText.Effect(g, Tech.Name);

    protected override async Task OnParametersSetAsync()
    {
        if (Current is not { } src || Database.Event(src.Id) is not { } ev || ev.Id == shownEvent) return;
        shownEvent = ev.Id;
        (picture, pictureLoading) = (null, ev.Picture is not null);
        var loaded = await Tree.EventPictureAsync(ev);
        if (shownEvent != ev.Id) return;
        (picture, pictureLoading) = (loaded, false);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (focused) return;
        focused = true;
        try { await dialog.FocusAsync(); }
        catch (Exception ex) when (ex is Microsoft.JSInterop.JSException or InvalidOperationException) { }
    }

    int? Step(int direction)
    {
        var events = EventIndexes;
        var at = events.IndexOf(Index);
        var next = at + direction;
        return at >= 0 && next >= 0 && next < events.Count ? events[next] : null;
    }

    Task Go(int direction) => Step(direction) is { } i ? IndexChanged.InvokeAsync(i) : Task.CompletedTask;

    Task Select(int index) => Sources[index].IsEvent ? IndexChanged.InvokeAsync(index) : Task.CompletedTask;

    Task Close() => OnClose.InvokeAsync();

    Task Key(KeyboardEventArgs e) => e.Key switch
    {
        "Escape" => Close(),
        "ArrowLeft" => Go(-1),
        "ArrowRight" => Go(1),
        _ => Task.CompletedTask,
    };
}
```

- [ ] **Step 2: Add the section to `Components/TechSidebar.razor`**

2a. Insert this block directly after the closing `</details>` of the Unlocks section, which starts with `<summary>Unlocks (@Unlocks.Count)</summary>`:

```razor
    <details open>
        <summary>Obtained from (@Grants.Count)</summary>
        @if (Grants.Count == 0)
        {
            <p class="muted small">No event or other game object gives, progresses or offers this technology.</p>
        }
        @for (var i = 0; i < Grants.Count; i++)
        {
            var g = Grants[i];
            var index = i;
            <div class="gsrc">
                <span class="gbadge @GrantText.BadgeClass(g.Grants)">@GrantText.Badge(g.Grants)</span>
                @if (g.IsEvent)
                {
                    <div>
                        <button type="button" class="link" @onclick="() => browserIndex = index">@g.Name</button>
                        <span class="muted small">@g.Id · @GrantText.Where(g, Database.Event(g.Id))</span>
                    </div>
                }
                else
                {
                    <div>@g.Kind: <b>@g.Name</b> <span class="muted small">@g.Id</span></div>
                }
            </div>
        }
    </details>

    @if (browserIndex is int shownIndex && shownIndex < Grants.Count)
    {
        <EventBrowser Tech="Tech" Database="Database" Tree="Tree" Sources="Grants" Index="shownIndex"
                      IndexChanged="i => browserIndex = i" OnClose="() => browserIndex = null" />
    }
```

2b. In `@code`, add the field `int? browserIndex;` and the property `IReadOnlyList<GrantSource> Grants => Database.GrantSources(Tech.Key);`. In `OnParametersSet`, find the block that resets `_tick` when `_shownTech != Tech.Key`, and also set `browserIndex = null;` inside it.

- [ ] **Step 3: Append the styles to `wwwroot/css/site.css`**

```css
.gsrc { display: flex; gap: .45rem; align-items: center; padding: .2rem .3rem; border-radius: 5px; }
.gsrc.on { background: #203247; outline: 1px solid var(--accent); }
.evlist .gsrc { cursor: pointer; }
.evlist .gsrc.static { cursor: default; opacity: .75; }
.gbadge { font-size: .7rem; padding: .05rem .45rem; border-radius: 8px; white-space: nowrap; }
.gbadge.give { background: #173a2a; color: #6fcf97; border: 1px solid #2e6b4a; }
.gbadge.prog { background: #3a2e12; color: #f2c983; border: 1px solid #6b5520; }
.gbadge.opt { background: #1b3550; color: #9fd2ff; border: 1px solid #2d5a85; }
.gbadge.mix { background: #2a2440; color: #c9b6ff; border: 1px solid #4d3f80; }
.evbrowser { display: grid; grid-template-columns: 260px minmax(0, 480px); gap: .8rem; background: var(--panel); border: 1px solid var(--border); border-radius: 10px; padding: .8rem; max-height: 92vh; overflow: auto; width: min(780px, 96vw); }
.evlist { display: flex; flex-direction: column; gap: .15rem; min-width: 0; }
.evlist-head { display: flex; justify-content: space-between; align-items: center; gap: .4rem; }
.evlist-head .x { border: 0; background: none; color: var(--muted); }
.evwin { background: linear-gradient(#18222c, #10171e); border: 2px solid #4c6a7d; border-radius: 6px; color: var(--text); align-self: start; }
.evtitle { background: linear-gradient(#2b4152, #1c2c38); padding: .4rem .7rem; font-weight: 700; font-size: 1rem; border-bottom: 1px solid #4c6a7d; }
.evid { display: block; font-weight: 400; font-size: .72rem; color: #8fb1c6; }
.evpic { display: block; width: 450px; max-width: calc(100% - 1rem); aspect-ratio: 3 / 1; margin: .5rem auto 0; border: 1px solid #4c6a7d; object-fit: cover; }
.evpic.placeholder { display: flex; align-items: center; justify-content: center; color: var(--muted); font-size: .8rem; background: #0b1016; }
.evnote { padding: .1rem .7rem 0; }
.evdesc { padding: .5rem .8rem; line-height: 1.4; white-space: pre-line; max-height: 240px; overflow: auto; }
.evcmd { color: #f2c983; }
.evopts { padding: .2rem .8rem .6rem; display: flex; flex-direction: column; gap: .3rem; }
.evopt { background: linear-gradient(#2f4a5c, #22394a); border: 1px solid #5d8299; border-radius: 3px; padding: .35rem .6rem; text-align: center; }
.evopt.gives { border-color: #e3b04b; box-shadow: 0 0 0 1px #e3b04b inset; }
.evopt .cnd { display: block; font-size: .7rem; color: #9fb6c6; }
.evopt .gtag { display: block; font-size: .72rem; color: #f2c983; }
.evstrip { border-top: 1px solid #33495a; padding: .4rem .8rem; background: #0f2a1c; font-size: .82rem; }
.evnav { display: flex; justify-content: space-between; align-items: center; padding: .4rem .8rem; border-top: 1px solid #33495a; }
@media (max-width: 820px) { .evbrowser { grid-template-columns: 1fr; } }
```

- [ ] **Step 4: Build and test**

Run: `dotnet build FazStellarisModmanager.sln -c Release --artifacts-path <art>` and `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art>`.
Expected: 0 errors, no new warnings, and all tests pass.

- [ ] **Step 5: Commit**

```bash
git add FazStellarisModmanager/Components/EventBrowser.razor FazStellarisModmanager/Components/TechSidebar.razor FazStellarisModmanager/wwwroot/css/site.css
git commit -m "Tech: Obtained from section and in-game style event browser"
```

---

### Task 5: Real-data check

- [ ] **Step 1:** In a scratch console project that references the Core Release DLL (not in the repo), build `TechDatabase` from the base game at `D:\SteamLibrary\steamapps\common\Stellaris`. Report:
  - the build time before and after this feature;
  - the number of techs with at least one source;
  - the number of events kept;
  - `GrantSources("tech_psionic_theory")` (id, badge, where);
  - `GrantSources("tech_colonization_3")`;
  - the warnings.
- [ ] **Step 2:** Spot-check `ancrel.2072`: it should report "after any option" for tech_colonization_3, with condition "not Has technology: …" or similar.
- [ ] **Step 3:** Fix anything wrong, test-first, in Core.

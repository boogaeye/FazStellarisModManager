# Tech Unlocks & Details Implementation Plan (Sub-project 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every tech in the Tech tab shows, in a right-hand sidebar with collapsible sections:
- **Unlocks:** everything in `common/` that requires the tech, with icons.
- **Effects:** stat bonuses with icons, custom unlock text, feature flags, tech swaps.
- **Weights:** research and AI weights, as readable script.
- **Conditions**, and the **raw definition**.

**Architecture:** additions to `FazStellarisModmanager.Core`.
- **Script display** (`Descriptors/`): `PdxScriptPrinter` turns parsed script back into indented text, and `ScriptHighlighter` splits printed lines into coloured tokens for the UI.
- **Tech details** (`Technology/`): `TechDetailsBuilder` derives stat bonuses, unlock text, flags, swaps, weights and scripts from each tech's parsed block.
- **Unlocks:** `UnlockScanner` indexes every `common/` object whose `prerequisites`/`show_in_tech` name a tech, with the same override rules as techs.
- **Icons:**
  - `SpriteIndex` maps GFX sprite names to textures from `interface/*.gfx`.
  - `IconResolver` turns unlock icon fields and modifier keys into an `IconRef` (path plus sprite-sheet frame).
  - `IconCache` crops frames.
  - `TechTreeService` prewarms unlock and bonus icons after the tech icons.

The UI gains `Components/ScriptView.razor` and `Components/TechDetails.razor`, and the Tech page becomes three columns.

**Tech Stack:** .NET 10, C#, Blazor Hybrid (WPF), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-03-tech-unlocks-design.md`. The approved layout is mockup option B (`.superpowers/brainstorm/2876-1791068121/content/tech-details.html`) as a right sidebar.

**Deviation from spec:** localisation `$ref$` resolution goes **3 levels** deep instead of 1. Modifier names chain references, so one level leaves them half-resolved. For example, `mod_country_energy_produces_mult` = "Monthly $energy$", `energy` = "$concept_energy$", and `concept_energy` = "Energy Credits".

**Conventions for every task:**
- Repo root: `C:\Users\SCP Fazbear\source\repos\FazStellarisModmanager`. Use Git Bash. Work on branch `feature/tech-unlocks`, created in Task 1 from `master`.
- **Always use `-c Release`** for dotnet build and test.
- **Never type the two characters backslash + lowercase u** in source.
- Commits end with a blank line and then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Existing code to know (read the files before changing them):**
  - `Descriptors/ParadoxScriptParser.cs`:
    - `PdxBlock` has `Entries` (`PdxEntry(Key, Op, Value)`, where Value is a string or a `PdxBlock`), `Items` (strings or `PdxBlock`s), `StringItems`, `GetString(key)` (last string value wins, case-insensitive) and `GetBlock(key)`.
    - Entries and bare items are stored separately, so their relative order is lost.
  - `Technology/Localisation.cs`: `Get(key)` uses a case-insensitive map, cleans the text and resolves `$ref$`. The private `Clean(text, depth)` currently resolves only while `depth < 1`.
  - `Technology/TechParser.cs`: `TechDefinition(Key, Area, Tier, Category, Cost, Prerequisites, IsStart, IsRare, IsDangerous, IsRepeatable, Icon, Dlcs)` and `TechParser.Parse(text)`.
  - `Technology/TechDatabase.cs`:
    - `Tech(Key, Name, Description, Area, Tier, Category, Cost, Prerequisites, IsStart, IsRare, IsDangerous, IsRepeatable, IconKey, Dlcs, Source, Overridden)`, `TechSourceRef(SourceName, IsBaseGame, File)`.
    - `TechDatabase.Build(sources, earlierWarnings, progress, ct)` uses a private constructor, `internal static readonly IComparer<string> LoadOrder`, `TryRead` (warnings) and `Localisation.Load`.
  - `Technology/IconCache.cs`:
    - `DataUri(sources, iconKey)` validates the key, then for `gfx/interface/icons/technologies/<key>.dds` scans sources from last to first.
    - The private `Load(source, rel, id)` decodes, downscales to 64, encodes PNG and caches it on disk.
    - It catches everything except OOM.
  - `Technology/TechTreeService.cs`:
    - `TechTree(Label, Database, Sources)` has an internal `Icons` map.
    - `IconUri(Tech)` is a memo lookup.
    - `PrewarmAsync(tree, ct)` fills `tree.Icons` for tech icon keys (non-null only) and reports "Loading icons… n/total".
  - `Technology/ContentSource.cs`: `Files(folder, ext)` (recursive, sorted), `Exists`, `Open`, `Stamp` and `ReadText`. It contains paths inside the root.
  - Tests: `TestUtil/TempDir` (`Path`, `Write(rel, text)`, `Mkdir`), `TestUtil/FakeInstall` (`GameDir`, `UserDir`, `DataDir`, `Write`), and `TestUtil/DdsBuilder` (`Bgra32(w, h, params (R,G,B,A)[])`).

---

## File structure

```
FazStellarisModmanager.Core/Descriptors/
  PdxScriptPrinter.cs        PdxBlock -> indented script text (+ "# name" annotations)
  ScriptHighlighter.cs       one printed line -> coloured tokens
FazStellarisModmanager.Core/Technology/
  Localisation.cs            (ref depth 3)
  TechParser.cs              (+ PdxBlock Block on TechDefinition)
  TechDetails.cs             StatBonus, CustomUnlock, TechSwap, TechDetails, TechDetailsBuilder
  SpriteIndex.cs             SpriteInfo, SpriteIndex
  IconResolver.cs            IconRef, IconResolver
  IconCache.cs               (+ DataUri(sources, IconRef) with frame crop)
  UnlockScanner.cs           Unlock, UnlockScanner
  TechDatabase.cs            (+ Tech.Details, Unlocks(), AllUnlocks, Sprites)
  TechTreeService.cs         (+ UnlockIconUri, BonusIconUri, prewarm all icons)
FazStellarisModmanager/
  Components/ScriptView.razor, Components/TechDetails.razor
  Pages/TechPage.razor, wwwroot/css/site.css   (modified: 3 columns, sidebar)
FazStellarisModmanager.Tests/
  ScriptPrintingTests.cs, TechDetailsTests.cs, SpriteIconTests.cs, UnlockScannerTests.cs
  (+ additions to LocalisationTests.cs, IconCacheTests.cs, TechTreeServiceTests.cs)
```

---

### Task 1: Branch, deeper localisation refs, script printer and highlighter

**Files:**
- Modify: `FazStellarisModmanager.Core/Technology/Localisation.cs`
- Create: `FazStellarisModmanager.Core/Descriptors/PdxScriptPrinter.cs`, `FazStellarisModmanager.Core/Descriptors/ScriptHighlighter.cs`
- Test: `FazStellarisModmanager.Tests/ScriptPrintingTests.cs`, plus one test added to `LocalisationTests.cs`

- [ ] **Step 1: Create the branch**

```bash
git checkout master && git checkout -b feature/tech-unlocks
```

- [ ] **Step 2: Write the failing tests**

Append this test inside the class in `FazStellarisModmanager.Tests/LocalisationTests.cs`:

```csharp
    [Fact]
    public void Resolves_chained_references_up_to_three_levels()
    {
        var loc = new Localisation();
        loc.AddText("l_english:\n energy:0 \"$concept_energy$\"\n concept_energy:0 \"Energy Credits\"\n mod_country_energy_produces_mult:0 \"Monthly $energy$\"\n");

        Assert.Equal("Monthly Energy Credits", loc.Get("MOD_COUNTRY_ENERGY_PRODUCES_MULT"));
    }
```

Create `FazStellarisModmanager.Tests/ScriptPrintingTests.cs`:

```csharp
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Tests;

public class ScriptPrintingTests
{
    [Fact]
    public void Prints_indented_script_with_annotations()
    {
        var block = ParadoxScriptParser.Parse("modifier = { factor = 1.25 has_ethic = ethic_militarist }\nyears_passed >= 5\ntags = { a b }\nempty = { }");

        var text = PdxScriptPrinter.Print(block, v => v == "ethic_militarist" ? "Militarist" : null);

        Assert.Equal(
            "modifier = {\n    factor = 1.25\n    has_ethic = ethic_militarist   # Militarist\n}\nyears_passed >= 5\ntags = {\n    a\n    b\n}\nempty = { }",
            text);
    }

    [Fact]
    public void Prints_a_named_block_and_quotes_values_with_spaces()
    {
        var text = PdxScriptPrinter.PrintNamed("t", ParadoxScriptParser.Parse("a = \"two words\" b = { { x } }"));

        Assert.Equal("t = {\n    a = \"two words\"\n    b = {\n        {\n            x\n        }\n    }\n}", text);
    }

    [Fact]
    public void Annotation_equal_to_the_value_is_omitted() =>
        Assert.Equal("a = b", PdxScriptPrinter.Print(ParadoxScriptParser.Parse("a = b"), v => "B"));

    [Fact]
    public void Highlights_key_operator_value_and_comment()
    {
        var tokens = ScriptHighlighter.Line("    has_ethic = ethic_militarist   # Militarist");

        Assert.Equal(new[]
        {
            ("    ", ScriptTokenKind.Plain), ("has_ethic", ScriptTokenKind.Key), (" ", ScriptTokenKind.Plain),
            ("=", ScriptTokenKind.Operator), (" ", ScriptTokenKind.Plain), ("ethic_militarist   ", ScriptTokenKind.Value),
            ("# Militarist", ScriptTokenKind.Comment),
        }, tokens.Select(t => (t.Text, t.Kind)));
    }

    [Theory]
    [InlineData("}", "}", ScriptTokenKind.Plain)]
    [InlineData("    a", "    a", ScriptTokenKind.Value)]
    public void Highlights_braces_and_bare_items(string line, string text, ScriptTokenKind kind) =>
        Assert.Equal((text, kind), ScriptHighlighter.Line(line).Select(t => (t.Text, t.Kind)).Single());

    [Fact]
    public void Highlights_comparison_and_block_openers()
    {
        Assert.Equal(new[] { "years_passed", " ", ">=", " ", "5" }, ScriptHighlighter.Line("years_passed >= 5").Select(t => t.Text));
        Assert.Equal(ScriptTokenKind.Plain, ScriptHighlighter.Line("modifier = {").Last().Kind);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter "ScriptPrintingTests|LocalisationTests"`
Expected: build FAILS with `The name 'PdxScriptPrinter' does not exist`.

- [ ] **Step 4: Deepen ref resolution in `FazStellarisModmanager.Core/Technology/Localisation.cs`**

In `Clean`, change the resolution condition from `depth < 1` to `depth < 3`. The lambda reads `return depth < 3 && _map.TryGetValue(key, out var v) ? Clean(v, depth + 1) : key;`. Update the XML doc of `Get` to say "resolved up to three levels".

- [ ] **Step 5: Implement `FazStellarisModmanager.Core/Descriptors/PdxScriptPrinter.cs`**

```csharp
using System.Text;

namespace FazStellarisModmanager.Core.Descriptors;

/// <summary>
/// Turns parsed Paradox script back into readable text: 4 spaces per level, "key op value" lines, bare items on their own lines.
/// The parser stores entries and bare items separately, so a block prints its entries first, then its items.
/// </summary>
public static class PdxScriptPrinter
{
    /// <param name="annotate">Optional: a display name for a string value, appended as "   # name" (skipped when null or equal to the value).</param>
    public static string Print(PdxBlock block, Func<string, string?>? annotate = null)
    {
        var sb = new StringBuilder();
        WriteBody(sb, block, 0, annotate);
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Prints "key = { … }" for a whole named block.</summary>
    public static string PrintNamed(string key, PdxBlock block, Func<string, string?>? annotate = null)
    {
        var sb = new StringBuilder();
        WriteEntry(sb, new PdxEntry(key, "=", block), 0, annotate);
        return sb.ToString().TrimEnd('\n');
    }

    static void WriteBody(StringBuilder sb, PdxBlock block, int depth, Func<string, string?>? annotate)
    {
        foreach (var e in block.Entries) WriteEntry(sb, e, depth, annotate);
        foreach (var item in block.Items)
        {
            Indent(sb, depth);
            if (item is PdxBlock child) WriteBlock(sb, child, depth, annotate);
            else sb.Append(Value((string)item)).Append(Note((string)item, annotate)).Append('\n');
        }
    }

    static void WriteEntry(StringBuilder sb, PdxEntry e, int depth, Func<string, string?>? annotate)
    {
        Indent(sb, depth);
        sb.Append(e.Key).Append(' ').Append(e.Op).Append(' ');
        if (e.Value is PdxBlock b) WriteBlock(sb, b, depth, annotate);
        else sb.Append(Value((string)e.Value)).Append(Note((string)e.Value, annotate)).Append('\n');
    }

    static void WriteBlock(StringBuilder sb, PdxBlock b, int depth, Func<string, string?>? annotate)
    {
        if (b.Entries.Count == 0 && b.Items.Count == 0)
        {
            sb.Append("{ }\n");
            return;
        }
        sb.Append("{\n");
        WriteBody(sb, b, depth + 1, annotate);
        Indent(sb, depth);
        sb.Append("}\n");
    }

    static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 4);

    static string Value(string s) => s.Length == 0 || s.Any(char.IsWhiteSpace) ? "\"" + s + "\"" : s;

    static string Note(string value, Func<string, string?>? annotate)
    {
        if (annotate is null) return "";
        var name = annotate(value);
        return string.IsNullOrEmpty(name) || string.Equals(name, value, StringComparison.OrdinalIgnoreCase) ? "" : "   # " + name;
    }
}
```

- [ ] **Step 6: Implement `FazStellarisModmanager.Core/Descriptors/ScriptHighlighter.cs`**

```csharp
using System.Text.RegularExpressions;

namespace FazStellarisModmanager.Core.Descriptors;

public enum ScriptTokenKind { Plain, Key, Operator, Value, Comment }

public sealed record ScriptToken(string Text, ScriptTokenKind Kind);

/// <summary>Splits one line printed by <see cref="PdxScriptPrinter"/> into parts the UI colours.</summary>
public static class ScriptHighlighter
{
    static readonly Regex Assignment = new(@"^(\s*)([^\s=<>!{}]+)(\s*)(>=|<=|!=|==|=|<|>)(\s*)(.*)$", RegexOptions.Compiled);

    public static List<ScriptToken> Line(string line)
    {
        var tokens = new List<ScriptToken>();
        var hash = line.IndexOf('#');
        var code = hash >= 0 ? line[..hash] : line;
        var m = Assignment.Match(code);
        if (m.Success)
        {
            Add(tokens, m.Groups[1].Value, ScriptTokenKind.Plain);
            Add(tokens, m.Groups[2].Value, ScriptTokenKind.Key);
            Add(tokens, m.Groups[3].Value, ScriptTokenKind.Plain);
            Add(tokens, m.Groups[4].Value, ScriptTokenKind.Operator);
            Add(tokens, m.Groups[5].Value, ScriptTokenKind.Plain);
            var rest = m.Groups[6].Value;
            Add(tokens, rest, rest.Trim() is "{" or "{ }" ? ScriptTokenKind.Plain : ScriptTokenKind.Value);
        }
        else
        {
            Add(tokens, code, code.Trim() is "" or "{" or "}" or "{ }" ? ScriptTokenKind.Plain : ScriptTokenKind.Value);
        }
        if (hash >= 0) Add(tokens, line[hash..], ScriptTokenKind.Comment);
        return tokens;
    }

    static void Add(List<ScriptToken> tokens, string text, ScriptTokenKind kind)
    {
        if (text.Length > 0) tokens.Add(new ScriptToken(text, kind));
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter "ScriptPrintingTests|LocalisationTests"`
Expected: all pass. That is 7 ScriptPrinting test cases plus the LocalisationTests including the new one. Then run the full suite.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Tech: script printer/highlighter, deeper localisation refs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Tech details (bonuses, unlock text, flags, swaps, weights, scripts)

**Files:**
- Modify: `FazStellarisModmanager.Core/Technology/TechParser.cs` (`TechDefinition` gains a last parameter `PdxBlock Block`)
- Create: `FazStellarisModmanager.Core/Technology/TechDetails.cs`
- Modify: `FazStellarisModmanager.Core/Technology/TechDatabase.cs` (`Tech` gains a last parameter `TechDetails Details`, built in `Build`)
- Test: `FazStellarisModmanager.Tests/TechDetailsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class TechDetailsTests
{
    const string Techs = """
        tech_x = {
        	area = physics
        	weight = @w
        	gateway = energy_weapons
        	modifier = { army_damage_mult = 0.05 ship_speed_mult = -0.1 country_admin_cap_add = 2 weird_flag = yes }
        	feature_flags = { unlocks_auto_research some_flag }
        	prereqfor_desc = { ship = { title = "T_SHIP" desc = "T_SHIP_DESC" } custom = { title = t_custom } }
        	technology_swap = { name = tech_y inherit_effects = yes trigger = { country_uses_bio_ships = yes } }
        	weight_modifier = { modifier = { factor = 1.25 has_ethic = ethic_militarist } }
        	ai_weight = { weight = 2 }
        	potential = { always = yes }
        }
        tech_y = { area = physics }
        """;

    const string Loc = "l_english:\n MOD_ARMY_DAMAGE_MULT:0 \"Army Damage\"\n mod_ship_speed_mult:0 \"Ship Speed\"\n T_SHIP:0 \"Science Ship\"\n T_SHIP_DESC:0 \"Build science ships\"\n t_custom:0 \"Particle storm\"\n ethic_militarist:0 \"Militarist\"\n";

    static TechDatabase Build(TempDir tmp)
    {
        tmp.Write("g/common/technology/00_t.txt", Techs);
        tmp.Write("g/common/scripted_variables/00_v.txt", "@w = 95\n");
        tmp.Write("g/localisation/english/t_l_english.yml", Loc);
        using var source = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        return TechDatabase.Build([source]);
    }

    [Fact]
    public void Formats_stat_bonuses()
    {
        using var tmp = new TempDir();

        var bonuses = Build(tmp).Techs["tech_x"].Details.Bonuses;

        Assert.Equal(new[]
        {
            ("army_damage_mult", "+5%", "Army Damage", false),
            ("ship_speed_mult", "-10%", "Ship Speed", true),
            ("country_admin_cap_add", "+2", "country_admin_cap_add", false),
            ("weird_flag", "yes", "weird_flag", false),
        }, bonuses.Select(b => (b.Key, b.Display, b.Name, b.IsNegative)));
    }

    [Fact]
    public void Reads_unlock_text_flags_gateway_swaps_and_weight()
    {
        using var tmp = new TempDir();

        var d = Build(tmp).Techs["tech_x"].Details;

        Assert.Equal(new[] { ("ship", "Science Ship", (string?)"Build science ships"), ("custom", "Particle storm", null) },
            d.CustomUnlocks.Select(c => (c.Kind, c.Title, c.Description)));
        Assert.Equal(new[] { "unlocks_auto_research", "some_flag" }, d.FeatureFlags);
        Assert.Equal("energy_weapons", d.Gateway);
        Assert.Equal("95", d.Weight);
        var swap = Assert.Single(d.Swaps);
        Assert.Equal(("tech_y", "country_uses_bio_ships = yes", true), (swap.Name, swap.TriggerScript, swap.InheritsEffects));
    }

    [Fact]
    public void Prints_weights_conditions_and_raw_definition()
    {
        using var tmp = new TempDir();

        var d = Build(tmp).Techs["tech_x"].Details;

        Assert.Equal("modifier = {\n    factor = 1.25\n    has_ethic = ethic_militarist   # Militarist\n}", d.WeightModifierScript);
        Assert.Equal("weight = 2", d.AiWeightScript);
        Assert.Equal("always = yes", d.PotentialScript);
        Assert.StartsWith("tech_x = {\n    area = physics\n    weight = @w\n", d.RawScript);
    }

    [Fact]
    public void A_plain_tech_has_empty_details()
    {
        using var tmp = new TempDir();

        var d = Build(tmp).Techs["tech_y"].Details;

        Assert.Empty(d.Bonuses);
        Assert.Empty(d.CustomUnlocks);
        Assert.Null(d.Weight);
        Assert.Null(d.PotentialScript);
        Assert.Equal("tech_y = {\n    area = physics\n}", d.RawScript);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter TechDetailsTests`
Expected: build FAILS with `'Tech' does not contain a definition for 'Details'`.

- [ ] **Step 3: Add the parsed block to `TechDefinition`**

In `FazStellarisModmanager.Core/Technology/TechParser.cs`, add `PdxBlock Block` as the **last** positional parameter of the `TechDefinition` record. In `TechParser.Parse`, pass `b` as the last constructor argument.

- [ ] **Step 4: Implement `FazStellarisModmanager.Core/Technology/TechDetails.cs`**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>One entry of a tech's modifier block, e.g. army_damage_mult = 0.05 shown as "+5% Army Damage".</summary>
public sealed record StatBonus(string Key, string RawValue, string Name, string Display, bool IsNegative);

/// <summary>A prereqfor_desc line: Kind is the child block name (ship, custom, …).</summary>
public sealed record CustomUnlock(string Kind, string Title, string? Description);

public sealed record TechSwap(string Name, string? TriggerScript, bool InheritsEffects);

public sealed record TechDetails(
    IReadOnlyList<StatBonus> Bonuses,
    IReadOnlyList<CustomUnlock> CustomUnlocks,
    IReadOnlyList<string> FeatureFlags,
    string? Gateway,
    IReadOnlyList<TechSwap> Swaps,
    string? Weight,
    string? WeightModifierScript,
    string? AiWeightScript,
    string? PotentialScript,
    string RawScript);

public static class TechDetailsBuilder
{
    static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled);

    public static TechDetails Build(string key, PdxBlock block, Localisation loc,
        IReadOnlyDictionary<string, string> locals, IReadOnlyDictionary<string, string> globals)
    {
        // "# name" annotations: only for identifier-like values with a short localised name.
        string? Annotate(string v) =>
            Identifier.IsMatch(v) && v is not ("yes" or "no") && loc.Get(v) is { Length: > 0 and <= 60 } name ? name : null;

        string? Script(string name) => block.GetBlock(name) is { } b ? PdxScriptPrinter.Print(b, Annotate) : null;

        var bonuses = (block.GetBlock("modifier")?.Entries ?? [])
            .Where(e => e.Value is string)
            .Select(e => Bonus(e.Key, (string)e.Value, loc))
            .ToList();

        var custom = new List<CustomUnlock>();
        if (block.GetBlock("prereqfor_desc") is { } descs)
            foreach (var e in descs.Entries)
                if (e.Value is PdxBlock b)
                {
                    var title = b.GetString("title");
                    var desc = b.GetString("desc");
                    custom.Add(new CustomUnlock(e.Key, title is null ? e.Key : loc.Get(title) ?? title, desc is null ? null : loc.Get(desc) ?? desc));
                }

        var swaps = block.Entries
            .Where(e => e.Key.Equals("technology_swap", StringComparison.OrdinalIgnoreCase) && e.Value is PdxBlock)
            .Select(e => (PdxBlock)e.Value)
            .Select(b => new TechSwap(
                b.GetString("name") ?? "?",
                b.GetBlock("trigger") is { } t ? PdxScriptPrinter.Print(t, Annotate) : null,
                string.Equals(b.GetString("inherit_effects"), "yes", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new TechDetails(
            bonuses,
            custom,
            block.GetBlock("feature_flags")?.StringItems.ToList() ?? [],
            block.GetString("gateway"),
            swaps,
            block.GetString("weight") is { } w ? ScriptedVariables.Resolve(w, locals, globals) : null,
            Script("weight_modifier"),
            Script("ai_weight"),
            Script("potential"),
            PdxScriptPrinter.PrintNamed(key, block, Annotate));
    }

    /// <summary>_mult values show as signed percentages, _add values as signed numbers, anything else as written. Name from mod_KEY localisation.</summary>
    public static StatBonus Bonus(string key, string raw, Localisation loc)
    {
        var name = loc.Get("mod_" + key) ?? key;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return new StatBonus(key, raw, name, raw, false);
        var sign = v < 0 ? "-" : "+";
        var display = key.EndsWith("_mult", StringComparison.OrdinalIgnoreCase) ? sign + Number(Math.Abs(v) * 100) + "%"
            : key.EndsWith("_add", StringComparison.OrdinalIgnoreCase) ? sign + Number(Math.Abs(v))
            : raw;
        return new StatBonus(key, raw, name, display, v < 0);
    }

    static string Number(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 5: Wire the details into `FazStellarisModmanager.Core/Technology/TechDatabase.cs`**
  - Add `TechDetails Details` as the **last** positional parameter of the `Tech` record.
  - In `Build`, where each `Tech` is constructed, pass `TechDetailsBuilder.Build(def.Key, def.Block, loc, locals, globals)` as the last argument.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter TechDetailsTests`
Expected: `Passed!  - Failed: 0, Passed: 4`. Then run the full suite; everything passes.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Tech: details (bonuses, unlock text, flags, swaps, weights, scripts)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: SpriteIndex and IconResolver

**Files:**
- Create: `FazStellarisModmanager.Core/Technology/SpriteIndex.cs`, `FazStellarisModmanager.Core/Technology/IconResolver.cs`
- Test: `FazStellarisModmanager.Tests/SpriteIconTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class SpriteIconTests
{
    static SpriteIndex Sprites(TempDir tmp)
    {
        tmp.Write("g/interface/a.gfx", """
            spriteTypes = {
            	spriteType = { name = "GFX_a" texturefile = "gfx/a.dds" }
            	spriteType = { name = "GFX_sheet" textureFile = "gfx/sheet.dds" noOfFrames = 3 }
            	frameAnimatedSpriteType = { name = "GFX_anim" texturefile = "gfx/anim.dds" noOfFrames = 2 }
            	spriteType = { name = "GFX_building_capital" texturefile = "gfx/sprites/capital.dds" }
            }
            """);
        tmp.Write("m/interface/b.gfx", "spriteTypes = { spriteType = { name = \"GFX_a\" texturefile = \"gfx/mod_a.dds\" } }");
        using var g = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        using var m = ContentSource.FromPath("Mod", Path.Combine(tmp.Path, "m"));
        var warnings = new List<string>();
        var index = SpriteIndex.Build([g, m], warnings);
        Assert.Empty(warnings);
        return index;
    }

    [Fact]
    public void Indexes_sprites_case_insensitively_with_frames_and_later_sources_winning()
    {
        using var tmp = new TempDir();

        var s = Sprites(tmp);

        Assert.Equal(4, s.Count);
        Assert.Equal("gfx/mod_a.dds", s.Find("gfx_a")!.TextureFile);
        Assert.Equal(("gfx/sheet.dds", 3), (s.Find("GFX_sheet")!.TextureFile, s.Find("GFX_sheet")!.Frames));
        Assert.Equal(2, s.Find("GFX_anim")!.Frames);
        Assert.Equal(1, s.Find("GFX_a")!.Frames);
        Assert.Null(s.Find("GFX_missing"));
    }

    [Fact]
    public void Resolves_unlock_icons_by_each_rule()
    {
        using var tmp = new TempDir();
        var s = Sprites(tmp);
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gfx/interface/icons/buildings/building_lab.dds",
            "gfx/interface/icons/decisions/decision_resources.dds",
        };
        bool Exists(string p) => existing.Contains(p);

        Assert.Equal(new IconRef("gfx/sheet.dds", 2, 3), IconResolver.ForUnlock("GFX_sheet", 2, "x", "id", s, Exists));
        Assert.Equal(new IconRef("gfx/sheet.dds", 1, 3), IconResolver.ForUnlock("GFX_sheet", null, "x", "id", s, Exists));
        Assert.Equal(new IconRef("gfx/sheet.dds", 3, 3), IconResolver.ForUnlock("GFX_sheet", 9, "x", "id", s, Exists));
        Assert.Null(IconResolver.ForUnlock("GFX_missing", null, "x", "id", s, Exists));
        Assert.Equal(new IconRef("gfx/interface/icons/traits/t.dds"), IconResolver.ForUnlock("gfx/interface/icons/traits/t.dds", null, "traits", "t", s, Exists));
        Assert.Equal(new IconRef("gfx/sprites/capital.dds"), IconResolver.ForUnlock("building_capital", null, "buildings", "b", s, Exists));
        Assert.Equal(new IconRef("gfx/interface/icons/decisions/decision_resources.dds"), IconResolver.ForUnlock("decision_resources", null, "decisions", "d", s, Exists));
        Assert.Equal(new IconRef("gfx/interface/icons/buildings/building_lab.dds"), IconResolver.ForUnlock(null, null, "buildings", "building_lab", s, Exists));
        Assert.Null(IconResolver.ForUnlock(null, null, "buildings", "building_none", s, Exists));
    }

    [Fact]
    public void Resolves_bonus_and_tech_icons()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gfx/interface/icons/modifiers/mod_a_mult.dds",
            "gfx/interface/icons/modifiers/mod_a_mult_negative.dds",
            "gfx/interface/icons/modifiers/mod_b_add.dds",
        };
        bool Exists(string p) => existing.Contains(p);
        static StatBonus B(string key, bool negative) => new(key, "1", key, "+1", negative);

        Assert.Equal(new IconRef("gfx/interface/icons/modifiers/mod_a_mult_negative.dds"), IconResolver.ForBonus(B("a_mult", true), Exists));
        Assert.Equal(new IconRef("gfx/interface/icons/modifiers/mod_a_mult.dds"), IconResolver.ForBonus(B("a_mult", false), Exists));
        Assert.Equal(new IconRef("gfx/interface/icons/modifiers/mod_b_add.dds"), IconResolver.ForBonus(B("b_add", true), Exists));
        Assert.Null(IconResolver.ForBonus(B("c_mult", false), Exists));
        Assert.Equal(new IconRef("gfx/interface/icons/technologies/tech_a.dds"), IconResolver.ForTech("tech_a"));
        Assert.Equal("gfx/sheet.dds#2/3", new IconRef("gfx/sheet.dds", 2, 3).CacheId);
        Assert.Equal("gfx/a.dds", new IconRef("gfx/a.dds").CacheId);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter SpriteIconTests`
Expected: build FAILS with `The type or namespace name 'SpriteIndex' could not be found`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/Technology/SpriteIndex.cs`**

```csharp
using System.Globalization;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

public sealed record SpriteInfo(string Name, string TextureFile, int Frames);

/// <summary>GFX sprite name -> texture file (and sprite-sheet frame count), from interface/**/*.gfx. Later sources win per name.</summary>
public sealed class SpriteIndex
{
    readonly Dictionary<string, SpriteInfo> _sprites;

    SpriteIndex(Dictionary<string, SpriteInfo> sprites) => _sprites = sprites;

    public static SpriteIndex Empty { get; } = new(new Dictionary<string, SpriteInfo>(StringComparer.OrdinalIgnoreCase));

    public int Count => _sprites.Count;

    public SpriteInfo? Find(string name) => _sprites.TryGetValue(name, out var s) ? s : null;

    public static SpriteIndex Build(IReadOnlyList<ContentSource> sources, ICollection<string> warnings, CancellationToken ct = default)
    {
        var map = new Dictionary<string, SpriteInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            foreach (var rel in source.Files("interface", ".gfx").Order(TechDatabase.LoadOrder))
            {
                ct.ThrowIfCancellationRequested();
                string text;
                try { text = source.ReadText(rel); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
                {
                    warnings.Add($"{source.Name}: {rel}: {ex.Message}");
                    continue;
                }
                if (!text.Contains("spriteType", StringComparison.OrdinalIgnoreCase)) continue;
                Collect(ParadoxScriptParser.Parse(text), map);
            }
        return new SpriteIndex(map);
    }

    static void Collect(PdxBlock block, Dictionary<string, SpriteInfo> map)
    {
        foreach (var e in block.Entries)
        {
            if (e.Value is not PdxBlock b) continue;
            if (e.Key.Equals("spriteType", StringComparison.OrdinalIgnoreCase) || e.Key.Equals("frameAnimatedSpriteType", StringComparison.OrdinalIgnoreCase))
            {
                if (b.GetString("name") is { } name && b.GetString("texturefile") is { } texture)
                {
                    var frames = int.TryParse(b.GetString("noOfFrames"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1;
                    map[name] = new SpriteInfo(name, texture.Replace('\\', '/'), frames);
                }
            }
            else
            {
                Collect(b, map);
            }
        }
    }
}
```

- [ ] **Step 4: Implement `FazStellarisModmanager.Core/Technology/IconResolver.cs`**

```csharp
namespace FazStellarisModmanager.Core.Technology;

/// <summary>A texture to show: relative path plus 1-based frame of a horizontal sprite sheet with <see cref="Frames"/> frames.</summary>
public sealed record IconRef(string Path, int Frame = 1, int Frames = 1)
{
    public string CacheId => Frames > 1 ? $"{Path}#{Frame}/{Frames}" : Path;
}

/// <summary>Turns icon fields (unlock objects) and modifier keys (stat bonuses) into texture references.</summary>
public static class IconResolver
{
    const string Icons = "gfx/interface/icons";

    public static IconRef ForTech(string iconKey) => new($"{IconCache.IconFolder}/{iconKey}.dds");

    /// <summary>
    /// 1) "GFX_…" -> sprite (frame = icon_frame, default 1, clamped). 2) a path ("/" or ".dds") as written.
    /// 3) a bare name -> sprite GFX_name, else icons/&lt;kind folder&gt;/name.dds. 4) no icon -> icons/&lt;kind folder&gt;/&lt;id&gt;.dds.
    /// </summary>
    public static IconRef? ForUnlock(string? icon, int? iconFrame, string kindFolder, string id, SpriteIndex sprites, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(icon)) return Existing($"{Icons}/{kindFolder}/{id}.dds", exists);
        icon = icon.Trim();
        if (icon.StartsWith("GFX_", StringComparison.OrdinalIgnoreCase)) return FromSprite(sprites.Find(icon), iconFrame);
        if (icon.Contains('/') || icon.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) return new IconRef(icon.Replace('\\', '/'));
        return FromSprite(sprites.Find("GFX_" + icon), iconFrame) ?? Existing($"{Icons}/{kindFolder}/{icon}.dds", exists);
    }

    /// <summary>icons/modifiers/mod_&lt;key&gt;.dds, or its _negative variant for a negative value when that file exists.</summary>
    public static IconRef? ForBonus(StatBonus bonus, Func<string, bool> exists) =>
        (bonus.IsNegative ? Existing($"{Icons}/modifiers/mod_{bonus.Key}_negative.dds", exists) : null)
        ?? Existing($"{Icons}/modifiers/mod_{bonus.Key}.dds", exists);

    static IconRef? FromSprite(SpriteInfo? sprite, int? frame) =>
        sprite is null ? null : new IconRef(sprite.TextureFile, Math.Clamp(frame ?? 1, 1, sprite.Frames), sprite.Frames);

    static IconRef? Existing(string path, Func<string, bool> exists) => exists(path) ? new IconRef(path) : null;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter SpriteIconTests`
Expected: `Passed!  - Failed: 0, Passed: 3`

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Tech: GFX sprite index and icon resolver

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: IconCache for any IconRef, with frame cropping

**Files:**
- Modify: `FazStellarisModmanager.Core/Technology/IconCache.cs`
- Test: add to `FazStellarisModmanager.Tests/IconCacheTests.cs`

- [ ] **Step 1: Write the failing tests**

Append these tests inside the class in `FazStellarisModmanager.Tests/IconCacheTests.cs`. The `using` directives the file needs are `System.Buffers.Binary`, `FazStellarisModmanager.Core.Technology` and `FazStellarisModmanager.Tests.TestUtil`; add any that are missing.

```csharp
    static int PngWidth(string dataUri) =>
        BinaryPrimitives.ReadInt32BigEndian(Convert.FromBase64String(dataUri["data:image/png;base64,".Length..]).AsSpan(16));

    [Fact]
    public void Crops_sprite_sheet_frames()
    {
        using var tmp = new TempDir();
        var root = tmp.Mkdir("g");
        Directory.CreateDirectory(Path.Combine(root, "gfx"));
        File.WriteAllBytes(Path.Combine(root, "gfx", "sheet.dds"),
            DdsBuilder.Bgra32(4, 1, (255, 0, 0, 255), (255, 0, 0, 255), (0, 0, 255, 255), (0, 0, 255, 255)));
        using var s = ContentSource.FromPath("g", root);
        var cache = new IconCache(Path.Combine(tmp.Path, "cache"));

        var first = cache.DataUri([s], new IconRef("gfx/sheet.dds", 1, 2));
        var second = cache.DataUri([s], new IconRef("gfx/sheet.dds", 2, 2));

        Assert.Equal(2, PngWidth(first!));
        Assert.Equal(2, PngWidth(second!));
        Assert.NotEqual(first, second);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(tmp.Path, "cache"), "*.png").Length);
    }

    [Theory]
    [InlineData("../evil.dds")]
    [InlineData("gfx/x.png")]
    [InlineData("C:/x.dds")]
    public void Rejects_unsafe_or_non_dds_paths(string path)
    {
        using var tmp = new TempDir();
        using var s = ContentSource.FromPath("g", tmp.Mkdir("g"));

        Assert.Null(new IconCache(Path.Combine(tmp.Path, "cache")).DataUri([s], new IconRef(path)));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter IconCacheTests`
Expected: build FAILS with an error that no `DataUri` overload takes an `IconRef`.

- [ ] **Step 3: Generalise `FazStellarisModmanager.Core/Technology/IconCache.cs`**

1. Replace the body of `DataUri(IReadOnlyList<ContentSource> sources, string iconKey)`. Keep its existing key validation, then `return DataUri(sources, IconResolver.ForTech(iconKey));`.
2. Add the general overload, and change `Load` so it receives the `IconRef`:

```csharp
    /// <summary>"data:image/png;base64,…" for the texture (cropped to its frame), or null when no source has it, the path is unsafe/not .dds, or it can't be decoded.</summary>
    public string? DataUri(IReadOnlyList<ContentSource> sources, IconRef icon)
    {
        var rel = icon.Path.Replace('\\', '/');
        if (!rel.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || rel.StartsWith('/') || rel.Contains(':') || rel.Split('/').Contains("..")) return null;
        try
        {
            for (var i = sources.Count - 1; i >= 0; i--)
            {
                var source = sources[i];
                if (!source.Exists(rel)) continue;
                var id = $"{source.Name}|{icon.CacheId}|{source.Stamp(rel)}";
                return Load(source, rel, icon, id);
            }
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
```

3. In `Load(ContentSource source, string rel, IconRef icon, string id)`, right after `var (w, h, rgba) = DdsDecoder.Decode(dds);`, crop to the frame before the downscale:

```csharp
                if (icon.Frames > 1 && w >= icon.Frames) (w, h, rgba) = CropFrame(w, h, rgba, icon.Frame, icon.Frames);
```

4. Add the helper:

```csharp
    /// <summary>Frame f (1-based) of a horizontal strip of n frames: columns [(f-1)·w/n, f·w/n).</summary>
    static (int W, int H, byte[] Rgba) CropFrame(int w, int h, byte[] rgba, int frame, int frames)
    {
        var fw = w / frames;
        var x0 = (Math.Clamp(frame, 1, frames) - 1) * fw;
        var result = new byte[fw * h * 4];
        for (int y = 0; y < h; y++)
            Array.Copy(rgba, (y * w + x0) * 4, result, y * fw * 4, fw * 4);
        return (fw, h, result);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter IconCacheTests`
Expected: all IconCacheTests pass (the existing ones plus 4 new cases). Then run the full suite.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Tech: icon cache for any texture ref, with sprite-sheet frames

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: UnlockScanner and database wiring

**Files:**
- Create: `FazStellarisModmanager.Core/Technology/UnlockScanner.cs`
- Modify: `FazStellarisModmanager.Core/Technology/TechDatabase.cs` (adds `Unlocks(key)`, `AllUnlocks` and `Sprites`, built in `Build`)
- Test: `FazStellarisModmanager.Tests/UnlockScannerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class UnlockScannerTests
{
    static TechDatabase Build(TempDir tmp)
    {
        tmp.Write("g/common/technology/00_t.txt", "tech_lasers_2 = { area = physics }\ntech_x = { area = physics }\ntech_lasers_3 = { area = physics prerequisites = { \"tech_lasers_2\" } }\n");
        tmp.Write("g/common/component_templates/00_w.txt", """
            weapon_component_template = {
            	key = "SMALL_BLUE_LASER"
            	icon = "GFX_ship_part_laser_1"
            	icon_frame = 2
            	prerequisites = { "tech_lasers_2" }
            }
            weapon_component_template = {
            	key = "OTHER"
            	prerequisites = { "tech_other" }
            }
            """);
        tmp.Write("g/common/buildings/00_b.txt", "building_lab = { prerequisites = { \"tech_lasers_2\" \"tech_x\" } }\nbuilding_plain = { potential = { always = yes } }\n");
        tmp.Write("g/common/starbase_modules/00_s.txt", "module_x = { show_in_tech = \"tech_lasers_2\" }\n");
        tmp.Write("g/common/inline_scripts/x.txt", "y = { prerequisites = { \"tech_lasers_2\" } }\n");
        tmp.Write("g/common/zones/00_z.txt", "zone_cap = { prerequisites = { \"tech_lasers_2\" } }\n");
        tmp.Write("g/common/my_custom_things/00.txt", "thing = { prerequisites = { \"tech_lasers_2\" } }\n");
        tmp.Write("g/interface/ships.gfx", "spriteTypes = { spriteType = { name = \"GFX_ship_part_laser_1\" texturefile = \"gfx/l.dds\" noOfFrames = 3 } }\n");
        tmp.Write("g/localisation/english/c_l_english.yml", "l_english:\n SMALL_BLUE_LASER:0 \"Small Blue Laser\"\n building_lab:0 \"Research Lab\"\n");
        tmp.Write("m/common/buildings/zz_b.txt", "building_lab = { prerequisites = { \"tech_x\" } }\n");
        using var g = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        using var m = ContentSource.FromPath("Mod", Path.Combine(tmp.Path, "m"));
        return TechDatabase.Build([g, m]);
    }

    [Fact]
    public void Finds_unlocks_by_prerequisites_show_in_tech_and_key_with_overrides()
    {
        using var tmp = new TempDir();

        var db = Build(tmp);

        var lasers = db.Unlocks("tech_lasers_2");
        Assert.Equal(new[]
        {
            ("My custom things", "thing"),
            ("Ship components", "SMALL_BLUE_LASER"),
            ("Starbase modules", "module_x"),
            ("Zones", "zone_cap"),
        }, lasers.Select(u => (u.Kind, u.Id)));
        var laser = lasers.Single(u => u.Id == "SMALL_BLUE_LASER");
        Assert.Equal(("Small Blue Laser", "component_templates", "GFX_ship_part_laser_1", (int?)2, "Base game"),
            (laser.Name, laser.KindFolder, laser.Icon, laser.IconFrame, laser.Source.SourceName));

        var lab = Assert.Single(db.Unlocks("TECH_X"));
        Assert.Equal(("Research Lab", "Mod", "common/buildings/zz_b.txt"), (lab.Name, lab.Source.SourceName, lab.Source.File));

        Assert.Empty(db.Unlocks("tech_nothing"));
        Assert.Equal(6, db.AllUnlocks.Count); // thing, SMALL_BLUE_LASER, OTHER, module_x, zone_cap, building_lab
        Assert.Equal(1, db.Sprites.Count);
    }

    [Theory]
    [InlineData("component_templates", "Ship components")]
    [InlineData("bypass", "Bypasses")]
    [InlineData("some_new_thing", "Some new thing")]
    public void Names_kinds(string folder, string expected) =>
        Assert.Equal(expected, UnlockScanner.KindName(folder));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter UnlockScannerTests`
Expected: build FAILS with `'TechDatabase' does not contain a definition for 'Unlocks'`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/Technology/UnlockScanner.cs`**

```csharp
using System.Globalization;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>Something in common/ that requires a tech. KindFolder is the common/ subfolder; Id is the block's key = "…" or its name.</summary>
public sealed record Unlock(string Kind, string KindFolder, string Id, string Name, TechSourceRef Source, string? Icon, int? IconFrame);

public static class UnlockScanner
{
    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "technology", "scripted_variables", "inline_scripts" };

    static readonly Dictionary<string, string> KindNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["component_templates"] = "Ship components",
        ["buildings"] = "Buildings",
        ["ship_sizes"] = "Ship sizes",
        ["section_templates"] = "Ship sections",
        ["starbase_buildings"] = "Starbase buildings",
        ["starbase_modules"] = "Starbase modules",
        ["megastructures"] = "Megastructures",
        ["edicts"] = "Edicts",
        ["districts"] = "Districts",
        ["armies"] = "Armies",
        ["traits"] = "Traits",
        ["deposits"] = "Deposits",
        ["decisions"] = "Decisions",
        ["policies"] = "Policies",
        ["zones"] = "Zones",
        ["strategic_resources"] = "Strategic resources",
        ["bypass"] = "Bypasses",
    };

    public static string KindName(string folder)
    {
        if (KindNames.TryGetValue(folder, out var name)) return name;
        var words = folder.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return folder;
        words[0] = char.ToUpperInvariant(words[0][0]) + words[0][1..];
        return string.Join(' ', words);
    }

    /// <summary>
    /// Tech key -> unlocks (sorted by kind, then name). Same override rules as techs: a later source's file at the same path
    /// replaces the earlier one, then objects are read in load order and the last (folder, id) definition wins.
    /// Only files mentioning "prerequisites" or "show_in_tech" are parsed.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<Unlock>> Scan(IReadOnlyList<ContentSource> sources, Localisation loc,
        ICollection<string> warnings, CancellationToken ct = default)
    {
        var files = new Dictionary<string, (ContentSource Source, string Rel)>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            foreach (var rel in source.Files("common", ".txt"))
            {
                var parts = rel.Split('/');
                if (parts.Length < 3 || Excluded.Contains(parts[1])) continue;
                files[rel] = (source, rel);
            }

        var objects = new Dictionary<string, (string Folder, string Id, string[] Techs, TechSourceRef Src, string? Icon, int? Frame)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, rel) in files.Values.OrderBy(f => f.Rel, TechDatabase.LoadOrder))
        {
            ct.ThrowIfCancellationRequested();
            string text;
            try { text = source.ReadText(rel); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
            {
                warnings.Add($"{source.Name}: {rel}: {ex.Message}");
                continue;
            }
            if (!text.Contains("prerequisites", StringComparison.OrdinalIgnoreCase) && !text.Contains("show_in_tech", StringComparison.OrdinalIgnoreCase)) continue;

            var folder = rel.Split('/')[1];
            var src = new TechSourceRef(source.Name, source.IsBaseGame, rel);
            foreach (var e in ParadoxScriptParser.Parse(text).Entries)
            {
                if (e.Value is not PdxBlock b || e.Key.StartsWith('@')) continue;
                var id = b.GetString("key") ?? e.Key;
                IEnumerable<string> shown = b.GetString("show_in_tech") is { } sit ? [sit] : [];
                var techs = (b.GetBlock("prerequisites")?.StringItems ?? []).Concat(shown).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var frame = int.TryParse(b.GetString("icon_frame"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) ? f : (int?)null;
                objects[folder + "|" + id] = (folder, id, techs, src, b.GetString("icon"), frame);
            }
        }

        var index = new Dictionary<string, List<Unlock>>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in objects.Values)
            foreach (var tech in o.Techs)
            {
                if (!index.TryGetValue(tech, out var list)) index[tech] = list = [];
                list.Add(new Unlock(KindName(o.Folder), o.Folder, o.Id, loc.Get(o.Id) ?? o.Id, o.Src, o.Icon, o.Frame));
            }

        return index.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<Unlock>)kv.Value
                .OrderBy(u => u.Kind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Wire the scanner into `FazStellarisModmanager.Core/Technology/TechDatabase.cs`**
  - Extend the private constructor with `IReadOnlyDictionary<string, IReadOnlyList<Unlock>> unlocks, SpriteIndex sprites`.
  - Store them, and add these members:

```csharp
    /// <summary>Things in common/ that require the tech (excluding other techs), sorted by kind then name.</summary>
    public IReadOnlyList<Unlock> Unlocks(string techKey) => _unlocks.TryGetValue(techKey, out var list) ? list : [];

    /// <summary>Every distinct unlock object (an object unlocked by several techs appears once).</summary>
    public IReadOnlyList<Unlock> AllUnlocks { get; }

    public SpriteIndex Sprites { get; }
```

  - Compute `AllUnlocks` in the constructor:
    `AllUnlocks = unlocks.Values.SelectMany(l => l).DistinctBy(u => u.KindFolder + "|" + u.Id, StringComparer.OrdinalIgnoreCase).ToList();`
  - In `Build`, after `var loc = Localisation.Load(sources, warnings);`, add:

```csharp
        progress?.Report("Finding what each technology unlocks…");
        var unlocks = UnlockScanner.Scan(sources, loc, warnings, ct);
        progress?.Report("Reading sprite definitions…");
        var sprites = SpriteIndex.Build(sources, warnings, ct);
```

  - Pass both values to the constructor in the `return`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter UnlockScannerTests`
Expected: `Passed!  - Failed: 0, Passed: 4`. Then run the full suite.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Tech: scan what each technology unlocks across common/

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Prewarm unlock and bonus icons

**Files:**
- Modify: `FazStellarisModmanager.Core/Technology/TechTreeService.cs`
- Test: add to `FazStellarisModmanager.Tests/TechTreeServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Append this test to `TechTreeServiceTests`. It builds its own FakeInstall content; read the file's existing `Create()` helper first and follow its pattern.

```csharp
    [Fact]
    public async Task Prewarms_unlock_and_bonus_icons()
    {
        var fake = new FakeInstall();
        using var _cleanup = fake;
        fake.Write("lib/steamapps/common/Stellaris/common/technology/00_t.txt", "tech_a = { area = physics start_tech = yes modifier = { army_damage_mult = 0.05 } }\n");
        fake.Write("lib/steamapps/common/Stellaris/common/component_templates/00_c.txt",
            "weapon_component_template = { key = \"LASER_X\" icon = \"GFX_laser_x\" icon_frame = 2 prerequisites = { \"tech_a\" } }\n");
        fake.Write("lib/steamapps/common/Stellaris/interface/x.gfx",
            "spriteTypes = { spriteType = { name = \"GFX_laser_x\" texturefile = \"gfx/interface/icons/ship_parts/laser.dds\" noOfFrames = 2 } }\n");
        void Dds(string rel, byte[] bytes)
        {
            var path = Path.Combine(fake.GameDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        Dds("gfx/interface/icons/ship_parts/laser.dds", DdsBuilder.Bgra32(2, 1, (1, 1, 1, 255), (2, 2, 2, 255)));
        Dds("gfx/interface/icons/modifiers/mod_army_damage_mult.dds", DdsBuilder.Bgra32(1, 1, (3, 3, 3, 255)));
        var paths = new AppPaths(fake.DataDir);
        SettingsStore.Save(paths.Settings, new AppSettings(UserDir: fake.UserDir));
        var tree = new TechTreeService(new ModManagerService(paths, _ => fake.GameDir));

        await tree.BuildAsync(tree.Choices()[0]);
        await tree.IconsReady;

        var db = tree.Current!.Database;
        var unlock = Assert.Single(db.Unlocks("tech_a"));
        Assert.StartsWith("data:image/png;base64,", tree.UnlockIconUri(unlock));
        var bonus = Assert.Single(db.Techs["tech_a"].Details.Bonuses);
        Assert.StartsWith("data:image/png;base64,", tree.BonusIconUri(bonus));
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter TechTreeServiceTests`
Expected: build FAILS with `'TechTreeService' does not contain a definition for 'UnlockIconUri'`.

- [ ] **Step 3: Extend `FazStellarisModmanager.Core/Technology/TechTreeService.cs`**

Add the lookups next to `IconUri`:

```csharp
    /// <summary>PNG data URI for an unlock's icon in the current tree, or null (memo lookup, no IO).</summary>
    public string? UnlockIconUri(Unlock unlock) => _current?.Icons.TryGetValue(UnlockKey(unlock), out var u) == true ? u : null;

    /// <summary>PNG data URI for a stat bonus icon in the current tree, or null (memo lookup, no IO).</summary>
    public string? BonusIconUri(StatBonus bonus) => _current?.Icons.TryGetValue(BonusKey(bonus), out var u) == true ? u : null;

    static string UnlockKey(Unlock u) => $"u:{u.KindFolder}/{u.Id}";

    static string BonusKey(StatBonus b) => b.IsNegative ? $"m:{b.Key}:neg" : $"m:{b.Key}";
```

Replace the body of `PrewarmAsync` so that it:
- builds one ordered work list (tech icons, then unlock icons, then bonus icons);
- resolves each `IconRef` inside the parallel loop, since resolution checks file existence;
- stores only non-null URIs.

Keep the existing cancellation, progress text format and final-report behaviour.

```csharp
    async Task PrewarmAsync(TechTree tree, CancellationToken ct)
    {
        try
        {
            var db = tree.Database;
            bool Exists(string path)
            {
                foreach (var s in tree.Sources)
                {
                    try { if (s.Exists(path)) return true; }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { }
                }
                return false;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var work = new List<(string Key, Func<IconRef?> Resolve)>();
            foreach (var t in db.Techs.Values)
                if (!string.IsNullOrEmpty(t.IconKey) && seen.Add(t.IconKey))
                {
                    var key = t.IconKey;
                    work.Add((key, () => IconResolver.ForTech(key)));
                }
            foreach (var u in db.AllUnlocks)
                if (seen.Add(UnlockKey(u)))
                {
                    var unlock = u;
                    work.Add((UnlockKey(u), () => IconResolver.ForUnlock(unlock.Icon, unlock.IconFrame, unlock.KindFolder, unlock.Id, db.Sprites, Exists)));
                }
            foreach (var b in db.Techs.Values.SelectMany(t => t.Details.Bonuses))
                if (seen.Add(BonusKey(b)))
                {
                    var bonus = b;
                    work.Add((BonusKey(b), () => IconResolver.ForBonus(bonus, Exists)));
                }

            var done = 0;
            await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, (item, token) =>
            {
                var icon = item.Resolve();
                var uri = icon is null ? null : _icons.DataUri(tree.Sources, icon);
                if (uri is not null) tree.Icons[item.Key] = uri;
                Report($"Loading icons… {Interlocked.Increment(ref done)}/{work.Count}", force: false);
                return ValueTask.CompletedTask;
            });
        }
        catch (OperationCanceledException) { return; }
        finally
        {
            if (!ct.IsCancellationRequested) Report(null, force: true);
        }
    }
```

Tech icon keys go through `IconResolver.ForTech`, and `IconCache.DataUri(sources, IconRef)` rejects unsafe paths (including `..`), so the old string-key validation is still enforced.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter TechTreeServiceTests`
Expected: all pass. Then run the full suite twice.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Tech: prewarm unlock and stat bonus icons per tree

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Sidebar UI

**Files:**
- Create: `FazStellarisModmanager/Components/ScriptView.razor`, `FazStellarisModmanager/Components/TechDetails.razor`
- Modify: `FazStellarisModmanager/Pages/TechPage.razor`, `FazStellarisModmanager/wwwroot/css/site.css`

- [ ] **Step 1: Create `FazStellarisModmanager/Components/ScriptView.razor`**

```razor
@using FazStellarisModmanager.Core.Descriptors

<div class="script">
    @foreach (var line in Text.Split('\n'))
    {
        <div class="sl">@foreach (var t in ScriptHighlighter.Line(line)) { <span class="@Css(t.Kind)">@t.Text</span> }</div>
    }
</div>

@code {
    [Parameter, EditorRequired] public string Text { get; set; } = "";

    static string Css(ScriptTokenKind kind) => kind switch
    {
        ScriptTokenKind.Key => "sk",
        ScriptTokenKind.Operator => "so",
        ScriptTokenKind.Value => "sv",
        ScriptTokenKind.Comment => "sc",
        _ => "",
    };
}
```

- [ ] **Step 2: Create `FazStellarisModmanager/Components/TechDetails.razor`**

```razor
@using System.Globalization

<div class="tech-sidebar-card">
    <div class="sb-head">
        <TechIcon Uri="@Tree.IconUri(Tech)" Size="52" />
        <div class="grow">
            <div class="title">@Tech.Name</div>
            <div class="muted small">@Tech.Key</div>
        </div>
    </div>
    <div class="tags">
        <span class="tag">@Tech.Area</span>
        @if (Tech.Category is not null) { <span class="tag">@Tech.Category</span> }
        <span class="tag">Tier @TierText</span>
        <span class="tag">Cost @Tech.Cost</span>
        @if (Details.Weight is not null) { <span class="tag">Weight @Details.Weight</span> }
        @if (Details.Gateway is not null) { <span class="tag">Gateway: @Details.Gateway</span> }
        @if (Tech.IsStart) { <span class="tag">Starting</span> }
        @if (Tech.IsRare) { <span class="tag rare">Rare</span> }
        @if (Tech.IsDangerous) { <span class="tag danger">Dangerous</span> }
        @if (Tech.IsRepeatable) { <span class="tag">Repeatable</span> }
        @foreach (var dlc in Tech.Dlcs) { <span class="tag dlc">DLC: @dlc</span> }
    </div>
    @if (Tech.Description is not null)
    {
        <p class="desc">@Tech.Description</p>
    }
    <div><span class="muted">Source:</span> <span class="@(Tech.ChangedByMods ? "modtext" : "")">@Tech.Source.SourceName</span> <span class="muted">· @Tech.Source.File</span></div>
    @if (Tech.Overridden.Count > 0)
    {
        <div><span class="muted">Overrides:</span> @string.Join(", ", Tech.Overridden.Select(o => $"{o.SourceName} ({Path.GetFileName(o.File)})"))</div>
    }
    <div class="path">
        <span class="muted">Path from start:</span>
        @{ var path = Database.PathFromStart(Tech.Key); }
        @for (int i = 0; i < path.Count; i++)
        {
            var step = path[i];
            if (i > 0) { <span class="muted"> → </span> }
            <a class="link" @onclick="() => OnSelect.InvokeAsync(step.Key)">@step.Name</a>
        }
    </div>

    <details open>
        <summary>Unlocks (@Unlocks.Count)</summary>
        @if (Unlocks.Count == 0)
        {
            <p class="muted small">Nothing in common/ requires this technology.</p>
        }
        @foreach (var group in Unlocks.GroupBy(u => u.Kind))
        {
            <h4>@group.Key (@group.Count())</h4>
            <div class="chips">
                @foreach (var u in group)
                {
                    <span class="uchip @(u.Source.IsBaseGame ? "" : "mod")" title="@($"{u.Id} · {u.Source.SourceName} · {u.Source.File}")">
                        <TechIcon Uri="@Tree.UnlockIconUri(u)" Size="18" />@u.Name
                    </span>
                }
            </div>
        }
    </details>

    <details open>
        <summary>Effects</summary>
        <h4>Stat bonuses</h4>
        @if (Details.Bonuses.Count == 0) { <p class="muted small">None.</p> }
        @foreach (var b in Details.Bonuses)
        {
            <div class="bonus">
                <TechIcon Uri="@Tree.BonusIconUri(b)" Size="18" />
                <span class="@(b.IsNegative ? "neg" : "pos")">@b.Display</span>
                <span>@b.Name</span>
                <span class="muted small">(@b.Key = @b.RawValue)</span>
            </div>
        }
        <h4>Custom unlock text</h4>
        @if (Details.CustomUnlocks.Count == 0) { <p class="muted small">None.</p> }
        @foreach (var c in Details.CustomUnlocks)
        {
            <div class="custom-unlock"><b>@c.Title</b> <span class="muted small">(@c.Kind)</span>
                @if (c.Description is not null) { <div class="muted">@c.Description</div> }
            </div>
        }
        <h4>Feature flags</h4>
        @if (Details.FeatureFlags.Count == 0) { <p class="muted small">None.</p> }
        else
        {
            <div class="chips">@foreach (var f in Details.FeatureFlags) { <span class="uchip">@f</span> }</div>
        }
        @if (Details.Swaps.Count > 0)
        {
            <h4>Technology swaps</h4>
            @foreach (var s in Details.Swaps)
            {
                <div class="swap">
                    Becomes
                    @if (Database.Techs.TryGetValue(s.Name, out var target))
                    {
                        var targetKey = target.Key;
                        <a class="link" @onclick="() => OnSelect.InvokeAsync(targetKey)">@target.Name</a>
                    }
                    else
                    {
                        <b>@s.Name</b>
                    }
                    @if (s.InheritsEffects) { <span class="muted small">(inherits effects)</span> }
                    @if (s.TriggerScript is not null) { <span class="muted small">when:</span><ScriptView Text="@s.TriggerScript" /> }
                </div>
            }
        }
    </details>

    <details>
        <summary>Weights</summary>
        <div><span class="muted">Base research weight:</span> @(Details.Weight ?? "default")</div>
        <h4>Research weight modifiers</h4>
        @if (Details.WeightModifierScript is null) { <p class="muted small">None.</p> } else { <ScriptView Text="@Details.WeightModifierScript" /> }
        <h4>AI weight</h4>
        @if (Details.AiWeightScript is null) { <p class="muted small">None.</p> } else { <ScriptView Text="@Details.AiWeightScript" /> }
    </details>

    <details>
        <summary>Conditions</summary>
        <h4>Available when</h4>
        @if (Details.PotentialScript is null) { <p class="muted small">Always.</p> } else { <ScriptView Text="@Details.PotentialScript" /> }
        <h4>Prerequisites</h4>
        @if (Tech.Prerequisites.Count == 0) { <p class="muted small">None.</p> }
        @foreach (var p in Tech.Prerequisites)
        {
            if (Database.Techs.TryGetValue(p, out var pre))
            {
                var preKey = pre.Key;
                <div><a class="link" @onclick="() => OnSelect.InvokeAsync(preKey)">@pre.Name</a></div>
            }
            else
            {
                <div class="muted">@p (not in this tree)</div>
            }
        }
    </details>

    <details>
        <summary>Raw definition</summary>
        <ScriptView Text="@Details.RawScript" />
    </details>
</div>

@code {
    [Parameter, EditorRequired] public Tech Tech { get; set; } = default!;
    [Parameter, EditorRequired] public TechDatabase Database { get; set; } = default!;
    [Parameter, EditorRequired] public TechTreeService Tree { get; set; } = default!;
    [Parameter] public EventCallback<string> OnSelect { get; set; }

    TechDetails Details => Tech.Details;
    IReadOnlyList<Unlock> Unlocks => Database.Unlocks(Tech.Key);
    string TierText => Tech.IsRepeatable ? "R" : Tech.Tier?.ToString(CultureInfo.InvariantCulture) ?? "?";
}
```

If the Razor compiler complains about a construct, for example `else` after a one-line `@if { }` block, rewrite it minimally with full braces on separate lines, keeping the same output.

- [ ] **Step 3: Make `FazStellarisModmanager/Pages/TechPage.razor` three columns**

Read the file first. Then:
1. In `.tech-focus`, **delete** the whole `<div class="tech-detail">…</div>` block that follows the `graph-scroll` div. Keep the `ghead` and the graph.
2. Directly after the closing `</div>` of `.tech-focus` (still inside `.tech-main`), add:

```razor
            <aside class="tech-sidebar">
                @if (selectedKey is not null && db.Techs.TryGetValue(selectedKey, out var side))
                {
                    <TechDetails Tech="side" Database="db" Tree="Tree" OnSelect="key => selectedKey = key" />
                }
                else
                {
                    <p class="status">Pick a technology to see its details.</p>
                }
            </aside>
```

3. Leave everything else as it is: list rows, `TierText` in `@code`, filters, debounce and so on.

- [ ] **Step 4: Update `FazStellarisModmanager/wwwroot/css/site.css`**

Change the `.tech-main` rule to three columns:

```css
.tech-main { display: grid; grid-template-columns: 280px minmax(0, 1fr) 380px; gap: .8rem; flex: 1; min-height: 0; }
```

Then append:

```css
.tech-sidebar { min-height: 0; overflow: auto; }
.tech-sidebar-card { display: flex; flex-direction: column; gap: .4rem; background: var(--panel); border: 1px solid var(--border); border-radius: 8px; padding: .7rem; }
.tech-sidebar-card .sb-head { display: flex; gap: .7rem; align-items: center; }
.tech-sidebar-card .title { font-size: 1.05rem; font-weight: 600; }
.tech-sidebar-card .tags { display: flex; gap: .3rem; flex-wrap: wrap; }
.tech-sidebar-card .desc { margin: .2rem 0; white-space: pre-line; }
.tech-sidebar-card details { border-top: 1px solid var(--border); padding-top: .4rem; }
.tech-sidebar-card summary { cursor: pointer; font-weight: 600; }
.tech-sidebar-card h4 { margin: .6rem 0 .25rem; font-size: .72rem; color: var(--muted); text-transform: uppercase; letter-spacing: .04em; }
.chips { display: flex; flex-wrap: wrap; gap: .3rem; }
.uchip { display: inline-flex; align-items: center; gap: .3rem; background: var(--panel-2); border: 1px solid var(--border); border-radius: 5px; padding: .1rem .45rem; font-size: .8rem; }
.uchip.mod { border-color: var(--warn); color: var(--warn); }
.bonus { display: flex; gap: .4rem; align-items: center; margin: .15rem 0; }
.bonus .pos { color: var(--ok); font-weight: 600; } .bonus .neg { color: var(--danger); font-weight: 600; }
.custom-unlock, .swap { margin: .2rem 0; }
.script { font-family: Consolas, monospace; font-size: .76rem; background: #0b0e13; border: 1px solid var(--border); border-radius: 5px; padding: .4rem .55rem; margin: .25rem 0; overflow-x: auto; }
.script .sl { white-space: pre; min-height: 1em; }
.script .sk { color: #9fd2ff; } .script .so { color: var(--muted); } .script .sv { color: #f5d142; } .script .sc { color: var(--muted); font-style: italic; }
```

The old `.tech-detail …` rules can stay; they become unused, which is harmless.

- [ ] **Step 5: Build, test and smoke-run**

Run: `dotnet build FazStellarisModmanager.sln -c Release`
Expected: 0 warnings, 0 errors.

Run: `dotnet test FazStellarisModmanager.Tests -c Release`
Expected: all tests pass.

Smoke-run:
1. Start `FazStellarisModmanager/bin/Release/net10.0-windows10.0.19041.0/FazStellarisModmanager.exe --data-dir <scratch dir>` in the background.
2. Wait 10 s and confirm it is alive.
3. Kill **only that instance**: find its Windows PID by its `--data-dir` command line, because `$!` is an MSYS PID. Never kill other instances; they are the user's.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Tech tab: details sidebar with unlocks, effects, weights, conditions, raw

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Manual check (human)

- [ ] **Step 1:** Open the Tech tab and pick **Blue Lasers**. The right sidebar should show:
  - **Unlocks:** Ship components (Small/Medium/Large Blue Laser, bio lasers and eye beams, with icons) and Zones (Laser Capacitors).
  - **Effects:** "+5% Army Damage" with its icon, and the tech swap to Bio-Lasers.
- [ ] **Step 2:** Expand **Weights**. You should see base 95 and the ×1.25/×1.5 militarist modifiers with "# Militarist" comments, plus the AI weight script.
- [ ] **Step 3:** Pick a tech with custom unlock text, e.g. Space Exploration (`tech_space_exploration`). It should show "Science Ship" text and feature flags.
- [ ] **Step 4:** Check that a modded tech or unlock shows its mod source, with orange chips.
- [ ] **Step 5:** Expand **Raw definition**. The full block should appear, indented and coloured.
- [ ] **Step 6:** Check the window still feels responsive while icons load. There are now thousands of unlock and bonus icons after the first build; later starts use the disk cache.

# Definition Conflicts Tab Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Conflicts tab that lists script definitions each mod in the edited list loses to other mods, with the reason. The file check also stops counting icon files.

**Architecture:**
- **Core:** a tokenizer (`DefinitionScanner`), a rule table (`DefinitionRules`), a pure analyzer (`DefinitionAnalyzer`), a cached scanner service over `ContentSource` (`DefinitionScanService`), and a shared edited list (`EditedModList`).
- **App:** the Mods page uses `EditedModList`; there is a new `ConflictsPage`.

**Tech Stack:** .NET 10, Blazor in BlazorWebView, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-04-definition-conflicts-design.md`

**Conventions:**
- Work in `C:\Users\SCP Fazbear\source\repos\FazStellarisModmanager` on branch `feature/mod-conflicts`.
- Build and test with `-c Release --artifacts-path <scratchpad>/art`. Never kill FazStellarisModmanager.exe.
- Don't use backslash escapes in C# strings. Use `(char)92` for a backslash and `(char)10` for a newline.
- Razor: inside `@if`/`@foreach`, don't use `<text>` elements that have attributes.

---

### Task 1: Ignore icons in the file check

**Files:** modify `FazStellarisModmanager.Core/Conflicts/ModFiles.cs` and `FazStellarisModmanager.Tests/ModFilesTests.cs`.

- [ ] **Step 1: Write the test.** Add it to ModFilesTests:

```csharp
    [Fact]
    public void Icons_are_not_content()
    {
        using var t = new TempDir();
        t.Write("m/gfx/interface/icons/resources/sr_a.dds", "x");
        t.Write("m/GFX/Interface/Icons/b.dds", "x");
        t.Write("m/gfx/models/portraits/x.dds", "x");
        t.Write("m/common/a.txt", "x");
        Assert.Equal(["common/a.txt", "gfx/models/portraits/x.dds"], ModFiles.List(Path.Combine(t.Path, "m")));
    }
```

- [ ] **Step 2: Run it and confirm it fails.**

- [ ] **Step 3: Implement.** In `ModFiles.List`, in the loop, after the existing `continue` check, add:
```csharp
            if (p.StartsWith(IconFolder, StringComparison.OrdinalIgnoreCase)) continue;
```
Add the constant to the class, with a doc comment:
```csharp
    /// <summary>Icons (resources, techs, buildings, …) are not counted: overriding them is cosmetic.</summary>
    public const string IconFolder = "gfx/interface/icons/";
```
Update the class summary to say that icons are excluded.

- [ ] **Step 4: Run the full suite.** It should PASS.
- [ ] **Step 5: Commit.** Message: `"feat: file conflict check ignores icons"`.

---

### Task 2: DefinitionScanner and DefinitionRules

**Files:**
- Create `FazStellarisModmanager.Core/Conflicts/DefinitionScanner.cs`.
- Create `FazStellarisModmanager.Core/Conflicts/DefinitionRules.cs`.
- Test: `FazStellarisModmanager.Tests/DefinitionScannerTests.cs`.

- [ ] **Step 1: Write the tests**

```csharp
using FazStellarisModmanager.Core.Conflicts;

namespace FazStellarisModmanager.Tests;

public class DefinitionScannerTests
{
    static string L(params string[] lines) => string.Join((char)10, lines);

    [Fact]
    public void Top_level_keys_only()
    {
        var text = L(
            "# comment = { }",
            "@cost = 5",
            "building_a = {",
            "    potential = { has_tech = x }",
            "    desc = \"has { braces } and # hash\"",
            "}",
            "building_b = { cost = { minerals >= 3 } }",
            "simple = yes");
        Assert.Equal(["@cost", "building_a", "building_b", "simple"], DefinitionScanner.Names(text, DefinitionKind.TopLevel));
    }

    [Fact]
    public void Defines_are_category_dot_key()
    {
        var text = L("NGameplay = {", "  MARKET_FEE = 0.3", "  LIST = { 1 2 3 }", "}", "NAI = { X = 1 }");
        Assert.Equal(["NGameplay.MARKET_FEE", "NGameplay.LIST", "NAI.X"], DefinitionScanner.Names(text, DefinitionKind.Defines));
    }

    [Fact]
    public void Events_are_ids()
    {
        var text = L(
            "namespace = gme",
            "country_event = {",
            "    id = gme.1",
            "    option = { id = not_this }",
            "}",
            "event = { id = \"gme.2\" hide_window = yes }",
            "planet_event = { is_triggered_only = yes id = gme.3 }",
            "something_else = { id = nope }");
        Assert.Equal(["gme.1", "gme.2", "gme.3"], DefinitionScanner.Names(text, DefinitionKind.Events));
    }

    [Fact]
    public void Unbalanced_braces_do_not_throw()
    {
        Assert.Equal(["a"], DefinitionScanner.Names("a = { } } }", DefinitionKind.TopLevel));
        Assert.Equal(["a"], DefinitionScanner.Names("a = { b = {", DefinitionKind.TopLevel));
    }

    [Theory]
    [InlineData("common/buildings/00_buildings.txt", "common/buildings", DefinitionKind.TopLevel)]
    [InlineData("Common/Defines/zz_defines.txt", "common/defines", DefinitionKind.Defines)]
    [InlineData("events/gme_events.txt", "events", DefinitionKind.Events)]
    [InlineData("common/buildings/sub/x.txt", "common/buildings", DefinitionKind.TopLevel)]
    public void Classify_scanned_files(string path, string folder, DefinitionKind kind)
    {
        Assert.Equal((folder, kind), DefinitionRules.Classify(path));
    }

    [Theory]
    [InlineData("common/on_actions/x.txt")]
    [InlineData("common/inline_scripts/a/b.txt")]
    [InlineData("common/buildings/readme.md")]
    [InlineData("common/x.txt")]
    [InlineData("localisation/english/x.yml")]
    [InlineData("descriptor.mod")]
    public void Classify_skips_other_files(string path) => Assert.Null(DefinitionRules.Classify(path));

    [Fact]
    public void Rules()
    {
        Assert.Equal(OverrideRule.LastWins, DefinitionRules.RuleFor("common/buildings"));
        Assert.Equal(OverrideRule.LastWins, DefinitionRules.RuleFor("common/defines"));
        Assert.Equal(OverrideRule.FirstWins, DefinitionRules.RuleFor("common/scripted_variables"));
        Assert.Equal(OverrideRule.Duplicated, DefinitionRules.RuleFor("common/strategic_resources"));
        Assert.Equal(OverrideRule.Unknown, DefinitionRules.RuleFor("events"));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.** Expect a compile error, since the types don't exist yet.

- [ ] **Step 3: Implement**

`DefinitionScanner.cs`:
```csharp
namespace FazStellarisModmanager.Core.Conflicts;

public enum DefinitionKind
{
    /// <summary>Each top-level "name = …" is a definition (common/&lt;type&gt;).</summary>
    TopLevel,
    /// <summary>"NCategory.Key" for each key inside a top-level block (common/defines).</summary>
    Defines,
    /// <summary>The id of each top-level "…event = { id = x }" block (events).</summary>
    Events,
}

/// <summary>Finds the names a Paradox script file defines without building a parse tree. Tolerates broken files.</summary>
public static class DefinitionScanner
{
    public static List<string> Names(string text, DefinitionKind kind)
    {
        var names = new List<string>();
        var depth = 0;
        string? prevWord = null;   // the previous token, when it was a word
        string? key0 = null;       // the key just assigned at depth 0
        string? block0 = null;     // the key of the depth-0 block we are inside
        var expectId = false;

        foreach (var (token, isWord) in Tokens(text))
        {
            if (isWord)
            {
                if (expectId) names.Add(token);
                expectId = false;
                prevWord = token;
                continue;
            }

            expectId = false;
            if (token == "=" && prevWord is not null)
            {
                if (depth == 0)
                {
                    key0 = prevWord;
                    if (kind == DefinitionKind.TopLevel) names.Add(prevWord);
                }
                else if (depth == 1 && block0 is not null)
                {
                    if (kind == DefinitionKind.Defines) names.Add(block0 + "." + prevWord);
                    else if (kind == DefinitionKind.Events && prevWord.Equals("id", StringComparison.OrdinalIgnoreCase)
                             && block0.EndsWith("event", StringComparison.OrdinalIgnoreCase)) expectId = true;
                }
            }
            else if (token == "{")
            {
                if (depth == 0) { block0 = key0; key0 = null; }
                depth++;
            }
            else if (token == "}")
            {
                if (depth > 0) depth--;
                if (depth == 0) block0 = null;
            }
            prevWord = null;
        }
        return names;
    }

    // Words (bare or quoted, quotes removed) and the symbols { } = and comparison operators. "#" starts a comment.
    static IEnumerable<(string Token, bool IsWord)> Tokens(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#')
            {
                while (i < text.Length && text[i] != (char)10) i++;
                continue;
            }
            if (c == '"')
            {
                var end = text.IndexOf('"', i + 1);
                if (end < 0) end = text.Length;
                yield return (text[(i + 1)..end], true);
                i = end + 1;
                continue;
            }
            if (c is '{' or '}')
            {
                yield return (c.ToString(), false);
                i++;
                continue;
            }
            if (c is '=' or '<' or '>' or '!' or '?')
            {
                var j = i + 1;
                if (j < text.Length && text[j] == '=') j++;
                var op = text[i..j];
                yield return (op is "=" or "==" or "?=" ? "=" : op, false);
                i = j;
                continue;
            }
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '=' or '#' or '"' or '<' or '>' or '!' or '?')) i++;
            yield return (text[start..i], true);
        }
    }
}
```

`DefinitionRules.cs`:
```csharp
namespace FazStellarisModmanager.Core.Conflicts;

/// <summary>What the game does with two definitions of the same name in different files of one folder.</summary>
public enum OverrideRule
{
    /// <summary>The definition loaded last is used.</summary>
    LastWins,
    /// <summary>The definition loaded first is used.</summary>
    FirstWins,
    /// <summary>Both are loaded (usually broken); only replacing the whole file overrides.</summary>
    Duplicated,
    /// <summary>Not documented (events): reported as a duplicate.</summary>
    Unknown,
}

/// <summary>Which files hold definitions and how duplicates resolve, after the Stellaris wiki's "Overwriting specific elements" table.</summary>
public static class DefinitionRules
{
    static readonly HashSet<string> FirstWins = new(StringComparer.OrdinalIgnoreCase)
    {
        "component_sets", "component_templates", "event_chains", "global_ship_designs", "scripted_loc",
        "scripted_variables", "solar_system_initializers", "special_projects", "start_screen_messages",
    };

    static readonly HashSet<string> Duplicated = new(StringComparer.OrdinalIgnoreCase)
    {
        "name_lists", "observation_station_missions", "strategic_resources", "terraform", "traits",
    };

    // on_actions merge; inline_scripts are pasted in by path rather than defined by name.
    static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase) { "on_actions", "inline_scripts" };

    /// <summary>The folder ("common/buildings", "events") and kind of a scanned script file, or null when the file is not scanned.</summary>
    public static (string Folder, DefinitionKind Kind)? Classify(string relativePath)
    {
        var parts = relativePath.Replace((char)92, '/').Split('/');
        if (!parts[^1].EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return null;
        if (parts.Length >= 2 && parts[0].Equals("events", StringComparison.OrdinalIgnoreCase)) return ("events", DefinitionKind.Events);
        if (parts.Length >= 3 && parts[0].Equals("common", StringComparison.OrdinalIgnoreCase) && !Skipped.Contains(parts[1]))
        {
            var type = parts[1].ToLowerInvariant();
            return ("common/" + type, type == "defines" ? DefinitionKind.Defines : DefinitionKind.TopLevel);
        }
        return null;
    }

    public static OverrideRule RuleFor(string folder)
    {
        if (folder.Equals("events", StringComparison.OrdinalIgnoreCase)) return OverrideRule.Unknown;
        var type = folder.StartsWith("common/", StringComparison.OrdinalIgnoreCase) ? folder["common/".Length..] : folder;
        if (FirstWins.Contains(type)) return OverrideRule.FirstWins;
        if (Duplicated.Contains(type)) return OverrideRule.Duplicated;
        return OverrideRule.LastWins;
    }
}
```

- [ ] **Step 4: Run the tests and the full suite.** They should PASS.
- [ ] **Step 5: Commit.** Message: `"feat: definition scanner and override rules"`.

---

### Task 3: DefinitionAnalyzer

**Files:** create `FazStellarisModmanager.Core/Conflicts/DefinitionAnalyzer.cs`. Test: `FazStellarisModmanager.Tests/DefinitionAnalyzerTests.cs`.

- [ ] **Step 1: Write the tests**

```csharp
using FazStellarisModmanager.Core.Conflicts;

namespace FazStellarisModmanager.Tests;

public class DefinitionAnalyzerTests
{
    static ScriptFile F(string path, params string[] names) => new(path, names);
    static ModScripts M(params ScriptFile[] files) => new(files);

    [Fact]
    public void Whole_file_replacement_like_galactic_market_expansion()
    {
        var r = DefinitionAnalyzer.Analyze([
            M(F("common/strategic_resources/00_strategic_resources.txt", "minerals", "energy"),
              F("common/strategic_resources/giga_strategic_resources.txt", "giga_x")),
            M(F("common/strategic_resources/acot_special_resources.txt", "acot_a")),
            M(F("common/strategic_resources/giga_strategic_resources.txt", "giga_x", "giga_y")),
            M(F("common/strategic_resources/00_strategic_resources.txt", "minerals", "energy", "food")),
        ]);
        Assert.Equal([3, 1, 2, 3], r.DefinitionCounts);
        var lost = r.Losses.Where(l => l.Mod == 0).OrderBy(l => l.Name).ToList();
        Assert.Equal(["energy", "giga_x", "minerals"], lost.Select(l => l.Name));
        Assert.All(lost, l => Assert.Equal(LossReason.FileReplaced, l.Reason));
        Assert.Equal(2, lost.Single(l => l.Name == "giga_x").OtherMod);
        Assert.Equal(3, lost.Single(l => l.Name == "minerals").OtherMod);
        Assert.DoesNotContain(r.Losses, l => l.Mod != 0);
    }

    [Fact]
    public void Last_wins_by_file_name_not_list_order()
    {
        var r = DefinitionAnalyzer.Analyze([
            M(F("common/buildings/zz_mine.txt", "b1")),
            M(F("common/buildings/00_other.txt", "b1", "b2")),
        ]);
        var loss = Assert.Single(r.Losses);
        Assert.Equal(1, loss.Mod);
        Assert.Equal(0, loss.OtherMod);
        Assert.Equal("common/buildings/zz_mine.txt", loss.OtherFile);
        Assert.Equal(LossReason.LoadsBeforeWinner, loss.Reason);
    }

    [Fact]
    public void First_wins_folder()
    {
        var r = DefinitionAnalyzer.Analyze([
            M(F("common/scripted_variables/zz_a.txt", "@v")),
            M(F("common/scripted_variables/00_b.txt", "@v")),
        ]);
        var loss = Assert.Single(r.Losses);
        Assert.Equal(0, loss.Mod);
        Assert.Equal(1, loss.OtherMod);
        Assert.Equal(LossReason.LoadsAfterWinner, loss.Reason);
    }

    [Fact]
    public void Duplicated_folder_and_events_report_every_copy()
    {
        var r = DefinitionAnalyzer.Analyze([
            M(F("common/strategic_resources/a.txt", "sr_x"), F("events/a.txt", "ev.1")),
            M(F("common/strategic_resources/b.txt", "sr_x"), F("events/b.txt", "ev.1")),
        ]);
        Assert.Equal(4, r.Losses.Count);
        Assert.All(r.Losses, l => Assert.Equal(LossReason.Duplicate, l.Reason));
        Assert.Contains(r.Losses, l => l.Mod == 0 && l.Name == "sr_x" && l.OtherMod == 1);
        Assert.Contains(r.Losses, l => l.Mod == 1 && l.Name == "ev.1" && l.OtherMod == 0);
    }

    [Fact]
    public void Same_mod_twice_in_its_own_files_is_not_a_conflict()
    {
        var r = DefinitionAnalyzer.Analyze([
            M(F("common/buildings/a.txt", "b1"), F("common/buildings/b.txt", "B1")),
            M(F("common/buildings/c.txt", "b2")),
        ]);
        Assert.Empty(r.Losses);
        Assert.Equal([2, 1], r.DefinitionCounts);
    }

    [Fact]
    public void Mod_listed_twice_loses_everything_in_its_first_copy()
    {
        var files = M(F("common/buildings/a.txt", "b1", "b2"));
        var r = DefinitionAnalyzer.Analyze([files, files]);
        Assert.Equal(2, r.Losses.Count);
        Assert.All(r.Losses, l => { Assert.Equal(0, l.Mod); Assert.Equal(1, l.OtherMod); Assert.Equal(LossReason.FileReplaced, l.Reason); });
    }

    [Fact]
    public void Unscanned_files_are_ignored()
    {
        var r = DefinitionAnalyzer.Analyze([
            M(F("common/on_actions/a.txt", "on_game_start")),
            M(F("common/on_actions/b.txt", "on_game_start")),
        ]);
        Assert.Empty(r.Losses);
        Assert.Equal([0, 0], r.DefinitionCounts);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**

- [ ] **Step 3: Implement**

```csharp
namespace FazStellarisModmanager.Core.Conflicts;

/// <summary>A scanned script file: its content-relative path (forward slashes) and the names it defines.</summary>
public sealed record ScriptFile(string Path, IReadOnlyList<string> Names);

/// <summary>One mod's scanned script files.</summary>
public sealed record ModScripts(IReadOnlyList<ScriptFile> Files);

public enum LossReason
{
    /// <summary>A mod later in the list has a file at the same path, so this whole file is not loaded.</summary>
    FileReplaced,
    /// <summary>Last-wins folder: the other definition's file loads later (file-name order).</summary>
    LoadsBeforeWinner,
    /// <summary>First-wins folder: the other definition's file loads earlier.</summary>
    LoadsAfterWinner,
    /// <summary>Duplicated folder or event id: both load (or the game keeps an unpredictable one).</summary>
    Duplicate,
}

/// <summary>A definition of the mod at <see cref="Mod"/> that the game does not use (or loads twice), because of the mod at <see cref="OtherMod"/>.</summary>
public sealed record DefinitionLoss(int Mod, string Folder, string Name, string File, int OtherMod, string OtherFile, LossReason Reason);

/// <summary>Per-mod definition counts (in list order) and every cross-mod loss.</summary>
public sealed record DefinitionReport(IReadOnlyList<int> DefinitionCounts, IReadOnlyList<DefinitionLoss> Losses);

/// <summary>Works out which definitions win in a load order: same path replaces the whole file (last mod wins), then files load in file-name order and the folder's <see cref="OverrideRule"/> decides.</summary>
public static class DefinitionAnalyzer
{
    public static DefinitionReport Analyze(IReadOnlyList<ModScripts> mods)
    {
        var counts = new int[mods.Count];
        var losses = new List<DefinitionLoss>();

        var owner = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < mods.Count; i++)
            foreach (var f in mods[i].Files)
                owner[Normalize(f.Path)] = i;

        var surviving = new List<(int Mod, ScriptFile File, string Folder)>();
        for (var i = 0; i < mods.Count; i++)
        {
            foreach (var f in mods[i].Files)
            {
                if (DefinitionRules.Classify(f.Path) is not { } c) continue;
                var names = f.Names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                counts[i] += names.Count;
                var winner = owner[Normalize(f.Path)];
                if (winner == i)
                {
                    surviving.Add((i, f, c.Folder));
                    continue;
                }
                foreach (var n in names) losses.Add(new DefinitionLoss(i, c.Folder, n, f.Path, winner, f.Path, LossReason.FileReplaced));
            }
        }

        foreach (var folder in surviving.GroupBy(s => s.Folder, StringComparer.OrdinalIgnoreCase))
        {
            var rule = DefinitionRules.RuleFor(folder.Key);
            var byName = new Dictionary<string, List<(int Mod, string File)>>(StringComparer.OrdinalIgnoreCase);
            var loadOrder = folder
                .OrderBy(s => System.IO.Path.GetFileName(s.File.Path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.File.Path, StringComparer.OrdinalIgnoreCase);
            foreach (var s in loadOrder)
            {
                foreach (var n in s.File.Names.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!byName.TryGetValue(n, out var defs)) byName[n] = defs = [];
                    defs.Add((s.Mod, s.File.Path));
                }
            }

            foreach (var (name, defs) in byName)
            {
                if (defs.Count < 2) continue;
                switch (rule)
                {
                    case OverrideRule.LastWins:
                        Lose(defs[^1], defs.Take(defs.Count - 1), LossReason.LoadsBeforeWinner);
                        break;
                    case OverrideRule.FirstWins:
                        Lose(defs[0], defs.Skip(1), LossReason.LoadsAfterWinner);
                        break;
                    default:
                        foreach (var d in defs)
                        {
                            var other = defs.FirstOrDefault(o => o.Mod != d.Mod);
                            if (other.File is not null) losses.Add(new DefinitionLoss(d.Mod, folder.Key, name, d.File, other.Mod, other.File, LossReason.Duplicate));
                        }
                        break;
                }

                void Lose((int Mod, string File) winner, IEnumerable<(int Mod, string File)> losers, LossReason reason)
                {
                    foreach (var d in losers)
                        if (d.Mod != winner.Mod) losses.Add(new DefinitionLoss(d.Mod, folder.Key, name, d.File, winner.Mod, winner.File, reason));
                }
            }
        }

        return new DefinitionReport(counts, losses);
    }

    static string Normalize(string path) => path.Replace((char)92, '/');
}
```

- [ ] **Step 4: Run the tests and the full suite.** They should PASS.
- [ ] **Step 5: Commit.** Message: `"feat: definition conflict analyzer"`.

---

### Task 4: DefinitionScanService and EditedModList

**Files:**
- Create `FazStellarisModmanager.Core/Conflicts/DefinitionScanService.cs`.
- Create `FazStellarisModmanager.Core/Lists/EditedModList.cs`.
- Tests: `FazStellarisModmanager.Tests/DefinitionScanServiceTests.cs` and `FazStellarisModmanager.Tests/EditedModListTests.cs`.

- [ ] **Step 1: Write the tests**

```csharp
using System.IO.Compression;
using FazStellarisModmanager.Core.Conflicts;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class DefinitionScanServiceTests
{
    static InstalledMod Mod(string name, string content) =>
        new("mod:" + name, name, "mod/" + name + ".mod", null, null, null, content, ModSource.Local, []);

    [Fact]
    public async Task Scans_folder_and_zip_mods()
    {
        using var t = new TempDir();
        t.Write("a/common/buildings/x.txt", "b1 = { } b2 = { }");
        t.Write("a/events/e.txt", "namespace = a country_event = { id = a.1 }");
        t.Write("a/common/on_actions/o.txt", "on_game_start = { }");
        var zip = Path.Combine(t.Path, "b.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var w = new StreamWriter(z.CreateEntry("common/buildings/y.txt").Open())) w.Write("b1 = { }");

        var service = new DefinitionScanService();
        var scan = await service.ScanAsync([Mod("a", Path.Combine(t.Path, "a")), Mod("b", zip), null], null, CancellationToken.None);

        Assert.Empty(scan.Errors);
        Assert.Equal(["common/buildings/x.txt", "events/e.txt"], scan.Mods[0].Files.Select(f => f.Path).Order());
        Assert.Equal(["b1", "b2"], scan.Mods[0].Files.Single(f => f.Path.EndsWith("x.txt")).Names);
        Assert.Equal(["a.1"], scan.Mods[0].Files.Single(f => f.Path.StartsWith("events")).Names);
        Assert.Equal(["b1"], Assert.Single(scan.Mods[1].Files).Names);
        Assert.Empty(scan.Mods[2].Files);
    }

    [Fact]
    public async Task Reuses_names_until_the_file_changes()
    {
        using var t = new TempDir();
        var f = t.Write("a/common/buildings/x.txt", "b1 = { }");
        var mod = Mod("a", Path.Combine(t.Path, "a"));
        var service = new DefinitionScanService();
        var first = await service.ScanAsync([mod], null, CancellationToken.None);
        var again = await service.ScanAsync([mod], null, CancellationToken.None);
        Assert.Same(first.Mods[0].Files[0].Names, again.Mods[0].Files[0].Names);

        File.WriteAllText(f, "b1 = { } b9 = { }");
        File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(1));
        var changed = await service.ScanAsync([mod], null, CancellationToken.None);
        Assert.Equal(["b1", "b9"], changed.Mods[0].Files[0].Names);
    }

    [Fact]
    public async Task Unreadable_mod_is_an_error_line()
    {
        using var t = new TempDir();
        var service = new DefinitionScanService();
        var scan = await service.ScanAsync([Mod("gone", Path.Combine(t.Path, "missing"))], null, CancellationToken.None);
        Assert.Empty(scan.Mods[0].Files);
        Assert.Contains("gone", Assert.Single(scan.Errors));
    }

    [Fact]
    public async Task Reports_progress_per_mod()
    {
        using var t = new TempDir();
        t.Mkdir("a");
        var seen = new System.Collections.Concurrent.ConcurrentBag<int>();
        await new DefinitionScanService().ScanAsync([Mod("a", Path.Combine(t.Path, "a")), null], n => seen.Add(n), CancellationToken.None);
        Assert.Equal([1, 2], seen.Order());
    }
}
```

```csharp
using FazStellarisModmanager.Core.Lists;

namespace FazStellarisModmanager.Tests;

public class EditedModListTests
{
    [Fact]
    public void Set_and_touch_bump_version_and_copy()
    {
        var list = new EditedModList();
        Assert.False(list.Loaded);
        var source = new ModList("L", [new ModListEntry("mod:a", "A", "mod/a.mod", null)], ["dlc1"]);
        list.Set(source);
        Assert.True(list.Loaded);
        Assert.Equal(1, list.Version);
        Assert.Equal("L", list.Name);
        list.Mods.Add(new ModListEntry("mod:b", "B", "mod/b.mod", null));
        Assert.Single(source.Mods);
        list.Touch();
        Assert.Equal(2, list.Version);
        var copy = list.ToModList();
        Assert.Equal(2, copy.Mods.Count);
        copy.Mods.Clear();
        Assert.Equal(2, list.Mods.Count);
        Assert.Equal(["dlc1"], copy.DisabledDlcs);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**

- [ ] **Step 3: Implement**

`DefinitionScanService.cs`:
```csharp
using System.Collections.Concurrent;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Conflicts;

/// <summary>Result of one scan: each mod's script files (null mods give none) and lines for anything unreadable.</summary>
public sealed record DefinitionScan(IReadOnlyList<ModScripts> Mods, IReadOnlyList<string> Errors);

/// <summary>The last scan shown on the Conflicts tab, for the list at <see cref="ListVersion"/>.</summary>
public sealed record ConflictSnapshot(int ListVersion, IReadOnlyList<ModListEntry> Entries, IReadOnlyList<string> Errors,
    DefinitionReport Report, int Definitions, TimeSpan Took);

/// <summary>Reads the definitions of a list's mods (folders or zips), caching each file's names by path and stamp for the app run. Thread-safe.</summary>
public sealed class DefinitionScanService
{
    readonly ConcurrentDictionary<string, IReadOnlyList<string>> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last result the Conflicts tab showed (kept so returning to the tab doesn't rescan).</summary>
    public ConflictSnapshot? Last { get; set; }

    /// <summary>Scans the mods four at a time; <paramref name="onProgress"/> gets the number of mods done (from any thread).</summary>
    public async Task<DefinitionScan> ScanAsync(IReadOnlyList<InstalledMod?> mods, Action<int>? onProgress, CancellationToken ct)
    {
        var result = new ModScripts[mods.Count];
        var errors = new ConcurrentQueue<string>();
        var done = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, mods.Count), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, (i, token) =>
        {
            result[i] = ScanMod(mods[i], errors, token);
            onProgress?.Invoke(Interlocked.Increment(ref done));
            return ValueTask.CompletedTask;
        });
        return new DefinitionScan(result, errors.ToList());
    }

    ModScripts ScanMod(InstalledMod? mod, ConcurrentQueue<string> errors, CancellationToken ct)
    {
        if (mod is null) return new ModScripts([]);
        ContentSource source;
        try
        {
            source = ContentSource.FromPath(mod.Name, mod.ContentPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            errors.Enqueue($"{mod.Name}: {ex.Message}");
            return new ModScripts([]);
        }

        using (source)
        {
            var files = new List<ScriptFile>();
            foreach (var rel in source.Files("common", ".txt").Concat(source.Files("events", ".txt")))
            {
                ct.ThrowIfCancellationRequested();
                if (DefinitionRules.Classify(rel) is not { } c) continue;
                try
                {
                    var key = mod.ContentPath + "|" + rel + "|" + source.Stamp(rel);
                    var names = _names.GetOrAdd(key, _ => DefinitionScanner.Names(source.ReadText(rel), c.Kind));
                    files.Add(new ScriptFile(rel, names));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    errors.Enqueue($"{mod.Name}: {rel}: {ex.Message}");
                }
            }
            return new ModScripts(files);
        }
    }
}
```

`EditedModList.cs`:
```csharp
namespace FazStellarisModmanager.Core.Lists;

/// <summary>The mod list being edited on the Mods page, shared with other pages (Conflicts). Used from the UI thread only. <see cref="Version"/> changes on every edit.</summary>
public sealed class EditedModList
{
    public string Name { get; set; } = "Current";
    public List<ModListEntry> Mods { get; private set; } = [];
    public List<string> DisabledDlcs { get; private set; } = [];
    public bool Loaded { get; private set; }
    public int Version { get; private set; }

    /// <summary>Replaces the list with a copy of <paramref name="list"/>.</summary>
    public void Set(ModList list)
    {
        Name = list.Name;
        Mods = list.Mods.ToList();
        DisabledDlcs = list.DisabledDlcs.ToList();
        Loaded = true;
        Version++;
    }

    /// <summary>Call after changing <see cref="Mods"/> in place.</summary>
    public void Touch() => Version++;

    public ModList ToModList() => new(Name.Trim(), Mods.ToList(), DisabledDlcs.ToList());
}
```

- [ ] **Step 4: Run the tests and the full suite.** They should PASS.
- [ ] **Step 5: Commit.** Message: `"feat: definition scan service and shared edited mod list"`.

---

### Task 5: Mods page uses EditedModList; services registered

**Files:**
- Modify `FazStellarisModmanager/AppServices.cs`. After the `ModFileCache` registration, add:
  ```csharp
  services.AddSingleton(new EditedModList());
  services.AddSingleton(new DefinitionScanService());
  ```
  Add `using FazStellarisModmanager.Core.Lists;` if it is missing.
- Modify `FazStellarisModmanager/Pages/Mods.razor`.

- [ ] **Step 1: Point the page at the shared list.** In Mods.razor:
  - Add `@inject EditedModList State`.
  - Remove these fields: `List<ModListEntry> current = [];`, `List<string> disabledDlcs = [];`, `string listName = "Current";`, `bool loaded;`.
  - Add these properties in their place, so the rest of the page keeps compiling:
    ```csharp
        List<ModListEntry> current => State.Mods;
        List<string> disabledDlcs => State.DisabledDlcs;
        string listName { get => State.Name; set => State.Name = value; }
        bool loaded => State.Loaded;
    ```
  - Delete every `loaded = true;` statement. `State.Set` sets Loaded.
  - Change `SetList(ModList list)` to:
    ```csharp
        void SetList(ModList list)
        {
            confirmEmpty = false;
            State.Set(list);
            ScheduleConflicts();
        }
    ```
  - In `ScheduleConflicts()`, add `State.Touch();` as the first line. Every list edit already calls it.
  - In `OnInitializedAsync`, change the body inside `RunAsync` so that the list is imported only when nothing is loaded yet:
    ```csharp
            await Manager.RefreshLibraryAsync();
            savedLists = Manager.Lists.LoadAll();
            var imported = !State.Loaded;
            if (imported) SetList(Manager.ImportCurrent("Current"));
            LibraryChanged();
            return imported
                ? $"Loaded {current.Count} enabled mods from dlc_load.json." + ErrorSuffix()
                : $"{current.Count} mods in '{listName}'." + ErrorSuffix();
    ```
- [ ] **Step 2: Build and run the full suite.** Expected: 0 errors and all tests passing.
- [ ] **Step 3: Commit.** Message: `"refactor: Mods page keeps its list in the shared EditedModList"`.

---

### Task 6: Conflicts page, nav and CSS

**Files:**
- Create `FazStellarisModmanager/Pages/ConflictsPage.razor`.
- Modify `FazStellarisModmanager/MainLayout.razor`. Add `<NavLink href="conflicts">Conflicts</NavLink>` after the Tech link.
- Modify `FazStellarisModmanager/wwwroot/css/site.css`. Append the CSS below.

- [ ] **Step 1: Write ConflictsPage.razor**

```razor
@page "/conflicts"
@implements IDisposable
@inject ModManagerService Manager
@inject EditedModList List
@inject DefinitionScanService Scanner

<div class="conflicts-page">
    <div class="mods-toolbar">
        <b>@List.Name</b>
        <span class="muted">@List.Mods.Count mods@(snap is null ? "" : $" · {snap.Definitions:N0} definitions in {snap.Took.TotalSeconds:0.0} s")</span>
        <span class="spacer"></span>
        <input class="search" placeholder="Search definition or file…" @bind="search" @bind:event="oninput" />
        <select @bind="folderFilter">
            <option value="">All types</option>
            @foreach (var f in Folders())
            {
                <option value="@f">@f</option>
            }
        </select>
        <button class="primary" @onclick="RescanAsync" disabled="@scanning">Rescan</button>
    </div>

    @if (scanning)
    {
        <p class="status">Scanning @done of @total mods…</p>
    }
    @if (status is not null)
    {
        <p class="status @(error ? "error" : "")">@status</p>
    }
    @if (snap is not null && snap.ListVersion != List.Version && !scanning)
    {
        <p class="status warn">The list changed since this scan. <button class="link" @onclick="RescanAsync">Rescan</button></p>
    }
    @if (snap is not null && snap.Errors.Count > 0)
    {
        <details class="library-errors">
            <summary>@snap.Errors.Count files could not be read</summary>
            <ul>
                @foreach (var err in snap.Errors)
                {
                    <li>@err</li>
                }
            </ul>
        </details>
    }

    @if (snap is not null)
    {
        var losing = LosingMods();
        <div class="conflicts-grid">
            <div class="clist">
                <div class="muted chead">Mods that lose definitions</div>
                @if (losing.Count == 0)
                {
                    <p class="muted">No mod loses definitions to another mod in this list.</p>
                }
                @foreach (var (mod, lost) in losing)
                {
                    var m = mod;
                    <button class="cmod @(m == selected ? "on" : "")" @onclick="() => selected = m">
                        <span class="n">#@(m + 1) @snap.Entries[m].Name</span>
                        <span class="lost">loses @lost of @snap.Report.DefinitionCounts[m]</span>
                    </button>
                }
            </div>
            <div class="cdetail">
                @if (selected is int sel && sel < snap.Entries.Count)
                {
                    var mine = snap.Report.Losses.Where(l => l.Mod == sel).ToList();
                    var shown = mine.Where(Matches).ToList();
                    var replacers = mine.Where(l => l.Reason == LossReason.FileReplaced).Select(l => l.OtherMod).Distinct().Order().ToList();
                    <h3>#@(sel + 1) @snap.Entries[sel].Name</h3>
                    <div class="muted">@mine.Select(l => (l.Folder, l.Name)).Distinct().Count() of its @snap.Report.DefinitionCounts[sel] definitions are affected.</div>
                    @if (replacers.Count > 0)
                    {
                        <p class="muted">Whole files are replaced by @string.Join(", ", replacers.Select(r => "#" + (r + 1))). To keep this mod's versions, move it below those mods; they will then lose these files instead.</p>
                    }
                    @foreach (var folder in shown.GroupBy(l => l.Folder).OrderBy(g => g.Key))
                    {
                        <div class="cgroup">@folder.Key</div>
                        <table class="ctable">
                            <tr><th>Definitions</th><th>Used instead</th><th>Why</th></tr>
                            @foreach (var g in folder.GroupBy(l => (l.OtherMod, l.OtherFile, l.Reason, l.File)).OrderBy(g => g.Key.OtherMod))
                            {
                                var names = g.Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
                                var groupKey = folder.Key + "|" + g.Key.OtherMod + "|" + g.Key.OtherFile + "|" + g.Key.File + "|" + g.Key.Reason;
                                var all = expanded.Contains(groupKey);
                                <tr>
                                    <td class="k">
                                        @string.Join(", ", all ? names : names.Take(12))
                                        @if (!all && names.Count > 12)
                                        {
                                            <button class="link" @onclick="() => expanded.Add(groupKey)"> +@(names.Count - 12) more</button>
                                        }
                                        <div class="sub">@g.Key.File</div>
                                    </td>
                                    <td>#@(g.Key.OtherMod + 1) @snap.Entries[g.Key.OtherMod].Name<div class="sub">@g.Key.OtherFile</div></td>
                                    <td class="why">
                                        @Why(g.Key.Reason, folder.Key, g.Key.File, g.Key.OtherFile)
                                        @if (g.Key.Reason == LossReason.FileReplaced) { <span class="tag file">file</span> }
                                        @if (g.Key.Reason == LossReason.Duplicate) { <span class="tag dup">duplicate</span> }
                                    </td>
                                </tr>
                            }
                        </table>
                    }
                    @if (shown.Count == 0 && mine.Count > 0)
                    {
                        <p class="muted">Nothing matches the search or type filter.</p>
                    }
                }
            </div>
        </div>
    }
</div>

@code {
    ConflictSnapshot? snap;
    int? selected;
    string search = "";
    string folderFilter = "";
    bool scanning;
    int done;
    int total;
    string? status;
    bool error;
    readonly HashSet<string> expanded = [];
    CancellationTokenSource? cts;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            if (Manager.Library.Count == 0) await Manager.RefreshLibraryAsync();
            if (!List.Loaded) List.Set(Manager.ImportCurrent("Current"));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException
                                   or System.Text.Json.JsonException or InvalidDataException)
        {
            (status, error) = (ex.Message, true);
            return;
        }
        if (Scanner.Last is { } last && last.ListVersion == List.Version) Show(last);
        else await ScanAsync();
    }

    async Task RescanAsync()
    {
        try
        {
            await Manager.RefreshLibraryAsync();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            (status, error) = (ex.Message, true);
            return;
        }
        await ScanAsync();
    }

    async Task ScanAsync()
    {
        cts?.Cancel();
        cts?.Dispose();
        var run = cts = new CancellationTokenSource();
        var version = List.Version;
        var entries = List.Mods.ToList();
        var byKey = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in Manager.Library) byKey.TryAdd(m.Key, m);
        var mods = entries.Select(e => byKey.GetValueOrDefault(e.Key)).ToList();

        (scanning, done, total, status, error) = (true, 0, entries.Count, null, false);
        long lastRender = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var scan = await Task.Run(() => Scanner.ScanAsync(mods, n =>
            {
                done = n;
                var now = Environment.TickCount64;
                if (now - Interlocked.Read(ref lastRender) >= 250)
                {
                    Interlocked.Exchange(ref lastRender, now);
                    _ = InvokeAsync(StateHasChanged);
                }
            }, run.Token), run.Token);
            var report = await Task.Run(() => DefinitionAnalyzer.Analyze(scan.Mods), run.Token);
            var result = new ConflictSnapshot(version, entries, scan.Errors, report, report.DefinitionCounts.Sum(), clock.Elapsed);
            Scanner.Last = result;
            Show(result);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!run.IsCancellationRequested) scanning = false;
        }
    }

    void Show(ConflictSnapshot result)
    {
        snap = result;
        expanded.Clear();
        selected = LosingMods().Select(x => (int?)x.Mod).FirstOrDefault();
    }

    List<(int Mod, int Lost)> LosingMods() =>
        snap is null ? [] : snap.Report.Losses
            .GroupBy(l => l.Mod)
            .Select(g => (Mod: g.Key, Lost: g.Select(l => (l.Folder, l.Name)).Distinct().Count()))
            .OrderByDescending(x => x.Lost).ThenBy(x => x.Mod)
            .ToList();

    IEnumerable<string> Folders() =>
        snap is null ? [] : snap.Report.Losses.Select(l => l.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);

    bool Matches(DefinitionLoss l) =>
        (folderFilter.Length == 0 || string.Equals(l.Folder, folderFilter, StringComparison.OrdinalIgnoreCase))
        && (search.Length == 0 || l.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || l.File.Contains(search, StringComparison.OrdinalIgnoreCase) || l.OtherFile.Contains(search, StringComparison.OrdinalIgnoreCase));

    static string Why(LossReason reason, string folder, string file, string otherFile) => reason switch
    {
        LossReason.FileReplaced => $"same file {Path.GetFileName(file)}, later in the list",
        LossReason.LoadsBeforeWinner => $"{Path.GetFileName(otherFile)} loads after {Path.GetFileName(file)}",
        LossReason.LoadsAfterWinner => $"{Path.GetFileName(otherFile)} loads first (this type keeps the first definition)",
        _ => folder == "events" ? "same event id: the game keeps only one" : "both load: duplicates in this type usually break",
    };

    public void Dispose()
    {
        cts?.Cancel();
        cts?.Dispose();
    }
}
```

`_Imports.razor` already has `@using FazStellarisModmanager.Core.Conflicts` and `Core.Lists`. Check this and add them if they are missing.

- [ ] **Step 2: Append the CSS**

```css
/* Conflicts tab */
.conflicts-page { display: flex; flex-direction: column; gap: .5rem; height: calc(100% - 2.5rem); }
.conflicts-grid { display: grid; grid-template-columns: 300px 1fr; gap: .6rem; flex: 1; min-height: 0; }
.clist, .cdetail { background: var(--panel); border: 1px solid var(--border); border-radius: 8px; padding: .4rem; overflow: auto; min-height: 0; }
.clist .chead { padding: .2rem .4rem .4rem; }
button.cmod { display: flex; gap: .4rem; align-items: center; width: 100%; text-align: left; background: none; border: 1px solid transparent; padding: .35rem .45rem; margin-bottom: 2px; }
button.cmod.on { background: #203247; border-color: var(--accent); }
button.cmod .n { flex: 1; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
button.cmod .lost { color: #f2c94c; font-size: .75rem; white-space: nowrap; }
.cdetail { padding: .6rem .8rem; }
.cdetail h3 { margin: 0 0 .2rem; }
.cdetail .cgroup { margin: .8rem 0 .2rem; color: #9fd2ff; }
table.ctable { width: 100%; border-collapse: collapse; font-size: .8rem; }
table.ctable th, table.ctable td { text-align: left; vertical-align: top; padding: .3rem .45rem; border-bottom: 1px solid var(--border); }
table.ctable th { color: var(--muted); font-weight: 400; }
table.ctable .k { font-family: Consolas, monospace; word-break: break-word; }
table.ctable .sub { font-size: .7rem; color: var(--muted); font-family: Consolas, monospace; }
table.ctable .why { color: var(--muted); }
.tag.file { background: #3a1d1d; color: #ff8a80; }
.tag.dup { background: #3a2e12; color: #f2c94c; }
```

- [ ] **Step 3: Build and run the full suite.** Expected: 0 errors and all tests passing.
- [ ] **Step 4: Commit.** Message: `"feat: Conflicts tab - script definitions each mod loses to others"`.

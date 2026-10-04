# Whole Tech Tree Overview & Research Route Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a zoomable whole-tree map to the Tech tab, with multi-target research route planning and researched marks saved per mod list.

**Architecture:**
- **Core (pure, unit-tested):**
  - `ResearchPlan`: route computation.
  - `TechOverviewLayout`: positions for bands × tier columns.
  - `ResearchStateStore`: JSON per mod-list label.
- **App (Blazor):**
  - `TechOverviewView`: owns pan and zoom (C#). A 5-line JS module only measures the element.
  - `OverviewLayer`: draws nodes and edges and redraws only when its inputs change.
  - `RouteBar`: target chips, totals and buttons.
  - `TechPage` and `TechSidebar` wire everything together.

**Tech Stack:** .NET 10, C#, WPF + BlazorWebView, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-03-tech-overview-design.md`

**Conventions for every task:**
- **Build and test:** run `dotnet test FazStellarisModmanager.Tests --artifacts-path <scratch>/art`. The normal `bin` folders may be locked by a running copy of the app; if they aren't, plain `dotnet test FazStellarisModmanager.Tests` is fine.
- **Escape sequences:** the authoring tools sometimes mangle backslash escapes. Never write `\n` or `\t` inside C# strings in this repo; use `(char)10` / `(char)9` instead.
- **Branch:** `feature/tech-overview` (already checked out).
- **Commits:** end every message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

## File structure

| File | Responsibility |
|---|---|
| Create `FazStellarisModmanager.Core/Technology/ResearchPlan.cs` | `ResearchRoute`, `MissingPrerequisite`, `ResearchPlan.Build/Compare` |
| Create `FazStellarisModmanager.Core/Technology/TechOverviewLayout.cs` | Overview records + `TechOverviewLayout.Build` |
| Create `FazStellarisModmanager.Core/Technology/ResearchStateStore.cs` | `ResearchState`, `ResearchStateStore` |
| Modify `FazStellarisModmanager.Core/AppPaths.cs` | `Research` folder |
| Modify `FazStellarisModmanager.Core/Technology/TechTreeService.cs` | `Research` store, `IconCount` |
| Create `FazStellarisModmanager/wwwroot/js/overview.js` | measure element rect |
| Create `FazStellarisModmanager/Components/OverviewLayer.razor` | SVG nodes/edges/bands, render guard |
| Create `FazStellarisModmanager/Components/TechOverviewView.razor` | pan/zoom/fit/centre, click routing |
| Create `FazStellarisModmanager/Components/RouteBar.razor` | route chips, totals, clear, unresearch all |
| Modify `FazStellarisModmanager/Components/TechSidebar.razor` | route/researched buttons, Research route section |
| Modify `FazStellarisModmanager/Pages/TechPage.razor` | view switch, state load/save, wiring |
| Modify `FazStellarisModmanager/wwwroot/css/site.css` | styles |
| Create tests `ResearchPlanTests.cs`, `TechOverviewLayoutTests.cs`, `ResearchStateStoreTests.cs` | |

---

### Task 1: Research route planning (`ResearchPlan`)

**Files:**
- Create: `FazStellarisModmanager.Core/Technology/ResearchPlan.cs`
- Test: `FazStellarisModmanager.Tests/ResearchPlanTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ResearchPlanTests
{
    // Names are the keys (no localisation). start is a starting tech; ghost is missing; loop1/loop2 depend on each other.
    const string Tree = """
        start = { area = physics tier = 0 start_tech = yes cost = 10 }
        a = { area = physics tier = 1 cost = 100 prerequisites = { "start" } }
        b = { area = engineering tier = 1 cost = 200 prerequisites = { "start" } }
        c = { area = physics tier = 2 cost = 300 prerequisites = { "a" "b" } }
        d = { area = society tier = 2 cost = @unknown prerequisites = { "a" } }
        e = { area = society tier = 3 cost = 500 prerequisites = { "c" "d" "ghost" } }
        rep = { area = physics tier = 1 cost = 40 levels = -1 prerequisites = { "a" } }
        loop1 = { area = physics tier = 1 cost = 1 prerequisites = { "loop2" } }
        loop2 = { area = physics tier = 1 cost = 1 prerequisites = { "loop1" } }
        """;

    static (TechDatabase Db, IDisposable Cleanup) Db()
    {
        var tmp = new TempDir();
        tmp.Write("g/common/technology/00_t.txt", Tree);
        var source = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        var db = TechDatabase.Build([source]);
        source.Dispose();
        return (db, tmp);
    }

    [Fact]
    public void Orders_prerequisites_first_then_tier_area_and_name_and_sums_costs()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, ["e"], []);

        Assert.Equal(["a", "b", "c", "d", "e"], route.Steps);
        Assert.Equal(1100, route.TotalCost);
        Assert.Equal(1, route.UnknownCostCount);
        Assert.Equal((0, 1), (route.SkippedResearched, route.SkippedStarting));
        Assert.Equal([new MissingPrerequisite("e", "ghost")], route.MissingPrerequisites);
        Assert.Empty(route.Cycles);
        Assert.Empty(route.UnknownTargets);
    }

    [Fact]
    public void Several_targets_share_prerequisites()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, ["c", "d"], []);

        Assert.Equal(["a", "b", "c", "d"], route.Steps);
        Assert.Equal(600, route.TotalCost);
    }

    [Fact]
    public void Researched_techs_are_skipped_and_not_expanded()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, ["c"], ["A", "not_a_tech"]);

        Assert.Equal(["b", "c"], route.Steps);
        Assert.Equal((1, 1), (route.SkippedResearched, route.SkippedStarting));
    }

    [Fact]
    public void Repeatables_come_after_tiered_techs_that_are_ready_at_the_same_time()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        Assert.Equal(["a", "b", "rep"], ResearchPlan.Build(db, ["rep", "b"], []).Steps);
    }

    [Fact]
    public void Prerequisite_loops_terminate_and_are_reported()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, ["loop1"], []);

        Assert.Equal(["loop1", "loop2"], route.Steps);
        Assert.Equal(["loop1", "loop2"], route.Cycles);
    }

    [Fact]
    public void Unknown_targets_are_reported_once_and_keys_are_case_insensitive()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, ["nope", "A", "NOPE"], []);

        Assert.Equal(["a"], route.Steps);
        Assert.Equal(["nope"], route.UnknownTargets);
        Assert.Contains("A", route.Needed);
    }

    [Fact]
    public void No_targets_gives_an_empty_route()
    {
        var (db, cleanup) = Db();
        using var _ = cleanup;

        var route = ResearchPlan.Build(db, [], ["a"]);

        Assert.Empty(route.Steps);
        Assert.Equal(0, route.TotalCost);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests --filter ResearchPlanTests`
Expected: the build fails with "The type or namespace name 'ResearchPlan' could not be found".

- [ ] **Step 3: Implement `ResearchPlan.cs`**

```csharp
using System.Globalization;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>A prerequisite named by a tech but missing from the tree.</summary>
public sealed record MissingPrerequisite(string Tech, string Prerequisite);

/// <summary>What is left to research for some targets. <see cref="Steps"/> is a valid research order: every tech after its needed prerequisites.</summary>
public sealed record ResearchRoute(
    IReadOnlyList<string> Steps,
    double TotalCost,
    int UnknownCostCount,
    int SkippedResearched,
    int SkippedStarting,
    IReadOnlyList<string> UnknownTargets,
    IReadOnlyList<MissingPrerequisite> MissingPrerequisites,
    IReadOnlyList<string> Cycles)
{
    public static ResearchRoute Empty { get; } = new([], 0, 0, 0, 0, [], [], []);

    HashSet<string>? _needed;

    /// <summary>The steps as a case-insensitive set, for highlighting.</summary>
    public IReadOnlySet<string> Needed => _needed ??= new HashSet<string>(Steps, StringComparer.OrdinalIgnoreCase);
}

public static class ResearchPlan
{
    /// <summary>
    /// Everything the targets need (all prerequisites, transitively) that is not researched yet. Researched techs (the given ones and every
    /// starting tech) are skipped and not expanded. Ties among ready techs: tier (repeatable, then unknown, last), area, name, key.
    /// Techs stuck in a prerequisite loop are appended in the same order and listed in <see cref="ResearchRoute.Cycles"/>.
    /// </summary>
    public static ResearchRoute Build(TechDatabase db, IEnumerable<string> targets, IEnumerable<string> researched)
    {
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in researched)
            if (db.Techs.TryGetValue(r, out var t)) done.Add(t.Key);

        var unknownTargets = new List<string>();
        var stack = new Stack<string>();
        foreach (var target in targets)
        {
            if (db.Techs.TryGetValue(target, out var t)) stack.Push(t.Key);
            else if (!unknownTargets.Contains(target, StringComparer.OrdinalIgnoreCase)) unknownTargets.Add(target);
        }

        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<MissingPrerequisite>();
        int skippedResearched = 0, skippedStarting = 0;
        while (stack.Count > 0)
        {
            var key = stack.Pop();
            if (!visited.Add(key)) continue;
            var tech = db.Techs[key];
            if (done.Contains(key)) { skippedResearched++; continue; }
            if (tech.IsStart) { skippedStarting++; continue; }
            needed.Add(key);
            foreach (var p in tech.Prerequisites)
            {
                if (db.Techs.TryGetValue(p, out var pre)) stack.Push(pre.Key);
                else missing.Add(new MissingPrerequisite(key, p));
            }
        }

        var order = Comparer<string>.Create((a, b) => Compare(db.Techs[a], db.Techs[b]));
        var waiting = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in needed)
        {
            var prereqs = db.Techs[key].Prerequisites
                .Where(p => db.Techs.ContainsKey(p))
                .Select(p => db.Techs[p].Key)
                .Where(needed.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            waiting[key] = prereqs.Count;
            foreach (var p in prereqs)
            {
                if (!dependents.TryGetValue(p, out var list)) dependents[p] = list = [];
                list.Add(key);
            }
        }

        var ready = new SortedSet<string>(needed.Where(k => waiting[k] == 0), order);
        var steps = new List<string>(needed.Count);
        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            steps.Add(next);
            if (dependents.TryGetValue(next, out var deps))
                foreach (var d in deps)
                    if (--waiting[d] == 0) ready.Add(d);
        }
        var cycles = needed.Where(k => waiting[k] > 0).Order(order).ToList();
        steps.AddRange(cycles);

        double total = 0;
        var unknownCost = 0;
        foreach (var key in steps)
        {
            if (double.TryParse(db.Techs[key].Cost, NumberStyles.Float, CultureInfo.InvariantCulture, out var cost) && double.IsFinite(cost)) total += cost;
            else unknownCost++;
        }
        return new ResearchRoute(steps, total, unknownCost, skippedResearched, skippedStarting, unknownTargets, missing, cycles);
    }

    /// <summary>Order of techs that are ready at the same time: tier (repeatable, then unknown, last), area, name, key.</summary>
    public static int Compare(Tech a, Tech b)
    {
        var c = TierRank(a).CompareTo(TierRank(b));
        if (c == 0) c = a.Area.CompareTo(b.Area);
        if (c == 0) c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        if (c == 0) c = string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
        return c;
    }

    static int TierRank(Tech t) => t.IsRepeatable ? int.MaxValue - 1 : t.Tier ?? int.MaxValue;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests --filter ResearchPlanTests`
Expected: 7 passed.

- [ ] **Step 5: Commit**

```bash
git add FazStellarisModmanager.Core/Technology/ResearchPlan.cs FazStellarisModmanager.Tests/ResearchPlanTests.cs
git commit -m "Tech: research route planning across several targets"
```

---

### Task 2: Whole-tree layout (`TechOverviewLayout`)

**Files:**
- Create: `FazStellarisModmanager.Core/Technology/TechOverviewLayout.cs`
- Test: `FazStellarisModmanager.Tests/TechOverviewLayoutTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class TechOverviewLayoutTests
{
    const string Tree = """
        p0 = { area = physics tier = 0 category = { particles } }
        p1b = { area = physics tier = 1 category = { particles } prerequisites = { "p0" } }
        p1a = { area = physics tier = 1 category = { computing } prerequisites = { "p0" } }
        p1c = { area = physics tier = 1 }
        x2 = { area = physics tier = 2 category = { particles } prerequisites = { "p1c" } }
        y2 = { area = physics tier = 2 category = { particles } prerequisites = { "p1a" } }
        z2 = { area = physics tier = 2 category = { particles } }
        s0 = { area = society tier = 0 category = { biology } }
        s1 = { area = society tier = 1 category = { biology } prerequisites = { "s0" "p0" "ghost" } }
        e3 = { area = engineering tier = 3 category = { industry } prerequisites = { "e3b" } }
        e3b = { area = engineering tier = 3 category = { industry } }
        rep = { area = physics tier = 1 levels = -1 category = { particles } prerequisites = { "p1b" } }
        none = { area = society category = { biology } }
        """;

    static (TechDatabase Db, IDisposable Cleanup) Db(string text)
    {
        var tmp = new TempDir();
        tmp.Write("g/common/technology/00_t.txt", text);
        var source = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);
        var db = TechDatabase.Build([source]);
        source.Dispose();
        return (db, tmp);
    }

    [Fact]
    public void Columns_are_used_tiers_then_repeatable_then_unknown_and_bands_are_the_areas()
    {
        var (db, cleanup) = Db(Tree);
        using var _ = cleanup;

        var o = TechOverviewLayout.Build(db);

        Assert.Equal(["Tier 0", "Tier 1", "Tier 2", "Tier 3", "Repeatable", "?"], o.Columns.Select(c => c.Label));
        Assert.Equal([TechArea.Physics, TechArea.Society, TechArea.Engineering], o.Bands.Select(b => b.Area));
        Assert.Equal(["Physics", "Society", "Engineering"], o.Bands.Select(b => b.Label));
        Assert.Equal(4, o.Find("rep")!.ColumnIndex);
        Assert.Equal(5, o.Find("none")!.ColumnIndex);
        Assert.Equal(13, o.Nodes.Count);
    }

    [Fact]
    public void Cells_order_by_category_then_prerequisite_position_then_name()
    {
        var (db, cleanup) = Db(Tree);
        using var _ = cleanup;

        var o = TechOverviewLayout.Build(db);

        string[] Cell(TechArea area, int column) =>
            o.Nodes.Where(n => n.Area == area && n.ColumnIndex == column).OrderBy(n => n.Row).Select(n => n.Key).ToArray();
        Assert.Equal(["p1a", "p1b", "p1c"], Cell(TechArea.Physics, 1));
        Assert.Equal(["y2", "x2", "z2"], Cell(TechArea.Physics, 2));
        Assert.Equal(["e3", "e3b"], Cell(TechArea.Engineering, 3));
    }

    [Fact]
    public void Geometry_follows_the_constants_and_nodes_never_overlap()
    {
        var (db, cleanup) = Db(Tree);
        using var _ = cleanup;

        var o = TechOverviewLayout.Build(db);

        const double pitch = TechOverviewLayout.RowPitch;
        Assert.Equal(TechOverviewLayout.HeaderHeight, o.Bands[0].Y);
        Assert.Equal(3 * pitch, o.Bands[0].Height);
        Assert.Equal(o.Bands[0].Y + o.Bands[0].Height + TechOverviewLayout.BandGap, o.Bands[1].Y);
        Assert.Equal(1 * pitch, o.Bands[1].Height);
        Assert.Equal(2 * pitch, o.Bands[2].Height);
        Assert.Equal(o.Bands[2].Y + o.Bands[2].Height, o.Height);
        Assert.Equal(TechOverviewLayout.LabelGutter + 6 * TechOverviewLayout.ColumnWidth, o.Width);

        var p1b = o.Find("p1b")!;
        Assert.Equal((TechOverviewLayout.LabelGutter + TechOverviewLayout.ColumnWidth, o.Bands[0].Y + pitch), (p1b.X, p1b.Y));

        foreach (var a in o.Nodes)
            foreach (var b in o.Nodes)
                if (!ReferenceEquals(a, b))
                    Assert.False(Math.Abs(a.X - b.X) < TechOverviewLayout.NodeWidth && Math.Abs(a.Y - b.Y) < TechOverviewLayout.NodeHeight,
                        $"{a.Key} overlaps {b.Key}");
    }

    [Fact]
    public void Edges_skip_missing_prerequisites_and_flag_forward_links()
    {
        var (db, cleanup) = Db(Tree);
        using var _ = cleanup;

        var o = TechOverviewLayout.Build(db);

        Assert.Equal(8, o.Edges.Count);
        Assert.Contains(new OverviewEdge("p0", "s1", true), o.Edges);
        Assert.Contains(new OverviewEdge("e3b", "e3", false), o.Edges);
        Assert.DoesNotContain(o.Edges, e => e.From == "ghost");
    }

    [Fact]
    public void An_Other_band_appears_only_when_needed()
    {
        var (db, cleanup) = Db("p = { area = physics tier = 0 }\no = { area = weird tier = 0 }\n");
        using var _ = cleanup;

        var o = TechOverviewLayout.Build(db);

        Assert.Equal([TechArea.Physics, TechArea.Society, TechArea.Engineering, TechArea.Other], o.Bands.Select(b => b.Area));
        Assert.Equal("Other", o.Bands[3].Label);
        Assert.Equal(["Tier 0"], o.Columns.Select(c => c.Label));
    }
}
```

Expected numbers, worked out:
- **Edges (8):** p0→p1b, p0→p1a, p1c→x2, p1a→y2, s0→s1, p0→s1, e3b→e3 and p1b→rep. `ghost` is dropped.
- **Physics band:** its tallest cells (tier 1 and tier 2) hold 3 techs, so its height is 3 × 42.
- **Engineering tier 3:** `e3` and `e3b` share a column, so neither has a placed prerequisite and they sort by name.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests --filter TechOverviewLayoutTests`
Expected: the build fails with "The type or namespace name 'TechOverviewLayout' could not be found".

- [ ] **Step 3: Implement `TechOverviewLayout.cs`**

```csharp
using System.Globalization;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>A tech's box on the overview map: top-left corner (X, Y), row within its band × column cell.</summary>
public sealed record OverviewNode(string Key, TechArea Area, int ColumnIndex, int Row, double X, double Y);

/// <summary>Prerequisite From -> tech To. Forward = To's column is right of From's (otherwise draw an arc over the top).</summary>
public sealed record OverviewEdge(string From, string To, bool Forward);

public sealed record OverviewBand(TechArea Area, string Label, double Y, double Height);

public sealed record OverviewColumn(string Label, double X);

public sealed record TechOverview(
    IReadOnlyList<OverviewNode> Nodes,
    IReadOnlyList<OverviewEdge> Edges,
    IReadOnlyList<OverviewBand> Bands,
    IReadOnlyList<OverviewColumn> Columns,
    double Width,
    double Height)
{
    Dictionary<string, OverviewNode>? _byKey;

    public OverviewNode? Find(string key) =>
        (_byKey ??= Nodes.ToDictionary(n => n.Key, StringComparer.OrdinalIgnoreCase)).GetValueOrDefault(key);
}

/// <summary>
/// The whole tree as one map: a band per area (Physics, Society, Engineering, then Other if used), a column per used tier, then
/// Repeatable and "?" (no tier) when present. Inside a cell: category (none last), then the average position of prerequisites already
/// placed further left, then name.
/// </summary>
public static class TechOverviewLayout
{
    public const double NodeWidth = 168, NodeHeight = 34, ColumnGap = 72, RowPitch = 42, BandGap = 36, LabelGutter = 96, HeaderHeight = 28;
    public const double ColumnWidth = NodeWidth + ColumnGap;

    // Rows of different bands compare on one scale: band index first, then row.
    const int BandRowScale = 100_000;

    static readonly TechArea[] AreaOrder = [TechArea.Physics, TechArea.Society, TechArea.Engineering, TechArea.Other];

    public static TechOverview Build(TechDatabase db)
    {
        var techs = db.Techs.Values.ToList();

        var tiers = techs.Where(t => !t.IsRepeatable && t.Tier is not null).Select(t => t.Tier!.Value).Distinct().Order().ToList();
        var labels = tiers.Select(t => "Tier " + t.ToString(CultureInfo.InvariantCulture)).ToList();
        var repeatableColumn = -1;
        var unknownColumn = -1;
        if (techs.Any(t => t.IsRepeatable)) { repeatableColumn = labels.Count; labels.Add("Repeatable"); }
        if (techs.Any(t => !t.IsRepeatable && t.Tier is null)) { unknownColumn = labels.Count; labels.Add("?"); }
        int ColumnOf(Tech t) => t.IsRepeatable ? repeatableColumn : t.Tier is { } tier ? tiers.BinarySearch(tier) : unknownColumn;

        var areas = AreaOrder.Where(a => a != TechArea.Other || techs.Any(t => t.Area == TechArea.Other)).ToList();
        int BandOf(Tech t) => areas.IndexOf(t.Area);

        var cells = new Dictionary<(int Band, int Column), List<Tech>>();
        foreach (var t in techs)
        {
            var cell = (BandOf(t), ColumnOf(t));
            if (!cells.TryGetValue(cell, out var list)) cells[cell] = list = [];
            list.Add(t);
        }

        var row = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var column = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = 0; c < labels.Count; c++)
            for (var band = 0; band < areas.Count; band++)
            {
                if (!cells.TryGetValue((band, c), out var members)) continue;
                var current = c;
                double Barycenter(Tech t)
                {
                    var rows = t.Prerequisites
                        .Select(p => db.Techs.TryGetValue(p, out var pre) ? pre : null)
                        .OfType<Tech>()
                        .Where(pre => column.TryGetValue(pre.Key, out var pc) && pc < current)
                        .Select(pre => (double)BandOf(pre) * BandRowScale + row[pre.Key])
                        .ToList();
                    return rows.Count == 0 ? double.MaxValue : rows.Average();
                }
                var ordered = members
                    .OrderBy(t => t.Category is null ? 1 : 0)
                    .ThenBy(t => t.Category ?? "", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(Barycenter)
                    .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.Key, StringComparer.Ordinal)
                    .ToList();
                cells[(band, c)] = ordered;
                for (var i = 0; i < ordered.Count; i++)
                {
                    row[ordered[i].Key] = i;
                    column[ordered[i].Key] = c;
                }
            }

        var bands = new List<OverviewBand>();
        var y = HeaderHeight;
        for (var band = 0; band < areas.Count; band++)
        {
            var rows = Math.Max(1, cells.Where(kv => kv.Key.Band == band).Select(kv => kv.Value.Count).DefaultIfEmpty(0).Max());
            bands.Add(new OverviewBand(areas[band], AreaLabel(areas[band]), y, rows * RowPitch));
            y += rows * RowPitch + BandGap;
        }
        var columns = labels.Select((label, i) => new OverviewColumn(label, LabelGutter + i * ColumnWidth)).ToList();

        var nodes = new List<OverviewNode>();
        foreach (var ((band, c), members) in cells.OrderBy(kv => kv.Key.Column).ThenBy(kv => kv.Key.Band))
            for (var i = 0; i < members.Count; i++)
                nodes.Add(new OverviewNode(members[i].Key, areas[band], c, i, columns[c].X, bands[band].Y + i * RowPitch));

        var edges = new List<OverviewEdge>();
        foreach (var t in techs)
            foreach (var p in t.Prerequisites.Distinct(StringComparer.OrdinalIgnoreCase))
                if (db.Techs.TryGetValue(p, out var pre))
                    edges.Add(new OverviewEdge(pre.Key, t.Key, column[t.Key] > column[pre.Key]));

        return new TechOverview(nodes, edges, bands, columns, LabelGutter + labels.Count * ColumnWidth, bands[^1].Y + bands[^1].Height);
    }

    static string AreaLabel(TechArea area) => area switch
    {
        TechArea.Physics => "Physics",
        TechArea.Society => "Society",
        TechArea.Engineering => "Engineering",
        _ => "Other",
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests --filter TechOverviewLayoutTests`
Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
git add FazStellarisModmanager.Core/Technology/TechOverviewLayout.cs FazStellarisModmanager.Tests/TechOverviewLayoutTests.cs
git commit -m "Tech: whole-tree layout in area bands and tier columns"
```

---

### Task 3: Saved research state (`ResearchStateStore`) and service wiring

**Files:**
- Create: `FazStellarisModmanager.Core/Technology/ResearchStateStore.cs`
- Modify: `FazStellarisModmanager.Core/AppPaths.cs`
- Modify: `FazStellarisModmanager.Core/Technology/TechTreeService.cs`
- Test: `FazStellarisModmanager.Tests/ResearchStateStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ResearchStateStoreTests
{
    [Fact]
    public void Round_trips_sorted_and_deduplicated_keys_per_label()
    {
        using var tmp = new TempDir();
        var store = new ResearchStateStore(Path.Combine(tmp.Path, "research"));

        store.Save("My list", new ResearchState(["b", "a", "A", " "], ["t"]));
        store.Save("Other", new ResearchState(["x"], []));

        var mine = store.Load("My list");
        Assert.Equal(["a", "b"], mine.Researched);
        Assert.Equal(["t"], mine.Targets);
        Assert.Equal(["x"], store.Load("Other").Researched);
        Assert.Same(ResearchState.Empty, store.Load("Never saved"));
    }

    [Fact]
    public void File_names_are_safe()
    {
        var store = new ResearchStateStore(Path.Combine("C:", "r"));

        Assert.Equal("a_b.json", Path.GetFileName(store.PathFor("a:b")));
        Assert.Equal("Current game (dlc_load.json).json", Path.GetFileName(store.PathFor("Current game (dlc_load.json)")));
        Assert.Equal("_CON.json", Path.GetFileName(store.PathFor("CON")));
        Assert.Equal("_.json", Path.GetFileName(store.PathFor("  ")));
    }

    [Fact]
    public void A_corrupt_file_gives_empty_state_and_a_warning()
    {
        using var tmp = new TempDir();
        var store = new ResearchStateStore(Path.Combine(tmp.Path, "research"));
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.PathFor("Bad"), "{ not json");
        File.WriteAllText(store.PathFor("Blank"), "{}");
        var warnings = new List<string>();

        Assert.Same(ResearchState.Empty, store.Load("Bad", warnings));
        Assert.Single(warnings);
        var blank = store.Load("Blank", warnings);
        Assert.Empty(blank.Researched);
        Assert.Empty(blank.Targets);
        Assert.Single(warnings);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests --filter ResearchStateStoreTests`
Expected: the build fails with "The type or namespace name 'ResearchStateStore' could not be found".

- [ ] **Step 3: Implement `ResearchStateStore.cs`**

```csharp
using System.Text.Json;
using FazStellarisModmanager.Core.IO;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>Techs marked researched and route targets for one mod-list choice. Keys are kept as written, even ones not in the current tree.</summary>
public sealed record ResearchState(IReadOnlyList<string> Researched, IReadOnlyList<string> Targets)
{
    public static ResearchState Empty { get; } = new([], []);
}

/// <summary>Stores each choice's <see cref="ResearchState"/> as &lt;directory&gt;\&lt;file-safe label&gt;.json.</summary>
public sealed class ResearchStateStore(string directory)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    static readonly HashSet<string> Reserved = new(
        ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).Select(i => "COM" + i), .. Enumerable.Range(1, 9).Select(i => "LPT" + i)],
        StringComparer.OrdinalIgnoreCase);

    sealed record StateFile(List<string?>? Researched, List<string?>? Targets);

    public string Directory { get; } = directory;

    /// <summary>The saved state; <see cref="ResearchState.Empty"/> when there is none. A corrupt file gives the empty state plus a warning.</summary>
    public ResearchState Load(string label, ICollection<string>? warnings = null)
    {
        var path = PathFor(label);
        if (!File.Exists(path)) return ResearchState.Empty;
        try
        {
            var file = JsonSerializer.Deserialize<StateFile>(File.ReadAllText(path), Json);
            return new ResearchState(Normalize(file?.Researched), Normalize(file?.Targets));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            warnings?.Add($"Research progress for '{label}' could not be read: {ex.Message}");
            return ResearchState.Empty;
        }
    }

    public void Save(string label, ResearchState state)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var file = new StateFile([.. Normalize(state.Researched)], [.. Normalize(state.Targets)]);
        AtomicFile.WriteAllText(PathFor(label), JsonSerializer.Serialize(file, Json));
    }

    /// <summary>File for a label: invalid characters become "_", reserved device names get a "_" prefix, an empty label is "_".</summary>
    public string PathFor(string label)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(label.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (safe.Length > 100) safe = safe[..100];
        safe = safe.TrimEnd('.', ' ');
        if (safe.Length == 0) safe = "_";
        if (Reserved.Contains(safe.Split('.')[0].TrimEnd(' '))) safe = "_" + safe;
        return Path.Combine(Directory, safe + ".json");
    }

    static List<string> Normalize(IEnumerable<string?>? keys) =>
        (keys ?? []).OfType<string>()
            .Select(k => k.Trim())
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
```

- [ ] **Step 4: Add the folder to `AppPaths.cs`** (after the `Icons` line)

```csharp
    public string Research => Path.Combine(Root, "research");
```

- [ ] **Step 5: Expose the store and an icon counter on `TechTreeService.cs`**

In the constructor, after `_icons = new IconCache(manager.Paths.Icons);`, add:

```csharp
        Research = new ResearchStateStore(manager.Paths.Research);
```

Next to the other public members (after `public event Action? Changed;`), add:

```csharp
    /// <summary>Researched marks and route targets, saved per mod-list choice label.</summary>
    public ResearchStateStore Research { get; }

    /// <summary>How many icons of the current tree are ready; changes while the background prewarm runs.</summary>
    public int IconCount => _current?.Icons.Count ?? 0;
```

- [ ] **Step 6: Run all tests**

Run: `dotnet test FazStellarisModmanager.Tests`
Expected: all pass, including the 3 new store tests.

- [ ] **Step 7: Commit**

```bash
git add FazStellarisModmanager.Core/Technology/ResearchStateStore.cs FazStellarisModmanager.Core/AppPaths.cs FazStellarisModmanager.Core/Technology/TechTreeService.cs FazStellarisModmanager.Tests/ResearchStateStoreTests.cs
git commit -m "Tech: save researched techs and route targets per mod list"
```

---

### Task 4: Map components (`OverviewLayer`, `TechOverviewView`, JS measure, CSS)

**Files:**
- Create: `FazStellarisModmanager/wwwroot/js/overview.js`
- Create: `FazStellarisModmanager/Components/OverviewLayer.razor`
- Create: `FazStellarisModmanager/Components/TechOverviewView.razor`
- Modify: `FazStellarisModmanager/wwwroot/css/site.css` (append)

These are UI components with no unit tests; they are verified by building here and manually in Task 6.

- [ ] **Step 1: Create `wwwroot/js/overview.js`**

```js
// Measures an element for the tech overview's pan and zoom; all other logic is C#.
export function rect(el) {
    const r = el.getBoundingClientRect();
    return [r.left, r.top, r.width, r.height];
}
```

- [ ] **Step 2: Create `Components/OverviewLayer.razor`**

```razor
@using System.Globalization

@foreach (var band in Overview.Bands)
{
    <rect class="ov-band" x="0" y="@F(band.Y - BandPad)" width="@F(Overview.Width)" height="@F(band.Height - (TechOverviewLayout.RowPitch - TechOverviewLayout.NodeHeight) + 2 * BandPad)" />
    <text class="ov-band-label" x="12" y="@F(band.Y + 22)" fill="@AreaColor(band.Area)">@band.Label</text>
}
@foreach (var col in Overview.Columns)
{
    <text class="ov-col-label" x="@F(col.X + TechOverviewLayout.NodeWidth / 2)" y="18" text-anchor="middle">@col.Label</text>
}
@foreach (var e in Overview.Edges)
{
    if (Overview.Find(e.From) is not { } from || Overview.Find(e.To) is not { } to) continue;
    <path class="@EdgeClass(e)" d="@EdgePath(e, from, to)" />
}
@foreach (var n in Overview.Nodes)
{
    var t = Database.Techs[n.Key];
    var key = n.Key;
    <g class="@NodeClass(t)" @onclick="args => OnNodeClick.InvokeAsync((key, args))"
       @oncontextmenu="() => OnNodeContext.InvokeAsync(key)" @oncontextmenu:preventDefault>
        <title>@Tooltip(t)</title>
        @if (Detailed)
        {
            <rect class="ov-box" x="@F(n.X)" y="@F(n.Y)" width="@F(TechOverviewLayout.NodeWidth)" height="@F(TechOverviewLayout.NodeHeight)" rx="4" />
            <rect x="@F(n.X)" y="@F(n.Y)" width="3" height="@F(TechOverviewLayout.NodeHeight)" fill="@AreaColor(t.Area)" />
            @if (IconUri(t) is { } icon)
            {
                <image href="@icon" x="@F(n.X + 6)" y="@F(n.Y + 5)" width="24" height="24" />
            }
            <text class="ov-name" x="@F(n.X + 34)" y="@F(n.Y + 21)">@Mark(t)@Trim(t.Name, 20)</text>
        }
        else
        {
            <circle class="ov-dot" cx="@F(n.X + TechOverviewLayout.NodeWidth / 2)" cy="@F(n.Y + TechOverviewLayout.NodeHeight / 2)" r="12" fill="@AreaColor(t.Area)" />
        }
    </g>
}

@code {
    [Parameter, EditorRequired] public TechOverview Overview { get; set; } = default!;
    [Parameter, EditorRequired] public TechDatabase Database { get; set; } = default!;
    [Parameter, EditorRequired] public Func<Tech, string?> IconUri { get; set; } = default!;
    [Parameter] public int IconVersion { get; set; }
    [Parameter] public string? SelectedKey { get; set; }
    [Parameter] public ResearchRoute Route { get; set; } = ResearchRoute.Empty;
    [Parameter] public IReadOnlySet<string> Targets { get; set; } = new HashSet<string>();
    [Parameter] public IReadOnlySet<string> Researched { get; set; } = new HashSet<string>();
    [Parameter] public bool Detailed { get; set; }
    [Parameter] public EventCallback<(string Key, MouseEventArgs Args)> OnNodeClick { get; set; }
    [Parameter] public EventCallback<string> OnNodeContext { get; set; }

    const double BandPad = 10;

    // Pan and zoom re-render the parent with unchanged inputs; redraw the ~2,000 elements only when something shown changed.
    object?[] _shown = [];
    bool _changed = true;

    protected override void OnParametersSet()
    {
        object?[] now = [Overview, Database, IconVersion, SelectedKey, Route, Targets, Researched, Detailed];
        _changed = now.Length != _shown.Length || now.Where((v, i) => !Equals(v, _shown[i])).Any();
        _shown = now;
    }

    protected override bool ShouldRender() => _changed;

    bool RouteOn => Route.Steps.Count > 0;

    string NodeClass(Tech t)
    {
        var cls = "ov-node";
        if (t.Key == SelectedKey) cls += " sel";
        if (Targets.Contains(t.Key)) cls += " tgt";
        if (Researched.Contains(t.Key)) cls += " done";
        if (t.ChangedByMods) cls += " mod";
        if (RouteOn && !Route.Needed.Contains(t.Key) && !Targets.Contains(t.Key)) cls += " dim";
        return cls;
    }

    string EdgeClass(OverviewEdge e) =>
        !RouteOn ? "ov-edge"
        : Route.Needed.Contains(e.From) && Route.Needed.Contains(e.To) ? "ov-edge route"
        : "ov-edge dim";

    string Mark(Tech t) => Researched.Contains(t.Key) ? "✓ " : Targets.Contains(t.Key) ? "★ " : "";

    string Tooltip(Tech t)
    {
        var tier = t.IsRepeatable ? "R" : t.Tier?.ToString(CultureInfo.InvariantCulture) ?? "?";
        var state = Researched.Contains(t.Key) ? " · researched" : Targets.Contains(t.Key) ? " · route target" : "";
        return $"{t.Name} · Tier {tier} · Cost {t.Cost}{state} (right-click: researched, Shift+click: route target)";
    }

    static string EdgePath(OverviewEdge e, OverviewNode from, OverviewNode to)
    {
        const double w = TechOverviewLayout.NodeWidth, h = TechOverviewLayout.NodeHeight;
        if (e.Forward)
        {
            double x1 = from.X + w, y1 = from.Y + h / 2, x2 = to.X, y2 = to.Y + h / 2, bend = Math.Max(30, (x2 - x1) / 2);
            return $"M{F(x1)},{F(y1)} C{F(x1 + bend)},{F(y1)} {F(x2 - bend)},{F(y2)} {F(x2)},{F(y2)}";
        }
        double sx = from.X + w / 2, sy = from.Y, ex = to.X + w / 2, ey = to.Y, lift = 40 + Math.Abs(sy - ey) / 4;
        return $"M{F(sx)},{F(sy)} C{F(sx)},{F(sy - lift)} {F(ex)},{F(ey - lift)} {F(ex)},{F(ey)}";
    }

    static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    static string AreaColor(TechArea a) => a switch
    {
        TechArea.Physics => "#3fa7ff",
        TechArea.Society => "#46c46e",
        TechArea.Engineering => "#e3a33b",
        _ => "#8a96a6",
    };
}
```

- [ ] **Step 3: Create `Components/TechOverviewView.razor`**

```razor
@using System.Globalization
@implements IAsyncDisposable
@inject IJSRuntime JS

<div class="overview" @ref="_host"
     @onwheel="OnWheel" @onwheel:preventDefault
     @onmousedown="OnMouseDown" @onmousemove="OnMouseMove" @onmouseup="EndDrag" @onmouseleave="EndDrag"
     @oncontextmenu:preventDefault>
    <svg class="overview-svg">
        <g transform="translate(@F(_tx) @F(_ty)) scale(@_scale.ToString("0.#####", CultureInfo.InvariantCulture))">
            <OverviewLayer Overview="Overview" Database="Database" IconUri="IconUri" IconVersion="IconVersion" SelectedKey="SelectedKey"
                           Route="Route" Targets="Targets" Researched="Researched" Detailed="_scale >= DetailScale"
                           OnNodeClick="NodeClick" OnNodeContext="key => OnToggleResearched.InvokeAsync(key)" />
        </g>
    </svg>
    <div class="overview-controls" @onmousedown:stopPropagation>
        <button title="Zoom out" @onclick="() => ZoomCentre(1 / ButtonStep)">−</button>
        <button title="Zoom in" @onclick="() => ZoomCentre(ButtonStep)">+</button>
        <button title="Show the whole tree" @onclick="FitAsync">Fit</button>
    </div>
</div>

@code {
    [Parameter, EditorRequired] public TechOverview Overview { get; set; } = default!;
    [Parameter, EditorRequired] public TechDatabase Database { get; set; } = default!;
    [Parameter, EditorRequired] public Func<Tech, string?> IconUri { get; set; } = default!;
    [Parameter] public int IconVersion { get; set; }
    [Parameter] public string? SelectedKey { get; set; }
    [Parameter] public ResearchRoute Route { get; set; } = ResearchRoute.Empty;
    [Parameter] public IReadOnlySet<string> Targets { get; set; } = new HashSet<string>();
    [Parameter] public IReadOnlySet<string> Researched { get; set; } = new HashSet<string>();
    [Parameter] public EventCallback<string> OnSelect { get; set; }
    [Parameter] public EventCallback<string> OnToggleResearched { get; set; }
    [Parameter] public EventCallback<string> OnToggleTarget { get; set; }

    const double MinScale = 0.08, MaxScale = 2, WheelStep = 1.15, ButtonStep = 1.4, DetailScale = 0.35, DragThreshold = 4, FitMargin = 0.95;

    ElementReference _host;
    IJSObjectReference? _module;
    double _scale = 0.3, _tx, _ty;
    double _left, _top, _width, _height;   // host rectangle in client pixels, from the last measurement
    TechOverview? _fitted;                 // the overview the view was last fitted to
    string? _shownSelection;
    bool _centreSelection;
    bool _down;
    double _downX, _downY, _lastX, _lastY, _moved;

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_fitted, Overview) || SelectedKey == _shownSelection) return;
        _shownSelection = SelectedKey;
        _centreSelection = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!ReferenceEquals(_fitted, Overview))
        {
            _shownSelection = SelectedKey;
            await FitAsync();
            return;
        }
        if (!_centreSelection) return;
        _centreSelection = false;
        if (SelectedKey is null || Overview.Find(SelectedKey) is not { } node || !await MeasureAsync()) return;
        double sx = node.X * _scale + _tx, sy = node.Y * _scale + _ty;
        var visible = sx >= 0 && sy >= 0
            && sx + TechOverviewLayout.NodeWidth * _scale <= _width
            && sy + TechOverviewLayout.NodeHeight * _scale <= _height;
        if (visible) return;
        _tx = _width / 2 - (node.X + TechOverviewLayout.NodeWidth / 2) * _scale;
        _ty = _height / 2 - (node.Y + TechOverviewLayout.NodeHeight / 2) * _scale;
        StateHasChanged();
    }

    async Task<bool> MeasureAsync()
    {
        try
        {
            _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/overview.js");
            var r = await _module.InvokeAsync<double[]>("rect", _host);
            (_left, _top, _width, _height) = (r[0], r[1], r[2], r[3]);
            return _width > 0 && _height > 0;
        }
        catch (JSDisconnectedException) { return false; }
        catch (JSException) { return false; }
    }

    async Task FitAsync()
    {
        _fitted = Overview;
        if (!await MeasureAsync()) return;
        _scale = Math.Clamp(Math.Min(_width / Overview.Width, _height / Overview.Height) * FitMargin, MinScale, MaxScale);
        _tx = (_width - Overview.Width * _scale) / 2;
        _ty = (_height - Overview.Height * _scale) / 2;
        StateHasChanged();
    }

    async Task OnWheel(WheelEventArgs e)
    {
        if (await MeasureAsync()) ZoomAt(e.ClientX - _left, e.ClientY - _top, e.DeltaY < 0 ? WheelStep : 1 / WheelStep);
    }

    async Task ZoomCentre(double factor)
    {
        if (await MeasureAsync()) ZoomAt(_width / 2, _height / 2, factor);
    }

    // Keeps the map point under (px, py) in place.
    void ZoomAt(double px, double py, double factor)
    {
        var next = Math.Clamp(_scale * factor, MinScale, MaxScale);
        _tx = px - (px - _tx) * next / _scale;
        _ty = py - (py - _ty) * next / _scale;
        _scale = next;
    }

    void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != 0) return;
        (_down, _downX, _downY, _lastX, _lastY, _moved) = (true, e.ClientX, e.ClientY, e.ClientX, e.ClientY, 0);
    }

    void OnMouseMove(MouseEventArgs e)
    {
        if (!_down) return;
        _tx += e.ClientX - _lastX;
        _ty += e.ClientY - _lastY;
        (_lastX, _lastY) = (e.ClientX, e.ClientY);
        _moved = Math.Max(_moved, Math.Abs(e.ClientX - _downX) + Math.Abs(e.ClientY - _downY));
    }

    // _moved is kept until the next mouse down, so the click that follows a drag can be ignored.
    void EndDrag() => _down = false;

    Task NodeClick((string Key, MouseEventArgs Args) click)
    {
        if (_moved > DragThreshold) return Task.CompletedTask;
        return click.Args.ShiftKey ? OnToggleTarget.InvokeAsync(click.Key) : OnSelect.InvokeAsync(click.Key);
    }

    static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try { await _module.DisposeAsync(); }
        catch (JSDisconnectedException) { }
    }
}
```

- [ ] **Step 4: Append the styles to `wwwroot/css/site.css`**

```css
.view-switch { display: flex; gap: .6rem; align-items: center; }
.seg { display: inline-flex; }
.seg button { border-radius: 0; }
.seg button:first-child { border-radius: 6px 0 0 6px; }
.seg button:last-child { border-radius: 0 6px 6px 0; border-left: none; }
.seg button.on { background: #203247; border-color: var(--accent); }
.route-bar { display: flex; gap: .5rem; align-items: center; flex-wrap: wrap; }
.route-chip { display: inline-flex; gap: .3rem; align-items: center; border: 1px solid #c9a24a; background: #3a2e12; border-radius: 12px; padding: .1rem .5rem; }
.route-chip .x { border: 0; background: none; padding: 0 .1rem; color: var(--muted); }
.overview { position: relative; flex: 1; min-height: 420px; overflow: hidden; background: #0e1218; border: 1px solid var(--border); border-radius: 8px; cursor: grab; user-select: none; }
.overview-svg { width: 100%; height: 100%; display: block; position: absolute; inset: 0; }
.overview-controls { position: absolute; top: .5rem; right: .5rem; display: flex; gap: .3rem; }
.ov-band { fill: #ffffff05; stroke: #ffffff10; }
.ov-band-label { font-size: 15px; font-weight: 700; }
.ov-col-label { fill: var(--muted); font-size: 13px; }
.ov-edge { fill: none; stroke: #3a4757; stroke-width: 1.2; }
.ov-edge.route { stroke: #f2c983; stroke-width: 2.4; }
.ov-edge.dim { opacity: .15; }
.ov-node { cursor: pointer; }
.ov-node .ov-box { fill: #1a2029; stroke: #2d3744; stroke-width: 1; }
.ov-node.mod .ov-box { stroke: #e3a33b; }
.ov-node.tgt .ov-box { stroke: #f2c983; stroke-width: 2.5; }
.ov-node.sel .ov-box { stroke: #ffffff; stroke-width: 2.5; }
.ov-node.done .ov-box { stroke-dasharray: 4 3; }
.ov-node.done .ov-name { fill: #6fcf97; }
.ov-node.dim { opacity: .22; }
.ov-node.sel .ov-dot, .ov-node.tgt .ov-dot { stroke: #ffffff; stroke-width: 6; }
.ov-name { fill: #dbe2ea; font-size: 12px; }
.route-actions { display: flex; gap: .4rem; flex-wrap: wrap; }
.route-list { margin: .2rem 0; padding-left: 1.4rem; }
.route-list li.here { font-weight: 600; }
```

- [ ] **Step 5: Build the app**

Run: `dotnet build FazStellarisModmanager`
Expected: 0 errors. If the output folder is locked by a running copy, add `--artifacts-path <scratch>/art`.

- [ ] **Step 6: Commit**

```bash
git add FazStellarisModmanager/wwwroot/js/overview.js FazStellarisModmanager/Components/OverviewLayer.razor FazStellarisModmanager/Components/TechOverviewView.razor FazStellarisModmanager/wwwroot/css/site.css
git commit -m "Tech: zoomable whole-tree map component"
```

---

### Task 5: Route bar, sidebar actions and Tech page wiring

**Files:**
- Create: `FazStellarisModmanager/Components/RouteBar.razor`
- Modify: `FazStellarisModmanager/Components/TechSidebar.razor`
- Modify: `FazStellarisModmanager/Pages/TechPage.razor`

- [ ] **Step 1: Create `Components/RouteBar.razor`**

```razor
@using System.Globalization

<div class="route-bar">
    <b>Route:</b>
    @if (Targets.Count == 0)
    {
        <span class="muted small">Shift+click techs on the map, or use ★ Add to route in the sidebar.</span>
    }
    @foreach (var key in Targets.Order(StringComparer.OrdinalIgnoreCase))
    {
        var k = key;
        var name = Database.Techs.TryGetValue(k, out var t) ? t.Name : k;
        <span class="route-chip">
            <button type="button" class="link" @onclick="() => OnSelect.InvokeAsync(k)">@name</button>
            <button type="button" class="x" title="Remove from route" @onclick="() => OnRemoveTarget.InvokeAsync(k)">✕</button>
        </span>
    }
    @if (Targets.Count > 0)
    {
        <span>@Route.Steps.Count techs · @Route.TotalCost.ToString("N0", CultureInfo.InvariantCulture) research@(Route.UnknownCostCount > 0 ? $" + {Route.UnknownCostCount} unknown" : "")</span>
        <button @onclick="() => OnClearRoute.InvokeAsync()">Clear route</button>
    }
    @if (_confirming)
    {
        <span>Unresearch @ResearchedCount techs?</span>
        <button class="danger" @onclick="Confirm">Yes</button>
        <button @onclick="() => _confirming = false">No</button>
    }
    else if (ResearchedCount > 0)
    {
        <button @onclick="Ask">Unresearch all (@ResearchedCount)</button>
    }
</div>

@code {
    [Parameter, EditorRequired] public TechDatabase Database { get; set; } = default!;
    [Parameter] public IReadOnlySet<string> Targets { get; set; } = new HashSet<string>();
    [Parameter] public ResearchRoute Route { get; set; } = ResearchRoute.Empty;
    [Parameter] public int ResearchedCount { get; set; }
    [Parameter] public EventCallback<string> OnSelect { get; set; }
    [Parameter] public EventCallback<string> OnRemoveTarget { get; set; }
    [Parameter] public EventCallback OnClearRoute { get; set; }
    [Parameter] public EventCallback OnUnresearchAll { get; set; }

    const int ConfirmAbove = 10;
    bool _confirming;

    Task Ask()
    {
        if (ResearchedCount <= ConfirmAbove) return OnUnresearchAll.InvokeAsync();
        _confirming = true;
        return Task.CompletedTask;
    }

    Task Confirm()
    {
        _confirming = false;
        return OnUnresearchAll.InvokeAsync();
    }
}
```

- [ ] **Step 2: Add the route actions and Research route section to `TechSidebar.razor`**

Insert this block directly after the closing `</div>` of the `<div class="path">…</div>` block (before `<details open>` for Unlocks):

```razor
    <div class="route-actions">
        <button @onclick="() => OnToggleTarget.InvokeAsync(Tech.Key)">@(Targets.Contains(Tech.Key) ? "★ Remove from route" : "★ Add to route")</button>
        <button @onclick="() => OnToggleResearched.InvokeAsync(Tech.Key)">@(Researched.Contains(Tech.Key) ? "✓ Unmark researched" : "✓ Mark researched")</button>
    </div>

    @if (Targets.Count > 0)
    {
        <details open>
            <summary>Research route (@Route.Steps.Count)</summary>
            @if (Route.Steps.Count == 0)
            {
                <p class="muted small">Everything the route needs is already researched.</p>
            }
            <ol class="route-list">
                @foreach (var stepKey in Route.Steps)
                {
                    var step = Database.Techs[stepKey];
                    var key = step.Key;
                    <li class="@(key == Tech.Key ? "here" : "")">
                        <button type="button" class="link" @onclick="() => OnSelect.InvokeAsync(key)">@step.Name</button>
                        <span class="muted small">· @AreaLetter(step.Area)@StepTier(step)@(Targets.Contains(key) ? " ★" : "")</span>
                    </li>
                }
            </ol>
            <div class="muted small">Already researched and skipped: @Route.SkippedResearched (+ @Route.SkippedStarting starting techs)</div>
            @foreach (var m in Route.MissingPrerequisites)
            {
                <div class="muted small">@m.Tech needs @m.Prerequisite, which is not in this tree.</div>
            }
            @if (Route.Cycles.Count > 0)
            {
                <div class="muted small">Prerequisite loop: @string.Join(", ", Route.Cycles)</div>
            }
            @foreach (var u in Route.UnknownTargets)
            {
                <div class="muted small">Target @u is not in this tree.</div>
            }
        </details>
    }
```

In the `@code` block, after the existing `[Parameter] public EventCallback<string> OnSelect { get; set; }` line, add:

```csharp
    [Parameter] public IReadOnlySet<string> Targets { get; set; } = new HashSet<string>();
    [Parameter] public IReadOnlySet<string> Researched { get; set; } = new HashSet<string>();
    [Parameter] public ResearchRoute Route { get; set; } = ResearchRoute.Empty;
    [Parameter] public EventCallback<string> OnToggleTarget { get; set; }
    [Parameter] public EventCallback<string> OnToggleResearched { get; set; }

    static string AreaLetter(TechArea a) => a switch
    {
        TechArea.Physics => "P",
        TechArea.Society => "S",
        TechArea.Engineering => "E",
        _ => "?",
    };

    static string StepTier(Tech t) => t.IsRepeatable ? "R" : t.Tier?.ToString(CultureInfo.InvariantCulture) ?? "?";
```

- [ ] **Step 3: Wire the page (`Pages/TechPage.razor`)**

3a. Replace the whole `<div class="tech-focus"> … </div>` block (it currently starts with `@if (selectedKey is not null && db.Techs.TryGetValue(selectedKey, out var sel))` and holds the `ghead` and `graph-scroll`) with:

```razor
            <div class="tech-focus">
                <div class="view-switch">
                    <span class="seg">
                        <button class="@(wholeTree ? "" : "on")" @onclick="() => wholeTree = false">Focus</button>
                        <button class="@(wholeTree ? "on" : "")" @onclick="() => wholeTree = true">Whole tree</button>
                    </span>
                    @if (wholeTree)
                    {
                        <span class="small">Drag to pan · wheel to zoom · right-click: researched · Shift+click: route target</span>
                    }
                </div>
                @if (wholeTree || targets.Count > 0)
                {
                    <RouteBar Database="db" Targets="targets" Route="route" ResearchedCount="researched.Count"
                              OnSelect="key => selectedKey = key" OnRemoveTarget="ToggleTarget" OnClearRoute="ClearRoute" OnUnresearchAll="UnresearchAll" />
                }
                @if (wholeTree)
                {
                    <TechOverviewView Overview="OverviewFor(db)" Database="db" IconUri="Tree.IconUri" IconVersion="Tree.IconCount"
                                      SelectedKey="selectedKey" Route="route" Targets="targets" Researched="researched"
                                      OnSelect="key => selectedKey = key" OnToggleResearched="ToggleResearched" OnToggleTarget="ToggleTarget" />
                }
                else if (selectedKey is not null && db.Techs.TryGetValue(selectedKey, out var sel))
                {
                    <div class="ghead">
                        <span>← Requires</span>
                        <b>@sel.Name</b>
                        <span>Leads to →</span>
                        <label class="inline">Depth
                            <select @bind="depth">
                                <option value="1">1</option>
                                <option value="2">2</option>
                                <option value="3">3</option>
                            </select>
                        </label>
                    </div>
                    <div class="graph-scroll">
                        <TechGraphView Graph="TechGraphLayout.Build(db, sel.Key, depth)" Database="db" IconUri="Tree.IconUri" OnSelect="key => selectedKey = key" />
                    </div>
                }
                else
                {
                    <p class="status">Pick a technology on the left.</p>
                }
            </div>
```

3b. Replace the `<TechSidebar … />` element with:

```razor
                    <TechSidebar Tech="side" Database="db" Tree="Tree" OnSelect="key => selectedKey = key"
                                 Targets="targets" Researched="researched" Route="route"
                                 OnToggleTarget="ToggleTarget" OnToggleResearched="ToggleResearched" />
```

3c. In `@code`, after the `CancellationTokenSource? searchCts;` field, add:

```csharp
    bool wholeTree;
    TechTree? stateFor;   // the tree whose saved research state is loaded
    HashSet<string> researched = new(StringComparer.OrdinalIgnoreCase);
    HashSet<string> targets = new(StringComparer.OrdinalIgnoreCase);
    ResearchRoute route = ResearchRoute.Empty;
    TechOverview? overview;
    TechDatabase? overviewDb;

    TechOverview OverviewFor(TechDatabase db)
    {
        if (!ReferenceEquals(overviewDb, db)) (overview, overviewDb) = (TechOverviewLayout.Build(db), db);
        return overview!;
    }

    void SyncResearchState()
    {
        if (Tree.Current is not { } tree || ReferenceEquals(stateFor, tree)) return;
        stateFor = tree;
        var warnings = new List<string>();
        var state = Tree.Research.Load(tree.Label, warnings);
        researched = new HashSet<string>(state.Researched, StringComparer.OrdinalIgnoreCase);
        targets = new HashSet<string>(state.Targets, StringComparer.OrdinalIgnoreCase);
        RecomputeRoute();
        if (warnings.Count > 0) (status, error) = (warnings[0], true);
    }

    void RecomputeRoute() =>
        route = targets.Count == 0 || Tree.Current is null ? ResearchRoute.Empty : ResearchPlan.Build(Tree.Current.Database, targets, researched);

    // New set instances on every change: the map compares them by reference to decide whether to redraw.
    void ChangeResearch(Action<HashSet<string>, HashSet<string>> change)
    {
        var r = new HashSet<string>(researched, StringComparer.OrdinalIgnoreCase);
        var t = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);
        change(r, t);
        (researched, targets) = (r, t);
        RecomputeRoute();
        if (stateFor is null) return;
        try
        {
            Tree.Research.Save(stateFor.Label, new ResearchState([.. researched], [.. targets]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            (status, error) = ($"Could not save research progress: {ex.Message}", true);
        }
    }

    void ToggleResearched(string key) => ChangeResearch((r, _) => { if (!r.Remove(key)) r.Add(key); });
    void ToggleTarget(string key) => ChangeResearch((_, t) => { if (!t.Remove(key)) t.Add(key); });
    void ClearRoute() => ChangeResearch((_, t) => t.Clear());
    void UnresearchAll() => ChangeResearch((r, _) => r.Clear());
```

3d. Call `SyncResearchState()` in three places:
- in `OnInitializedAsync`, as the last line inside `if (Tree.Current is { } current) { … }`;
- in `OnTreeChanged`, inside the `InvokeAsync` lambda, just before `StateHasChanged();`;
- in `Build()`, right after `await Tree.BuildAsync(choices[choiceIndex]);`.

- [ ] **Step 4: Build and run all tests**

Run: `dotnet build FazStellarisModmanager` then `dotnet test FazStellarisModmanager.Tests`
Expected: 0 errors; all tests pass.

- [ ] **Step 5: Commit**

```bash
git add FazStellarisModmanager/Components/RouteBar.razor FazStellarisModmanager/Components/TechSidebar.razor FazStellarisModmanager/Pages/TechPage.razor
git commit -m "Tech: whole-tree view, route bar and research marks in the Tech tab"
```

---

### Task 6: Manual verification

- [ ] **Step 1: Run the app** (`dotnet run --project FazStellarisModmanager`) and open the Tech tab with the current game.
- [ ] **Step 2: Check the map.** Click **Whole tree**. The map is fitted and shows three bands and the tier columns. At the fitted zoom, techs are dots; zooming in with the wheel (about the cursor) turns them into boxes with icons and names.
- [ ] **Step 3: Check pan and clicks.** Dragging pans, and a drag never selects a tech. A plain click selects: the sidebar updates and the left list highlights the same tech.
- [ ] **Step 4: Check targets.** Shift+click two techs. Gold route lines and stars appear, the rest is dimmed, and the route bar shows both chips with count and cost. The sidebar shows an ordered Research route whose entries select techs.
- [ ] **Step 5: Check researched marks.** Right-click a tech on the route: it shows ✓, leaves the route, and the count and cost drop. With more than 10 marked, **Unresearch all** asks Yes / No.
- [ ] **Step 6: Check saving.** Switch to Focus: the route bar is still shown while there are targets. Restart the app: targets and researched marks are restored for the same mod list, and a different mod list starts empty.
- [ ] **Step 7: Check centring.** Selecting a tech in the left list while it is off-screen on the map centres it.

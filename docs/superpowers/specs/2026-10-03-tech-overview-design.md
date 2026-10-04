# Whole Tech Tree Overview & Research Route — Design (Sub-project 6)

## Goal
The Tech tab gains a **whole-tree view**: every technology of the current mod list on one zoomable map. On that map you can:
- see the tree's overall shape;
- plan a research route to one or more targets;
- mark techs as already researched;
- jump to any tech's details in the existing sidebar.

Mockups (approved):
- `.superpowers/brainstorm/4540-1791072957/content/overview-layout.html`, option A;
- `.superpowers/brainstorm/4540-1791072957/content/overview-ui.html`.

Reading researched techs from a save game is a later sub-project.

## Facts (base game on the local install)
- **Techs per area and tier:**

  | Area | Count | Per tier |
  |---|---|---|
  | Physics | 168 | T0 10, T1 19, T2 46, T3 29, T4 27, T5 31, Repeatable 6 |
  | Society | 312 | T0 12, T1 39, T2 67, T3 80, T4 47, T5 57, Repeatable 10 |
  | Engineering | 199 | T0 16, T1 24, T2 47, T3 33, T4 34, T5 35, Repeatable 10 |

  Total 679. The largest single cell is Society tier 3, with 80 techs.
- **Links:** 568 prerequisite links, of which 74 cross areas. The longest prerequisite chain is 7.
- **Categories:** 13, between 24 and 121 techs each.
- **Mods:** a large mod list roughly doubles the counts.
- **Prerequisites:** all of a tech's prerequisites are required (AND). Starting techs (`start_tech = yes`) are owned by every empire.

## Decisions

### Placement
- The Tech tab's middle column gets a **Focus | Whole tree** switch. Focus is the current graph.
- The left list and the right sidebar stay. Selection (`selectedKey`) is shared: selecting anywhere updates the list highlight, the sidebar and both views.
- The switch state is kept while the page lives. The default is Focus.

### Layout (`TechOverviewLayout`, Core, pure)
- **Bands:** one band per area, top to bottom: Physics, Society, Engineering, then Other, which appears only if any tech has area Other.
- **Columns:** every tier that some non-repeatable tech uses, in ascending order (no empty columns), then a "Repeatable" column (only if any repeatable tech exists), then a "?" column for techs without a tier, only if any exist. Repeatable techs always go in the Repeatable column, whatever their tier.
- **Order within a cell** (band × column):
  1. category, case-insensitive, with a null category last;
  2. barycenter: the average row of the tech's prerequisites already placed in earlier columns of any band, on a global row scale. Techs without placed prerequisites sort after those with one;
  3. name, case-insensitive.
- **Geometry** (constants in the layout):

  | Constant | Value |
  |---|---|
  | Node size | 168 × 34 |
  | Column width | node width + 72 gap |
  | Row pitch | 42 |
  | Band gap | 36 |
  | Band label gutter (left) | 96 |
  | Column header row (top) | 28 |

  A band's height is its tallest cell. Positions are the node's top-left corner.
- **Output:**
  - `TechOverview(Nodes, Edges, Bands, Columns, Width, Height)`;
  - `OverviewNode(Key, Area, ColumnIndex, Row, X, Y)`;
  - `OverviewBand(Area, Label, Y, Height)`;
  - `OverviewColumn(Label, X)`;
  - `OverviewEdge(From, To, Forward)`. Forward means the target column is greater than the source column. Same-column and backward links have Forward = false.

  Edges whose prerequisite isn't in the tree are left out. Renderers draw:
  - forward edges as a cubic curve from the source's right-middle to the target's left-middle;
  - other edges as an arc that leaves the source's top and enters the target's top.

### Route plan (`ResearchPlan`, Core, pure)
- **Inputs:** `ResearchPlan.Build(db, targets, researched)`.
  - Unknown target keys are ignored and reported in `UnknownTargets`.
  - Researched = the given set ∪ all starting techs.
- **Needed set:** the targets plus all their transitive prerequisites that are in the tree, minus the researched set. A researched tech is not expanded further, because its prerequisites must already be done.
- **Order:** a topological order where every tech comes after its needed prerequisites. Among techs that are ready, the order is (tier, with Repeatable last and null last), then area, then name.
- **Loops:** a dependency loop must not hang. Any techs left over when no tech is ready are appended in the same tie order and listed in `Cycles`.
- **Missing prerequisites:** prerequisites missing from the tree go in `MissingPrerequisites` as (tech, prerequisite) pairs.
- **Cost:** sum of `Tech.Cost` values that parse as invariant-culture numbers. `UnknownCostCount` counts the rest.
- **Skipped:** `SkippedResearched` counts techs in the closure that were skipped because they were in the given researched set. `SkippedStarting` counts those skipped as starting techs.
- **Result:** `ResearchRoute(Steps, TotalCost, UnknownCostCount, SkippedResearched, SkippedStarting, UnknownTargets, MissingPrerequisites, Cycles)`, where Steps are the ordered tech keys. The set of needed keys is exposed for highlighting, along with the edges between them.

### Saved state (`ResearchStateStore`, Core)
- **File:** `%AppData%\FazStellarisModmanager\research\<file-safe list label>.json`, one per mod-list choice label. The current-game choice uses its own label.
- **Content:** `{ "researched": [keys], "targets": [keys] }`, sorted and de-duplicated, with keys stored as written.
- **Writes:** temp file plus replace, matching the other stores.
- **Problems:**
  - A missing file gives an empty state.
  - A corrupt file gives an empty state plus a warning string. The bad file is left in place until the next save overwrites it.
- **Unknown keys:** kept, so a key from a temporarily removed mod survives. Only keys in the current tree affect the plan.

### UI
- **Map (`TechOverviewView`):** pan and zoom logic is C#. The only JavaScript is a 5-line module (`wwwroot/js/overview.js`) that measures the map element, because Blazor cannot read element sizes and Fit and zoom-at-cursor need them. The map is an SVG with an outer `<g transform="translate(x y) scale(s)">`.
  - The layer of nodes and edges is a child component that re-renders only when its inputs change: tree, selection, route set, researched set, or level-of-detail bucket. Pan and zoom only re-render the outer transform.
  - **Pan:** drag with the left button anywhere (pointer events with capture). A drag longer than 4 px does not count as a click.
  - **Zoom:** the wheel zooms about the cursor, by a factor of 1.15 per notch, clamped to 0.08..2.
  - **Buttons:** −, + and Fit (fits the whole map).
  - **Opening:** the first open fits the map. After that, selecting a tech in the list centres it if it is off-screen.
  - **Level of detail:** below 0.35 scale, nodes are dots in the area colour. At or above it, a box with the tech's icon (memo lookup, as elsewhere) and its name, ellipsised.
  - **Band and column labels:** drawn in map space.
  - **States:**
    - selected: highlighted outline;
    - target: ★ and gold outline;
    - researched: ✓, dashed, muted;
    - mod-changed: orange tint, as in the list;
    - route on: techs not in the route and their edges are dimmed, and route edges are gold.
  - **Mouse:**
    - click selects;
    - right-click toggles researched (context menu suppressed);
    - Shift+click toggles target.
  - **Tooltips:** name · tier · cost.
- **Route bar** (above the map in Whole tree mode, and above the focus graph too when there are targets):
  - target chips with ✕;
  - "N techs · cost" (plus "+ M unknown");
  - **Clear route** (targets only);
  - **Unresearch all** (researched only; with more than 10 it asks inline: "Unresearch N techs? Yes / No").
- **Sidebar additions** at the top, under the header:
  - **★ Add to route** / **Remove from route**;
  - **✓ Mark researched** / **Unmark**;
  - a **Research route** section, open by default and only shown when there are targets. It is the ordered steps with tier and area badges (P/S/E + tier), with targets starred, and each entry selects its tech. After the list: "Already researched and skipped: N (+ M starting techs)", plus any missing prerequisites or loops as muted notes.
- **State flow:** the page loads the state when a tree becomes current and saves it after each change. The plan is recomputed on change; it is cheap at this size.

## Errors and performance
- **Layout cost:** the layout is computed once per tree and cached on the page by tree identity. 1,500 nodes is the expected upper size.
- **Saving:** failures (IO) show in the page status line and never crash.
- **Rendering:** the dots level of detail keeps zoomed-out drawing light. Icons use the existing prewarmed memo, so the map does no IO while rendering.

## Testing
- **Layout:**
  - band and column assignment, including Repeatable, unknown tier and area Other;
  - the category-then-barycenter-then-name order;
  - no two nodes overlap;
  - positions match the geometry constants;
  - band heights;
  - the edge Forward flag;
  - missing prerequisites dropped.
- **Route plan:**
  - single target;
  - shared prerequisites counted once across several targets;
  - researched and starting techs skipped, and their prerequisites not expanded;
  - topological order with tie-breaks;
  - loops terminate and are reported;
  - missing prerequisites and unknown targets reported;
  - cost sum with unknown costs.
- **Store:**
  - round trip;
  - separate files per label, with file-safe names;
  - missing file;
  - corrupt file (empty state plus warning);
  - sorted and de-duplicated output.
- **UI:** verified manually (pan, zoom, level of detail, clicks, persistence).

## Out of scope
- Reading save games (later sub-project).
- Research odds and weights in the plan.
- Drawing the route inside the focus graph; the bar and sidebar list still show it there.
- Editing the layout by hand.

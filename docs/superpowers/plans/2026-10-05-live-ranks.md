# Live Game: per-stat ranks and sorting (Sub-project 17)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development or superpowers:executing-plans. Use TDD for the Core work and make one commit per task.

**Goal (user request):**
- Every stat on the Live Game tab shows a rank, in both the sidebar and the scoreboard.
- The scoreboard can be sorted by any stat; the default is victory score.

**Decisions:**
- **Victory score rank:** use the game's own `VictoryRank`, which is global.
- **Other stats** (military, economy and tech power, fleet size, empire size, pops):
  - Rank them among the empires the viewer can see: known rows plus the viewer. Clients never receive stats for uncontacted empires.
  - Ranks are 1-based, highest value first. Use the `StatValue.Real` value. Ties share a rank (competition ranking: 1, 2, 2, 4).
  - Unknown rows have no rank.
- **Scoreboard columns:** add Empire size and Pops, so every sortable stat is visible.
- **Sorting:**
  - Clicking a column header sorts by that stat, highest first; the active header gets a ▼ marker.
  - Sorting by victory score orders rows by `VictoryRank`.
  - With any other stat, known rows come first by value, with ties broken by VictoryRank; unknown rows follow, ordered by VictoryRank.
  - The chosen sort survives new saves while the page stays open.
- **Animation:** changing the sort plays the existing slide (`js/livegame.js` `animate`). The green/red ▲▼ glow must only appear when a *new save* changes a row's victory rank, not when the sort changes.
  - Today `animate` compares `data-rank` (the victory rank), so a re-sort alone changes no ranks and therefore shows no glow; check this.
  - Call `animate` after a sort change too: include the sort in the page's animation key.

## Tasks
1. **Core: `LiveBoard`** (`FazStellarisModmanager.Core/Saves/LiveBoard.cs`) — tests in `LiveBoardTests` or a new `LiveRanksTests`.
   - Add `EmpireSize` and `Pops` to `BoardRow` as `StatValue?`, filled like the others and null for unknown rows. Keep positional compatibility where possible, or update callers and tests.
   - Add `public enum BoardStat { Score, Military, Economy, Tech, Fleet, EmpireSize, Pops }`.
   - Add `public static IReadOnlyDictionary<int, IReadOnlyDictionary<BoardStat, int>> Ranks(IReadOnlyList<BoardRow> rows)`. It maps country id to stat to rank, following the rules above. Score uses `row.Rank`.
   - Add `public static IReadOnlyList<BoardRow> Sort(IReadOnlyList<BoardRow> rows, BoardStat stat)`, following the rules above.
   - Add `public static StatValue? Value(BoardRow row, BoardStat stat)` as a helper.
   - Tests:
     - competition ranking with ties;
     - unknown rows have no rank;
     - Score rank equals VictoryRank;
     - sort by Economy puts the highest economy first and unknown rows last in victory order;
     - sort by Score uses victory order.
2. **UI** (`FazStellarisModmanager/Pages/LiveGamePage.razor`, `wwwroot/css/site.css`):
   - **Scoreboard:**
     - The header cells for Score, Military, Economy, Tech, Fleet, Empire size and Pops become buttons that set the sort (`class="lsort"`, with ▼ on the active one).
     - Each stat cell shows the value with a small muted rank under it or after it (`#3`), using a `.lr` class. Use the `LiveStat` component, plus the rank next to it.
     - Widen the grid for the two new columns. Keep it readable; shrink the number columns a little if needed.
   - **Sidebar:** each stat row shows its rank next to the value ("#2 of 49" or simply "#2"). Use `Ranks` for the viewer's own row. Victory score shows the game rank. Diplomatic weight has no rank.
   - **Data flow:** keep the rows from `LiveBoard.Build`. Compute the ranks once per rebuild, then sort with `LiveBoard.Sort(rows, sort)` when rendering, or when the sort changes.
   - **Animation key:** include the sort, so that a sort change animates the slide.
   - Build the app and run the full suite. Do not launch the app.

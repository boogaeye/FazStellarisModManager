# Mod File Conflicts — Design (Sub-project 12)

## Goal
The Mods page warns about mods that are mostly overwritten by mods later in the load order.
- A row gets **⚠ NN% overwritten** when 75% or more of that mod's content files are also provided by a later mod.
- Clicking the badge opens a details panel under the row. The panel lists the overwritten files, grouped by the mod that wins.

Conflicts between definitions (same object name in different files) and a separate Conflicts tab are out of scope. They may come later.

Mockup (approved, option A): `.superpowers/brainstorm/345-1791165514/content/conflicts.html`.

## Rules
- **Content files:** every file inside a subfolder of the mod's content (folder or `.zip`). Files at the content root (`descriptor.mod`, `thumbnail.png`, readme, …) are ignored, because the game doesn't load them as content.
- **Path comparison:** paths use `/` and are compared case-insensitively. Duplicate paths inside one mod count once.
- **Overwritten:** a file of the mod at position *i* is overwritten when a mod at a later position has the same path. In the details it is credited to the **last** such mod, which is the version the game uses.
- **Percent:** `floor(overwritten × 100 / total)`. **Heavy** means total > 0 and percent ≥ 75.
- **Excluded:** mods with no content files, mods that aren't installed, and unreadable mods have total 0, so no warning. Base-game files are not considered.
- **Duplicates in the list:** if the same mod appears twice, the earlier copy is overwritten by the later one. That is correct.

## Components
- **`Core/Conflicts/ModFiles.List(contentPath)`:**
  - Returns content paths, sorted case-insensitively and with no duplicates, for a folder or `.zip`. Anything else gives an empty list.
  - It throws IO and zip errors.
- **`Core/Conflicts/ModFileCache`:**
  - `Get(contentPath)` remembers results for the app run. Errors give an empty list, which is not remembered.
  - `Clear()` forgets everything; the page calls it on Rescan mods.
  - Thread-safe.
  - Registered as a singleton.
- **`Core/Conflicts/ConflictAnalyzer.Analyze(IReadOnlyList<IReadOnlyCollection<string>> filesInLoadOrder)`:**
  - Returns one `ModConflicts(Total, Overwritten, Groups)` per input. `Groups` is a list of `ConflictGroup(WinnerIndex, Files)`, ordered by winner index, with files sorted.
  - `ModConflicts` also has `Percent` and `IsHeavy`. `HeavyPercent = 75` is a constant on the analyzer.

## Mods page
- **When it runs:** after any list change, the page schedules a check. List changes are: set list (load or import), add, remove, drag move, position change, library refresh. Rescan clears the file cache first.
- **How it runs:**
  - Scheduling cancels the previous pending run.
  - It snapshots the list and the matching installed mods (via the key lookup), waits 500 ms, then lists files and analyzes on the thread pool.
  - It publishes the results on the UI thread unless cancelled.
- **Results:** they are stored per list entry (by reference) along with the snapshot, so they can name the winners. After a change, the old results stay visible until the new run finishes.
- **Badge:** heavy rows show a `⚠ NN% overwritten` button (title: "NN% of this mod's files are overwritten by mods later in the list"). The drag code ignores buttons.
- **Details panel:** clicking the badge toggles the panel. It is an `<li class="conflict-panel">` after the row, keyed to the entry, so it stays open across reorders. It shows:
  - "X of Y files are overwritten by mods later in the list";
  - per group: "by #P Name (N files)", where P is the winner's **current** position, or "?" if it has been removed;
  - the first 10 paths, then a "… N more" link that shows the rest of that group;
  - the hint "Usually means this mod has little effect here. Move it below those mods, or remove it."
- **Disposing** the page cancels any pending run.

## Testing
- **ModFiles:** folder and zip listing; root files excluded; backslashes in zip entry names normalized; case-insensitive de-duplication; a missing path gives an empty list.
- **ModFileCache:** remembers results; `Clear` refreshes; an error gives an empty list that is not remembered.
- **ConflictAnalyzer:**
  - the last mod wins and groups are by winner;
  - percent and the heavy threshold, with 3/4 heavy and 74/100 not;
  - an empty mod gives no warning;
  - case-insensitive matching;
  - an earlier copy of a duplicated mod is fully overwritten;
  - the last mod is never overwritten.
- **UI:** checked by hand.

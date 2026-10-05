# Definition Conflicts Tab — Design (Sub-project 13)

## Goal
A **Conflicts** tab that checks the mod list being edited on the Mods page. It lists every script definition a mod provides that the game does not end up using, says what is used instead, and explains why.

This sub-project also changes the file check from sub-project 12: icon files no longer count.

Mockup (approved): `.superpowers/brainstorm/345-1791165514/content/conflict-tab.html`

The motivating case is Galactic Market Expansion (#4). All four of its `common/strategic_resources` files are replaced by same-named files from mods later in the list:
- `00_strategic_resources.txt` by Plentiful Traditions (#37);
- `giga_strategic_resources.txt` and `giga_amb_strategic_resources.txt` by Gigastructures (#24);
- `acot_special_resources.txt` by ACOT (#20).

## Game rules used
- **Same path → whole file replaced.** When several mods have a file at the same relative path, only the copy from the mod latest in the list loads. Every definition in the other copies is lost (reason: *file replaced*).
- **Folder load order.** The remaining files in a folder load in file-name order, ignoring case, regardless of which mod they come from. Ties are broken by full path.
- **Same-named definitions in different files** follow the folder's rule. This follows the Stellaris wiki's "Overwriting specific elements" table, kept in one place in code (`DefinitionRules`):
  - **First wins:**
    - component_sets
    - component_templates
    - event_chains
    - global_ship_designs
    - scripted_loc
    - scripted_variables
    - solar_system_initializers
    - special_projects
    - start_screen_messages
  - **Duplicated** (both copies load, which usually breaks things): name_lists, observation_station_missions, strategic_resources, terraform, traits.
  - **Skipped** (entries merge, or the files are not definitions): on_actions, inline_scripts.
  - **Events:** the rule isn't documented, so a duplicate id is reported as *duplicate event id*.
  - **Everything else:** last wins.
- **Conflicts only count between different mods.** A mod that defines the same name twice in its own files is not reported.
- **Base game files** are not considered.

## What counts as a definition
- **`common/<type>/**/*.txt`:** each top-level `name = …`.
- **`common/defines`:** `NCategory.Key` for each key inside a top-level block.
- **`events/**/*.txt`:** the `id` of each top-level `…event = { }` block.
- **How files are read:** a small tokenizer that handles `#` comments, quoted strings, braces and operators. It does not build a parse tree.

## Components (Core)
- **`Conflicts/DefinitionScanner.Names(text, DefinitionKind)`:** returns the names defined in one file. `DefinitionKind` is TopLevel, Defines or Events.
- **`Conflicts/DefinitionRules`:**
  - `Classify(relPath)` returns `(Folder, Kind)`, or null for files that aren't scanned. Folders look like `common/buildings` or `events`.
  - `RuleFor(folder)` returns LastWins, FirstWins, Duplicated or Unknown.
- **`Conflicts/DefinitionAnalyzer.Analyze(IReadOnlyList<ModScripts>)`:**
  - Returns a `DefinitionReport` with each mod's definition count and a list of `DefinitionLoss(Mod, Folder, Name, File, OtherMod, OtherFile, Reason)`.
  - Reasons are FileReplaced, LoadsBeforeWinner, LoadsAfterWinner and Duplicate.
- **`Conflicts/DefinitionScanService`** (singleton):
  - `ScanAsync(mods, onProgress, ct)` reads each mod's folder or zip through `ContentSource`, four mods at a time. File names are cached per content path, file and stamp for the app run.
  - Unreadable mods or files become error lines and are skipped.
  - It keeps `Last`, a `ConflictSnapshot` (list version, entries, errors, report, definition count, duration), so returning to the tab shows the last result.
- **`Lists/EditedModList`** (singleton):
  - Holds the list being edited: name, mods, disabled DLCs, Loaded, and Version.
  - `Set(ModList)` replaces the list. `Touch()` bumps Version after an edit. `ToModList()` returns a copy.

## Mods page changes
- **Shared list:** the page keeps its list in `EditedModList` instead of private fields. Every edit calls `Touch()`.
- **Revisiting the page:** if the list is already loaded, the page does not import `dlc_load.json` again, so edits survive switching tabs. Import current still re-imports on demand.
- **File check (sub-project 12):** `ModFiles` ignores files under `gfx/interface/icons/`.

## Conflicts tab (`Pages/ConflictsPage.razor`, `/conflicts`, nav item after Tech)
- **On open:**
  - If the library is empty, refresh it.
  - If the list isn't loaded, load it from `dlc_load.json`.
  - If `Last` matches the list's Version, show it. Otherwise scan.
- **Rescan button:** refreshes the library, then scans.
- **Scanning:** runs in the background with a "Scanning N of M mods…" status. Leaving the tab cancels it.
- **Toolbar:**
  - the list name and mod count;
  - stats: "N definitions in M s";
  - a search box that matches definition names and file paths;
  - a type filter listing the folders that have losses;
  - Rescan.
- **Warnings:** a "The list changed since this scan" note appears if the version moved on. Errors appear in a `<details>`.
- **Left panel:** "Mods that lose definitions", sorted by number lost, then position. Each row reads "#P Name" and "loses X of Y". Clicking a row selects it; the first is selected by default. If no mod loses anything, it says "No mod loses definitions to another mod in this list."
- **Right panel:** the selected mod. Its losses, after the filters, are grouped by folder and then by (other mod, other file, reason). Each group is a row showing:
  - **Definitions:** up to 12 names, then "+N more"; clicking expands the group.
  - **Used instead:** "#P Name", with the file underneath.
  - **Why:**
    - *FileReplaced:* "same file `x.txt`, later in the list"
    - *LoadsBeforeWinner:* "`y.txt` loads after `x.txt`"
    - *LoadsAfterWinner:* "`y.txt` loads first (this type keeps the first definition)"
    - *Duplicate:* for events, "same event id: the game keeps only one"; otherwise "both load: duplicates in this type usually break"
- **Fix hint:** when any loss is FileReplaced, the panel shows "Whole files are replaced by #a, #b. To keep this mod's versions, move it below those mods; they will then lose these files instead."

## Testing
- **Scanner:**
  - top-level keys, including `@variables` and `key = value`;
  - comments and quoted braces;
  - nested blocks are ignored;
  - defines produce `NCategory.Key`;
  - event ids, with `namespace` ignored;
  - operators such as `>=` are not definitions.
- **Rules:** classification (events, common types, skipped folders, non-txt files, root files) and the rule table.
- **Analyzer:**
  - whole-file replacement, as in the Galactic Market Expansion case;
  - last-wins by file name, regardless of list order;
  - first-wins;
  - duplicated folders and events;
  - no loss within the same mod;
  - a mod listed twice;
  - definition counts.
- **Scan service:** folder and zip mods, cached names reused until the stamp changes, an unreadable mod gives an error line, a null mod gives an empty result.
- **EditedModList:** Set and Touch bump the version, and ToModList returns a copy.
- **ModFiles:** icons are ignored.
- **UI:** checked by hand.

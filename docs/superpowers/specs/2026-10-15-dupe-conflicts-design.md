# Dupe Conflicts: Design (Sub-project 20b, same branch as launcher playset sync)

## Problem
Stellaris skips **every** mod whose descriptor `name` is shared with another `mod/*.mod` file, even when only one of them is in `dlc_load.json`. Nothing in the logs says so.

On the user's PC, 52 of 60 enabled mods were skipped this way. They clashed with `8cde_…` local copies, which the Paradox launcher had renamed to the plain Workshop names, using `mods.displayName` in `launcher-v2.sqlite`, when it started. Renaming the unused copies fixed it; the user confirmed this in game.

## Rules
- **Dupe group:** two or more installed mods (from `ModLibrary`, one per `mod/*.mod`) whose `Name`, trimmed and compared case-insensitively, is equal. Ids don't matter. Two different Workshop items with the same name are a dupe group too.
- **Default copy to keep** for a group, given the current list's descriptor paths:
  1. if exactly one member is in the list: that one;
  2. otherwise, among the members in the list (or all members if none is in the list): the first Workshop (`ugc_`) member by DescriptorRel;
  3. otherwise the first member by DescriptorRel.
- **Resolving a group** means keeping one member's name and renaming every other member to `<name> (duplicate: <descriptor file stem>)`, e.g. `UI Overhaul Dynamic (duplicate: 8cde_cf2c63b4)`. For each renamed member:
  - copy the `.mod` file to `<backups>/descriptors_<yyyyMMdd_HHmmss_fff>/` first;
  - change only the value of the first top-level `name="…"` line, keeping every other byte. If there is no name line, insert `name="…"` as the first line;
  - set `mods.displayName` to the new name in `launcher-v2.sqlite`, if it exists, for rows whose `gameRegistryId` matches (case-insensitive, `/`). The database is backed up the same way as for the playset sync, in one transaction. Otherwise the launcher would rename it back the next time it starts. A database failure becomes a warning, and the descriptor renames stay.
  - Nothing is deleted, and every copy can still be used.
- **Launcher open:** resolving is refused while the Paradox launcher runs, with the same message as Apply.

## UI
- **Mods tab, current list:**
  - each entry whose descriptor is in a dupe group shows a **"⚠ Dupe conflict"** `warnbadge` button. Its tooltip: "Another mod in your mod folder has the same name; Stellaris skips mods that share a name."
  - Clicking it opens the dupe dialog for that one group, with the button "Keep selected".
- **Add mods dialog:** members of dupe groups show a non-clickable `⚠ Dupe conflict` badge with the same tooltip.
- **Dupe dialog** (`Components/DupeDialog.razor`, using the existing `modal` styles):
  - **intro:** "Stellaris skips every mod whose name is shared with another .mod file. Pick the copy that keeps the name; the others are renamed to "<name> (duplicate: <file>)". Nothing is deleted."
  - **one block per group:** the name as a heading, then one radio row per member:
    - the descriptor file;
    - a Workshop/Local badge;
    - the version;
    - the content folder (small, muted);
    - an "in your list" tag when it is in the current list.
  - **preselection:** the default copy.
  - **buttons:** Cancel, and the confirm button.
- **Apply / Apply & Launch:**
  - If any group has a member in the current list, Apply first opens the dupe dialog with **all** those groups and the button "Fix and apply". Confirming resolves them, refreshes the library, then runs the normal Apply (or Apply & Launch). Cancel does nothing.
  - The status message starts with "Renamed N duplicate-named mods so Stellaris loads them." Any launcher warning is appended in the error style.
- **Match host (Session):** there is no dialog. After the plan is made and before Apply, groups touching `plan.ToApply` are resolved with the defaults. The Match host result shows "Renamed N duplicate-named mods so Stellaris loads them." when N > 0, plus any warning.

## API
- **`Core/Library/DupeConflicts.cs`:**
  - `public sealed record DupeGroup(string Name, IReadOnlyList<InstalledMod> Mods)`, with members ordered by DescriptorRel (ordinal, ignoring case);
  - `public static IReadOnlyList<DupeGroup> Find(IReadOnlyList<InstalledMod> library)`, groups ordered by Name;
  - `public static InstalledMod DefaultKeep(DupeGroup group, IReadOnlySet<string> inListRels)`;
  - `public static string DuplicateName(string name, string descriptorRel)`.
- **`Core/Library/DupeResolver.cs`:**
  - `public sealed record DupeChoice(DupeGroup Group, InstalledMod Keep)`;
  - `public sealed record DupeRename(string DescriptorRel, string OldName, string NewName)`;
  - `public sealed record DupeResolution(IReadOnlyList<DupeRename> Renamed, string? BackupDir, string? LauncherWarning)`;
  - `public static DupeResolution Resolve(string userDir, IReadOnlyList<DupeChoice> choices, string backupDir, DateTimeOffset now)`;
  - `public static string SetName(string descriptorText, string newName)`, the text transform.
- **`LauncherPlaysetFile`:** `public static string? RenameMods(string userDir, IReadOnlyList<(string DescriptorRel, string NewName)> renames, string backupDir, DateTimeOffset now)`. It returns null on success, when there is no database, or when there is nothing to rename. Otherwise it returns a warning.
- **`ModManagerService`:**
  - `public DupeResolution ResolveDupes(IReadOnlyList<DupeChoice> choices)`: refuses while the launcher runs, resolves, then refreshes the library;
  - `public IReadOnlyList<DupeGroup> DupesIn(ModList list)`: groups with a member in the list;
  - `public DupeResolution ResolveDupesWithDefaults(ModList list)`, used by Match host.
- **`SessionService`:** `LastDupeRenames` (int) next to `LastLauncherWarning`, reset at the same places. The launcher warning from the dupe resolution is folded into `LastLauncherWarning`.

## Testing
- **`DupeConflicts`:**
  - grouping that is case- and whitespace-insensitive;
  - singletons excluded;
  - each default-keep rule;
  - the `DuplicateName` format.
- **`DupeResolver`:**
  - losers renamed and the keeper untouched;
  - the rest of each descriptor byte-identical;
  - a missing name line inserted;
  - backups written;
  - the database `displayName` updated when the database exists;
  - no database means no warning;
  - a corrupt database gives a warning while descriptors are still renamed.
- **`ModManagerService`:**
  - `ResolveDupes` refuses while the launcher runs;
  - `ResolveDupes` refreshes the library, so the clash is gone afterwards;
  - `ResolveDupesWithDefaults` keeps the listed copy.
- **Real data:** run `Find` on the user's mod folder. It should give the 2 restored groups (UIOD, DarkSpace) plus Plentiful Traditions and Preftl Observation, with the defaults keeping `ugc_1623423360`, `ugc_2719075597`, `ugc_1311725711` and `preftlobservefixer.mod`. That last one is in the list.
- **Manual:**
  - the badges show;
  - the dialog renames;
  - Apply opens the dialog;
  - after Fix and apply, UIOD and DarkSpace load in game;
  - opening the launcher afterwards keeps the new names.

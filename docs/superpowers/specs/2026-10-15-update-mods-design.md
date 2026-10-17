# Update Mods: Design (Sub-project 22)

## Goal
One button on the Mods tab makes Steam check and download updates for every Workshop mod in the current list. Afterwards it shows what was updated, what was already up to date, and what failed.

## Behaviour
- **Toolbar button.** "⟳ Update mods" goes next to "+ Add mods" on the Mods tab's second toolbar. It is disabled while the page is busy, the list isn't loaded, or the list has no Workshop mods. Those are entries whose key is `ugc:<id>` (`ModKeys.WorkshopId`), deduplicated by id and kept in list order.
- **Dialog `UpdateModsDialog`.** Uses the existing `modal` styles and the WorkshopPrompt look.
  1. **Confirm:**
     - "Update the N Workshop mods in '<list name>' through Steam? Steam shows you as playing Stellaris while this runs."
     - Buttons: **Update** and **Cancel**.
  2. **Working:**
     - "X of N done" above one row per mod: name, state text (Waiting / Checking… / Downloading 42% / Done / Failed: reason / Cancelled) and a progress bar.
     - **Cancel** cancels the token; the current item finishes or stops, and the rest are reported as Cancelled. The modal can't be closed while working.
  3. **Done:**
     - Summary: "Updated U · Already up to date A · Failed F", plus "Cancelled C" when C > 0.
     - Lists of updated mods (names) and failed mods (name and error).
     - Button: **Close**.
  - **Errors.** If Steam can't be reached (`WorkshopUnavailableException`), the dialog shows the message in the Done phase with Close.
- **How "updated" is decided.**
  - For each mod, a `FolderStamp` of its install folder is taken before the run and again after it: file count, total bytes and the newest last-write time in UTC, recursive. Use the library's `InstalledMod.ContentPath` when the mod is installed. After a successful install, use the result's `InstallFolder` (falling back to ContentPath).
  - **Updated:** the install succeeded and the stamp differs, or there was no folder before.
  - **Up to date:** the install succeeded and the stamp is the same.
  - **Failed or Cancelled:** taken from the result.
  - A folder that can't be read gives a null stamp, which counts as "changed" only if the before stamp was non-null and the after stamp differs.
- **After closing the dialog, if anything was attempted:**
  - the Mods page does what "Rescan mods" does: `RefreshLibraryAsync`, clear icons and the file cache, `LibraryChanged()`;
  - if the session is active (`Session.Role != None`) and not busy, it calls `Session.RescanAsync()` so the comparison reflects the new files. Errors there go to the status line.
  - The status line shows the summary.
- **Launching.** Launching is already blocked while `IWorkshopService.IsActive`; no change.

## Code
- **`Core/Workshop/FolderStamp.cs`:**
  - `public sealed record FolderStamp(int Files, long Bytes, DateTime NewestUtc)`;
  - `public static FolderStamp? Of(string? folder)`, which returns null for a missing or unreadable folder.
- **`Core/Workshop/ModUpdater.cs`:**
  - `public static IReadOnlyList<ulong> WorkshopIds(ModList list)`;
  - `public enum ModUpdateOutcome { Updated, UpToDate, Failed, Cancelled }`;
  - `public sealed record ModUpdateResult(ulong Id, ModUpdateOutcome Outcome, string? Error)`;
  - `public static async Task<IReadOnlyList<ModUpdateResult>> RunAsync(IWorkshopService workshop, IReadOnlyList<ulong> ids, Func<ulong, string?> folderOf, IProgress<WorkshopProgress> progress, CancellationToken ct)`. It stamps before and after (off the UI thread) and maps the `WorkshopItemResult`s. A result whose Error is "Cancelled." or whose id is missing is Cancelled.
- **`Components/UpdateModsDialog.razor`.** Parameters:
  - `IReadOnlyList<(ulong Id, string Name)> Mods`;
  - `string ListName`;
  - `Func<ulong, string?> FolderOf`;
  - `EventCallback<IReadOnlyList<ModUpdateResult>?> OnClose` (null when cancelled before starting).

  It injects `IWorkshopService` and gets titles for unknown names from the list entries.
- **`Pages/Mods.razor`:** the button, the dialog, and the after-close refresh (and session rescan via an injected `SessionService`).

## Testing
- **`FolderStamp`:**
  - a missing folder gives null;
  - counts and bytes are correct;
  - changing a file's content or time, or adding a file, changes the stamp;
  - two stamps of an unchanged folder are equal.
- **`ModUpdater`,** with a fake `IWorkshopService` whose `InstallAsync` changes the folder of some ids, leaves others alone, fails one, and cancels one:
  - the outcomes come out as Updated / UpToDate / Failed / Cancelled;
  - progress is forwarded;
  - `WorkshopIds` keeps the ugc entries in order and dedupes them.
- **UI:** checked by hand. The button counts the Workshop mods; the dialog phases, cancel, summary, the library refresh afterwards, and the session rescan.

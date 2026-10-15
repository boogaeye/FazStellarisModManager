# Launcher Playset Sync: Design (Sub-project 20)

## Problem
The game loads only `dlc_load.json`. The Paradox launcher keeps its own playsets in `Documents\Paradox Interactive\Stellaris\launcher-v2.sqlite`. Every time it starts, and again when Play is pressed, it rewrites `dlc_load.json` (and the `mod/ugc_*.mod` descriptors) from its **active** playset.

The app writes only `dlc_load.json`. So opening the launcher silently replaces the list the app applied.

Seen on the user's PC:
- On 2026-10-13 at 16:52:40 the launcher's `lastPlaysetBackup` time, the `dlc_load.json` timestamp and every `ugc_*.mod` timestamp all match.
- `dlc_load.json` now equals the active "IronyModManager" playset: 58 mods, same order.

Irony Mod Manager avoids this by writing its own playset into the database and making it active. We do the same.

`game_data.json` and `mods_registry.json` belong to the old launcher (v1); the current launcher and game don't use them. They stay untouched.

## Behaviour
On every `ModManagerService.Apply(list)`, which covers the Mods tab's Apply and Apply & Launch and the Session tab's Match host:

1. **Launcher open:** if the Paradox launcher is running (process `Paradox Launcher` or `dowser`), throw `InvalidOperationException("Close the Paradox launcher first, or it will overwrite your mod list.")` before writing anything.
2. **`dlc_load.json`:** written as today, with its backup.
3. **No launcher database:** if `<userDir>/launcher-v2.sqlite` doesn't exist (the launcher never ran), stop here. There is no warning.
4. **Database backup:** copy it to `<backups>/launcher-v2_<yyyyMMdd_HHmmss_fff>.sqlite` and keep the newest 20 such files.
5. **Write, in one transaction:**
   - **Playset:** find the playset named `FazStellarisModManager` that isn't removed (`isRemoved = 0`). If there isn't one, insert it:
     - `id`: a new lower-case GUID;
     - `isActive = 1`, `loadOrder = 'custom'`, `isRemoved = 0`, `hasNotApprovedChanges = 0`, `syncState = 'NOT_ELIGIBLE'`;
     - `createdOn` and `updatedOn`: Unix ms;
     - `pdxId`, `pdxUserId`, `syncedOn` and `lastServerChecksum`: NULL.
   - **Active flag:** set `isActive = 0` on every other playset where `isActive = 1`. Set `isActive = 1` and `updatedOn = now` (ms) on ours.
   - **Playset mods:** delete our playset's `playsets_mods` rows, then insert one row per list entry in order: `position` 0…n-1, `enabled = 1`.
   - **Mod lookup:** each entry's launcher mod is the `mods` row whose `gameRegistryId` equals `DescriptorRel` (`mod/xyz.mod`), compared case-insensitively with `/` separators. If several rows match, prefer `status = 'ready_to_play'`, then the first.
   - **Missing mod rows:** if there is no such row, read the descriptor at `<userDir>/<DescriptorRel>` and insert a row:
     - `id`: a new GUID;
     - `steamId`: the Workshop id for `ugc_<id>.mod` or a numeric `remote_file_id`, else NULL;
     - `gameRegistryId`: DescriptorRel;
     - `displayName`: name (fallback: the file name);
     - `version`, `requiredVersion` (= supported_version), `tags` (JSON array);
     - `dirPath` (`path=`, resolved like `ModLibrary.ResolveContent`, Windows separators) or `archivePath` (`archive=`);
     - `status = 'ready_to_play'`, `source` = `steam` for `ugc_` descriptors, else `local`;
     - `isNew = 0`, `createdDate`: Unix **seconds**.

     Other columns use their defaults. An entry whose descriptor file is missing is skipped. That list is in the warning below.
   - **Commit.**
6. **Failures:** `SqliteException`, `IOException`, `UnauthorizedAccessException` or an unexpected schema (missing table or column) become a **warning** in the result, not an exception. `dlc_load.json` is already written, and the database is untouched because the transaction rolls back.

## API
- `Core/Game/LauncherPlaysetFile.cs`:
  - `public const string FileName = "launcher-v2.sqlite";`
  - `public const string PlaysetName = "FazStellarisModManager";`
  - `public static bool IsLauncherRunning()`
  - `public static LauncherSyncResult Write(string userDir, IReadOnlyList<string> descriptorRels, string backupDir, DateTimeOffset now)`
  - `public sealed record LauncherSyncResult(bool Written, string? BackupPath, string? Warning, IReadOnlyList<string> Skipped)`
- `ModManagerService`:
  - `public Func<bool> IsLauncherRunning { get; set; } = LauncherPlaysetFile.IsLauncherRunning;`
  - `Apply` returns `ApplyResult(string? BackupPath, string? LauncherWarning)`. LauncherWarning combines the result's Warning and its Skipped list.
- **UI:**
  - **Mods tab:** the Apply message adds "Set as the active Paradox launcher playset." when Written. A warning is appended in the error style.
  - **Session tab:** the "Match host result" paragraph shows the launcher warning when there is one.
- **Package:** `Microsoft.Data.Sqlite` in Core.

## Testing
Unit tests build a temporary `launcher-v2.sqlite` with the real CREATE statements for `playsets`, `playsets_mods` and `mods`. They cover:
- creating the playset;
- reusing it by name, with its rows replaced and no duplicate playset;
- other active playsets deactivated, while `NULL` and `0` stay as they are;
- positions following the list order;
- existing mod rows matched case-insensitively;
- missing rows inserted from the descriptor (steam vs local, archive vs path), and missing descriptors skipped;
- no database meaning no write and no backup;
- the backup being created and pruned to 20;
- a corrupt database giving a warning, with the database file unchanged;
- `ModManagerService.Apply` refusing while `IsLauncherRunning` returns true, with `dlc_load.json` unchanged.

**Real data:** run `Write` against a copy of the user's `launcher-v2.sqlite` with the current list, and check that the playset is created and active with 58 rows in order.

**Manual:**
- Apply in the app, open the Paradox launcher, and confirm `dlc_load.json` still has the app's list and the launcher shows the "FazStellarisModManager" playset.
- With the launcher open, Apply should refuse.

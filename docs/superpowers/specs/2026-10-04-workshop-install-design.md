# Workshop Install & Update — Design (Sub-project 10)

## Goal
When a client's mods don't match the host's, the app can subscribe to and download the missing Steam Workshop mods, update outdated ones, and then apply the host's list. A pop-up lists what's needed. Every mod is ticked by default, and the player can untick any of them. The pop-up never opens on its own. It opens only from a **Workshop mods** button, or when the player presses **Match host** and something needs installing or updating.

This builds on sub-project 9 (mod identity): `MatchPlan.NeedsWorkshopInstall`, `NeedsWorkshopUpdate` and `ModMatcher.WorkshopIdOf`.

## Facts
- Stellaris Workshop items can only be fetched by a process talking to the running Steam client as the game (AppID 281990). SteamCMD needs a login, and there is no `steam://` URL for subscribing.
- **Facepunch.Steamworks 2.3.3** (NuGet, 2020) bundles `steam_api64.dll` under `content/` and offers async `Steamworks.Ugc.Item` APIs. **Steamworks.NET 2024.8.0** ships without the native DLL.
- While connected, Steam shows the user as "Playing Stellaris". The connection only lasts for the duration of a Workshop call.
- Steam downloads Workshop items to `<library>/steamapps/workshop/content/281990/<id>`. `ModManagerService.RefreshLibrary` → `ModLibrary.EnsureWorkshopDescriptors` already creates `mod/ugc_<id>.mod` for new folders.
- `SessionService.MatchHostAsync` already does: diff → refresh library → `MatchPlan` → apply `dlc_load.json` → rescan → send snapshot.

## Decisions

### Core contract (`Core/Workshop`)
- **`IWorkshopService`:**
  - `bool IsActive`: true while connected to Steam.
  - `Task<IReadOnlyList<WorkshopItemInfo>> GetInfoAsync(ids, ct)`: title and size (null when unknown).
  - `Task<IReadOnlyList<WorkshopItemResult>> InstallAsync(ids, IProgress<WorkshopProgress>, ct)`.
  - Implementations connect only inside a call and disconnect before returning. They throw `WorkshopUnavailableException` when Steam can't be reached.
- **Records and states:**
  - `WorkshopItemState`: Waiting, Subscribing, Downloading, Installed, Failed, Cancelled.
  - `WorkshopProgress(Id, State, Fraction, Message)`
  - `WorkshopItemInfo(Id, Title, SizeBytes)`
  - `WorkshopItemResult(Id, Success, InstallFolder, Error)`
- **`WorkshopNeeds.From(MatchPlan)`:**
  - **Install:** every NeedsWorkshopInstall entry with a Workshop id. That covers host `ugc:` mods and host local copies that carry a `remote_file_id`.
  - **Update:** every NeedsWorkshopUpdate unit, by **my** `ugc:` key.
  - Ids are distinct, and order follows the host list.
  - **NotInstallable:** names of NeedsManualInstall entries.

### Session flow (`SessionService`)
- **`PlanAsync`:** a client-only exclusive operation. It refreshes the library and builds `MatchPlan` from the current host target and my snapshot, without applying anything.
- **`InstallFromWorkshopAndMatchAsync(ids, progress, ct)`:** a client-only exclusive operation.
  1. Tells the host "Downloading Workshop mods…" (busy).
  2. Calls `InstallAsync` for the selected ids, if any. It skips this step when none are selected, which gives "match without installing".
  3. Runs the same match steps as Match host: refresh library (creates the new descriptors), plan, apply, rescan, send the snapshot.
  4. Returns `WorkshopMatchResult(Items, Plan)`.

  On an error or a cancel before applying, my last snapshot is sent back so the host doesn't stay "busy". Without a Workshop service the method throws `InvalidOperationException`.
- **`MatchHostAsync`:** unchanged behaviour. It shares its core with the method above.
- **`SessionService(manager, workshop = null)`**.

### Steam adapter (`FazStellarisModmanager.Steam`, new project)
- **Setup:** `SteamWorkshopService : IWorkshopService` uses Facepunch.Steamworks 2.3.3. The project copies the package's `content/steam_api64.dll` to the output, so it reaches the app's output and the release zip.
- **Connection:** `SteamClient.Init(281990, asyncCallbacks: true)` inside a call, and `SteamClient.Shutdown()` in `finally`. Calls are serialised.
- **Per item, in sequence:**
  1. Get the item. If it isn't found, the item fails with "not found or not visible to this account".
  2. Subscribe if not subscribed.
  3. Download at high priority with progress.
  4. Re-query; it succeeds only when the item is installed and doesn't need an update.
- **Stalls:** a download stalls when there is no progress for 2 minutes. The item then fails with "The download stalled", and the rest continue.
- **Cancel:** the current item and the remaining ones become Cancelled.
- **The API is checked first:** task 1 is a spike that connects, queries one known item without subscribing, and records the real member names. The adapter follows its findings.

### Pop-up (`WorkshopPrompt.razor`, Session page)
- **When it opens:**
  - The **Workshop mods** button (client role) opens it from a fresh `PlanAsync`.
  - **Match host:** when `WorkshopNeeds` from a fresh plan has items or not-installable mods, the pop-up opens instead of matching directly. Otherwise Match host runs as before.
  - It **never** opens automatically on join or when the host's list changes.
- **Contents:**
  - Needed items, each with a checkbox (all ticked), name, Workshop id, "Install" or "Update", and size (filled in when `GetInfoAsync` returns). If Steam can't be reached for the info, the pop-up shows one muted line saying so.
  - A "Get these from the host" list of not-installable mods.
- **Buttons:**
  - **Install/update selected (N) and match**
  - **Match without installing**
  - **Close**
- **While working:** each item shows its state and a progress bar, with an overall count, and **Cancel** calls `Session.CancelCurrent()`.
- **When done, a summary:**
  - each item: installed or updated, or failed with the reason and an **Open in Steam** link (`steam://url/CommunityFilePage/<id>`);
  - the match outcome (from `MatchPlan`);
  - "still different" items from the new diff. An updated mod that still differs gets the note "the host may be on an older version".

### Safety
- `ModManagerService.Launch` throws "Wait for the Workshop downloads to finish…" while `IWorkshopService.IsActive`. The app wires this check through a `LaunchBlockedReason` function set at startup.
- Only one session operation runs at a time (the existing `Exclusive`).

## Errors
- **Steam unavailable:** `WorkshopUnavailableException` reaches the pop-up as an error, with **Open in Steam** links for every selected item, and nothing is applied.
- **Per-item failures:** these never stop the match. The match applies whatever is installed.

## Testing
- **`WorkshopNeeds.From`:** install from a host `ugc:` and from a host local copy with a Workshop id; update by my `ugc:` key; distinct ids; not-installable names.
- **`SessionService` with a fake Workshop service** that "downloads" by writing a Workshop folder into the fake install:
  - install then match: the descriptor is created, `dlc_load.json` gets the host order, and the diff matches;
  - a failed item is reported, and the match still applies the rest;
  - "match without installing" with no ids;
  - no Workshop service throws;
  - `PlanAsync` doesn't write `dlc_load.json`.
- **`ModManagerService.Launch`** is blocked while the Workshop service is active.
- **Steam adapter:** checked by the spike and manually.

## Out of scope
Unsubscribing, browsing the Workshop, background auto-updates outside sessions, and showing Steam's own download queue.

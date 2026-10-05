# Workshop Install & Update — Design (Sub-project 10)

## Goal
When a client's mods don't match the host's, the app can subscribe to and download the missing Steam Workshop mods, update outdated ones, and then apply the host's list. A pop-up lists what's needed. Every mod is ticked by default, and the player can untick any of them. The pop-up never opens on its own. It opens only from a **Workshop mods** button, or when the player presses **Match host** and something needs installing or updating.

This builds on sub-project 9 (mod identity): `MatchPlan.NeedsWorkshopInstall`, `NeedsWorkshopUpdate` and `ModMatcher.WorkshopIdOf`.

## Facts
- Stellaris Workshop items can only be fetched by a process talking to the running Steam client as the game (AppID 281990). SteamCMD needs a login, and there is no `steam://` URL for subscribing.
- **Facepunch.Steamworks 2.3.3** (NuGet, 2020) bundles `steam_api64.dll` under `content/` and offers async `Steamworks.Ugc.Item` APIs. **Steamworks.NET 2024.8.0** ships without the native DLL.
- While connected, Steam shows the user as "Playing Stellaris". The connection only lasts for the duration of an install.
- Item titles and download sizes come from Steam's public Web API (`ISteamRemoteStorage/GetPublishedFileDetails/v1/`, no key), so looking items up never connects to the Steam client. Facepunch's `Item.SizeBytes` is the installed size (0 for items that aren't installed), so it can't size an install.
- Steam downloads Workshop items to `<library>/steamapps/workshop/content/281990/<id>`. `ModManagerService.RefreshLibrary` → `ModLibrary.EnsureWorkshopDescriptors` already creates `mod/ugc_<id>.mod` for new folders.
- `SessionService.MatchHostAsync` already does: diff → refresh library → `MatchPlan` → apply `dlc_load.json` → rescan → send snapshot.

## Decisions

### Core contract (`Core/Workshop`)
- **`IWorkshopService`:**
  - `bool IsActive`: true while connected to Steam.
  - `Task<IReadOnlyList<WorkshopItemInfo>> GetInfoAsync(ids, ct)`: title and size (null when unknown), from the Web API through `WorkshopWebApi` (Core, 15 s limit, no Steam connection).
  - `Task<IReadOnlyList<WorkshopItemResult>> InstallAsync(ids, IProgress<WorkshopProgress>, ct)`.
  - `InstallAsync` implementations connect only inside the call and disconnect before returning. They throw `WorkshopUnavailableException` when Steam can't be reached.
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
  4. Returns `WorkshopMatchResult(Items, Plan, Note)`.

  If the host disconnects during the downloads, the item results are kept and returned with a null `Plan` and the Note "The host disconnected; the downloads finished but the host's list was not applied."

  On an error or a cancel before applying, my last snapshot is sent back so the host doesn't stay "busy". Without a Workshop service the method throws `InvalidOperationException`.
- **`MatchHostAsync`:** unchanged behaviour. It shares its core with the method above.
- **`SessionService(manager, workshop = null)`**.

### Steam adapter (`FazStellarisModmanager.Steam`, new project)
- **Setup:** `SteamWorkshopService : IWorkshopService` uses Facepunch.Steamworks 2.3.3. The project copies the package's `content/steam_api64.dll` to the output, so it reaches the app's output and the release zip.
- **Connection:** `SteamClient.Init(281990, asyncCallbacks: false)` inside a call, and `SteamClient.Shutdown()` in `finally`. Calls are serialised.
- **Helper process:** Steam treats the process that called `SteamClient.Init` as the running game until that process *exits* (`Shutdown` is not enough), which kept Stellaris "running" until the mod manager closed. So the app never runs `SteamWorkshopService.InstallAsync` itself:
  - The app registers `HelperProcessWorkshopService` (Core). `InstallAsync` starts the app's own exe with `--workshop-helper`; `Program.Main` (the WPF startup object) then runs `WorkshopHelperHost` with `SteamWorkshopService` and no window, and exits when done. `GetInfoAsync` stays in-process (Web API).
  - Protocol (`WorkshopHelperProtocol`): the app writes one request line `{"ids":[...]}` to the helper's stdin and keeps stdin open. The helper writes one JSON line per message to stdout: `{"type":"progress","progress":{...}}`, then `{"type":"results","items":[...]}` or `{"type":"unavailable","message":"..."}`. Lines are ASCII (everything else is JSON-escaped), so console code pages don't matter. Other lines (Steam's native output) are ignored, and a message is found after any text before its `{`. An empty request answers `[]` without connecting.
  - Exit codes: 0 ok, 1 failed, 2 unavailable, 3 bad request, 4 cancelled. The app trusts the messages: results win even with a failing exit code; otherwise the unavailable message, otherwise "stopped without an answer (exit code N)", all as `WorkshopUnavailableException`. A helper that can't start is unavailable too.
  - Cancel: the app closes the helper's stdin (the end of stdin cancels the helper's install, which also happens if the app dies), waits up to 5 s for its answer, then kills the helper's process tree. Without results it throws `OperationCanceledException`.
  - `IsActive` is true while the helper runs; calls are serialised. The helper is the same exe, so the self-contained release needs nothing extra (`steam_api64.dll` is already next to it).
- **Manual pumping:** Facepunch's background pump is off. We call `SteamClient.RunCallbacks()` ourselves every ~16 ms while waiting for a Steam call (`Pumped`) and in the download poll. So none of our code, and never `Shutdown`, runs inside a callback frame, and the calls work from any thread (the session runs them via `Task.Run`). `Dispatch.OnException` logs to Debug.
- **Per item, in sequence:**
  1. Get the item. If its `Result` isn't OK, the item fails with "not found or not visible to this account".
  2. Subscribe if not subscribed. Getting and subscribing each have a 30 s limit ("Steam did not respond.").
  3. Always `Download(highPriority: true)`, even for an installed item: that is what makes Steam check for an update. Then poll every 16 ms (pumping callbacks) and report progress at most every 250 ms.
  4. Progress comes from Steam's live byte counters (`DownloadBytesDownloaded / DownloadBytesTotal`); `Item.DownloadAsync` reports nothing until a download completes. The pure `Core/Workshop/DownloadWatch` decides from state snapshots. "Finished" means installed, and not downloading, pending or needing an update. It only counts once Steam has visibly reacted, meaning the item was missing, downloading, pending or needing an update at some point, or after an 8 s grace period with the item still finished, which means it really is up to date.
  5. Any other exception fails only that item.
- **Stalls:** a download stalls when the downloaded bytes don't grow for 2 minutes. The item then fails with "The download stalled.", and the rest continue. If it is still pending with nothing downloaded, the message is "Steam queued the download but didn't start it; check Steam's Downloads page." A finished item is never reported as stalled.
- **Init failure:** `SteamClient.Shutdown()` is still called, and the `SteamAppId`/`SteamGameId` variables are cleared after every connection so the game doesn't inherit them.
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
- **Staying open:** leaving the client role closes a pop-up that is still choosing. Once a run starts, the pop-up stays until **Close**, even if the host disconnects, so the summary and its "host left" note can be read.

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
- **`DownloadWatch`** (the adapter's download decision): an up-to-date item is done after the grace period; an outdated item that starts downloading is done when it finishes; bytes that stop growing stall; queued but never started; a finish just before the stall is done.
- **Helper protocol:** message and request round trips, malformed and foreign lines ignored, ASCII single lines; the helper host (progress then results, unavailable, bad request, stdin end cancels); the process plumbing against `cmd.exe` fake helpers (request sent, progress order, results despite a failing exit code, exit codes without an answer, missing exe, graceful cancel, kill after the grace period, `IsActive`).
- **Steam adapter:** checked by the spike and manually.

## Out of scope
Unsubscribing, browsing the Workshop, background auto-updates outside sessions, and showing Steam's own download queue.

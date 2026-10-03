# FazStellarisModmanager — Design & Build Plan

## Context
The goal is a Stellaris mod manager for playing multiplayer with friends.
- One player hosts a session from inside the app, and the others join.
- Everyone sees which mods they are missing or have different from the host, and can fix it in one click. That click auto-subscribes to the Workshop mods, then writes `dlc_load.json`.
- The app manages saved mod lists, applies a list to the game and launches Stellaris.
- A later tab shows the technology tree for the current mod list.

Current state:
- `FazStellarisModmanager/` is an empty Blazor Server skeleton on .NET 7.
- `C:\Users\SCP Fazbear\Downloads\HashCoop\StellarisHasher` is a .NET 10 console app. It already does the mod scanning, hashing and diffing, but:
  - its TCP networking supports only one host and one client, with one exchange;
  - its diff only prints text;
  - its descriptor parsing is regex-based;
  - it has a hardcoded `D:\SteamLibrary` path and no Steam API.

Decisions made with the user:
- **Blazor Hybrid desktop:** WPF with BlazorWebView, on .NET 10.
- **App-owned mod lists:** stored as JSON. "Apply" writes `dlc_load.json`; "Launch" runs `stellaris.exe` directly, skipping the Paradox launcher.
- **Workshop:** Steamworks.NET running as AppID 281990, started only while subscribing or downloading.
- **Networking:** direct IP over TCP (port forwarding or Hamachi/ZeroTier/Tailscale), with many clients.
- **The host is the source of truth.** Clients get a "Match host" button, and the host sees a ready grid.

The work splits into four sub-projects, built in order: **(1) Core and mod lists → (2) Multiplayer sync → (3) Workshop auto-install → (4) Tech tree.** This plan covers 1–3 in full. Sub-project 4 (the tech tree) gets its own brainstorming and spec once 1–3 work, because its parsing and graph layout are a separate problem.

## Solution layout
Replace the skeleton in `C:\Users\SCP Fazbear\source\repos\FazStellarisModmanager\`:

| Project | Type | Purpose |
|---|---|---|
| `FazStellarisModmanager` | `net10.0-windows`, WPF + `Microsoft.AspNetCore.Components.WebView.Wpf` | Window host, Razor pages and UI state |
| `FazStellarisModmanager.Core` | `net10.0` class lib | Paths, descriptors, mod library, lists, `dlc_load.json`, launch, hashing, diff, session protocol |
| `FazStellarisModmanager.Steam` | `net10.0-windows` class lib, Steamworks.NET NuGet | `IWorkshopService` implementation (isolated because of the native `steam_api64.dll`) |
| `FazStellarisModmanager.Tests` | xUnit | Tests for Core |

Steps for the skeleton:
- Delete `Program.cs`, `Pages/_Host.cshtml` and `appsettings*.json`.
- Keep `App.razor`, `MainLayout.razor`, `_Imports.razor` and `wwwroot/css/site.css`, adapting them for BlazorWebView: add `wwwroot/index.html` and a `MainWindow.xaml` with `<blazor:BlazorWebView HostPage="wwwroot\index.html">`.

## Sub-project 1: Core and mod lists

**Port from HashCoop.** Copy these into Core with the same logic but cleaned up, and don't reference the Downloads folder:
- the records `ModFile`, `ModSnapshot` and `MachineSnapshot` from `Mods.cs`;
- `ModScanner.Scan`, `FindGameDir` and `ReadGameVersion` from `Mods.cs`;
- `Manifest.Parse`, `Hasher.CollectFiles` and `Hasher.Compute` from `Program.cs`.

Fixes to make while porting:
- Remove the hardcoded `D:\SteamLibrary` path. Look up the Steam registry key, then `libraryfolders.vdf`, then use the path set in settings.
- Don't swallow exceptions; log them instead.
- Never print to the console. Return data and report progress through `IProgress<T>`.
- Use case-insensitive path keys so `ToDictionary` can't throw on paths that differ only in case.
- Handle duplicate mod keys.

New Core components (folders inside Core):
- `Paths/GameLocator`: game dir, user dir (`Documents\Paradox Interactive\Stellaris`) and the Workshop content dir (`<library>\steamapps\workshop\content\281990`).
- `Descriptors/ParadoxScriptParser`: a small tokenizer for `key = value`, quoted strings and `{ }` blocks, replacing HashCoop's single-line regex. Its `ModDescriptor` covers name, path, archive, remote_file_id, version, supported_version, tags and dependencies.
- `Library/ModLibrary`: finds every installed mod by combining `mod/*.mod` in the user dir with the Workshop content folders. If a Workshop mod has no `mod/ugc_<id>.mod`, the app creates one from the mod's own `descriptor.mod` plus `path="..."`, the same way the Paradox launcher does. Sub-project 3 depends on this.
- `Lists/ModListStore`: named lists saved in `%AppData%\FazStellarisModmanager\lists\*.json`. Each `ModList` has a name, entries (key, `remoteId`, name, descriptor path) in load order, and disabled DLCs. It also handles importing the current `dlc_load.json` into a list.
- `Game/DlcLoadFile`: reads and writes `dlc_load.json` (`enabled_mods`, `disabled_dlcs`). Before every write it saves a timestamped backup to `%AppData%\...\backups`.
- `Game/GameLauncher`: starts `<gameDir>\stellaris.exe` with its working directory set to the game dir. It checks that Steam is running first and warns if not.
- `Hashing/HashCache`: caches results by path, size and mtime in `%AppData%\...\hashcache.json`, so rescans only rehash changed files. Hashing runs in parallel with `Parallel.ForEachAsync`.
- `Diff/ModDiffer`: returns a structured `DiffResult` instead of HashCoop's `ModDiff.Print`. Each mod gets a status: Missing, Extra, OrderMismatch, ContentMismatch (with the files that differ), or Ok. It also returns the GameVersion, Base and DLC differences.

UI (Razor pages):
- **Mods** tab: library list on the left; on the right, the current list with drag-to-reorder and enable toggles. Buttons for Save list, Load list, Import current, Apply, and Apply & Launch.
- **Settings** page: game dir, user dir and player name, auto-detected with manual override.

## Sub-project 2: Multiplayer sync

Changes to `Core/Session`, replacing HashCoop's `Network.cs`:
- **Framing:** a 4-byte length written with `BinaryPrimitives` (explicitly little-endian), then a gzip-compressed UTF-8 JSON envelope `{ type, payload }`. The size cap drops to 64 MB.
- **Messages:**
  - `Hello{protocolVersion, appVersion, playerName}`, which is rejected if the protocol version doesn't match
  - `Snapshot{MachineSnapshot}`
  - `HostTarget{ModList, MachineSnapshot}`
  - `Roster{players[{name, status, diffSummary}]}`
  - `Status{Ready|Syncing|Mismatch}`
  - `Bye`
- **`SessionHost`:** a `TcpListener` with one async loop per client.
  - On join, it sends `HostTarget` and receives the client's `Snapshot`.
  - It diffs the client against itself and broadcasts `Roster` to everyone.
  - It rebroadcasts whenever any snapshot changes or a player rescans.
- **`SessionClient`:** connects to IP:port, sends `Hello` and `Snapshot`, receives `HostTarget`, and runs `ModDiffer` locally against the host.

UI:
- **Session** tab:
  - Host form (port) or Join form (IP:port).
  - For the host: the roster grid of players with ✅/⚠️/❌ and an expandable diff.
  - For clients: a diff list grouped by Missing, Different, Extra and Order, plus a **Match host** button.
- **Match host** does the following in order:
  1. Install missing Workshop mods (sub-project 3).
  2. Force a re-download of Workshop mods with a ContentMismatch.
  3. Write the host's list to `dlc_load.json`.
  4. Rescan and send the new `Snapshot`.

  A missing *local* (non-Workshop) mod can't be fixed automatically. The app reports "host must share this mod" for it.

## Sub-project 3: Workshop auto-install

The interface lives in Core so it can be mocked in tests:

```csharp
public interface IWorkshopService {
    Task<bool> InitializeAsync();
    Task SubscribeAndDownloadAsync(IEnumerable<ulong> ids, IProgress<WorkshopProgress> progress, CancellationToken ct);
    Task ForceUpdateAsync(IEnumerable<ulong> ids, IProgress<WorkshopProgress> progress, CancellationToken ct);
    void Shutdown();
}
```

`SteamWorkshopService` in the Steam project:
- Writes `steam_appid.txt` (`281990`) next to the exe, and calls `SteamAPI.Init()` only when needed.
- Runs a callback pump task that calls `SteamAPI.RunCallbacks()` every 50 ms.
- For each mod, calls `SteamUGC.SubscribeItem` and then `DownloadItem(id, true)`. It polls `GetItemState` and `GetItemDownloadInfo` for progress, and uses `GetItemInstallInfo` to get the install folder.
- Calls `SteamAPI.Shutdown()` when finished, so the app doesn't keep showing as "playing Stellaris".
- `GameLauncher` refuses to launch while the service is active.

After a download, `ModLibrary` creates the `ugc_<id>.mod` descriptor for the new mod and refreshes. If Steam isn't running or the account doesn't own Stellaris, it shows a clear error, plus a fallback "Open Workshop page" link (`steam://url/CommunityFilePage/<id>`).

## Sub-project 4: Tech tree (deferred)
This gets a separate brainstorm and spec after 1–3 ship. Expected inputs:
- `common/technology/*.txt` from the base game plus the enabled mods, in load order, with later definitions overriding earlier ones;
- `localisation/*_l_english.yml`;
- the icon `.dds` files.

It will reuse `ParadoxScriptParser` and `ModLibrary`.

## Execution order
1. Run `git init` in the repo with a .NET `.gitignore`. Write the spec to `docs/superpowers/specs/2026-10-02-mod-manager-design.md` and commit it.
2. Restructure the solution: create the projects and get the empty WPF + BlazorWebView window running.
3. Core sub-project 1, test-first:
   - descriptor parser
   - `DlcLoadFile`
   - `ModListStore`
   - `ModLibrary`
   - hashing (ported) and `HashCache`
   - `ModDiffer`
4. Mods and Settings UI, Apply and Launch.
5. Session protocol and host/client (tested over loopback), then the Session UI.
6. `SteamWorkshopService` and the Match host flow.

## Verification
- `dotnet build` the solution, then `dotnet test FazStellarisModmanager.Tests`. Tests cover:
  - the parser against real descriptor samples;
  - the `dlc_load.json` round trip and its backup;
  - `ModDiffer` cases (missing, extra, order, content);
  - the session protocol, with a host and two clients on `127.0.0.1` in one test.
- Manual checks:
  - Run the app and import the current `dlc_load.json`. Save the list, change the order, Apply, and confirm the file contents and that a backup exists. Apply & Launch should start Stellaris with those mods.
  - Session: run two app instances on one PC. The second instance uses a settings override for the user dir, pointing at a copy with a mod removed. Host on 127.0.0.1:27015, join, and confirm the missing mod shows up and the host's roster shows ❌. Match host should then show ✅.
  - Workshop: unsubscribe from a small mod, then use Match host and confirm Steam subscribes, downloads it, creates `ugc_<id>.mod`, and that the Steam API shuts down afterwards (no longer "playing Stellaris").

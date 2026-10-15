# Session Tab Redesign: Design (Sub-project 21)

Approved mockup: `.superpowers/brainstorm/595-1792101682/content/session-b-ring-v3.html`. It combines design B (sidebar + detail) with design C's ready ring and adds a vertical "Streaming to players" panel.

## 1. Remembered inputs
`AppSettings` gains three fields, each with a default so old settings files still load:
- `IReadOnlyList<string>? RecentHosts = null`: join addresses, newest first, at most 5, without duplicates (compared case-insensitively).
- `int? HostPort = null`
- `bool StreamLiveGame = false, bool StreamTechnology = true`

When they change:
- **Joining:** after a successful join, the address, normalised to `host:port` by `SessionProtocol.ParseAddress`, moves to the front of `RecentHosts`.
- **Hosting:** after hosting starts, `HostPort` is saved.
- **Forgetting:** "✕" on a recent chip removes it.

`Core/Session/RecentHosts.cs` has pure helpers: `Add(list, address)` and `Remove(list, address)`.

## 2. Host streaming
There are two host toggles. Their **effective** values:
- **LiveGame:** as set.
- **Technology:** `StreamTechnology || StreamLiveGame`. Live game includes the techs; the UI shows Technology as on, disabled, and marked "included".

**Modes:**
- **Off:** the session reads no saves, and peers get no live data.
- **Technology only:**
  - The session runs a second, light `LiveGameService` that reads with `SaveReader.ReadTechs` and has no enrich step.
  - Each peer gets `LiveFilter.TechOnly(snapshot, viewer)`: the snapshot's Players, plus only the viewer's country with Id and Techs. Every other field of that country is empty or default; no other countries are sent, and Community/Megastructures/Diplo/GlobalFlags are null.
  - About 30 KB per player before gzip.
- **Live game:** as today, using the shared `LiveGameService` with full reads and diplomatic weight, and `LiveFilter.For` per peer.

**Fast tech read:**
- `GamestateScanner.ScanTechs(byte[])` returns the players and, for player countries only, a `SaveCountry` with Id, Type and Techs filled.
- It uses the same byte reader; in non-player countries it skips everything, and in player countries everything but `tech_status`.
- `SaveReader.ReadTechs(path)` builds a `GameSnapshot` with **`TechOnly = true`** (a new GameSnapshot field, default false) from meta and `ScanTechs`.

**Lower priority:** every `LiveGameService` read (and enrich) runs on a dedicated background thread at `ThreadPriority.BelowNormal`, so the game keeps priority over the app. This also helps single player.

**Switching at runtime** (`SessionService.SetStreamingAsync(bool liveGame, bool technology)`):
- Saves the settings.
- While hosting:
  - starts or stops the sources;
  - tells `SessionHost` the new mode, which sends each peer the matching view of the newest data it has. Data that is already there gets resent at once in the new mode: going from live to technology resends tech views from the last full snapshot.
  - Broadcasts `StreamingState(bool LiveGame, bool Technology)` (effective values) to all peers.
- The host's own Live Game tab is unaffected: it reads local saves itself when opened, as today.

**HostAsync:**
- Starts only the sources the settings ask for. Today it always starts the full `LiveGameService`; it now subscribes to it only in live-game mode.
- New peers receive `StreamingState` right after `HostTarget`.

**Client:**
- `SessionService.HostStreaming` (`StreamingState?`) is shown in the header.
- When it becomes Off (both false), the client drops the kept host live data (`ForgetHostLive` behaviour), so tabs fall back to local saves.
- A `TechOnly` snapshot feeds the Tech and Events tabs (`LiveFeed.ResearchedTechs` works unchanged). The Live Game tab shows "The host is streaming technology only. Ask the host to turn on Stream live game for the scoreboard." instead of the board.

**Protocol:** the version goes from 2 to 3. `StreamingState` is a new message type.

## 3. Public address
While hosting, `PublicAddressLookup` fetches the public IPv4 once per host start:
- **Source:** `GET https://api.ipify.org`, plain text, 5 s timeout.
- **Validation:** the reply is accepted only if it parses as an IPv4 address.
- **Display:** shown as a share chip `ip:port`. On failure the chip is left out and only the LAN chips show.

## 4. Layout (`Pages/SessionPage.razor`, styles in site.css)
**Not connected:**
- One centred card (`session-card`) with a **Host | Join** segmented switch. It defaults to Join when RecentHosts isn't empty, otherwise Host.
- **Join tab:**
  - the address box, prefilled with RecentHosts[0];
  - "Recent" chips: click to fill, ✕ to forget;
  - "Playing as <name>" and a **Join session** button.
- **Host tab:** the port box (prefilled with HostPort or the default), "Playing as", and **Start hosting**.
- **Messages:** the last disconnect reason sits above the card, the status below it.

**Connected (both roles):**
- **Header bar** (`session-head`):
  - **the ready ring:** a conic gradient. Green is the share of players with status Ready, red the share with Mismatch/Unreliable, amber the share with Waiting/Busy. The middle shows "N/M ready", where M counts every player including the host.
  - **title:** "Hosting on port P" or "Connected to ADDRESS";
  - **share chips** (host only): public `ip:port` when known, plus each LAN `ip:port`, each with ⧉ to copy through `navigator.clipboard.writeText`;
  - **client line:** "Host: NAME" plus "Host streams:" pills (live game on/off, technology ✓/off);
  - **buttons:** host has Rescan my mods and Stop hosting; client has Match host, Workshop mods, Rescan and Leave; Cancel appears while busy. Enabled states are as today.
- **Body grid:**
  - **Left column** (230px):
    - **Players panel:** a row per player, at most 64 rows:
      - a coloured initial avatar (hue from a stable hash of the name);
      - the name;
      - a sub-line ("host · you", the summary such as "2 missing · 24 different", or the activity text);
      - a status dot.
      - **Selecting:** the host clicks a player to select them; the host's own row isn't selectable. For a client the list is display-only, and the client's own row is highlighted.
    - **Host only:** a **Streaming to players** panel under it. Its two vertical toggle cards (Live game, Technology) have the approved notes and a status line:
      - "Off";
      - "On · last sent <save date> · <size>/player", where size is the gzip-free JSON size of the last view sent to one peer, rounded to KB;
      - "Included in live game".
  - **Right column:**
    - **Host:** the selected player's comparison, or "Click a player to see how their mods differ from yours."
    - **Client:** "Your mods compared with the host".
    - **Comparison card:**
      - **title row:** the name, "compared with you", and a status;
      - **five tiles:** missing, different, extra, order, DLC, with counts from the DiffResult. Clicking a tile filters the list to that kind; clicking it again shows all;
      - **list:** the existing `DiffView`, which gets a `Kind` filter parameter (null means all). File lists stay expandable as they are now;
      - **client only:** the Match host result as a summary card below. Its contents are unchanged: written list, renamed dupes, missing Workshop and manual mods, launcher warning.
- **Unchanged:** the Workshop pop-up, Cancel, the activity line.

## 5. Testing
- **RecentHosts:** add, dedupe, cap and remove.
- **AppSettings:** an old JSON file without the new fields loads with the defaults.
- **`ScanTechs`:** on a small synthetic gamestate it returns player techs only, and gives the same techs as `ScanWithGlobals` for those countries.
- **`LiveFilter.TechOnly`:** only the viewer's techs survive.
- **`SessionHost`, over loopback:**
  - Off sends no LiveUpdate;
  - tech mode sends a TechOnly view with one country;
  - switching modes sends `StreamingState` and resends in the new mode;
  - a new peer gets `StreamingState`.
- **`SessionService`:**
  - streaming settings persist;
  - a client receiving Off drops the host live data.
- **Protocol:** the v3 constant is checked.
- **`LiveGameService`:** reads still work, and the existing tests pass with the low-priority thread.
- **Real data:**
  - time `SaveReader.ReadTechs` against `Read` on the user's newest save;
  - check that the techs match for country 0;
  - report the TechOnly view size.
- **Manual (UI):**
  - the not-connected card remembers the address and port;
  - the ring and the sidebar;
  - the toggles change what the client receives;
  - copying addresses;
  - tile filtering.

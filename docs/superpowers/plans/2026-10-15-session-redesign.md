# Session Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the Session tab to the approved design. The parts:
- remembered join addresses and host port;
- a header with a ready ring;
- a player sidebar;
- a comparison with filter tiles;
- host toggles for streaming the live game and the technology, plus a light tech-only save read that runs at low priority.

**Architecture:**
- **Core:** settings fields, `RecentHosts`, a fast tech-only save scan (`GamestateScanner.ScanTechs`, `SaveReader.ReadTechs`, `LiveFilter.TechOnly`), and a below-normal-priority read thread in `LiveGameService`.
- **Session:** a `StreamingState` message, protocol v3, streaming modes in `SessionHost` and `SessionService`.
- **UI:** `SessionPage` rewritten, `DiffView` gains a kind filter, and `LiveGamePage` shows a tech-only notice.

**Tech Stack:** .NET 10, WPF + BlazorWebView, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-15-session-redesign-design.md`. Read it fully; it is the source of truth for behaviour and text. **Mockup:** `.superpowers/brainstorm/595-1792101682/content/session-b-ring-v3.html`, the approved look. Copy its structure and colours.

**Repo conventions (must follow):**
- **Build and test:** `-c Release --artifacts-path C:/Users/SCPFAZ~1/AppData/Local/Temp/claude/C--Users-SCP-Fazbear-source-repos-FazStellarisModmanager/b74155d1-9762-4fc6-b5b4-9edf787ffd31/scratchpad/art`. Build the whole `FazStellarisModmanager.sln`; Razor errors only show in the app project.
- **Processes:** never kill FazStellarisModmanager.exe, and never launch the app.
- **No backslash escape sequences in regular C# string literals:** use `(char)10`, `(char)92` or raw strings. Verbatim regex patterns are fine.
- **Razor:** no `<text>` element with attributes inside `@if`/`@foreach`.
- **Never touch** the user's real `Documents\Paradox Interactive\Stellaris` in tests.
- **TDD for Core and Session code.** UI is checked by building.
- **Flaky test:** `HelperProcessWorkshopService` has a timing-flaky test; if only it fails, rerun once.
- **Commits** end with a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Remembered inputs (settings and RecentHosts)

**Files:** `Core/AppSettings.cs`, new `Core/Session/RecentHosts.cs`, `Core/Session/SessionService.cs`, tests (`AppSettingsTests` or an existing settings test file, new `RecentHostsTests.cs`, `SessionServiceTests.cs`).

- [ ] **Settings fields.** Append to the `AppSettings` positional record:
  - `IReadOnlyList<string>? RecentHosts = null`
  - `int? HostPort = null`
  - `bool StreamLiveGame = false`
  - `bool StreamTechnology = true`

  Test: JSON with only the old fields loads with these defaults, and a round trip keeps them.
- [ ] **`RecentHosts` helpers** (static, pure):
  - `const int Max = 5`
  - `IReadOnlyList<string> Add(IReadOnlyList<string>? list, string address)`: trims, moves to the front, dedupes ignoring case, caps at 5.
  - `IReadOnlyList<string> Remove(IReadOnlyList<string>? list, string address)`

  Test each.
- [ ] **SessionService hooks.**
  - After a successful `JoinAsync(host, port)`, save `RecentHosts = RecentHosts.Add(old, $"{host}:{port}")` through `manager.UpdateSettings(manager.Settings with {...})`.
  - After a successful `HostAsync(port)`, save `HostPort = port`.
  - Add `public void ForgetRecentHost(string address)`.

  Tests, using the existing loopback test rigs in `SessionServiceTests`: join saves the address, host saves the port, forget removes.
- [ ] **Build, test, commit:** `feat: remember recent join addresses and the host port`

### Task 2: Fast tech-only read, low-priority reads

**Files:** `Core/Saves/GameSnapshot.cs`, `Core/Saves/GamestateScanner.cs`, `Core/Saves/SaveReader.cs`, `Core/Saves/LiveFilter.cs`, `Core/Saves/LiveGameService.cs`, tests (`GamestateScannerTests`, `SaveReaderTests` if present, `LiveFilterTests`, `LiveGameServiceTests`).

- [ ] **`GameSnapshot`:** append `bool TechOnly = false`.
- [ ] **`GamestateScanner.ScanTechs(byte[] data)`** returns `(List<SavePlayer> Players, List<SaveCountry> Countries)`.
  - It walks the top level with the existing `Reader`: it parses `player`, and in `country` it handles only the entries whose id is a player's country id.
  - The player block comes before the country block in real saves. If player ids aren't known yet when `country` is reached, remember the country block's range and handle it after the walk.
  - For each player country it reads only `tech_status`, the same way `ScanCountries` gets `Techs` today; reuse that helper and refactor if needed. Everything else is skipped by brace matching.
  - It returns `SaveCountry(id, type, null, empty dict, null, null, 0, 0…, [], techs)`.
  - **Tests:** a synthetic gamestate with 2 player countries and 1 AI country gives only the 2 player countries, with the same Techs as `ScanWithGlobals` gives for them, and no other data.
- [ ] **`SaveReader.ReadTechs(path)`:** like `Read`, but uses `ScanTechs`, sets `TechOnly = true`, and leaves Community, Megastructures, GlobalFlags and Galaxy null. It keeps `Dlcs` from meta. Test with a small synthetic `.sav` zip; check how the existing SaveReader tests build one.
- [ ] **`LiveFilter.TechOnly(GameSnapshot s, int? viewerId)`** returns `s with`:
  - `SavePath` = file name only, as in `For`;
  - `TechOnly = true`;
  - `Countries` = the viewer's country only, as `new SaveCountry(c.Id, c.Type, null, empty, null, null, 0, 0,0,0,0,0,0,0, null, [], c.Techs)`, or empty when the viewer is null or unknown;
  - Community, Megastructures, Diplo and GlobalFlags null.

  It works on full and tech-only snapshots alike. Test both.
- [ ] **Low-priority reads in `LiveGameService`:** run the read (and enrich) on a dedicated thread with `IsBackground = true` and `Priority = ThreadPriority.BelowNormal`. Use a `TaskCompletionSource` helper such as `static Task<T> RunLowPriority<T>(Func<T> work)` instead of `Task.Run`. Exceptions must propagate to the awaiting code exactly as before, and existing tests must pass.
- [ ] **Build, test, commit:** `feat: fast tech-only save read; save reads run at below-normal priority`

### Task 3: Streaming modes in the session

**Files:** `Core/Session/SessionMessages.cs`, `Core/Session/SessionProtocol.cs`, `Core/Session/SessionHost.cs`, `Core/Session/SessionClient.cs`, `Core/Session/SessionService.cs`, `FazStellarisModmanager/AppServices.cs` (if constructor changes), tests (`SessionHostTests`, `SessionServiceTests`, protocol tests).

- [ ] **Protocol:** `SessionProtocol.Version = 3`; update the test that asserts the version, if there is one.
- [ ] **Messages:**
  - `public sealed record StreamingState(bool LiveGame, bool Technology) : SessionMessage;` with `[JsonDerivedType(typeof(StreamingState), "streaming")]`.
  - A `public enum StreamMode { Off, Technology, LiveGame }` in Core/Session, plus a helper turning the two settings into the effective mode: LiveGame wins, then Technology, else Off.
- [ ] **SessionHost:**
  - **Mode:** holds `StreamMode` (initial value passed in by the constructor or a setter before peers connect).
  - **`UpdateLive(GameSnapshot?)`** stores the newest snapshot as today.
  - **`SendLive(p)`:**
    - Off: send nothing.
    - Technology: `LiveFilter.TechOnly(live, p.LiveCountry)`.
    - LiveGame: `LiveFilter.For` as today.
  - **Sizes:** record the JSON size of the last view sent (UTF-8 bytes of the serialized `LiveUpdate` payload; reuse the framing serializer if it is accessible, otherwise `JsonSerializer.SerializeToUtf8Bytes` with the session JSON options). Expose `LastLiveSentDate` (snapshot Date) and `LastLiveSentBytes` (max over peers in that broadcast).
  - **`SetMode(StreamMode)`:** updates the mode, broadcasts `StreamingState` (effective booleans) to all peers, and resends `SendLive` to every peer when a snapshot is there and the mode isn't Off.
  - **New peers** get `StreamingState` right after `HostTarget`, then live data as today, if any and not Off.
- [ ] **SessionClient:** handles `StreamingState` by storing `Streaming` and raising its change event, the same way `Live` is handled.
- [ ] **SessionService:**
  - **`HostStreaming`** (`StreamingState?`): for a client, from the client; for a host, its own effective state.
  - **`SetStreamingAsync(bool liveGame, bool technology)`:**
    - saves the settings;
    - if hosting, (re)configures the sources;
    - calls `host.SetMode`;
    - raises `Changed`.
  - **Sources while hosting:**
    - **LiveGame:** subscribe `PushLive` to the shared full `live` service, start it, and push `live.Current`.
    - **Technology:** create, if needed, a light `LiveGameService(saveGamesDir, read: SaveReader.ReadTechs)` with no enrich. Subscribe, start it, and push its Current. If its Current is null but the full service has a snapshot, push the full one first, so clients get techs right away.
    - **Off:** unsubscribe both; the light service can be disposed.
    - **Wiring:** the light service's `saveGamesDir` is the same as AppServices uses (`Path.Combine(manager.Resolve().UserDir, "save games")`, guarded). Allow injecting a `Func<string, GameSnapshot>` tech reader through an optional constructor parameter for tests.
  - **`HostAsync`:** sets the mode from settings instead of always pushing full live data.
  - **Stopping or disposing** unsubscribes and disposes the light service.
  - **Client:** when `Streaming` changes to both false, clear `_hostLive`/`_hostLiveReceivedUtc` (as `ForgetHostLive` does while not a client) and raise `Changed`.
  - **Expose for the UI:** `LastLiveSentDate`, `LastLiveSentBytes`.
- [ ] **Tests, over loopback like the existing ones:**
  - Off: a client gets `StreamingState(false, false)` and no LiveUpdate after `UpdateLive`.
  - Technology: the LiveUpdate snapshot has `TechOnly`, only the viewer's country, and Techs equal to the source.
  - Switching LiveGame → Technology resends a TechOnly view without a new `UpdateLive`.
  - A peer joining later gets `StreamingState`.
  - `SetStreamingAsync` persists the settings.
  - A client receiving Off drops `HostLive`.
- [ ] **Build, test, commit:** `feat: host streaming modes (off / technology / live game), protocol v3`

### Task 4: UI

**Files:** `FazStellarisModmanager/Pages/SessionPage.razor` (rewrite the markup and add handlers; keep the existing logic: Run/RunAsync, the Workshop prompt flow, OnSessionChanged, the disconnect handling), `Components/DiffView.razor` (add `[Parameter] public string? Kind { get; set; }` with values "missing", "different", "extra", "order", "dlc"; null shows all sections; the ShouldRender cache must also compare Kind), `Pages/LiveGamePage.razor` (tech-only notice), new `Services/PublicAddressLookup.cs` (singleton in AppServices), `wwwroot/css/site.css`, and `wwwroot/js/` for a small `copyText(text)` helper if no clipboard helper exists (check the existing js files and how `index.html` loads scripts).

- [ ] **`PublicAddressLookup`:**
  - `Task<string?> GetAsync(CancellationToken)`;
  - a single static `HttpClient`;
  - `GET https://api.ipify.org` with a 5 s timeout;
  - returns the text only when it is a valid IPv4 (`IPAddress.TryParse` and `AddressFamily.InterNetwork`);
  - caches the result per app run, but retries after a failure.

  SessionPage fetches it when the role becomes Host and shows the chip once it is known.
- [ ] **SessionPage**, following spec section 4 and the mockup:
  - **Not-connected card:** segmented Host/Join (default Join if RecentHosts is non-empty), recent chips (click fills, ✕ calls `ForgetRecentHost`), "Playing as", the buttons. The join box is prefilled with RecentHosts[0] and the host port with `Settings.HostPort ?? SessionProtocol.DefaultPort`.
  - **Header:** the ring as a conic-gradient computed from the players' statuses, title, share chips with copy (JS interop), the client "Host streams" pills, and buttons.
  - **Left column:**
    - **Players panel:** avatar hue from a stable hash, e.g. the sum of chars × 31 mod 360, then `hsl(h 45% 45%)`; the sub-line from summary/activity; the status dot.
    - **Host only:** the Streaming panel with the two toggle cards. Clicking calls `Session.SetStreamingAsync`. Technology is disabled with the status "Included in live game" when live game is on; otherwise its status is "On · last sent DATE · N KB/player" or "Off".
  - **Right column:** the comparison card with the 5 tiles (counts from the DiffResult; `DiffSummary.From` gives missing/different/extra/order/DLC). The tile state selects `DiffView Kind`. Then the Match host result card (client), restyled as a card with the same content as today.
  - Keep `MaxRosterRows` and `Trunc`.
- [ ] **LiveGamePage:** when `Feed.Current?.TechOnly == true`, show the spec's notice instead of the scoreboard and sidebar. Tech and Events need no change.
- [ ] **CSS:** session classes modelled on the mockup's CSS: `session-card`, `seg`, `chip`, `session-head`, `ring`, `session-grid`, `players-panel`, `prow`, `pav`, `stream-panel`, `tog`/`sw`, `cmp-card`, `tiles`/`tile`. Reuse the existing `--panel`, `--border`, `--muted`, `--accent`, `--ok`, `--warn` and `--danger` variables.
- [ ] **Build the solution and run all tests.** Commit: `feat: redesigned Session tab (ready ring, player sidebar, streaming toggles, remembered addresses)`

### Task 5: Real-data check (controller)
On the user's newest save:
- time `SaveReader.ReadTechs` against `SaveReader.Read`;
- check that the techs of country 0 are equal;
- report the size of `LiveFilter.TechOnly(..., 0)` as JSON.

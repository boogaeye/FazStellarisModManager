# Live Game — Design (Sub-project 14)

## Goal
A **Live Game** tab with:
- a sidebar for the player's own empire;
- a ranked scoreboard of every empire, with flags;
- an animation when two empires swap places.

Empires the player has not contacted are hidden as **???**. Numbers are the real values from the save. Values that overflowed in the game are flagged.

Data comes only from **save files**:
- **Single player:** the player's own saves.
- **Multiplayer:** the host's saves. The host's app shares the data through the session.

Later, the Tech tab takes researched techs from this data instead of manual marking.

Mockup (approved): `.superpowers/brainstorm/506-1791170762/content/live-game.html`.

## Research findings (from the user's real files)
- **Logs:** `game.log` has no gameplay data, so the log-mod approach was dropped.
- **Saves:** a `.sav` is a zip containing `meta` (about 3 KB) and `gamestate`, both plain Paradox script. In the user's late game, `gamestate` is **409 MB** (28 MB zipped) and inflates in about 0.65 s.
- **Autosaves:** they rotate, keeping only the latest 5. `hotjoin_*.sav` files are written when someone joins. In the user's game, quarterly autosaves arrive about 9 real minutes apart.
- **`player={ { name="SCP Fazbear" country=0 } … }`** maps Steam names to country ids.
- **Each country has:**
  - `type`, `victory_rank`, `victory_score`, `military_power`, `economy_power`, `tech_power`, `fleet_size`, `empire_size`, `num_sapient_pops`;
  - `name={ key=… variables=… }`, `adjective={…}`;
  - `flag={ icon={category file} background={category file} colors={…} }`;
  - `variables={…}`;
  - `relations_manager={ relation={ owner country contact=yes communications=yes … } … }`;
  - `tech_status={ technology="x" level=1 … }`.
- **Large values are stored in full** (for example `military_power=3231227834.78`). They are not wrapped.
- **Diplomatic weight is not in the save.**
  - The game computes it from `NGameplay` defines:
    - `DIPLOMACY_WEIGHT_NAVAL_FACTOR` (0.025)
    - `…_ECONOMY_FACTOR` (0.15)
    - `…_TECHNOLOGY_FACTOR` (0.1)
    - `…_POP_BASE` (0.01)
    - `…_POP_HAPPINESS` (2.0)
    - `…_BASE` (0)
  - The result is then multiplied by modifiers, such as Galactic Community resolutions.
  - The mod Galactic Market Overhaul caches its own value in the country variable `egm_cached_diplo_weight`.
  - Checked against the user's game: the base is about 81.8M and the game shows about 358M. That matches roughly ×4.4 from modifiers, so the 358M is not an overflow.

## Parts (each one usable on its own)
1. **Reading saves and the Live Game tab, locally:** single player, or the host viewing their own saves.
2. **Sharing through the session:** the host sends each client a version filtered to that client's empire. The tab shows a "host disconnected" state. The Tech tab switches to live researched techs, and manual marking is removed.
3. **Real flags and full localised names:** flags are composited from `gfx/flags` with the save's colours, and names are resolved from localisation templates.

## Part 1 design

### Reading saves (Core, `Saves/`)
- **`SaveFiles.Newest(saveGamesDir)`:**
  - Returns the newest `*.sav` (by last write time) in `<user dir>/save games/*/`, or null.
  - It includes autosaves, manual saves and hotjoin saves.
- **`SaveReader.Read(path)`** returns a `GameSnapshot`. It:
  - opens the zip;
  - parses `meta` with `ParadoxScriptParser` (name, date, version);
  - reads `gamestate` into a byte array.
- **`GamestateScanner`** works on those bytes:
  - It walks the top-level keys and skips unwanted blocks by matching braces. Quotes and comments are respected.
  - **`player`:** the block is parsed.
  - **`country`:** for each `<id>={ … }`:
    - simple `key=value` lines at depth 1 are read directly;
    - the depth-1 blocks `name`, `adjective`, `flag`, `variables`, `relations_manager` and `tech_status` are cut out and parsed with `ParadoxScriptParser`;
    - all other blocks are skipped;
    - `none` entries are skipped.
- **The records:**
  - `GameSnapshot(SaveName, Date, Version, SavePath, SavedUtc, Players, Countries)`
  - `SavePlayer(Name, CountryId)`
  - `SaveCountry(Id, Type, NameKey, NameVariables, AdjectiveText, Flag, VictoryRank, VictoryScore, MilitaryPower, EconomyPower, TechPower, FleetSize, EmpireSize, Pops, CachedDiploWeight, ContactedIds, Techs)`
  - `CountryFlag(IconCategory, IconFile, BackgroundCategory, BackgroundFile, Colors)`
- **Contacts:** contacted ids are the `country` values of `relation` entries that have `contact=yes`.

### Logic (Core, pure)
- **`LiveBoard.Build(GameSnapshot, viewerId)`** returns `BoardRow`s:
  - It includes countries with `victory_rank > 0`, sorted by rank.
  - Each row: Id, Rank, DisplayName, Flag, Known, IsViewer, PlayerName, and the stats.
  - **Known** means the viewer itself, or a country in the viewer's contacted ids.
  - **Unknown rows** have DisplayName "???" and null stats and flag.
- **`StatValue(raw)`:**
  - For stats that cannot be negative (score, the three powers, fleet, empire size, pops), a negative raw value is flagged `Overflowed`.
  - The estimated real value is then raw + 4,294,967.296, the 32-bit ×1000 wrap.
  - Display: "⚠ overflowed in game, real ≈ X".
- **`DiploWeight.Estimate(country, defines)`** returns its parts:
  - Naval = MilitaryPower × naval factor.
  - Economy = EconomyPower × economy factor.
  - Tech = TechPower × tech factor.
  - The pop part is a range from Pops × base up to Pops × (base + happiness × 1).
  - Base, plus a min/max total.
- **`DiploDefines.Load(sources)`:**
  - Reads `common/defines/*.txt` from the game directory and then the enabled mods.
  - Files load in file-name order, and the last value wins.
  - It takes `NGameplay.DIPLOMACY_WEIGHT_*`. Missing values fall back to the vanilla numbers above.
- **Display names (part 1):**
  - If the name key has no `%` and no `_`, it is used as is (for example "Interstellar Battlecat Regime").
  - Otherwise, the adjective text (from `adjective` variables) is joined with a prettified key, or "Empire <id>" is used.
  - Part 3 replaces this with real localisation.

### Live game service (Core, singleton)
- **`LiveGameService(Func<string?> saveGamesDir)`:**
  - Watches with `FileSystemWatcher` (`*.sav`, subdirectories), with a 15 s polling fallback.
  - When a newer save appears, it waits until the file size has stayed the same for 2 s and the file can be opened.
  - It then reads the save in the background and publishes `Current` (a GameSnapshot), `Status` and `Error`, and raises `Changed`.
  - A read is cancelled when an even newer save appears.
  - A failed read keeps the previous snapshot and sets `Error`.
- **When it starts:** the first time the Live Game tab is opened (`Start()`). It keeps running for the rest of the app session.
- **Viewer selection:**
  1. If there is exactly one player in the save, the viewer is that player's country.
  2. Otherwise, a player whose name matches `AppSettings.PlayerName` or `Environment.UserName` (ignoring case).
  3. Otherwise, a remembered choice from `%AppData%/FazStellarisModmanager/live-game.json`, which maps save name to country id.
  4. Otherwise, the user picks from a dropdown of the save's players, and the choice is remembered.

### Live Game tab (`Pages/LiveGamePage.razor`, `/live`, nav item after Conflicts)
- **Status bar:**
  - green when a snapshot is loaded: "Save <name> · game date <date> · read <N min> ago" plus the save file name;
  - "Waiting for a save…" when there is none;
  - the error text, if any;
  - a "Viewing as" dropdown when there are several players.
- **Sidebar (the viewer's country):**
  - a flag placeholder;
  - the name and rank;
  - victory score, diplomatic weight (see below), military, economy and tech power, fleet size, empire size, pops;
  - "Known empires: X of Y".
- **Diplomatic weight in the sidebar:**
  - if `egm_cached_diplo_weight` exists, its value is shown with the note "(from Galactic Market Overhaul)";
  - otherwise the base estimate range is shown;
  - a `<details>` shows the breakdown, with the note "bonuses from resolutions etc. multiply this in game".
- **Scoreboard:**
  - Columns: rank, flag placeholder, name with player tags ("YOU" or the player's name), score, military, economy, tech, fleet.
  - Rows are keyed by country id.
  - Unknown rows show "???" in every column.
  - Overflowed values show ⚠ with a tooltip.
- **Animation:**
  - After each new snapshot, `js/livegame.js` animates rows from their old to their new positions.
  - Rows that went up glow green with ▲; rows that went down glow red with ▼.
  - The script keeps each id's last position and rank.
- **Flag placeholder (part 1):** a coloured square, coloured from the first flag colour name through a small table of named colours, with the first letter of the name.
- **Number format:** grouped thousands, no decimals.

## Errors
Unreadable or corrupt saves, a missing save folder, and a viewer that can't be found are all shown in the status bar. The tab never throws, and the scoreboard keeps the last good snapshot.

## Testing
- **GamestateScanner:**
  - players;
  - country fields;
  - blocks that must be skipped, including braces inside quotes and comments;
  - `none` countries;
  - contacts, with `contact=yes` only;
  - techs;
  - the cached diplomatic weight variable.
  - Fixtures are small handwritten gamestates.
- **SaveReader:** a zip with meta and gamestate.
- **SaveFiles.Newest.**
- **LiveBoard:**
  - ordering by rank;
  - unranked countries excluded;
  - unknown rows hidden;
  - the viewer always known;
  - player tags.
- **StatValue:** overflow and normal values.
- **DiploWeight and DiploDefines:** last value wins, and missing values fall back to defaults.
- **Display names.**
- **LiveGameService:** reads the newest save, picks up a newer one, keeps the old snapshot on a corrupt one. Uses a temporary folder and an explicit `Refresh()` instead of waiting on the watcher.
- **Viewer resolution:** single player, name match, remembered choice.
- **UI:** checked by hand against the user's save.

## Part 2 design (approved): sharing through the session, and the Tech tab using live data
- **Messages (protocol version 2):**
  - `LiveUpdate(GameSnapshot Snapshot, int? ViewerId)`: host → client. The snapshot is already filtered for that client.
  - `LiveViewAs(int CountryId)`: client → host.
  - Older clients are rejected by the existing version check.
- **Filtering on the host, `LiveFilter.For(snapshot, viewerId)`:**
  - The viewer's own country is sent in full.
  - Contacted countries are sent without their techs and contact lists.
  - Uncontacted countries keep only Id, Type and VictoryRank. Everything else is blanked: no name, flag or stats.
  - With a null viewer, only Players are sent, so the client can pick.
  - `SavePath` is reduced to the file name.
- **Host (`SessionHost`):**
  - Keeps the latest snapshot. Each peer has a viewer country, auto-matched by session name against the save's players (ignoring case) unless the peer chose one.
  - Sends each peer its filtered update in these cases:
    - a new snapshot arrives (`UpdateLive`);
    - a peer joins while a snapshot is loaded;
    - a peer sends a valid `LiveViewAs`. The country must be one of the save's player countries; invalid choices are ignored.
  - A chosen country that is not in a new save's players is reset.
- **`SessionService`:**
  - Takes the `LiveGameService`. While hosting, it pushes `live.Current` to the host on every change and once at start.
  - As a client, it keeps the latest `LiveUpdate` and when it arrived (`HostLive`, `HostLiveReceivedUtc`), and keeps them after a disconnect.
  - `ViewAsAsync(id)` sends the choice to the host.
  - `ForgetHostLive()` clears the kept update. It does nothing while connected as a client.
  - Joining clears the old data.
  - These members form the interface `IHostLiveSource`.
- **`LiveFeed` (Core, singleton):** one source for the app.
  - `Source` is Local, Host (connected client with host data) or HostDisconnected (kept host data, no longer connected).
  - `Current`, `ViewerId`, `ReceivedUtc`, `Status`, `Error`, `Reading`, `ResearchedTechs` (the viewer's techs), `Changed`, `Start()`.
  - `SetViewerAsync(id)`: for a local source, the choice is remembered in `LiveViewerStore`; for a host source, it is sent to the host.
  - `UseLocal()`.
  - The local viewer is resolved with `LiveViewer.Resolve` whenever the local snapshot changes.
- **Live Game tab:** uses `LiveFeed`. The status bar reads:
  - "Live from host · game date · received N min ago" for the Host source;
  - a warning "Host disconnected, showing … from N min ago" plus a **Use my own saves** button for HostDisconnected;
  - the save information, as in part 1, for the Local source.
- **Tech tab:**
  - Manual "researched" marking is removed: the right-click toggle and the saved Researched list. Route targets stay.
  - Researched techs = `LiveFeed.ResearchedTechs`. The tab shows the source line ("Researched: N techs from Live Game (<date>)") or "No live game data…".
  - The research store keeps reading old files, but the Researched field is ignored and is written as empty.
- **Assumption:** the app session host is also the Stellaris game host.

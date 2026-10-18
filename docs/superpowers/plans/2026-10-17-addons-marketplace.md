# Addons and Marketplace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** an Addons tab that builds a generated "Faz Addons" mod. The mod always loads last, is rebuilt identically on every player in a session, and in v1 carries the Marketplace addon: resources on and off the market, bundle size and price, trade deals, AI trading, base storage, and the market rules (max per trade, trade fee, price swing).

**Architecture:**
- **New `Core/Addons/` namespace:**
  - `AddonSettings` + `AddonSettingsStore` (settings);
  - `MarketCatalog` (reads the current game);
  - `AddonBuilder` (a pure, deterministic text build);
  - `AddonMod` (writes `mod/faz_addons*`).
- **`ModManagerService.Apply`** appends the addon last when the effective settings are on.
- **Sessions** carry the host's settings, and the player builds from them.
- **App UI:** a new Addons page, plus a fixed row on the Mods tab.

**Spec:** `docs/superpowers/specs/2026-10-17-addons-marketplace-design.md`. It is the source of truth for rules, names and texts; read it fully. **Mockup:** `.superpowers/brainstorm/497-1792275941/content/addons-marketplace-v2.html`.

**Repo conventions (must follow):**
- **Build and test:** `-c Release --artifacts-path C:/Users/SCPFAZ~1/AppData/Local/Temp/claude/C--Users-SCP-Fazbear-source-repos-FazStellarisModmanager/153d2094-5622-4356-8c72-a06263024945/scratchpad/art`, on the whole `FazStellarisModmanager.sln`.
- **Processes:** never kill or launch the app.
- **No backslash escape sequences** in regular C# string literals: use `(char)10`, `(char)92` or raw strings.
- **Razor:** no `<text>` element with attributes inside `@if`/`@foreach`.
- **Never touch** the user's real Documents\Paradox Interactive\Stellaris or Steam folders in tests.
- **TDD** for Core and Session; the UI is checked by building.
- **Flaky test:** if only the `HelperProcessWorkshopService` timing test fails, rerun once.
- **Commits** end with a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Reusable code:**
  - `Core/Technology/{ContentSource,TechSources,Localisation,SpriteIndex,IconCache,DdsDecoder}.cs`;
  - `Core/Descriptors/ParadoxScriptParser`;
  - `Core/Conflicts/*` (how defines and definitions are read);
  - `Core/IO/AtomicFile`;
  - `Core/Game/LauncherPlaysetFile`.

### Task 1: Settings and MarketCatalog
**Files:** new `Core/Addons/{AddonSettings,AddonSettingsStore,MarketCatalog,ResourceInfo}.cs`, `Core/AppPaths.cs` (`Addons => Root/addons.json`), new tests `AddonSettingsStoreTests`, `MarketCatalogTests` (build synthetic sources with `ContentSource.FromPath` over temp folders).
- [ ] **Tests first:**
  - the store round trip; corrupt or missing files give the defaults;
  - overrides equal to the game value are dropped (`AddonSettings.Normalize(MarketCatalog)` or similar);
  - the catalog cases from the spec: winning file per name, the parsed fields (OnMarket, Hidden, Gated, Max, FixedMax, category, source name, files), last-wins defines (NEconomy keys and the NAI lists), and the localised name with a fallback to the key.
- [ ] **Implement as specified.**
  - **Icons:** resolve them lazily or in a separate step (`MarketCatalog.LoadIcons(sprites, icons, sources)`), so tests don't need images. In tests, `IconUri` can stay null.
  - **Category suggestions:** a `static int SuggestedBundle(string? category)`.
- [ ] **Full suite, then commit:** `feat: addon settings and a market catalog of the current game's resources and market defines`

### Task 2: AddonBuilder and AddonMod
**Files:** new `Core/Addons/{AddonBuilder,AddonOutput,AddonMod,ScriptTextEditor}.cs`, plus tests.
- [ ] **Tests first:**
  - every `AddonBuilder` case in the spec's Testing section, including byte-identical untouched regions and CRLF/BOM preservation;
  - determinism;
  - the defines file contents;
  - the descriptor;
  - `AddonMod.Write`: path line, stale file removal, nothing outside the folder touched.
- [ ] **`ScriptTextEditor`:** finds a top-level block's span by key, brace-matching with quotes and `#` comments respected. It finds a top-level key inside that block, replaces its value token, or inserts `key = value` after `{` with the file's line ending. It is pure string in, string out.
- [ ] **`AddonBuilder.Build`:** returns `AddonOutput(IReadOnlyList<(string Path, byte[] Bytes)> Files, bool HasChanges)`. The files are ordered by path (ordinal).
- [ ] **`AddonMod`:**
  - `const string DescriptorRel = "mod/faz_addons.mod"`, `ContentRel = "mod/faz_addons"`, `Name = "Faz Addons"`;
  - `Write(userDir, output)`;
  - `bool IsUpToDate(userDir, output)`: the file set and bytes are equal.
- [ ] **Full suite, then commit:** `feat: deterministic Faz Addons builder for the Marketplace and its writer`

### Task 3: Apply integration and AddonService
**Files:** `Core/ModManagerService.cs`, `Core/Lists/ModList.cs` (FromDlcLoad drops the addon), new `Core/Addons/AddonService.cs` (catalog cache plus settings: `Settings`, `Save(settings)`, `Catalog`, `BuildCatalogAsync(ModList)`, `Changed`), `FazStellarisModmanager/AppServices.cs`, tests (`ModManagerServiceTests`, `AddonServiceTests`).
- [ ] **ModManagerService:**
  - new `Func<AddonSettings?> EffectiveAddons { get; set; } = () => null;`;
  - **Apply,** when the effective settings are on with changes: build the sources for the list (`TechSources.Build`, needing the game dir; if it's missing, return a warning and don't append), build the catalog without icons, build, `AddonMod.Write`, then add `AddonMod.DescriptorRel` last to both the dlc_load list and the launcher playset list;
  - `ApplyResult` gains `AddonsApplied`;
  - `Library` excludes `AddonMod.DescriptorRel`.
- [ ] **AddonService (singleton):**
  - loads and saves settings through the store;
  - builds the catalog with icons off the UI thread for a given `ModList`, cached by the list's descriptor sequence plus a library version;
  - `Changed` event.
- [ ] **AppServices:** `ModManagerService.EffectiveAddons = () => session is client ? session.HostAddons : addonService.Settings`. Wire it after the services exist without creating a cycle, e.g. set it from a lazy lookup in the `SessionService` registration, or give the property a provider closure that resolves `SessionService` through `IServiceProvider` on first call.
- [ ] **Tests:**
  - Apply on → `mod/faz_addons.mod` is last in dlc_load and the playset, and the folder files are written;
  - Apply off → not appended;
  - the library and `ImportCurrent` exclude it;
  - a missing game dir gives a warning, not a crash.
- [ ] **Full suite, then commit:** `feat: Apply builds Faz Addons and loads it last; the addon is hidden from the mod library`

### Task 4: Sessions
**Files:** `Core/Session/{SessionMessages,SessionProtocol,SessionHost,SessionClient,SessionService,MatchPlan}.cs`, `Components/DiffView.razor` (label and hint for the addon unit), tests.
- [ ] **Protocol and messages:** protocol 4. `Welcome` and `HostTargetUpdate` get an `AddonSettings? Addons` parameter. The host fills it from a `Func<AddonSettings?>` given to `SessionService` (an optional constructor parameter, defaulting to null).
- [ ] **SessionService:**
  - `HostAddons`: for a client, from the client's target; null otherwise;
  - `RescanAsync` as host sends the current settings;
  - `MatchCoreAsync` relies on Apply using the effective settings, so no extra work is needed beyond making sure `HostAddons` is current.
- [ ] **MatchPlan:** a diff unit whose key is `local:faz_addons.mod` is excluded from `NeedsManualInstall` and `DiffersLocally`. It is applied through the addon build, not the host list.
- [ ] **DiffView:** for that unit, show the name "Faz Addons" and the hint "Click Match host to rebuild Faz Addons from the host's settings".
- [ ] **Tests (loopback):**
  - the player receives `HostAddons`;
  - the player's Match host makes the addon unit Ok in `MyDiff`. Use a fake game dir with a `common/strategic_resources` file so the build has content; see how `FakeInstall` builds a game dir;
  - leaving clears `HostAddons`;
  - the protocol is version 4.
- [ ] **Full suite, then commit:** `feat: sessions carry the host's addon settings; players rebuild Faz Addons to match`

### Task 5: UI
**Files:** new `Pages/AddonsPage.razor` (`@page "/addons"`), new `Components/AddonFilesDialog.razor` (the preview), the nav entry in the layout (find where the other tabs are listed, e.g. `Shared/MainLayout.razor` or `NavMenu`, after Live Game), `Pages/Mods.razor` (the fixed last row when the effective settings are on), `wwwroot/css/site.css`.
- [ ] **AddonsPage** follows spec section "Addons page" and the mockup v2 closely. It shows a "Reading resources…" state while the catalog builds.
  - **Edits:** `AddonService.Save(normalized settings)` on each change, debounced about 300 ms for text inputs.
  - **Apply:** calls the Mods page's apply path. Reuse it via a small shared helper if practical, otherwise call `Manager.Apply(new ModList(State.Name, State.Mods, State.DisabledDlcs))` with launcher-open errors shown. If `Session.Role != None && !Session.IsBusy`, it then calls `await Session.RescanAsync()`.
  - **Read-only:** when `Session.Role == SessionRole.Client`, show the banner, disable every control, and display `Session.HostAddons` (or "The host has addons off").
- [ ] **Mods page:** the 🔒 Faz Addons row after the list when `Manager.EffectiveAddons()` is on.
- [ ] **CSS:** copy the mockup's classes under an `.addons-page` scope.
- [ ] **Build the solution, run all tests, then commit:** `feat: Addons tab with the Marketplace editor and a read-only view for session players`

### Task 6: Real-data check (controller)
Build the catalog for the user's current dlc_load list, read-only:
- report the resource count, how many are on the market, the winning files and the defines;
- build with sample settings: MaxBaseAmounts 1000, Nuclei Soup on the market, AI trades for Cultrobium, minerals base storage 30000;
- inspect the generated files. Don't write them into the user's mod folder.

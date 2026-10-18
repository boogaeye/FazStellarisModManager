# Technologies Addon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** a Technologies addon on the Addons tab that creates simple custom techs (repeatable or not, with ethic locks, prerequisites, cost, weight and modifiers). Validation blocks broken techs, and the custom techs show in the Tech tab.

**Architecture:** these parts extend the existing addon pipeline (`Core/Addons/*`):
- the settings model gains `Technologies`;
- the new `TechAddonCatalog` reads techs, icons, categories, ethics and modifiers;
- the new `TechValidator` sorts problems into errors (deterministic; they skip the tech) and warnings (machine-dependent; they never change the build);
- `AddonBuilder` emits the tech and localisation files;
- `TechTreeService` adds the Faz Addons folder as the last source for the current-game tree;
- the protocol goes to 5;
- the AddonsPage gains a Technologies section.

**Spec:** `docs/superpowers/specs/2026-10-18-addons-technologies-design.md`. It is the source of truth; read it fully. **Mockup:** `.superpowers/brainstorm/1832-1792284174/content/addons-technologies.html`. **Previous addon spec**, for context: `docs/superpowers/specs/2026-10-17-addons-marketplace-design.md`.

**Repo conventions (must follow):**
- **Build and test:** `-c Release --artifacts-path C:/Users/SCPFAZ~1/AppData/Local/Temp/claude/C--Users-SCP-Fazbear-source-repos-FazStellarisModmanager/153d2094-5622-4356-8c72-a06263024945/scratchpad/art`, on the whole `FazStellarisModmanager.sln`.
- **Processes:** never kill or launch the app.
- **No backslash escape sequences** in regular C# string literals: use `(char)10`, `(char)34`, `(char)92` or raw strings.
- **Razor:** no `<text>` element with attributes inside `@if`/`@foreach`.
- **Never touch** the real Documents\Paradox Interactive\Stellaris or Steam folders in tests. Reading them only to learn formats is fine.
- **TDD** for Core; the UI is checked by building.
- **Flaky test:** if only the `HelperProcessWorkshopService` timing test fails, rerun once.
- **Commits** end with a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Reusable code:**
  - `Core/Addons/{AddonSettings,AddonSettingsStore,MarketCatalog,AddonBuilder,AddonService,AddonMod}.cs`;
  - `Core/Technology/{ContentSource,TechSources,Localisation,IconCache,SpriteIndex,TechParser,TechTreeService}.cs`;
  - `Core/Descriptors/ParadoxScriptParser.cs`;
  - `Tests/TestUtil/{MarketFixture,FakeInstall}.cs`.

### Task 1: Model, TechAddonCatalog, TechValidator (Core, TDD)
- [ ] **Settings model:** add `TechnologiesSettings`, `CustomTech` and `TechModifier`, plus the `Technologies` property on `AddonSettings`, exactly as in the spec.
  - Value equality covers the lists (`Techs`, `Ethics`, `Prerequisites`, `Modifiers`).
  - JSON round-trips through `AddonSettingsStore`, which drops null techs and null list items on load (the same hardening as resources).
  - `AddonSettings.Normalize` keeps techs as they are.
- [ ] **`TechAddonCatalog`** as in the spec, with tests over synthetic sources (`MarketFixture` style) and a sample `modifiers.log` text.
- [ ] **`TechValidator`** as in the spec. There is one test per error and warning, plus `Buildable`.
- [ ] **AddonService:** builds the `TechCatalog` alongside the market catalog, using the same sources and localisation, with `modifiersLogPath = <userDir>/logs/script_documentation/modifiers.log`. It exposes `TechCatalog`. Extend the existing tests.
- [ ] **Full suite, then commit:** `feat: custom technology settings, a technology catalog of the current game and validation`

### Task 2: Builder, Apply, Tech tab, protocol (Core, TDD)
- [ ] **`AddonBuilder`:** emits `common/technology/zzzzz_faz_addons_tech.txt` and `localisation/english/faz_addons_l_english.yml` for the buildable techs. The text format is exactly as in the spec; tests compare full text.
  - `Build` needs the tech catalog for validation and for fanatic variants. Change the signature to take a combined input, e.g. `Build(MarketCatalog market, TechAddonCatalog techs, AddonSettings settings, string? gameVersion)`, and update the callers. An empty catalog (`TechAddonCatalog.Empty`) is allowed when Technologies is off.
- [ ] **Apply in `ModManagerService`:**
  - build the tech catalog without localisation, icon images or the log; icon names come from file names;
  - `ApplyResult` gets the "Not added (fix on the Addons tab): …" warning for techs with errors, folded into `AddonWarning`.
- [ ] **AddonService.Preview** includes the techs.
- [ ] **TechSources / TechTreeService:**
  - `TechSources.Build(..., bool includeAddon = false)` appends a `Faz Addons` source for `<userDir>/mod/faz_addons` when the folder exists;
  - `TechTreeService` passes true only for the current-game choice, and only when `dlc_load.json` lists `AddonMod.DescriptorRel`;
  - tests.
- [ ] **Protocol 5;** the techs round-trip through `Welcome` in a framing test.
- [ ] **Full suite, then commit:** `feat: Faz Addons builds custom technologies; the Tech tab shows them; protocol 5`

### Task 3: UI
- [ ] **AddonsPage:** the Technologies entry in the left list (switch plus count) and the Technologies section exactly as in the spec's UI part and the mockup:
  - cards with error/warning badges;
  - the editor;
  - the icon picker (lazy `IconUri`), the modifier search and the prerequisite search;
  - the preview card;
  - Technology/Localisation tabs showing the tech's generated text: use `AddonBuilder` on a settings copy holding just that tech, or a small public helper `AddonBuilder.TechBlock(tech, catalog)`;
  - the issue list;
  - the read-only mode for session clients.
- [ ] **Static UI helpers** in Core (slug, cost-at-level text, effect text with sign and localised name), unit-tested.
- [ ] **CSS:** copy the mockup's classes under `.addons-page`.
- [ ] **Build the solution, run all tests, then commit:** `feat: Technologies editor on the Addons tab with live preview and validation`

### Task 4: Real-data check (controller)
- Report the tech catalog counts for the user's list.
- Validate and build the mockup's example tech in the scratchpad.
- Inspect both files.

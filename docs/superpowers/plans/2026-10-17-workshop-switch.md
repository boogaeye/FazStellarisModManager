# Workshop Switch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Match host offers to switch a client's differing local copy of a Workshop mod to the Workshop item the host uses. It downloads the item, applies the list with it, and the local copy's name is renamed out of the way.

**Architecture:**
- `MatchPlan` gets the `NeedsWorkshopSwitch` and `SwitchedToWorkshop` lists.
- `WorkshopNeeds` gets a `Switch` kind.
- `WorkshopPrompt` and the Session result card show them.
- The download and apply path (`InstallFromWorkshopAndMatchAsync` → `MatchCoreAsync` → the replaced rule → `ResolveDupesWithDefaults` → Apply) already does the rest.

**Spec:** `docs/superpowers/specs/2026-10-17-workshop-switch-design.md`. It has the exact rules and texts.

**Repo conventions:**
- **Build and test:** `-c Release --artifacts-path C:/Users/SCPFAZ~1/AppData/Local/Temp/claude/C--Users-SCP-Fazbear-source-repos-FazStellarisModmanager/b74155d1-9762-4fc6-b5b4-9edf787ffd31/scratchpad/art`, on the whole `FazStellarisModmanager.sln`.
- **Processes:** never kill or launch the app.
- **No backslash escape sequences** in regular C# strings.
- **Razor:** no `<text>` with attributes in `@if`/`@foreach`.
- **Never touch** the real Stellaris or Steam folders in tests.
- **Flaky test:** if only the `HelperProcessWorkshopService` timing test fails, rerun once.
- **Commits** end with a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

### Task 1: Core (TDD)
- [ ] **Tests first:**
  - `MatchPlanTests` and `WorkshopNeedsTests` cases from the spec's Testing section;
  - the `SessionServiceTests` end-to-end switch case, using the existing install-and-match rig and `FakeWorkshop`. Check how the current install-and-match tests create the Workshop folder and descriptors.
- [ ] **`MatchPlan`:**
  - add `List<UnitDiff> NeedsWorkshopSwitch` and `List<UnitDiff> SwitchedToWorkshop` to the record, and update every constructor call;
  - classify as in the spec;
  - extend `IsComplete`;
  - fill `SwitchedToWorkshop` from the replaced set: the original UnitDiffs whose host key is `ugc:`. Note: replaced pairs may be Ok or ContentMismatch; include both.
- [ ] **`WorkshopNeeds`:** add `WorkshopNeedKind.Switch`, then build and dedupe the items as the spec says.
- [ ] **Run the tests, the full suite, and commit:** `feat: Match host can switch a differing local copy to the host's Workshop item`

### Task 2: UI
- [ ] **`Components/WorkshopPrompt.razor`:**
  - the Switch badge (reuse the `badge local` style used by Update, or add `badge switch`);
  - the note text;
  - the existing tick, run and results flow, which covers Switch items without other changes. Check the results phase's wording copes with Switch, e.g. "Installed/updated" → "Downloaded".
- [ ] **`Pages/SessionPage.razor`,** in the Match host result card:
  - the "Now using the Workshop version instead of your local copy:" list;
  - the "switch from the Workshop mods pop-up" list for leftover `NeedsWorkshopSwitch`;
  - the `DiffersLocally` note for "host uses a local copy too". A small static helper in Core for that check, `MatchPlan.HostUsesLocalCopyOfSameItem(UnitDiff, ...)`, needs the host's RemoteId (`UnitDiff.RemoteId`) and my RemoteId. If my RemoteId isn't available on UnitDiff, look it up from the library through `MineKey`, or simply check that the host key is not `ugc:` while `UnitDiff.RemoteId` is a Workshop id.
- [ ] **Build the solution, run all tests, and commit:** `feat: Workshop pop-up and result card show local-copy switches`

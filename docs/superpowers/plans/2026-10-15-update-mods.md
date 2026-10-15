# Update Mods Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An "⟳ Update mods" button that makes Steam update every Workshop mod in the current list and reports Updated / Up to date / Failed.

**Architecture:**
- **Core:** `FolderStamp` (folder fingerprint) and `ModUpdater` (picks the ids, runs `IWorkshopService.InstallAsync`, classifies the results by comparing stamps).
- **UI:** an `UpdateModsDialog` component, plus the button and the post-close refresh in `Mods.razor`.

**Tech Stack:** .NET 10, Blazor (BlazorWebView), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-15-update-mods-design.md`. It has the exact behaviour, texts and signatures.

**Repo conventions:**
- **Build and test:** `-c Release --artifacts-path C:/Users/SCPFAZ~1/AppData/Local/Temp/claude/C--Users-SCP-Fazbear-source-repos-FazStellarisModmanager/b74155d1-9762-4fc6-b5b4-9edf787ffd31/scratchpad/art`, on the whole `FazStellarisModmanager.sln`.
- **Processes:** never kill or launch the app.
- **No backslash escape sequences** in regular C# strings.
- **Razor:** no `<text>` with attributes in `@if`/`@foreach`.
- **Never touch** the real Stellaris folder in tests.
- **Flaky test:** if only the `HelperProcessWorkshopService` timing test fails, rerun once.
- **Commits** end with a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

### Task 1: Core (TDD)
- [ ] **`FolderStampTests`:**
  - a missing folder gives null;
  - counts and bytes are correct;
  - changing content, mtime or adding a file changes the stamp;
  - stamping an unchanged folder twice gives equal stamps.
- [ ] **`ModUpdaterTests`,** using or extending `Tests/TestUtil/FakeWorkshop.cs`. The fake's `InstallAsync` must:
  - modify the folder of some ids;
  - leave others alone;
  - fail one;
  - return "Cancelled." for one.

  Check the outcomes, that progress is forwarded, and that `WorkshopIds` keeps the ugc entries in order, deduped, skipping local ones.
- [ ] **Implement `Core/Workshop/FolderStamp.cs` and `Core/Workshop/ModUpdater.cs`** as specified. Stamping runs inside `Task.Run`. `IOException` and `UnauthorizedAccessException` while stamping give a null stamp.
- [ ] **Run the tests, the full suite, and commit:** `feat: ModUpdater updates a list's Workshop mods and tells updated from up-to-date by folder stamps`

### Task 2: UI
- [ ] **`Components/UpdateModsDialog.razor`.** Copy the modal markup and progress-row pattern from `Components/WorkshopPrompt.razor`: Confirm → Working → Done as the spec says.
  - Use `IProgress<WorkshopProgress>` and marshal updates to the UI with `InvokeAsync(StateHasChanged)`, throttled to about 4 per second.
  - Use a `CancellationTokenSource` for Cancel.
  - Handle `WorkshopUnavailableException`.
  - Escape and the backdrop don't close the dialog while it's working.
- [ ] **`Pages/Mods.razor`:**
  - the button `⟳ Update mods (N)` after "+ Add mods";
  - `FolderOf(id)`: the library mod with key `ugc:<id>`, then its ContentPath;
  - names from the list entries.
  - **On close with results:**
    - the Refresh logic (RefreshLibraryAsync, `Icons.Refresh()`, `FileCache.Clear()`, `LibraryChanged()`);
    - then, if `Session.Role != SessionRole.None && !Session.IsBusy`, `await Session.RescanAsync()`, with errors going to the status line;
    - the status shows the summary.
  - Inject `SessionService` and `IWorkshopService` as needed.
- [ ] **Build the solution, run all tests, and commit:** `feat: Update mods button and dialog on the Mods tab`

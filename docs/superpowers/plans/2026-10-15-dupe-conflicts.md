# Dupe Conflicts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect mods whose descriptor names clash (Stellaris then skips all of them) and let the user pick which copy keeps the name; the rest are renamed in their `.mod` files and in the launcher database. Apply asks first; Match host uses defaults.

**Architecture:**
- **Core:** `DupeConflicts` (pure grouping and default choice), `DupeResolver` (file renames, backups) and `LauncherPlaysetFile.RenameMods` (database update).
- **Service:** `ModManagerService` wraps them with the launcher-open check and a library refresh.
- **UI:** a new `DupeDialog` component, plus badges and Apply gating in `Mods.razor` and a badge in `AddModsDialog`. `SessionService` resolves with the defaults during Match host.

**Tech Stack:** .NET 10, Blazor (BlazorWebView), Microsoft.Data.Sqlite, xUnit.

Spec: `docs/superpowers/specs/2026-10-15-dupe-conflicts-design.md`. The launcher sync it builds on is `docs/superpowers/specs/2026-10-15-launcher-playset-sync-design.md`.

**Repo conventions (must follow):**
- **Build and test:** always pass `-c Release --artifacts-path C:/Users/SCPFAZ~1/AppData/Local/Temp/claude/C--Users-SCP-Fazbear-source-repos-FazStellarisModmanager/b74155d1-9762-4fc6-b5b4-9edf787ffd31/scratchpad/art`. Use the solution `FazStellarisModmanager.sln`.
- **Processes:** never kill FazStellarisModmanager.exe, and never launch the app.
- **No backslash escape sequences in regular C# string literals:** use `(char)10`, `(char)92` or raw string literals. Verbatim `@"..."` regex patterns are fine; `ModKeys` already uses one.
- **Razor:** no `<text>` element with attributes inside `@if`/`@foreach`.
- **Never touch** the user's real `Documents\Paradox Interactive\Stellaris` folder in tests.
- **Commits** end with a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Flaky test:** `HelperProcessWorkshopService` has a timing-flaky test; if only it fails, rerun once.

---

### Task 1: Core: DupeConflicts, DupeResolver, LauncherPlaysetFile.RenameMods, AtomicFile.WriteAllBytes

**Files:**
- Create: `FazStellarisModmanager.Core/Library/DupeConflicts.cs`
- Create: `FazStellarisModmanager.Core/Library/DupeResolver.cs`
- Modify: `FazStellarisModmanager.Core/Game/LauncherPlaysetFile.cs` (add `RenameMods`, reusing the private `Backup` and connection helpers)
- Modify: `FazStellarisModmanager.Core/IO/AtomicFile.cs` (add `WriteAllBytes`, the same temp-and-move pattern)
- Create: `FazStellarisModmanager.Tests/DupeConflictsTests.cs`
- Create: `FazStellarisModmanager.Tests/DupeResolverTests.cs`

- [ ] **Step 1: Tests for `DupeConflicts`.** Build `InstalledMod` values directly; the record signature is in `Core/Library/InstalledMod.cs`. A helper:

```csharp
static InstalledMod Mod(string rel, string name, bool workshop = false) =>
    new(ModKeys.For(rel), name, rel, null, "1", "v4.5.*", "C:/x/" + rel, workshop ? ModSource.Workshop : ModSource.Local, []);
```

Cases:
- **Grouping:** `Find` groups "UI Overhaul Dynamic" (`mod/ugc_1.mod`, Workshop) with " ui overhaul dynamic " (`mod/8cde_a.mod`, Local). "Other" (single) is excluded. Members are ordered by DescriptorRel, ignoring case (`mod/8cde_a.mod` first). Groups are ordered by Name, and a group's Name is its first member's trimmed name.
- **`DefaultKeep`:**
  - (a) exactly one member is in the list → that member, even when it is Local and another is Workshop;
  - (b) none are in the list → the Workshop member;
  - (c) two members are in the list, one Local and one Workshop → the Workshop one;
  - (d) no Workshop member and none in the list → the first by DescriptorRel.
- **`DuplicateName`:** `DuplicateName("UI Overhaul Dynamic", "mod/8cde_cf2c63b4.mod")` == `"UI Overhaul Dynamic (duplicate: 8cde_cf2c63b4)"`.

- [ ] **Step 2: Tests for `DupeResolver`.** Use `TempDir` and `LauncherDb` from TestUtil. Write descriptors with raw string literals:
- **Keeper and losers:** `Resolve` with keep = `mod/ugc_1.mod` renames `mod/8cde_a.mod`.
  - The loser's file equals the original text with only the name value changed. Write the original with CRLF line endings and a UTF-8 BOM, and check both survive: compare bytes against the expected bytes.
  - The keeper's file is byte-identical to before.
  - `Renamed` has one entry (rel, old name, new name), and `BackupDir` contains `8cde_a.mod` with the original bytes.
- **Missing name line:** a descriptor without a `name=` line gets `name="X (duplicate: f)"` plus a newline (the file's own newline style) inserted at the start, after any BOM. Here "X" is the group name.
- **`SetName`:** it only changes the first top-level `name=` line (column 0, optionally with leading whitespace), not lines inside braces such as `dependencies = { "name" }`. Test a descriptor where `name="A"` comes after a `tags={...}` block.
- **Launcher database:** when `launcher-v2.sqlite` exists (`LauncherDb.Create` plus `AddMod(db, "id8", "mod/8cde_a.mod")`), `displayName` of `id8` becomes the new name, and `LauncherWarning` is null.
- **No database:** no warning.
- **Corrupt database** (junk bytes): the descriptor is still renamed, `LauncherWarning` is not null, and the database bytes are unchanged.

- [ ] **Step 3: Run the tests and confirm they fail** (compile errors).

- [ ] **Step 4: Implement.**

`DupeConflicts.cs`:
```csharp
namespace FazStellarisModmanager.Core.Library;

/// <summary>Installed mods sharing one descriptor name. Stellaris skips every mod whose name another mod/*.mod also uses.</summary>
public sealed record DupeGroup(string Name, IReadOnlyList<InstalledMod> Mods);

public static class DupeConflicts
{
    /// <summary>Groups of two or more mods whose names match (trimmed, case-insensitive), ordered by name; members by descriptor path.</summary>
    public static IReadOnlyList<DupeGroup> Find(IReadOnlyList<InstalledMod> library) =>
        library
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .GroupBy(m => m.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var mods = g.OrderBy(m => m.DescriptorRel, StringComparer.OrdinalIgnoreCase).ToList();
                return new DupeGroup(mods[0].Name.Trim(), mods);
            })
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The member that keeps the name by default: the only one in the list; else, among those in the list (or all), the first
    /// Workshop one; else the first.
    /// </summary>
    public static InstalledMod DefaultKeep(DupeGroup group, IReadOnlySet<string> inListRels)
    {
        var listed = group.Mods.Where(m => inListRels.Contains(m.DescriptorRel)).ToList();
        if (listed.Count == 1) return listed[0];
        var pool = listed.Count > 0 ? listed : group.Mods;
        return pool.FirstOrDefault(m => m.Source == ModSource.Workshop) ?? pool[0];
    }

    public static string DuplicateName(string name, string descriptorRel) =>
        $"{name} (duplicate: {Path.GetFileNameWithoutExtension(descriptorRel)})";
}
```
The caller passes `inListRels` as a `HashSet<string>(StringComparer.OrdinalIgnoreCase)`.

`DupeResolver.cs`:
```csharp
using System.Text;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.IO;

namespace FazStellarisModmanager.Core.Library;

public sealed record DupeChoice(DupeGroup Group, InstalledMod Keep);
public sealed record DupeRename(string DescriptorRel, string OldName, string NewName);
public sealed record DupeResolution(IReadOnlyList<DupeRename> Renamed, string? BackupDir, string? LauncherWarning);

/// <summary>Renames every member of a dupe group except the kept one, in its mod/*.mod file and in the launcher database.</summary>
public static class DupeResolver
{
    // The first top-level name line: optional indentation, name = "value".
    static readonly Regex NameLine = new(@"^([ \t]*name[ \t]*=[ \t]*"")([^""]*)("")", RegexOptions.Multiline);

    public static DupeResolution Resolve(string userDir, IReadOnlyList<DupeChoice> choices, string backupDir, DateTimeOffset now)
    {
        var renames = new List<DupeRename>();
        string? dir = null;
        foreach (var (group, keep) in choices)
            foreach (var m in group.Mods)
            {
                if (string.Equals(m.DescriptorRel, keep.DescriptorRel, StringComparison.OrdinalIgnoreCase)) continue;
                var file = Path.Combine(userDir, m.DescriptorRel);
                if (!File.Exists(file)) continue;
                dir ??= Directory.CreateDirectory(Path.Combine(backupDir, "descriptors_" + now.UtcDateTime.ToString("yyyyMMdd_HHmmss_fff"))).FullName;
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
                var newName = DupeConflicts.DuplicateName(group.Name, m.DescriptorRel);
                var bytes = File.ReadAllBytes(file);
                var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                var text = Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
                var output = Encoding.UTF8.GetBytes(SetName(text, newName));
                AtomicFile.WriteAllBytes(file, bom ? [0xEF, 0xBB, 0xBF, .. output] : output);
                renames.Add(new DupeRename(m.DescriptorRel, m.Name, newName));
            }
        var warning = LauncherPlaysetFile.RenameMods(userDir, renames.Select(r => (r.DescriptorRel, r.NewName)).ToList(), backupDir, now);
        return new DupeResolution(renames, dir, warning);
    }

    /// <summary>The descriptor text with its first top-level name value replaced (or a name line added at the top).</summary>
    public static string SetName(string text, string newName)
    {
        var m = NameLine.Match(text);
        if (m.Success) return string.Concat(text.AsSpan(0, m.Groups[2].Index), newName, text.AsSpan(m.Groups[2].Index + m.Groups[2].Length));
        var newline = text.Contains((char)13 + "" + (char)10) ? (char)13 + "" + (char)10 : (char)10 + "";
        return "name=" + '"' + newName + '"' + newline + text;
    }
}
```

The regex needs the name line to start at column 0 (after optional spaces or tabs). Lines inside blocks are usually indented with tabs too, but a `name=` key only appears at the top level of `.mod` files: dependencies hold bare strings, not `name=`. Make sure the "name after a tags block" test passes.

`AtomicFile.WriteAllBytes(string path, byte[] contents)`: the same as `WriteAllText`, but with `File.WriteAllBytes`.

`LauncherPlaysetFile.RenameMods`:
```csharp
/// <summary>
/// Sets mods.displayName for these descriptors so the launcher keeps the new names when it rewrites mod/*.mod on start.
/// Null when done, when there is no database or nothing to rename; otherwise a warning (the transaction leaves the file unchanged).
/// </summary>
public static string? RenameMods(string userDir, IReadOnlyList<(string DescriptorRel, string NewName)> renames, string backupDir, DateTimeOffset now)
{
    var db = Path.Combine(userDir, FileName);
    if (renames.Count == 0 || !File.Exists(db)) return null;
    try
    {
        Backup(db, backupDir, now);
        var cs = new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 5 }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        RequireSchema(conn);
        using var tx = conn.BeginTransaction();
        foreach (var (rel, name) in renames)
            Exec(conn, "update mods set displayName = $n where lower(replace(gameRegistryId, char(92), '/')) = lower($g)",
                ("$n", name), ("$g", rel.Replace((char)92, '/')));
        tx.Commit();
        return null;
    }
    catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
    {
        return "Could not update the Paradox launcher's mod names: " + ex.Message;
    }
}
```
Factor the connection-string line into a private helper shared with `WriteDatabase` if that keeps things tidy.

- [ ] **Step 5: Run the tests and confirm they pass.** Then run the full suite.

- [ ] **Step 6: Commit:** `feat: detect mods with clashing names and rename the unused copies (descriptor + launcher database)`

---

### Task 2: Service and Session

**Files:**
- Modify: `FazStellarisModmanager.Core/ModManagerService.cs`
- Modify: `FazStellarisModmanager.Core/Session/SessionService.cs`
- Modify: `FazStellarisModmanager/Pages/SessionPage.razor`
- Modify: `FazStellarisModmanager.Tests/ModManagerServiceTests.cs`

- [ ] **Step 1: Failing tests in `ModManagerServiceTests`.** FakeInstall has `user/mod/local.mod`, name "Local Mod". Add `fake.Write("user/mod/copy.mod", "name=" + '"' + "Local Mod" + '"' + (char)10 + "path=" + '"' + "mod/local" + '"' + (char)10)`, then `svc.RefreshLibrary()`.
- `DupesIn(list)` with a list containing `mod/local.mod` returns one group with 2 members.
- `ResolveDupesWithDefaults(list)`:
  - keeps `mod/local.mod`, which is in the list;
  - renames `mod/copy.mod` to "Local Mod (duplicate: copy)";
  - after that, `svc.Library` shows the new name and `DupesIn(list)` is empty.
- `ResolveDupes` throws InvalidOperationException containing "Paradox launcher" while `IsLauncherRunning = () => true`, and `copy.mod` is unchanged.

- [ ] **Step 2: Implement in `ModManagerService`:**
```csharp
/// <summary>Dupe groups (clashing descriptor names) with at least one member in the list. Uses the last RefreshLibrary.</summary>
public IReadOnlyList<DupeGroup> DupesIn(ModList list)
{
    var rels = RelsOf(list);
    return DupeConflicts.Find(Library).Where(g => g.Mods.Any(m => rels.Contains(m.DescriptorRel))).ToList();
}

/// <summary>Renames the unkept members of each group (descriptor + launcher database), then rescans. Refuses while the launcher runs.</summary>
public DupeResolution ResolveDupes(IReadOnlyList<DupeChoice> choices)
{
    if (IsLauncherRunning()) throw new InvalidOperationException("Close the Paradox launcher first, or it will overwrite your mod list.");
    DupeResolution result;
    lock (_gate) result = DupeResolver.Resolve(Resolve().UserDir, choices, _paths.Backups, DateTimeOffset.UtcNow);
    RefreshLibrary();
    return result;
}

/// <summary><see cref="ResolveDupes"/> for every group touching the list, keeping each group's default.</summary>
public DupeResolution ResolveDupesWithDefaults(ModList list)
{
    var rels = RelsOf(list);
    var choices = DupesIn(list).Select(g => new DupeChoice(g, DupeConflicts.DefaultKeep(g, rels))).ToList();
    return choices.Count == 0 ? new DupeResolution([], null, null) : ResolveDupes(choices);
}

static HashSet<string> RelsOf(ModList list) =>
    new(list.Mods.Select(m => m.DescriptorRel.Replace((char)92, '/')), StringComparer.OrdinalIgnoreCase);
```
`RefreshLibrary` takes `_gate` itself, so call it outside the lock as shown. `Lock` isn't re-entrant-safe to rely on here.

- [ ] **Step 3: SessionService.**
- Add `volatile int _lastDupeRenames;` and `public int LastDupeRenames => _lastDupeRenames;`. Reset it to 0 everywhere `_lastLauncherWarning = null;` is set.
- In the Match host flow, after `ct.ThrowIfCancellationRequested()` and the host-connected check, and before `manager.Apply(plan.ToApply)`:
```csharp
var dupes = manager.ResolveDupesWithDefaults(plan.ToApply);
var result = manager.Apply(plan.ToApply);
_lastBackupPath = result.BackupPath;
_lastDupeRenames = dupes.Renamed.Count;
_lastLauncherWarning = string.Join(" ", new[] { dupes.LauncherWarning, result.LauncherWarning }.OfType<string>()) is { Length: > 0 } w ? w : null;
```
`ResolveDupesWithDefaults` refreshes the library. `plan.ToApply` entries keep their DescriptorRel, so the plan stays valid.

- [ ] **Step 4: SessionPage.razor.** Under the "Wrote the host's list" paragraph, before the launcher warning, add:
```razor
@if (Session.LastDupeRenames > 0)
{
    <p class="status">Renamed @Session.LastDupeRenames duplicate-named @(Session.LastDupeRenames == 1 ? "mod" : "mods") so Stellaris loads them.</p>
}
```

- [ ] **Step 5: Build the solution and run all tests.** Then commit: `feat: ModManagerService resolves dupe conflicts; Match host renames clashing copies with the defaults`

---

### Task 3: UI: DupeDialog, badges, Apply gating

**Files:**
- Create: `FazStellarisModmanager/Components/DupeDialog.razor`
- Modify: `FazStellarisModmanager/Pages/Mods.razor`
- Modify: `FazStellarisModmanager/Components/AddModsDialog.razor`
- Modify: `FazStellarisModmanager/wwwroot/css/site.css` (small styles)

- [ ] **Step 1: `DupeDialog.razor`.** Follow `AddModsDialog.razor`'s modal structure: backdrop click handling, `modal-head` with a ✕, and `modal-actions`.

Parameters:
```csharp
[Parameter, EditorRequired] public IReadOnlyList<DupeGroup> Groups { get; set; } = [];
[Parameter, EditorRequired] public IReadOnlySet<string> InListRels { get; set; } = default!;
[Parameter] public string Title { get; set; } = "Mods with the same name";
[Parameter] public string ConfirmText { get; set; } = "Keep selected";
[Parameter] public EventCallback<IReadOnlyList<DupeChoice>> OnConfirm { get; set; }
[Parameter] public EventCallback OnClose { get; set; }
```
- **State:** `Dictionary<DupeGroup, InstalledMod> keep`, filled in `OnParametersSet` with `DupeConflicts.DefaultKeep(g, InListRels)` for groups not yet in the dictionary. Use reference equality.
- **Intro text** (`<p class="muted">`): "Stellaris skips every mod whose name is shared with another .mod file. Pick the copy that keeps the name; the others are renamed to "&lt;name&gt; (duplicate: &lt;file&gt;)". Nothing is deleted."
- **Per group:** `<div class="dupe-group"><h3>@g.Name</h3>`, then one `<label class="dupe-row">` per member:
  - a radio input (`name="dupe-@index"`, `checked` when it is the kept one, `@onchange` sets `keep[g] = m`);
  - `<span class="dfile">@Path.GetFileName(m.DescriptorRel)</span>`;
  - the Workshop/Local badge, the same markup as AddModsDialog;
  - `<span class="muted">v@(m.Version ?? "?")</span>`;
  - when it is in the list, `<span class="badge">in your list</span>`;
  - `<div class="sub">@m.ContentPath</div>`.
- **Actions:** Cancel (`OnClose`) and `<button class="primary">@ConfirmText</button>`. The confirm button invokes `OnConfirm` with `Groups.Select(g => new DupeChoice(g, keep[g])).ToList()`.
- **Escape key** closes, like AddModsDialog.

- [ ] **Step 2: Mods.razor.**
- **Dupe lookup:**
  - field `Dictionary<string, DupeGroup> dupeByRel = new(StringComparer.OrdinalIgnoreCase);`
  - in `LibraryChanged()`, rebuild it from `DupeConflicts.Find(Manager.Library)`, mapping each member's DescriptorRel to its group.
- **Badge:** in the list row, after the overwritten badge:
```razor
@if (dupeByRel.GetValueOrDefault(e.DescriptorRel) is { } dupe)
{
    <button class="warnbadge" title="Another mod in your mod folder has the same name; Stellaris skips mods that share a name."
            @onclick="() => OpenDupes([dupe], null)">⚠ Dupe conflict</button>
}
```
- **Dialog state:**
  - `IReadOnlyList<DupeGroup>? dupeGroups;`
  - `Func<string?>? afterDupes;` (null means badge mode);
  - `string? dupeNote;`
- **`OpenDupes(groups, after)`** sets `dupeGroups = groups; afterDupes = after;`.
- **Rendering** (next to the AddModsDialog render):
```razor
@if (dupeGroups is not null)
{
    <DupeDialog Groups="dupeGroups" InListRels="ListRels()" ConfirmText="@(afterDupes is null ? "Keep selected" : "Fix and apply")"
                OnConfirm="ConfirmDupes" OnClose="() => { dupeGroups = null; afterDupes = null; }" />
}
```
- **`ListRels()`** returns `new HashSet<string>(current.Select(e => e.DescriptorRel), StringComparer.OrdinalIgnoreCase)`.
- **`ConfirmDupes(IReadOnlyList<DupeChoice> choices)`:**
  - Use `Run(...)` so exceptions such as the launcher-open refusal become status errors.
  - Call `var r = Manager.ResolveDupes(choices);`, then `LibraryChanged();` (rebuilds the badges, since names changed).
  - Build the note: `$"Renamed {r.Renamed.Count} duplicate-named {(r.Renamed.Count == 1 ? "mod" : "mods")} so Stellaris loads them."`, plus `" " + r.LauncherWarning` when there is one.
  - Close the dialog.
  - If `afterDupes` was set, invoke it and prefix its returned message (or the current `status` when it returns null) with the note.
  - Make sure an error state is kept (`error = true`) if either part produced a warning.
- **Apply:** split `Apply()` / `ApplyAndLaunch()` into the gate and the existing body:
  - Rename the existing lambdas' bodies to `string? DoApply()` and `string? DoApplyAndLaunch()`. They keep the confirmEmpty logic and messages.
  - `void Apply()`: if `Manager.DupesIn(Build())` is non-empty, `OpenDupes(groups, DoApply)`; otherwise `Run(DoApply)`. Do the same for `ApplyAndLaunch` with `DoApplyAndLaunch`.

  Keep the existing behaviour of `Run` (status/error handling) for both paths.

- [ ] **Step 3: AddModsDialog.razor.** Compute `var dupes = DupeConflicts.Find(Library).SelectMany(g => g.Mods).Select(m => m.DescriptorRel).ToHashSet(StringComparer.OrdinalIgnoreCase);` once per parameter set (in `OnParametersSet`, stored in a field). In each pick row, after the source badge, add:
```razor
@if (dupeRels.Contains(m.DescriptorRel))
{
    <span class="badge dupe" title="Another mod in your mod folder has the same name; Stellaris skips mods that share a name.">⚠ Dupe conflict</span>
}
```

- [ ] **Step 4: CSS** (site.css, near `.warnbadge`):
```css
.badge.dupe { color: #f2c94c; background: #3a2e12; border: 1px solid #6b5520; }
.dupe-group { border: 1px solid var(--border); border-radius: 8px; padding: .5rem .7rem; }
.dupe-group h3 { margin: 0 0 .35rem; font-size: .95rem; }
.dupe-row { display: grid; grid-template-columns: auto auto auto auto 1fr; gap: .45rem; align-items: center; padding: .2rem 0; cursor: pointer; }
.dupe-row .sub { grid-column: 2 / -1; font-size: .72rem; color: var(--muted, #8a96a6); word-break: break-all; }
.dupe-list { overflow: auto; display: flex; flex-direction: column; gap: .5rem; }
```
Wrap the groups in `<div class="dupe-list">`. Check that a `--muted` variable exists in site.css; if not, use the literal color.

- [ ] **Step 5: Build the solution and run all tests.** UI is checked by hand. Commit: `feat: Dupe conflict badges and picker on the Mods tab; Apply asks to fix clashing names first`

---

### Task 4: Real-data check (controller)
- Run `DupeConflicts.Find(ModLibrary.Scan(<real user dir>))`. This is read-only.
- Expect 4 groups (UIOD, DarkSpace, Plentiful Traditions 4.4.x, Preftl Observation Event Sound Fix), with these defaults for the current dlc_load list:
  - `ugc_1623423360`
  - `ugc_2719075597`
  - `ugc_1311725711`
  - `preftlobservefixer.mod`

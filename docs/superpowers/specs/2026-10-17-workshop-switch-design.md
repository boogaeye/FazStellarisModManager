# Switch Local Copies to the Workshop Version: Design (Sub-project 23)

## Problem
A client may use a **local copy** of a Workshop mod: a `mod/*.mod` that isn't `ugc_<id>.mod` but has `remote_file_id = <id>`, such as the `8cde_…` copies. When the host uses the Workshop item itself (`ugc:<id>`) and the files differ, Match host currently puts the mod under "Local mods whose files differ from the host's" (`MatchPlan.DiffersLocally`) and does nothing more.

## Behaviour
- **Classification** (`MatchPlan.Create`). Each `ContentMismatch` mod diff is classified like this, in this order:
  1. **Already handled:** the pairing was replaced by my exact copy of the host's key (the existing "replaced" rule). Nothing changes; its differences no longer apply.
  2. **Mine is the Workshop item** (`ModKeys.WorkshopId(MineKey)` is not null) → `NeedsWorkshopUpdate`, as today.
  3. **New: switch.** Mine is local, **and** the host's key is the Workshop item (`ModKeys.WorkshopId(u.Key)` is not null, i.e. `ugc:<id>`) → **`NeedsWorkshopSwitch`**. The id is the host key's Workshop id.
  4. **Otherwise** → `DiffersLocally`, as today. If both sides are local but share a Workshop id (`ModMatcher.WorkshopIdOf` of the host key and remote id, and of my key and remote id), the UI notes "the host uses a local copy too — ask the host to share it, or both switch to the Workshop version".
- **`IsComplete`** also requires `NeedsWorkshopSwitch.Count == 0`.
- **`MatchPlan.SwitchedToWorkshop`.** Host mods for which the plan uses my installed exact copy of the host's Workshop key instead of the copy the diff paired. These are the existing `replaced` set restricted to host keys that are `ugc:`, carrying the original UnitDiff, which has my old copy's name in MineName. After a switch download, these are the switched mods.
- **`WorkshopNeeds.From(plan)`.** Adds `WorkshopNeedKind.Switch` items for `NeedsWorkshopSwitch`: Name = the host's name, Id = the host key's Workshop id. Items are deduplicated by id, keeping the first kind in this order: Install, Update, Switch.
- **`WorkshopPrompt`:**
  - Switch items show a "Switch" badge, styled like Update, with the muted note "your local copy differs from the host's Workshop version". They are ticked by default, like the others.
  - Running them uses the existing `InstallFromWorkshopAndMatchAsync`. `InstallAsync` subscribes when needed and always asks Steam to download, which also updates an installed but outdated `ugc_<id>`.
- **After the downloads:**
  - `MatchCoreAsync` → `RefreshLibraryAsync`. This creates `mod/ugc_<id>.mod` for new downloads (`EnsureWorkshopDescriptors`).
  - `MatchPlan.Create` then picks the exact `ugc:<id>` through the replaced rule. Because the diff still pairs the host mod with my local copy, the replaced rule moves it onto `ugc:<id>`.
  - Then `ResolveDupesWithDefaults(plan.ToApply)` renames the local copy, which shares the name and is not in the list, and Apply runs.
  - The rescan after matching shows whether the Workshop version now matches.
- **Session result card (client):**
  - **When `plan.SwitchedToWorkshop` isn't empty:** "Now using the Workshop version instead of your local copy:" followed by the names (MineName → host name when they differ, using the existing UnitName helper).
  - **When `plan.NeedsWorkshopSwitch` isn't empty:** this happens when Match ran without installing, or a download failed. "Your local copy differs from the host's Workshop version; switch to the Workshop version from the Workshop mods pop-up:" followed by the names.
  - **`DiffersLocally` entries:** they get the "host uses a local copy too" note when the rule above applies.

## Testing
- **`MatchPlanTests`:**
  - host `ugc:X` with my local copy (remote X) differing → `NeedsWorkshopSwitch` with id X, not `DiffersLocally`, and `IsComplete` false;
  - host local copy with my local copy (same remote id) differing → `DiffersLocally`;
  - host local with my `ugc:X` → `NeedsWorkshopUpdate`, unchanged;
  - after `ugc_X` is installed in the library, `Create` with the same diff → ToApply uses `mod/ugc_X.mod`, `SwitchedToWorkshop` contains X, and `NeedsWorkshopSwitch` is empty.
- **`WorkshopNeedsTests`:** a Switch item is created; dedupe keeps Install over Update over Switch.
- **`SessionServiceTests`** (the existing install-and-match rig with the fake Workshop, which "downloads" by creating the workshop folder):
  - client with a differing local copy, host `ugc:X`;
  - `InstallFromWorkshopAndMatchAsync([X])` → dlc_load.json lists `mod/ugc_X.mod`;
  - the local copy's descriptor is renamed "(duplicate: …)";
  - `LastPlan.SwitchedToWorkshop` contains X.
- **Manual:** with Flam, Match host offers Switch for their `8cde_` copies; after switching, their list uses `ugc_`, and the comparison is clean except for mods the host has out of date.

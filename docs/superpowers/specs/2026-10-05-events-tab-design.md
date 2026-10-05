# Events Tab — Design (Sub-project 19)

## Goal
A new **Events** tab (nav item after Tech) for browsing every event in the current mod list. For each event it shows:
- **How it's triggered:** its direct callers, which can be expanded one level at a time up to the roots.
- **The event card:** picture, title, description, tags and options. Selecting an option shows what that option fires.
- **Conditions,** with the live ✓ / ✗ / ? marks.

The tech tree's existing "obtained from" event browser stays as it is.

Mockup (approved): `.superpowers/brainstorm/772-1791234558/content/events-tab.html`, based on the real Toxoids chain toxoids.7270 ← 7265 ← 7260 ← 7255.

## How events get fired (what the graph records)
- **On actions** (`common/on_actions`): `events = { id … }` and `random_events = { weight = id … }`.
  - Several files can add to the same on-action key; all their entries are merged. Record the weight for random events.
  - The on-action is a **root**.
- **Event-firing effects** (`country_event`, `planet_event`, `fleet_event`, `ship_event`, `pop_event`, `pop_faction_event`, `leader_event`, `system_event`, `observer_event`, `starbase_event`, `species_event`, `first_contact_event`, `situation_event`, `agreement_event`, `astral_rift_event`, `espionage_operation_event`, `archaeological_site_event`, plain `event`, and any key ending in `_event` with `{ id = … }`). Capture `days`, `random` (giving a delay range of days to days + random) and `scopes` when present. They can appear in:
  - **events:** `immediate`, `option` blocks (name and index) and `after`;
  - **common/ objects** whose effect blocks fire events: decisions, special projects, anomalies, archaeological sites, situations, relics, buildings, megastructures, edicts, policies, traditions, perks, technologies, resolutions, agendas and so on. Such an object is a **root**, labelled with its kind and localised name.
- **Scripted effects:** an effect that names a scripted effect (from the existing script library) is expanded recursively, at most 5 deep. The call keeps the original caller and gains "via scripted effect X".
- **Inline scripts and event inheritance (`base = id`):** handled the same way the tech grant scanner handles them. Reuse or refactor its code; don't duplicate it.
- **Context on each call:**
  - the chain of enclosing `if`/`else_if` limits and `limit` blocks, kept as condition text and as a `PdxBlock` so it can be marked;
  - `random_list` branches: the branch's base weight and its `modifier = { factor = X <triggers> }` list;
  - `random = N` inside effect blocks: the chance.
- **Events nobody calls:** if not `is_triggered_only`, the event "fires on its own" (pulse or mean time to happen); show its `trigger` and `mean_time_to_happen`. Otherwise it is "No known caller in the loaded files" (it may be fired by code or by a mod that isn't loaded).

## UI (as in the mockup)
- **Left: the event list.**
  - Search box (title, id, description text).
  - Namespace filter and source filter (base game or a mod).
  - Each row shows the title, or "(no title)" plus its first option text, with the id underneath.
  - At most about 300 rows.
- **Middle: the event card.**
  - The picture (reuse `TechTreeService.EventPictureAsync`), the title and the id.
  - Tags: event type, "hidden", "triggered only" or "fires on its own", and source plus file.
  - The description.
  - The options as selectable cards; the first is selected by default.
  - The selected option's **"fires"** panel lists every event that option fires, nested where the fired event's own options fire more. Show the first level fully; deeper levels come one click at a time. Each entry has a delay ("immediately", "after 90 days", "after 90–120 days"), its condition and chance where known, and a link to that event.
  - The panel also lists the event's own `immediate` and `after` calls under "When the event fires" and "After an option".
- **Right: "How it's triggered".**
  - The direct callers as rows: a kind badge (event / on action / decision / special project / … / root), a link, and a "via" line (option name, immediate or after, delay, condition, weighted chance, scripted effect).
  - An expander (▸ / ▾) on event callers loads *their* callers. Lazy and cycle-safe: a visited id shows "↺ already above".
  - Roots get a green "root" badge.
- **Conditions:**
  - The event's `trigger` (and the selected option's `trigger`/`allow`) are evaluated with the existing `ConditionEvaluator` against the viewer's `EmpireFacts` from Live Game, using the same `ConditionView` with animated marks.
  - Without live data, show the script text.
  - For weighted picks (`random_list` and `random_events`):
    - list each branch's weight and modifiers, with the modifier conditions marked;
    - when every modifier of every branch is decided, show the chance per branch for this empire (weight × factors / total).
- **Navigation:** clicking any event link selects it. A Back button walks the selection history.

## Data and services
- **`Core/Events/EventGraph`** (immutable):
  - `Events`: id → `EventInfo` (Id, Type, Title, Description, Picture, Hidden, TriggeredOnly, HasMtth, Options with Name / condition script / calls, ImmediateCalls, AfterCalls, Trigger script, Source).
  - `CallersOf(id)`: a list of `EventCall`.
  - `Search(text, namespace, source)`.
- **`EventCall`:**
  - TargetId;
  - CallerKind (Event / OnAction / Object) and CallerId;
  - CallerName;
  - Part (Immediate / Option / After / Effect / OnActionEvents / OnActionRandom), plus OptionIndex and OptionName;
  - DaysMin and DaysMax;
  - ConditionScript;
  - Weight info (base, modifiers script, siblings, for random_list and random_events);
  - Via (scripted effect name).
- **`EventGraphScanner.Build(sources, localisation, library, ct)`:**
  - Reads `events/**`, `common/on_actions/**` and the other `common/**` folders.
  - Base-game common files can be pre-filtered to those that mention `_event`, `event =` or a scripted effect that (transitively) does, like the grant scanner's prefilter.
  - Reports progress and warnings.
- **`EventGraphService`** (singleton):
  - Builds the graph from `TechTreeService.Current` (same sources and localisation), lazily when the Events tab opens. If no tree is loaded, it asks `TechTreeService` to build the current game first.
  - Caches the graph per tree, and rebuilds when the tree changes.
  - Exposes a progress string.

## Testing
- **Scanner:** events fired from immediate, option and after blocks, with delays; on_actions `events` and `random_events` merged across files; common objects as callers; a scripted effect firing an event ("via"); conditions from `if`/`limit`; random_list weights and modifiers; `base =` inheritance; inline scripts; a cycle; uncalled events (triggered-only versus not).
- **Graph queries:** CallersOf, Search, and the option fires lists.
- **Chances:** weight × factor maths with decided and undecided modifiers.
- **Real data:** build from the game plus the enabled mods. Check the Toxoids chain (toxoids.7270 ← 7265 option "We shalt see … anon." ← 7260 option "Anything to know beforehand?"), and that `crisis_trigger.1` ← on_five_year_pulse fires crisis.10 inside a random_list. Report the build time and the event count.
- **UI:** checked by hand.

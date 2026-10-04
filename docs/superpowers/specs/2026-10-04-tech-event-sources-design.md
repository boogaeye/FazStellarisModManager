# Tech Event Sources — Design (Sub-project 8)

## Goal
Each technology lists everything that grants it. Three kinds of grant count:
- **Gives:** it is researched outright.
- **Progress:** it gains research progress, e.g. +25 %.
- **Research option:** it is offered as a research choice.

Grants come from events, and from other game objects (traditions, ascension perks, council agendas, archaeology sites, …). An event opens in an in-game-style window: id, title, picture, description and options. The window shows which option grants the tech, or whether the grant happens when the event fires or after any option.

Mockups (approved): `.superpowers/brainstorm/335-1791100790/content/event-sources.html` (layout A) and `event-sources-multi.html` (option 2, the pop-up event browser).

## Facts (base game, local install)
- **Effects:**
  - `give_technology = { tech = X }` occurs 258× in `events/` and 281× in `common/`.
  - `add_tech_progress = { tech = X progress = 0.25 }` occurs 393× / 107×.
  - `add_research_option = X` occurs 281× / 216×. A block form `{ tech = X }` is also accepted.
- **Where grants sit:** inside an `option = { … }`, `immediate = { … }` or `after = { … }`, often nested in `if`/`else_if`/`else` (or uppercase `IF`/`ELSE`), `random_list`, scope blocks (`owner = { … }`) or `hidden_effect`.
- **Indirect grants:** events call scripted effects (`common/scripted_effects`, `name = yes` or `name = { PARAM = value }`, with `$PARAM$` / `$PARAM|default$` in the body). They also call inline scripts (`inline_script = path` or `inline_script = { script = path PARAM = value }`, file `common/inline_scripts/<path>.txt`). Inline scripts can inject whole `option` blocks at event level, e.g. astral-rift tech options built from `$TECH$`.
- **Event blocks:** a top-level key ending in `_event` (country, fleet, ship, situation, astral_rift, first_contact, …) or `event`. Each has:
  - `id`;
  - `title` (a key, or a block holding `text`);
  - `desc` (a key, or blocks with `trigger`/`text`, possibly several);
  - `picture` (a GFX sprite, or a block `{ trigger picture }`);
  - `hide_window = yes` for windowless events.
- **Event pictures:** `GFX_evt_*` sprites, mostly 450×150 DDS.
- **Duplicate event ids:** the FIRST definition loaded wins, so mods override events with early-sorting file names. Scripted effects: the last definition wins. Both also follow per-path file overrides between sources.
- **Non-event grants:** in `common/` folders such as traditions, ascension_perks, council_agendas, country_focus, patrons, federation_perks, specialist_subject_perks, astral_actions, solar_system_initializers and tradable_actions.

## Decisions

### Script library (`ScriptLibrary`)
- **Scripted effects:** `common/scripted_effects` across sources, with per-path file override, load order, and the last definition per name winning.
- **Inline scripts:** `common/inline_scripts/**/*.txt` by relative path without `.txt` (case-insensitive, `/`), with per-path override, kept as raw text.
- **Parameters:** substitution replaces `$NAME$` and `$NAME|default$` before parsing (inline scripts) or in a copy of the parsed tree (scripted effects). Unknown parameters without a default stay as written.

### Finding grants (`GrantFinder`)
`Find(effectBlock)` walks an effect block and returns `(tech, kind, progress, condition, via)` tuples.

- **Effects found:**
  - `give_technology`: block `tech`, or a string.
  - `add_tech_progress`: block `tech` + `progress`. The progress is parsed as an invariant double; it is null when it is not a number.
  - `add_research_option`: a string, or block `tech`.
- **Conditions:** `if` adds the condition from its `limit` (worded by `TriggerSummary`, scripted triggers expanded); `else_if` adds the same prefixed with "otherwise, " (e.g. "otherwise, Gestalt"). `else` adds "otherwise". A child of `random_list` or `locked_random_list` adds "by chance". In `switch = { trigger = T  case = { … }  default = { … } }` each case adds "T = case" ("not T = case" for `inverted_switch`) and `default` adds "otherwise"; the `trigger` entry itself is not walked. Conditions accumulate, joined with "; ".
- **Skipped (trigger) blocks:** `limit`, `trigger`, `exclusive_trigger`, `allow`, `potential`, `ai_chance`, `weight`, `modifier`, and `tooltip` (it only displays its contents; vanilla repeats real grants inside tooltips). The `effect` block of `create_country` is skipped too, since it runs for the newly created country. Every other block is walked as an effect scope.
- **Scripted effects:** a key naming a scripted effect, with value `yes` or a parameter block, is walked with its parameters substituted. `via` records the name of the effect the original block called, i.e. the outermost one.
- **Inline scripts:** expanded and walked transparently.
- **Limits:** recursion stops at depth 6, and a call stack guards against cycles.
- **Dynamic techs:** only tech keys present in the tech database count, so unresolved `$TECH$` and `event_target:` values drop out.

### Event scan (`EventScanner`)
- **Files:** `events/**/*.txt` from all sources, with per-path override, processed in load order. Every file is parsed (no pre-filter: the base game's 13 MB of events parse in about 0.4 s), so for each id the first definition wins exactly.
- **Inheritance:** `base = <event id>` is resolved after all definitions are collected, so the base may be in another file. A derived event takes title, desc and picture from its base unless it sets its own (desc is not inherited with `desc_clear = yes`); the base's options come first, then its own, unless `option_clear = yes` (option indices count over the merged list); `immediate` and `after` are inherited when it has none. Chains are followed up to 5 deep; cycles are ignored.
- **Expansion:** event-level `inline_script` entries are expanded in place, so injected options are seen.
- **Grants:** `immediate` gives part Immediate, each `option` (0-based index) gives part Option, and `after` gives part After.
- **Event model**, kept only for events that grant a tech in the database:
  - id and type (the block key);
  - Title: the localised `title`, or the first `text` of a title block; the event id when it is missing;
  - Description: the first desc entry. A key is localised; a block uses its first `text`, searched depth-first through `first_valid`, `random_valid`, nested `desc` and `trigger = { text = …  if = { limit = { … } text = … }  success_text = { text = … } }` (limit blocks skipped). Titles and option names use the same lookup. DescriptionVaries is true when there are several desc entries or any block form.
  - Picture: a sprite name from the string, or from the first block's `picture`. PictureVaries uses the same rule as the description.
  - Hidden: `hide_window = yes`.
  - Options: a localised name (a key, or a name block's first `text`; `option N` when missing) and a Condition from `trigger` or `exclusive_trigger`.
  - Source: the mod and file.
- **Localisation:** handled by `Localisation.Get`. Commands such as `[From.Planet.GetName]` stay in the text, and the UI highlights them.

### Other sources (`ObjectGrantScanner`)
- **Folders:** every `common/` folder except technology, scripted_effects, inline_scripts, scripted_triggers, scripted_variables, script_values, defines, pop_jobs, random_names, name_lists, on_actions and solar_system_initializers. The initializers are excluded on purpose: their grants go to scripted NPC empires at galaxy generation and would add hundreds of rows of noise.
- **Rules:** the same per-path override, load order, and last (folder, id) wins.
- **Pre-filter:** a base-game file is parsed only if it contains (case-insensitive substring, via `SearchValues`) one of the three effect names, `inline_script`, or the name of a scripted effect that (transitively) mentions one. Skipped base files still have their top-level object ids read (a light scan honouring comments, strings and an inner `key = …`), so they replace earlier definitions as objects without grants. Mod files are always parsed.
- **Duplicates:** identical grants of one source (same part, option, kind, progress, condition and via) are kept once.
- **Grants:** `GrantFinder` walks each top-level object block.
- **Naming:** the source shows as its kind (`UnlockScanner.KindName(folder)`) and its localised name (`UnlockScanner.UnlockName`: the id, `id_name`, then the folder's prefixes with and without `_name`, e.g. `council_agenda_<id>_name`; the id when none is localised).

### Model and API
- `TechGrant(Kind, Progress, Part, OptionIndex, Condition, Via)`.
- `GrantSource(Kind, KindFolder, Id, Name, Source, Grants)`. Events use `KindFolder = "events"` and `Kind = "Event"`.
- `GameEvent(Id, Type, Title, Description, DescriptionVaries, Picture, PictureVaries, Hidden, Options, Source)` with `EventOption(Name, Condition)`.
- `TechDatabase.GrantSources(techKey)` returns events first, then other sources, each sorted by name. `TechDatabase.Event(id)` returns the event.
- **`GrantText` (Core, tested):**
  - **Badge:** "Gives", "+25 %", "Research option", "Option + 25 %", or "+?" for progress that is not a number.
  - **Where:** "option “Name”", "when the event fires", "after any option", or "N places".
  - **Effect line:** used by the event window, e.g. "★ Gives Psionic Theory", followed by "via X" or the condition when present.

### Pictures
- `IconCache.DataUri` gains a max-size parameter; it stays 64 for icons, and the size is part of the cache key.
- `TechTreeService.EventPictureAsync(GameEvent)` resolves the sprite through `SpriteIndex` and decodes on the thread pool, up to 512 px. Results are remembered per picture, and it returns null when the picture can't be found or decoded.

### UI
- **Sidebar:** `TechSidebar` gets an **Obtained from (N)** section, open, under Unlocks. Each source shows on one line as badge, name, id and where. Event names are links; other sources show "Kind: **Name**".
- **Event browser:** `EventBrowser.razor` is a modal dialog.
  - **Left:** the source list, with the current one highlighted. Non-event entries are shown but cannot be opened.
  - **Right:** the in-game-style window:
    - the title, plus "id · type", and "hidden event (no window in game)" when Hidden;
    - the picture (450×150 or smaller, with a placeholder while loading or when missing);
    - the description, with newlines kept, `[commands]` highlighted, and "(text varies)" when it does;
    - the options, each with an "Only if: …" line when conditional; an option that grants this tech is outlined in gold and labelled with its effect line(s);
    - strips at the bottom for Immediate ("When the event fires: …") and After ("After any option: …") grants.
  - **Navigation:** ◀ ▶ step through the event sources (focus returns to the dialog, so Esc and the arrows keep working); Esc, ✕ or a click that starts and ends on the backdrop closes it. Event rows in the list are buttons. An event source without an event record shows "Event details not found."; an event without a picture (diplomatic events) shows no picture area.

## Errors and performance
- Unreadable or malformed files become warnings, as before.
- The scans read files in parallel and merge in load order. In `TechDatabase.Build`, reading and parsing the grant files (`ScriptLibrary.Load` + `GrantScanner.Read`) starts first and overlaps the tech files and localisation; finding the grants (`GrantScanner.Scan`, events analysed in parallel) then runs alongside the unlock and sprite scans. Each task has its own warning list; they are merged as earlier warnings, unlocks, sprites, grants, and duplicate warnings are dropped (keeping order).
- Pictures load on demand, and only for opened events: one decode per sprite per tree; a missing or failed picture is not remembered, so it is retried.
- Only events that grant a tech are kept in memory.

## Testing
All tests use fake installs.
- **Finder:**
  - all three effects and their forms;
  - option, immediate and after;
  - if/else conditions, random_list and scope blocks;
  - skipped trigger blocks;
  - a scripted effect with `$TECH$` and a default;
  - nested effects (`via` = the outermost name);
  - a cycle;
  - inline scripts with parameters;
  - unknown techs dropped.
- **Events:**
  - title, description and picture forms, with the varies flags;
  - hidden events;
  - option names and conditions;
  - injected options from event-level inline scripts;
  - first definition wins across a mod;
  - the base-file pre-filter.
- **Objects:** a tradition grant from a mod, with kind and name.
- **GrantText:** badges and where texts.
- **IconCache:** the max size.
- **Database:** the sort order of `GrantSources`.
- **UI:** verified manually.

## Out of scope
- Event chains (what fires an event).
- Option effects other than tech grants.
- Evaluating conditions for a specific empire.
- Pictures that vary by condition (the first one is shown).

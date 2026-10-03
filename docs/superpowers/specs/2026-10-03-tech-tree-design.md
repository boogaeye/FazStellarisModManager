# Tech Tree Tab — Design (Sub-project 4)

## Goal
A **Tech** tab that shows the Stellaris technology tree produced by a mod list. The user can:
- search and filter technologies;
- see where each one comes from: the base game, or which mod added or overrode it;
- see its real icon;
- see its prerequisites and what it leads to, drawn as a tree.

The layout is option A ("search + focus view") from the brainstorming mockups, refined with tabs, filters, sources, icons and drawn lines. The mockup is at `.superpowers/brainstorm/401-1791060083/content/tech-layout-v3.html`.

## Facts about the game data (verified on the local install)
- Technologies are defined in `common/technology/*.txt`. There are 35 files and 678 techs in the base game: physics 167, society 312, engineering 199. Category and tier metadata live in `common/technology/category/` and `common/technology/tier/`.
- A tech is a top-level block `tech_x = { ... }`. The fields used here:
  - `area`, `tier`, `category = { name }`, `cost` (a number or `@variable`);
  - `prerequisites = { "tech_a" "tech_b" }`;
  - `start_tech = yes`, `is_rare = yes`, `is_dangerous = yes`;
  - `levels` (repeatable; `levels = -1` means unlimited);
  - `icon = "other_tech"`;
  - `potential` / `weight_modifier` containing `host_has_dlc = "Apocalypse"`.
- `@variables` are defined at the top of a technology file or in `common/scripted_variables/*.txt`.
- Names come from localisation keys `<tech>` and `<tech>_desc` in `localisation/english/*_l_english.yml`, in the form `key:0 "text"`. Text can contain `§Y…§!` colour codes, `£icon£` tags and `$key$` references.
- Icons are `gfx/interface/icons/technologies/<tech or icon>.dds`. Most are uncompressed RGBA; a few are DXT3 or DXT5.

## Decisions
- **Mod list:** by default, the mods currently enabled in `dlc_load.json`. A picker also allows any saved list, to preview a tree before applying it.
- **Override semantics** (matching how Stellaris loads `common/`):
  - The sources are the base game, then each mod in load order. A mod's content is a folder, or a zip for old-style mods.
  - **File level:** a file with the same relative path (case-insensitive) from a later source replaces the earlier one entirely.
  - **Object level:** the surviving files are processed in ordinal filename order. For duplicate tech keys the **last** definition wins.
  - Every tech records its winning source (the base game or a mod's name, plus the relative file) and the list of earlier sources it replaced.
- **Localisation:** English only. All `*_l_english.yml` files under `localisation/` (any depth) of the base game are read, then those of each mod in order. Later sources override, and files inside any `replace` folder (`localisation/replace/…` or `localisation/english/replace/…`) override everything else. `§X…§!` codes and `£…£` tags are stripped, and `$key$` is resolved one level deep.
- **Cost:** a number, or an `@var` resolved first from the file's own variables and then from the global scripted variables. If it can't be resolved, the variable name is shown.
- **DLC tag:** the set of `host_has_dlc = "X"` values found anywhere in the tech block.
- **Icons:**
  - The icon is the `icon =` value if present, otherwise the tech key. The file is looked up across the sources, with the last one winning, under `gfx/interface/icons/technologies/`.
  - Decoding is done in .NET with the Pfim library (uncompressed, DXT1/3/5), and the result is encoded to PNG by a small built-in encoder that uses zlib from the BCL.
  - PNGs are cached in `%AppData%\FazStellarisModmanager\icons\` under a hash of the source path, size and mtime.
  - A failure produces a placeholder.
  - The UI loads icons on demand as `data:` URIs, only for rows and nodes actually shown.
- **Graph (focus view):**
  - Ancestors go in columns −1…−D and descendants in +1…+D, where D is the user's depth (1–3, default 2). A node sits in the column nearest the centre.
  - Each column is ordered by barycenter, the average row of its neighbours in the adjacent column nearer the centre, to reduce crossings.
  - Columns show at most 25 nodes plus "+N more".
  - It is rendered as SVG in Blazor, with cubic Bézier edges. Edges touching the selected tech are highlighted.
- **Path from start:** the shortest prerequisite chain (BFS over prerequisites) from any `start_tech`, or from any tech with no prerequisites, to the selected tech.
- **Performance:**
  - The tree is built off the UI thread with progress reporting.
  - The result is cached in memory per (list identity + content fingerprint), and rebuilt on demand or when the chosen list changes.
- **Errors:** an unreadable or malformed file or mod is skipped and recorded as a warning, shown in a collapsible section. The tree still builds.

## Components
Core (`FazStellarisModmanager.Core/Tech/`):
- **`ContentSource`:** a base-game or mod root, either a directory or a zip. It enumerates relative files under a folder and opens them.
- **`TechSources`:** builds the ordered source list from a `ModList` (or `dlc_load.json`) plus the game dir and the library.
- **`ScriptedVariables`:** collects `@var = value` from files.
- **`Localisation`:** loads and merges the English YAML-like files, then cleans the text.
- **`TechParser`:** turns one file's text into `TechDefinition`s (raw fields).
- **`TechDatabase`:** applies the override semantics. It produces `Tech` records (key, name, description, area, tier, category, cost text, prerequisites, flags, DLCs, icon key, source, overridden sources) and the reverse "leads to" index. It also offers search and filter queries and path-from-start.
- **`TechGraphLayout`:** a pure function from the focus tech and depth to positioned nodes and edges.
- **`DdsIcon`/`IconCache`:** DDS to PNG bytes, with the disk cache.
- **`TechTreeService`:** the UI facade. It builds the database for a list (in the background, cached), exposes the warnings, and gets icon data URIs.

App:
- **`Pages/TechPage.razor`** (route `/tech`): the list picker, area tabs with counts, filters, the result list, the SVG graph, and the detail card.
- **`Components/TechGraph.razor`** (SVG).
- A nav link and a DI registration.

## Testing
xUnit with small fake installs (folders and a zip) covering:
- parsing of every field;
- file-level and object-level overrides and the recorded sources;
- `@var` resolution;
- localisation merge, `replace` and text cleaning;
- DDS decoding of a tiny generated uncompressed DDS and a DXT1 block;
- the PNG encoder output (it decodes back);
- graph layout depth, columns, the "+N more" cap and barycenter ordering;
- path from start;
- the filters.

The UI is verified manually against the real install.

## Out of scope
- What a tech unlocks (buildings, components and so on).
- Research weights and chances.
- Non-English localisation.
- Editing techs.

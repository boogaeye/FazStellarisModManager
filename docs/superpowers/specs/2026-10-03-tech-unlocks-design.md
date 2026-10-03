# Tech Unlocks & Details — Design (Sub-project 5)

## Goal
Extend the Tech tab so each technology shows everything the game knows about it. A **right-hand sidebar** with collapsible sections (layout B from the mockups, placed as a sidebar per the user) shows:
- **Unlocks:** buildings, ship components and so on, with icons.
- **Effects:** stat bonuses with their icons, custom unlock text, feature flags, and the tech swap.
- **Weights:** research and AI.
- **Conditions.**
- **Raw definition.**

The mockup is at `.superpowers/brainstorm/2876-1791068121/content/tech-details.html`, option B.

Follow-on sub-projects, each with its own spec: (6) whole-tree view, (7) GitHub auto-update.

## Facts about the game data (verified on the local install)
- **Prerequisite links.** Objects in many `common/` folders point back at techs with `prerequisites = { "tech_x" }`. Counts:

  | Folder | Count |
  |---|---|
  | `component_templates` | 1,178 |
  | `buildings` | 242 |
  | `ship_sizes` | 105 |
  | `megastructures` | 65 |
  | `edicts` | 46 |
  | `section_templates` | 27 |
  | `traits` | 18 |
  | `districts` | 18 |
  | `armies` | 17 |
  | `deposits` | 12 |

  Also `strategic_resources`, `bypass`, `decisions`, `policies` and `zones`.
- **show_in_tech.** Starbase buildings and modules use `show_in_tech = "tech_x"`.
- **Component ids.** Ship components sit inside `weapon_component_template = { key = "SMALL_BLUE_LASER" … }`. The real id is `key`, not the block name.
- **Tech fields:**
  - `modifier = { army_damage_mult = 0.05 }`: stat bonuses.
  - `prereqfor_desc = { ship = { title = KEY desc = KEY } custom = { title = KEY } }`: custom unlock text.
  - `feature_flags = { a b }`.
  - `gateway = x`.
  - `technology_swap = { name = other trigger = { … } inherit_effects = yes }`.
  - `weight = @var`, `weight_modifier = { … }`, `ai_weight = { … }`, `potential = { … }`.
- **Modifier text and icons.**
  - Names are localisation keys `MOD_<KEY>` (upper case) or `mod_<key>`, e.g. `MOD_ARMY_DAMAGE_MULT` = "Army Damage".
  - Icons are `gfx/interface/icons/modifiers/mod_<key>.dds`. There are 1,415 files, including `_negative` variants.
- **Object icons.** An `icon` field takes one of three forms:
  - a GFX sprite name (`"GFX_ship_part_laser_1"`, often with `icon_frame = N`);
  - a direct path (`"gfx/interface/icons/traits/x.dds"`);
  - a bare name (`building_capital`), which is a file in the kind's icon folder.
- **Sprite files.** `interface/*.gfx` (116 files) contains `spriteType = { name = "GFX_x" texturefile = "gfx/…dds" noOfFrames = N }`. The key's case varies (`textureFile`).

## Decisions

### Unlock discovery (`UnlockScanner`)
- **Where it looks:** every folder directly under `common/` except `technology`, `scripted_variables` and `inline_scripts`, recursively, `*.txt`.
- **Pre-filter:** a file is parsed only if its text contains `prerequisites` or `show_in_tech`.
- **Overrides:** the same rules as techs.
  - File level: the same relative path in a later source replaces the earlier file.
  - Object level: per folder, last wins, processed in the same lower-cased ASCII path order.
  - The object's identity is (folder, id).
- **What makes an unlock:** a top-level block whose `prerequisites` items contain the tech key, or whose `show_in_tech` equals it. For blocks that have a direct child `key = "…"`, the id is that value, otherwise the block name.
  - Container files whose top-level blocks wrap several templates are handled by scanning top-level blocks only. Each component template is its own top-level block in Stellaris.
- **Kind names:** `component_templates` → "Ship components", `buildings` → "Buildings", `ship_sizes` → "Ship sizes", `section_templates` → "Ship sections", `starbase_buildings` → "Starbase buildings", `starbase_modules` → "Starbase modules", `megastructures`, `edicts`, `districts`, `armies`, `traits`, `deposits`, `decisions`, `policies` and `zones` likewise. Any other folder becomes its name in title case with underscores turned into spaces.
- **Names:** localisation of the id, falling back to the id itself.
- **Each unlock records:** its source (source name and file), its kind, and its icon reference (`icon` value plus `icon_frame`).
- **Tech-to-tech links:** "Leads to" techs are not repeated in Unlocks, because the tree already shows them.

### Tech details (extend `TechParser`/`Tech`)
- **Stat bonuses.** One entry per string entry in `modifier`, holding key, value text, localised name and formatted value. Formatting:
  - keys ending `_mult` with a numeric value show as a percentage, e.g. +5% or −10%;
  - keys ending `_add` show as a signed number;
  - other keys show the value as written.
  - Names come from `MOD_<KEY>`, then `mod_<key>`, then the key itself.
- **Custom unlock text.** Each child block of `prereqfor_desc` gives (kind = child key, title localised, desc localised or null).
- **Feature flags:** the string items of `feature_flags`. **Gateway:** the `gateway` value.
- **Technology swaps.** Each `technology_swap` gives the target name and the trigger script text.
- **Weights.** `weight` is resolved through variables and shown as a number. `weight_modifier`, `ai_weight` and `potential` keep their **script text**.
- **Raw.** The whole winning block is pretty-printed as script text.
- **Script pretty-printer.** It turns a `PdxBlock` into indented text, with 4 spaces per level, `key op value` lines and bare items. A value that is a localisation key gets a trailing `# <name>` comment, e.g. `ethic_militarist # Militarist`.

### Icons (one resolver for unlocks and bonuses)
- **`SpriteIndex`.**
  - It parses `interface/**/*.gfx` across sources, with the last source winning per sprite name, case-insensitively.
  - It reads `spriteType`/`frameAnimatedSpriteType` blocks: name, `texturefile` (any case) and `noOfFrames` (default 1).
  - It is built during the tech build and stored on the tree.
- **Icon reference resolution**, in order. The result is (texture path, frame or null):
  1. An `icon` value starting with `GFX_` resolves through `SpriteIndex`. The frame is `icon_frame`, 1-based, defaulting to 1 when the sprite has frames.
  2. An `icon` value containing `/` or ending in `.dds` is used as a direct path.
  3. A bare `icon` value: try sprite `GFX_<value>`, then `gfx/interface/icons/<kind folder>/<value>.dds`.
  4. No `icon`: try `gfx/interface/icons/<kind folder>/<id>.dds`.
  5. **Stat bonuses:** `gfx/interface/icons/modifiers/mod_<key>.dds`. When the value is negative and a `_negative` file exists, use that.
- **Frames.** A texture with N frames is a horizontal strip, and frame f (1-based) is the slice `[(f-1)·W/N, f·W/N)`, cropped before encoding.
- **Caching.** It extends the existing icon pipeline (DDS decode, downscale to 64, PNG, disk cache, per-tree background prewarm). Cache keys include the frame. Unlock and bonus icons are prewarmed after the tech icons.
- **Failures** give a placeholder, as before.

### UI
- **Layout.** The Tech tab becomes three columns: list (280 px) | tree (flexible) | sidebar (380 px, scrolls on its own). The detail card moves out from under the graph into the sidebar.
- **Sidebar header:** icon, name and key, tags (area, category, tier, cost, weight, gateway, DLC and flags), description, source and overrides, and path from start.
- **Sections** (`<details>`):
  - **Unlocks (count):** grouped by kind, as chips with an icon and name, with a tooltip showing the id and source. Mod-provided items are tinted orange.
  - **Effects:**
    - stat bonuses (icon, signed value and name, plus a muted raw key);
    - custom unlock text;
    - feature flags;
    - tech swaps (target as a link if it is a known tech, with the trigger as script).
  - **Weights:** base weight, then the `weight_modifier` script and the `ai_weight` script.
  - **Conditions:** the `potential` script ("always" if absent) and the prerequisites as links.
  - **Raw definition.**
- Unlocks and Effects are open by default; the rest are collapsed.
- Script blocks are shown in a monospace, pre-formatted style, with keys, values and comments coloured by simple token rules done in C# (no JS).

## Errors and performance
- Every unreadable or malformed file becomes a warning, as before. Unlock scanning runs in the background build.
- Pre-filtering by substring keeps the scan cheap. Expect thousands of files across mods.
- The unlock index is a dictionary from tech key to a list of unlocks, built once per tree.

## Testing
Small fake installs cover:
- discovery via `prerequisites`, `show_in_tech` and `key =`;
- excluded folders;
- file-level and object-level overrides of unlock objects;
- kind naming;
- modifier formatting (`_mult`, `_add`, other, negative) and name fallbacks;
- `prereqfor_desc`, `feature_flags`, `gateway` and `technology_swap`;
- weight resolution;
- the pretty-printer, including annotations, nesting, operators and bare items;
- SpriteIndex parsing (case-insensitive keys, frames, later source wins);
- each icon resolution rule;
- frame cropping.

The UI is verified manually.

## Out of scope
- Effects granted indirectly (scripted effects, on_actions).
- Evaluating `show_tech_unlock_if` per empire: all unlocks are shown.
- Non-English text.

# Technologies Addon: Design (Sub-project 25)

Approved mockup: `.superpowers/brainstorm/1832-1792284174/content/addons-technologies.html`. It shows the tech cards, the editor, a preview card and the generated script. The user picked suggestions 1 (custom techs appear in the Tech tab) and 5 (validation).

## Goal
A second addon on the Addons tab, **Technologies**, lets a modpack owner create simple techs that Faz Addons adds to the game. The defining example is a repeatable, Authoritarian-only tech giving +1 `divided_patrongage_max_subjects` per level, with its own weight, cost and cost per level. Techs travel with the addon settings, so every player in a session builds identical techs.

## Facts (verified on the user's install, Stellaris 4.5.2)
- **Where techs live:** `common/technology/*.txt`.
- **Fields used:**
  - `area` (physics/society/engineering);
  - `tier`;
  - `category = { <key> }` (keys from `common/technology/category/*.txt`);
  - `cost`;
  - `cost_per_level` and `levels = -1` for repeatables;
  - `icon = <name>`: any `gfx/interface/icons/technologies/<name>.dds`, so existing icons can be reused;
  - `weight`;
  - `weight_modifier = { factor = … }`;
  - `ai_weight = { factor = … }`;
  - `weight_groups = { repeatable }` and `mod_weight_if_group_picked = { repeatable = 0.01 }`, as the base game's repeatables use;
  - `is_rare = yes`;
  - `potential = { … }`;
  - `prerequisites = { "tech_x" }`;
  - `modifier = { key = value }`, which applies per level on repeatables.
- **Base game repeatable values:** cost 50000, cost_per_level 5000, tier 5, weight 15, weight_modifier factor 0.5.
- **Ethics:** `common/ethics/*.txt`. Each regular ethic has a fanatic variant named `ethic_fanatic_<suffix>` (e.g. `ethic_authoritarian` / `ethic_fanatic_authoritarian`); gestalt is `ethic_gestalt_consciousness`. Icons are `gfx/interface/icons/ethics/<key>.dds`, names come from localisation `<key>`.
- **Modifiers:** the game writes `<userDir>/logs/script_documentation/modifiers.log` with lines `- <key>, Category: <Category>` for **every modifier in the current mod list**. On the user's PC that's 5,589 Countries modifiers, including `divided_patrongage_max_subjects`. Display names are localisation `mod_<key>`, case-insensitive; some have none.
- **Localisation:** `localisation/english/*_l_english.yml` must start with a UTF-8 BOM and `l_english:`. Entries look like ` key:0 "text"`.

## Data model (additions to `Core/Addons/AddonSettings.cs`)
```csharp
public sealed record AddonSettings(bool Enabled = false, MarketplaceSettings? Marketplace = null, TechnologiesSettings? Technologies = null);
public sealed record TechnologiesSettings(bool Enabled = true, IReadOnlyList<CustomTech>? Techs = null);
public sealed record CustomTech(
    string Key, string Name, string Description, string Icon,
    string Area = "society", string Category = "statecraft", int Tier = 5,
    bool Repeatable = true, bool Rare = false,
    int Cost = 50000, int CostPerLevel = 5000, int Weight = 15,
    IReadOnlyList<string>? Ethics = null,          // regular ethic keys (e.g. ethic_authoritarian) or ethic_gestalt_consciousness; any-of
    IReadOnlyList<string>? Prerequisites = null,   // tech keys
    IReadOnlyList<TechModifier>? Modifiers = null);
public sealed record TechModifier(string Key, double Value);
```
- **Equality:** records compare lists by value (the same approach as `MarketplaceSettings.Resources`).
- **Key:** `CustomTech.Key` defaults in the UI to `tech_faz_` plus a slug of the name (lower case, `[a-z0-9_]`). It can be edited.
- **Persistence:** saved in the existing `addons.json`. Sessions carry the whole `AddonSettings`, as today.

## Reading the game (`TechAddonCatalog`)
`TechAddonCatalog.Build(IReadOnlyList<ContentSource> sources, Localisation? loc, string? modifiersLogPath, ICollection<string>? warnings, CancellationToken ct)` uses the same sources as `MarketCatalog`, which never include Faz Addons.
- **`Techs`:** key → `(Name, Area, Tier, IsRepeatable)`. It reads every top-level block of `common/technology/*.txt`, with the winning file per path; the last definition of a key wins. Names come from localisation.
- **`Icons`:** a sorted set of icon names, the `.dds` file stems under `gfx/interface/icons/technologies/` in all sources. `IconUri(name)` is loaded lazily via `IconCache`.
- **`Categories`:** keys and names from `common/technology/category`, the winning file per path.
- **`Ethics`:** regular ethics (not starting with `ethic_fanatic_`) plus gestalt, each with Name, its `FanaticKey` if that ethic exists, and an icon URI.
- **`Modifiers`:** from `modifiersLogPath` when that file exists: key, category, and a localised name (or null). Otherwise from every localisation key `mod_<x>` (category null). `ModifiersFromLog` (bool) records which source was used.
- **In `AddonService`:** the catalog is built together with the MarketCatalog, with the same caching and invalidation, and exposed as `TechCatalog`.

## Validation (`TechValidator`, suggestion 5)
`TechValidator.Validate(CustomTech tech, IReadOnlyList<CustomTech> all, TechAddonCatalog catalog)` returns `IReadOnlyList<TechIssue>`, where `TechIssue(TechIssueLevel Level, string Field, string Message)` and `TechIssueLevel` is `Error` or `Warning`.
- **Errors** depend only on mod files and settings, so they are the same for every player in a session. A tech with any error is **left out of the build**:
  - the key is empty, isn't `[a-z0-9_]+`, or doesn't start with `tech_`;
  - the key is used by an existing tech in the mod list, or by another custom tech;
  - the name is empty;
  - the area isn't physics, society or engineering;
  - the category is unknown;
  - the tier isn't between 0 and 5;
  - the icon is unknown;
  - a prerequisite is unknown (not an existing tech or another valid custom tech);
  - the cost is 0 or less, the cost per level is below 0 (repeatables only), or the weight is below 0;
  - an ethic is unknown.
- **Warnings** may depend on this PC's `modifiers.log` and never change the build:
  - the modifier key isn't in the modifier list, worded as "not found in this PC's modifier list" (or, without the log, "couldn't be checked");
  - a modifier value is 0;
  - the tech has no effects;
  - a repeatable has tier below 5 (the game shows repeatables at tier 5);
  - a modifier's category is not Countries, Pops, Planets or similar country-applicable scopes (from the log), worded as "may not apply to an empire".
- **`TechValidator.Buildable(all, catalog)`:** the techs without errors, in settings order.

## Building (`AddonBuilder`)
When `settings.Enabled && settings.Technologies is { Enabled: true }` and there are buildable techs:
- **Techs file:** `common/technology/zzzzz_faz_addons_tech.txt` holds one block per buildable tech, in settings order:
  ```
  <key> = {
  	area = <area>
  	tier = <tier>
  	category = { <category> }
  	cost = <cost>
  	cost_per_level = <cpl>        # repeatable only
  	levels = -1                   # repeatable only
  	icon = <icon>
  	is_rare = yes                 # only when Rare
  	weight = <weight>

  	potential = {                 # only when Ethics given
  		OR = {
  			has_ethic = <regular>
  			has_ethic = <fanatic>     # when the catalog has the fanatic variant
  			…
  		}
  	}
  	prerequisites = { "<p1>" "<p2>" }   # only when given

  	weight_modifier = { factor = 0.5 }  # repeatable only
  	ai_weight = { factor = 1 }
  	weight_groups = { repeatable }                    # repeatable only
  	mod_weight_if_group_picked = { repeatable = 0.01 } # repeatable only

  	modifier = {
  		<key> = <value>          # invariant culture, up to 4 decimals
  	}
  }
  ```
  Blocks are separated by a blank line, with LF line endings and no BOM.
- **Localisation:** `localisation/english/faz_addons_l_english.yml`, with a UTF-8 BOM, `l_english:`, then for each tech ` <key>:0 "<name>"` and ` <key>_desc:0 "<description>"`. Text is cleaned: line breaks become spaces and `"` becomes `'`.
- **Changes:** `AddonOutput.HasChanges` is true when either addon contributes files.
- **Apply:** Apply builds the tech catalog the same way it builds the market catalog: no localisation, icons or modifiers log are needed for errors (icon existence uses file names only). The Apply result gets a warning listing techs skipped for errors: "Not added (fix on the Addons tab): Name (reason)".

## Tech tab shows custom techs (suggestion 1)
- **When:** the "Current game (dlc_load.json)" tree is built and `dlc_load.json` lists `mod/faz_addons.mod` while the folder exists.
- **How:** `TechTreeService` adds `ContentSource.FromPath("Faz Addons", <userDir>/mod/faz_addons)` as the **last** source, using a new `includeAddon` parameter on `TechSources.Build`, false by default. The custom techs then appear in the tree with names, icons, conditions and research routes. The Events tab, which shares the tree's sources, also sees them.
- **Saved-list trees** don't include it.
- **The Marketplace and tech catalogs** never include it.

## Sessions
- `AddonSettings` gains `Technologies`, so it's carried as before.
- The protocol goes from 4 to 5, because older apps would silently drop the techs and build a different addon.
- Players build from the host's settings. Errors are deterministic, so the same techs are skipped on every machine.

## UI (Addons tab, following the mockup)
- **Left list:** Technologies gets its own switch and "N techs".
- **Technologies panel:**
  - **Cards:** icon, name, "Area · Category · Repeatable/Tier N", the first effect, and ethic chips. A red "!" appears when the tech has errors, a yellow one for warnings. There's also a "＋ New technology" card that creates a tech with the defaults and the name "New technology".
  - **Editor for the selected tech:**
    - **Identity:** name, key (auto-generated until edited by hand), description, and an icon picker (a grid of the matching icons from the search, at most 60 shown, with the selected one outlined);
    - **Research:** an area switch, a category dropdown, tier, a repeatable switch and a rare switch;
    - **Cost and weight:** base cost, additional cost per level (repeatables), weight, and the hint "Level 10 costs X";
    - **Who can research it:** ethic chips with icons (any-of) and prerequisite chips with an "＋ Add technology" search over the catalog's techs and the other custom techs;
    - **Effects:** rows of a modifier search (name and key, at most 30 results, with the category shown), the value, and ✕;
    - **Delete technology** (asks for confirmation).
  - **Preview column:** an in-game-style card (icon, name, area · category · repeatable, cost text, effects with the localised modifier names and signed values, requirements, description), Technology/Localisation tabs showing that tech's generated text, and the issue list (✔ / ⚠ / ✖ lines).
- **Read-only:** as for the Marketplace when connected as a client. It shows the host's techs, everything is disabled, and the cards and preview are still visible.
- **Saving:** edits save through `AddonService.Save`, with the same debouncing as the Marketplace.

## Testing
- **`TechAddonCatalog`:**
  - techs, icons, categories and ethics come from synthetic sources, with the winning file per path;
  - the fanatic variant is detected;
  - modifiers are parsed from a sample log, with names from localisation, or fall back to `mod_` keys.
- **`TechValidator`:** every error and warning listed above. `Buildable` drops techs with errors and keeps the order. A prerequisite on another *valid* custom tech is accepted, but one on an invalid custom tech is an error.
- **`AddonBuilder`:**
  - the exact text of a repeatable ethic-locked tech (the mockup's example) and of a non-repeatable rare tech with no ethics;
  - localisation with BOM and cleaned text;
  - a tech with errors is absent;
  - Technologies off gives no files;
  - the output is deterministic.
- **`TechSources` / `TechTreeService`:** `includeAddon` adds the folder last only when dlc_load lists it and the folder exists.
- **Session:** protocol 5, and the techs round-trip through `Welcome`.
- **Real data:**
  - build the tech catalog for the user's list and report the tech, icon, category, ethic and modifier counts;
  - validate and build the example tech, and inspect the output.

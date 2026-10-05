# Diplomatic Weight Calculator — Design (Sub-project 15)

## Goal
Compute each player's diplomatic weight from the save and from the game and mod files, without depending on any mod. The result is laid out like the game's tooltip:
- the base parts (fleet, economy, tech, pops), each with its own bonuses;
- the overall bonuses;
- the total.

It shows the **real** value and the **in-game** value (clamped at 0 when negative). Bonus sources the app can't compute are named, and the total is marked "≈".

## Verified facts (from the user's in-game tooltips and saves)
**Base parts:**
- **Fleet** = `military_power` × `NGameplay.DIPLOMACY_WEIGHT_NAVAL_FACTOR` (0.025) × (1 + Σ `diplo_weight_naval_mult`).
  - Check: 644,875,181 × 0.025 = 16,121,880, matching "From Fleets" exactly; × 2.15 = 34,662,041.
- **Economy** = `economy_power` × 0.15 × (1 + Σ `diplo_weight_economy_mult`).
  - Check: 371,934 × 3.40 = 1,264,574.
- **Tech** = `tech_power` × 0.1 × (1 + Σ `diplo_weight_technology_mult`). This matches to within save timing.
- **Pops:** the game showed 11,451 from 399,579 pops, × (1 + Σ `diplo_weight_pops_mult`). The per-pop formula is not matched (happiness). Show it as the defines range and mark it "≈". It is about 0.01% of the total.

**Total:**
- Total = (fleet + economy + tech + pops) × (1 + Σ `diplo_weight_mult`).
- Check: 36,220,973 × 8.37 ≈ 303.7M, within percent rounding.
- The game shows a negative total as 0.

**Data caveat:** `military_power` in a save is as of that save. A save the game later recalculates can give a different value (the 2387.07.01 save stored 3.23B; the game used 1.81B after loading). The UI says "fleet power as stored in the save".

## Bonus sources the app computes
Each source contributes `diplo_weight_mult`, `diplo_weight_naval_mult`, `diplo_weight_economy_mult`, `diplo_weight_technology_mult` and `diplo_weight_pops_mult` values. They are read from these definition blocks:

| Source (from the save) | Definitions folder | Block holding the modifiers |
|---|---|---|
| traditions | common/traditions | `modifier` |
| ascension_perks | common/ascension_perks | `modifier` |
| government civics + origin | common/governments/civics | `modifier` |
| government authority | common/governments/authorities | `country_modifier`, `modifier` |
| active_policies `selected` | common/policies | `option = { name = X modifier = {…} }` |
| edicts | common/edicts | `modifier` |
| tech_status | common/technology | `modifier` |
| relics | common/relics | `passive_modifier`, `modifier`. The game groups these as one "From Relics" line. |
| timed_modifier items (× `multiplier`) | common/static_modifiers | the top-level block itself |
| owned megastructures (`megastructures` section, `owner` = country) | common/megastructures | `country_modifier` |
| passed resolutions (`galactic_community.passed` ids → `resolution` section types), when the country is a community member | common/resolutions | `modifier` |
| Galactic Council seat (`galactic_community.council` contains the country) | common/static_modifiers | static modifier `council_member` |

**Reading rules:**
- Definitions come from the game and then the enabled mods, in file-name order; the last definition of a key wins.
- Values can be `@variables`. These resolve from the same file first, then from `common/scripted_variables`.
- Only `diplo_weight_*` entries directly inside the named block count. Values inside triggers and weights are ignored.

**Line names:** taken from the game's English localisation (key, or `modifier_<key>`). Otherwise the key is prettified.

**Not computed (named in the UI):** leader, councilor and delegate bonuses (Delegate, Gravitas, Expert Negotiation, Conspirator Liaison, Politics Traditions), faction bonuses ("From Factions") and other scripted sources. These may be added later.

## Design
- **Save model (Core `Saves/`):**
  - `SaveCountry` gains `Holdings`: civics, origin, authority, traditions, perks, policy selections, edicts, relics, and timed modifiers with their multipliers. Techs are already present.
  - `GameSnapshot` gains:
    - `Resolutions`: the passed resolution types;
    - `CommunityMembers`;
    - `Council`;
    - `Megastructures`: owner → list of types, owned only.
  - `GamestateScanner` reads these from `country` (depth-1 blocks), `resolution`, `galactic_community` and `megastructures`.
- **`ModifierCatalog` (Core `Diplomacy/`):**
  - `Load(sources)` builds, per category, a map from key to a `DiploMods` record (Naval, Economy, Tech, Pops, Overall).
  - It also loads the scripted variables.
  - It is built once per mod list.
- **`DiploCalculator.Compute(country, snapshot, catalog, defines, names)`** returns a `DiploBreakdown`:
  - `Fleet`, `Economy`, `Tech`, `Pops`: each a `DiploPart(Input, Factor, Base, Bonuses, Total)`, where a bonus is a `DiploBonus(Name, Percent)`.
  - `OverallBonuses`.
  - `BaseTotal`, `Real`, `InGame` (Real clamped to 0).
  - `PopsMin`/`PopsMax`.
  - `Missing` (the not-computed note), `Approximate`.
- **Wiring:**
  - `LiveGameService` takes an optional `Func<GameSnapshot, GameSnapshot> enrich`. The app uses it to compute breakdowns for the save's **player** countries into `GameSnapshot.Diplo` (country id → breakdown).
  - The catalog, defines and names come from the game directory and the applied mod list. They are cached until the list of content paths changes.
  - `LiveFilter` keeps only the viewer's breakdown, so clients get theirs from the host.
  - `CachedDiploWeight` (the mod's variable) is removed.
- **UI (Live Game sidebar):**
  - "Diplomatic weight": the real value (plus "≈" while sources are missing), and "In game: 0 (negative)" when clamped.
  - An expandable breakdown laid out like the tooltip:
    - the overall bonus lines;
    - the base, with the fleet, pops, economy and tech parts, each with its input × factor and its bonus lines;
    - the not-computed note.

## Testing
- **Scanner:** holdings, resolutions/passed/council/members, and owned megastructures, from small gamestate fixtures.
- **Catalog:**
  - each category's container block;
  - policy options;
  - `@variables` (local and global);
  - last definition wins;
  - values in triggers ignored.
- **Calculator:**
  - a fixture that rebuilds the user's tooltip (2387.09.17): fleet input 644,875,181 with naval bonuses +10/+25/+100/−20 gives 34,662,041; economy 2,479,558 × 0.15 × 3.40 = 1,264,574;
  - an overall sum;
  - a negative total gives InGame 0;
  - council and resolution sources apply only to members.
- **Manual:** a scratchpad harness runs the real save and lists the computed lines next to the screenshot's lines.

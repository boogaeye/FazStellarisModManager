# Diplomatic Weight: Leaders, Delegate, Factions and Councilors (Sub-project 16)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. TDD with one commit per task.

**Goal:** Add the diplomatic weight bonus lines that depend on leaders, the Galactic Community delegate, pop factions and councilors. Also evaluate `@[ … ]` expressions, and show percentages truncated the way the game does. On the user's real save (2387.09.17, country 0), the app must reproduce these tooltip lines:

| Line | Value |
|---|---|
| Delegate | +170% |
| Politics Traditions | +42% (42.5) |
| Gravitas | +42% (42.5) |
| From Factions | +71% (71.5) |
| Conspirator Liaison | +23% |
| Shared Destiny | +20% |
| Kardashev's Type 2 | +5% |

**Research** (verified against the tooltip; read these first):
- `<scratchpad>/leaders/compute.py`, which reproduces all seven values;
- `ld.py` and `blk.py` for the save layout;
- `defs.py`, which prints the definitions.

Here `<scratchpad>` = `C:/Users/SCPFAZ~1/AppData/Local/Temp/claude/C--Users-SCP-Fazbear-source-repos-FazStellarisModmanager/2ff7c1a2-dff6-4e3a-95bb-98beb2bf24b6/scratchpad`. The research findings are summarized in the rules below.

**Conventions:**
- Branch: `feature/diplo-leaders`.
- Build and test with `-c Release --artifacts-path <scratchpad>/art`. Never kill FazStellarisModmanager.exe.
- No backslash escapes in C# strings. Use raw string literals for fixtures.
- Read the existing code first: `Core/Saves/*` (GamestateScanner, GameSnapshot) and `Core/Diplomacy/*` (ModifierCatalog, DiploCalculator, DiploContext).

## Rules (from the research)
- **Leader skill** = `level` + `bonus_skill_level` (missing counts as 0). Both are fields of the leader's entry in the top-level `leaders` section.
- **GC delegate:** the leader in the country's `owned_leaders` whose `location = { type=galactic_community … }`.
- **Delegate bonuses:** every `diplo_weight_delegate_mult` source becomes an *overall* line: value × delegate skill, named after its source.
  - Sources: traditions, perks, static/timed modifiers, relics and so on.
  - The static modifier `galactic_community_delegate` (`diplo_weight_delegate_mult = 0.10`) applies to every Galactic Community member that has a delegate, and is named "Delegate".
  - Without a delegate, delegate mults contribute nothing.
- **Pop factions:** the top-level `pop_factions` section has entries `{ country=N type="…" support_power=X faction_approval=Y }`.
  - The faction type's definition (`common/pop_faction_types`, inline scripts expanded) has `country_modifier = { diplo_weight_mult = v }`. Its contribution is v × support_power.
  - Sum all factions into one overall line, "From Factions".
  - Approval is not applied (unverified).
- **Councilors:**
  - The country's `government.council_positions` lists ids. Each id is an entry in the top-level `council_positions` section: `{ country=N type="councilor_x" leader=L }`.
  - The councilor type's definition (`common/governments/councilors`) has `modifier = { diplo_weight_mult = v … }`. Its contribution is v × (skill(L) + country councilor_skill_add), named after the councilor type.
  - **councilor_skill_add** = Σ `councilor_skill_add` over all the country's modifier sources already in the calculator (timed/static modifiers, traditions, perks, civics, policies, edicts, techs, relics, megastructures) plus the **founder species' traits**.
  - The country's `founder_species_ref` points into the top-level `species_db` section, which lists the species' `traits` strings. Trait definitions are in `common/traits`, under `modifier`.
  - In the research case: +4 from timed `prototype_vir_core_modifier` and +2 from trait `trait_uncanny_intuition`.
- **`@[ expression ]` values:** for example `diplo_weight_mult = @[ 0.2 * sartek_utopian_legacy_mod_active ]`, where the variable comes from scripted variables (`@sartek_utopian_legacy_mod_active = 1`; referenced inside without the @). Evaluate `+ - * /`, parentheses, numbers and variable names; unknown names make the value null (skip it).
- **Tradition swaps (MECR):** a tradition may define swaps. Check how the real definitions write them (inline scripts `MECR_swap_tr_politics_adopt.txt` and `MECR_swap_tr_politics_gravitas.txt`) and support the form they use. The swap's trigger uses `is_galactic_emperor` / `is_galactic_custodian`.
  - Evaluate only those two triggers, plus a plain NOT/OR of them.
  - Emperor: `galactic_community.leader` == country and `empire=yes`. Custodian: `galactic_community.leader` == country and no `empire=yes`.
  - When a swap's trigger holds, use the swap's modifier instead of the base.
  - If supporting swaps is impractical, report why. The base `tr_politics_gravitas` already has `diplo_weight_delegate_mult = 0.025`, so check whether the base values reproduce the numbers anyway.
- **Display:** the game truncates percentages (42.5 → 42, 71.5 → 71). Format bonus percentages with `Math.Truncate(p*100)` in the Live Game breakdown, keeping the sign.
- **Names:** "Kardashev's Type 2" is the localisation of `achievement_01`, whose text contains nested `$other_key$` references and colour codes. Make `DiploContext.Name` resolve `$key$` references (depth ≤ 3) and strip colour and icon codes, unless `Localisation.Get` already does this. Check first.

## Tasks
1. **Scanner** (with tests on small fixtures):
   - Read the top-level `leaders` section: id → level, bonus_skill_level, location type.
   - Read the top-level `council_positions` (id → country, type, leader), `pop_factions` (country, type, support_power, faction_approval) and `species_db` (id → traits).
   - For each country, read `owned_leaders`, `government.council_positions` and `founder_species_ref`.
   - Read `galactic_community.leader` and `empire`.
   - Keep the new data small: only what the calculator needs. Leaders and positions can be read only for owned leaders, or for all; mind the 409 MB file and use the byte reader for large sections.
   - Add the data to GameSnapshot/SaveCountry as optional members. LiveFilter must strip other countries' data, as it does for Holdings.
2. **Catalog** (tests):
   - Add `DelegateMult` and `CouncilorSkill` to DiploMods (`diplo_weight_delegate_mult` and `councilor_skill_add`).
   - Add new sources: `PopFaction` (`common/pop_faction_types`, `country_modifier`), `Councilor` (`common/governments/councilors`, `modifier`) and `SpeciesTrait` (`common/traits`, `modifier`).
   - Evaluate `@[ ]` expressions.
   - Support tradition swaps, as above.
3. **Calculator** (tests): the delegate, faction and councilor rules, with councilor_skill_add from all sources plus the founder species' traits. Lines are named after their sources ("Delegate" for the GC static modifier; "From Factions").
4. **Display and names:** truncated percentages and nested-name resolution.
5. **Real-save check:** extend the harness in `<scratchpad>/conftry/Program.cs` (it already loads the catalog from the game and the enabled mods and prints the breakdown for the real save). Show that all seven lines appear with the values above, then report the full overall bonus list and the total.

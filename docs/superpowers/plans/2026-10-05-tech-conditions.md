# Tech conditions with live ✓ / ✗ / ? marks (Sub-project 18)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development or superpowers:executing-plans. Use TDD for the Core tasks and make one commit per task.

**Goal (user request):** the tech sidebar's "Available when" conditions show, for the viewer's empire in the live game data, whether each condition is true (✓), false (✗) or unknown (?), like the game does. Marks are animated:
- ✓ pops in;
- ✗ shakes once;
- **?** keeps shaking and rotating like a puzzled question mark.

**Approach:** a three-valued evaluator (Kleene logic) for common country triggers, with scripted triggers expanded. Anything it can't evaluate (scope changes such as `any_owned_planet` or `owner_species`, comparisons, unknown triggers) is Unknown.

**Conventions:**
- Branch `feature/tech-conditions`.
- Build and test with `-c Release --artifacts-path <scratchpad>/art`, where `<scratchpad>` = `C:/Users/SCPFAZ~1/AppData/Local/Temp/claude/C--Users-SCP-Fazbear-source-repos-FazStellarisModmanager/2ff7c1a2-dff6-4e3a-95bb-98beb2bf24b6/scratchpad`.
- Never kill FazStellarisModmanager.exe, and don't launch the app.
- No backslash escapes in C# strings. Use the Write/Edit tools.
- Commit trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

**Read first:**
- `Core/Saves/*` (GamestateScanner, GameSnapshot, LiveFilter, LiveFeed, SaveReader);
- `Core/Descriptors/ParadoxScriptParser.cs`;
- `Core/Technology/TechDetails.cs` (`PotentialScript`), `TriggerSummary.cs` and `Localisation.cs` (`ScriptedTriggers`, loaded in `TechDatabase`);
- `FazStellarisModmanager/Components/TechSidebar.razor` (the "Conditions → Available when" section) and `ScriptView.razor`;
- `FazStellarisModmanager/Pages/TechPage.razor` (already injects `LiveFeed`).

## Task 1: Save facts
- **SaveCountry** (new optional trailing members):
  - `Flags`: the keys in the country's `flags = { name=date … }` block.
  - `Ethics`: from `ethos = { ethic="ethic_x" ethic="ethic_y" }`.
  - Read both in the country scan; there are already depth-1 parsed blocks.
- **GameSnapshot** (new optional trailing members):
  - `GlobalFlags`: the keys of the top-level `flags = { … }` block. Use the byte reader; the top-level `flags` block can be large.
  - `Dlcs`: the strings in meta's `required_dlcs = { … }`, read in `SaveReader` from `meta`.
- **LiveFilter:** keep `Flags` and `Ethics` only for the viewer, and clear them for others. `GlobalFlags` and `Dlcs` stay.
- **Tests:** scanner and filter fixtures.

## Task 2: Condition evaluator (Core, `Core/Conditions/ConditionEvaluator.cs`)

```csharp
public enum Truth { True, False, Unknown }
/// <summary>One condition line with its result; Children for blocks (AND/OR/NOT/…, scripted triggers, unknown scopes).</summary>
public sealed record ConditionNode(string Text, Truth Result, IReadOnlyList<ConditionNode> Children);
/// <summary>What the viewer's empire has, from live data. Sets are case-insensitive.</summary>
public sealed record EmpireFacts(IReadOnlySet<string> Techs, IReadOnlySet<string> Flags, IReadOnlySet<string> GlobalFlags,
    IReadOnlySet<string> Perks, IReadOnlySet<string> Traditions, IReadOnlySet<string> Civics, IReadOnlySet<string> Ethics,
    string? Origin, string? Authority, string? CountryType, IReadOnlySet<string> Dlcs, bool IsPlayer)
{ public static EmpireFacts From(SaveCountry c, GameSnapshot s, bool isPlayer) … }
public sealed class ConditionEvaluator(Func<string, PdxBlock?> scriptedTrigger)
{ public ConditionNode Evaluate(PdxBlock block, EmpireFacts facts); /* root = implicit AND of all entries */ }
```

**Logic** (Kleene):
- **AND:** any False → False; else any Unknown → Unknown; else True.
- **OR:** any True → True; else any Unknown → Unknown; else False.
- **`NOT = {…}`** = NOR of its entries (Paradox semantics), so NOT(True)=False, NOT(Unknown)=Unknown.
- **NOR** = NOT(OR); **NAND** = NOT(AND).
- **`hidden_trigger = {…}`** = AND.
- **`custom_tooltip = { fail_text = … success_text = … <triggers> }`** = AND of the trigger entries; ignore `fail_text`, `success_text` and `text`. If no triggers remain, Unknown.

**Leaf triggers** (value yes/no or a key; `= no` negates boolean triggers):
- `always = yes/no`
- `has_technology`, `has_country_flag`, `has_global_flag`, `has_ascension_perk`, `has_tradition`, `has_ethic` (exact key)
- `has_civic` / `has_valid_civic`, `has_origin`, `has_authority`, `is_country_type`
- `is_fallen_empire` (type `fallen_empire` or `awakened_fallen_empire`), `is_regular_empire` (type `default`)
- `is_machine_empire` (authority `auth_machine_intelligence`), `is_hive_empire` (authority `auth_hive_mind`), `is_gestalt` (either)
- `host_has_dlc = "Name"` (Dlcs contains it)
- `is_ai` (True if not a player)
- Values may be quoted.

**Scripted triggers:**
- A key whose name is in `scriptedTrigger` with value `yes`/`no`: evaluate its body (negate for `no`). It becomes a node whose children are the body's lines.
- If the body contains `$`, the result is Unknown.
- Guard recursion with depth ≤ 8 → Unknown.

**Everything else is Unknown:**
- comparisons (`<`, `>`, numeric);
- any block value not listed above (scope changes);
- unknown keys.

**Node text:**
- `key = value` for leaves;
- `key = { … }` → `key`, with child nodes for its contents;
- operators other than `=` are kept, e.g. `num_owned_planets > 5`.

**Tests:**
- AND/OR/NOT/NOR mixes with Unknown;
- each leaf trigger kind, including `= no`;
- `host_has_dlc`;
- a scripted trigger expanded to children, and one with `$` giving Unknown;
- custom_tooltip;
- scope blocks Unknown;
- an OR that is True despite an Unknown sibling.

## Task 3: Sidebar view with animations
- **New component `Components/ConditionView.razor`:** renders a `ConditionNode` tree as indented lines. Each line has a mark span followed by the text.
  - Mark classes: `cv-mark cv-true` (✓), `cv-false` (✗), `cv-unknown` (?).
  - Lines get a staggered entrance (`style="animation-delay: Nms"`, about 35 ms per line, capped at about 700 ms).
- **TechSidebar:**
  - Add a parameter `EmpireFacts? Facts`, plus `Func<string, PdxBlock?>? ScriptedTrigger`, or pass a ready evaluator.
  - In "Available when", when `Facts` is not null and the potential exists: parse `Details.PotentialScript` with `ParadoxScriptParser`, evaluate it, and render a header "Available to you: ✓ / ✗ / ?" (an animated mark plus a word: yes / no / unknown), then the `ConditionView`.
  - Keep a small toggle, "show script", that reveals the existing `ScriptView` text.
  - When `Facts` is null, show the existing `ScriptView` plus the muted note "Live game data needed for ✓/✗ marks."
  - Re-key the view when the selected tech or the facts change, so the animations replay on selection.
- **TechPage:** build the `EmpireFacts` from `Feed.Current` and `Feed.ViewerId`: the viewer's SaveCountry and the snapshot, with `IsPlayer` = the viewer is in Players. Rebuild on `Feed.Changed`, and pass the facts to TechSidebar. The scripted triggers come from the loaded tech tree's Localisation (`ScriptedTriggers`); check how `TechTreeService` exposes it.
- **CSS animations** (in `site.css`):
  - **✓** `@keyframes cv-pop`: scale 0 → 1.25 → 1 over about 0.45 s. Green #6fcf97.
  - **✗** `@keyframes cv-shake-x`: a quick horizontal shake (translateX ±3px, 4 times, about 0.5 s), played once. Red #ff8a80.
  - **?** `@keyframes cv-wonder`: it keeps moving like a puzzled question mark. Rotate −14° → 14° with a small translateX shake and a slight scale pulse, about 2.4 s per cycle, infinite, ease-in-out, with a pause segment in the keyframes (e.g. 0–60% moving, 60–100% resting). `transform-origin: 50% 80%`. Grey-blue #9fb3c8.
  - Lines: `@keyframes cv-in` (fade and slide from 4px left). Marks are `display:inline-block` with a fixed width so the text lines up.
  - Respect `@media (prefers-reduced-motion: reduce)` by disabling the animations.
- Build the app and run the full suite.

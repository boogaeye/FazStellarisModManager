# Diplomatic Weight Calculator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compute diplomatic weight from the save plus the game and mod definitions, with tooltip-style lines. Show the real value and the in-game value. The Galactic Market Overhaul mod's cached variable is no longer used.

**Architecture:**
- `GamestateScanner` also reads, per country, what it holds: civics, origin, authority, traditions, perks, policies, edicts, relics and timed modifiers. It also reads the Galactic Community (members, council, passed resolutions) and owned megastructures.
- `ModifierCatalog` loads `diplo_weight_*` values per definition from the game and mod files.
- `DiploCalculator` produces a `DiploBreakdown`.
- `LiveGameService` enriches each read snapshot with breakdowns for player countries, through a hook the app provides. `LiveFilter` passes on only the viewer's breakdown.
- The Live Game sidebar shows the breakdown.

**Spec:** `docs/superpowers/specs/2026-10-05-diplo-calculator-design.md`

**Conventions:**
- Work in `C:\Users\SCP Fazbear\source\repos\FazStellarisModmanager`, branch `feature/diplo-calculator`.
- Build and test with `-c Release --artifacts-path <scratchpad>/art`. Never kill FazStellarisModmanager.exe.
- Avoid backslash escapes in C# strings. Use raw string literals for fixtures.
- Read the real existing code before changing it: `Core/Saves/*`, `Core/Descriptors/ParadoxScriptParser.cs`, `Core/Technology/ContentSource.cs` and `Core/Technology/Localisation.cs`.

---

### Task 1: Scanner reads holdings, the Galactic Community and megastructures

**Files:**
- Modify: `FazStellarisModmanager.Core/Saves/GameSnapshot.cs`
- Modify: `FazStellarisModmanager.Core/Saves/GamestateScanner.cs`
- Modify: `FazStellarisModmanager.Core/Saves/SaveReader.cs`, if the scanner's return type changes.
- Test: `FazStellarisModmanager.Tests/GamestateScannerHoldingsTests.cs`

- [ ] **Step 1: Model additions.** Every new member is optional, so existing constructors and tests keep compiling.

```csharp
/// <summary>A timed (static) modifier on a country; Multiplier scales its values (1 when absent).</summary>
public sealed record TimedModifier(string Key, double Multiplier);

/// <summary>What a country has that can carry modifiers.</summary>
public sealed record CountryHoldings(
    IReadOnlyList<string> Civics,
    string? Origin,
    string? Authority,
    IReadOnlyList<string> Traditions,
    IReadOnlyList<string> Perks,
    IReadOnlyList<string> Policies,
    IReadOnlyList<string> Edicts,
    IReadOnlyList<string> Relics,
    IReadOnlyList<TimedModifier> Timed)
{
    public static CountryHoldings Empty { get; } = new([], null, null, [], [], [], [], [], []);
}

/// <summary>The Galactic Community: member and council country ids, and the types of passed resolutions.</summary>
public sealed record GalacticCommunity(IReadOnlyList<int> Members, IReadOnlyList<int> Council, IReadOnlyList<string> PassedResolutions);
```

Add `CountryHoldings? Holdings = null` as the **last** positional parameter of `SaveCountry`.

Add these as **last** positional parameters of `GameSnapshot`:
- `GalacticCommunity? Community = null`
- `IReadOnlyDictionary<int, IReadOnlyList<string>>? Megastructures = null` (owner country id → megastructure types)
- `IReadOnlyDictionary<int, FazStellarisModmanager.Core.Diplomacy.DiploBreakdown>? Diplo = null`

Task 3 creates the `DiploBreakdown` type. Add the `Diplo` parameter in Task 3 instead if that is cleaner, but keep the order shown.

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Text;
using FazStellarisModmanager.Core.Saves;

namespace FazStellarisModmanager.Tests;

public class GamestateScannerHoldingsTests
{
    const string State = """
        resolution=
        {
        	0={ type="resolution_galactic_focus_x" }
        	1={ type="resolution_mutualdefense_renegade_containment" }
        	2={ type="resolution_never_passed" }
        }
        country=
        {
        	0=
        	{
        		government=
        		{
        			type="gov_imperial_domain"
        			authority="auth_imperial"
        			civics={ "civic_secret_societies" "civic_galactic_sovereign" }
        			origin="origin_alderson"
        		}
        		traditions={ "tr_diplomacy_finish" "tr_politics_finish" }
        		ascension_perks={ "ap_lord_of_war" }
        		active_policies=
        		{
        			{ policy="diplomatic_stance" selected="diplo_stance_condescending_authority_advanced" date="2306.12.02" }
        			{ policy="war_philosophy" selected="unrestricted_wars" }
        		}
        		edicts={ { edict="diplomatic_grants" perpetual=yes } }
        		timed_modifier=
        		{
        			items=
        			{
        				{ modifier="galactic_market_founder" days=-1 }
        				{ multiplier=1.15 modifier="FW_x" days=-1 }
        			}
        		}
        		relics={ "r_ancient_sword" }
        		victory_rank=1
        		type="default"
        	}
        }
        megastructures=
        {
        	10={ type="interstellar_assembly_4" owner=0 }
        	11={ type="interstellar_assembly_restored" owner=0 }
        	12={ type="dyson_sphere_5" owner=7 }
        	13={ type="ring_world_0" owner=4294967295 }
        }
        galactic_community=
        {
        	members={ 0 1 7 }
        	council={ 0 1 }
        	passed={ 0 1 }
        }
        """;

    static (List<SavePlayer>, List<SaveCountry>, GalacticCommunity?, IReadOnlyDictionary<int, IReadOnlyList<string>>) Scan() =>
        GamestateScanner.ScanAll(Encoding.UTF8.GetBytes(State));

    [Fact]
    public void Reads_country_holdings()
    {
        var h = Scan().Item2.Single().Holdings!;
        Assert.Equal(["civic_secret_societies", "civic_galactic_sovereign"], h.Civics);
        Assert.Equal("origin_alderson", h.Origin);
        Assert.Equal("auth_imperial", h.Authority);
        Assert.Equal(["tr_diplomacy_finish", "tr_politics_finish"], h.Traditions);
        Assert.Equal(["ap_lord_of_war"], h.Perks);
        Assert.Equal(["diplo_stance_condescending_authority_advanced", "unrestricted_wars"], h.Policies);
        Assert.Equal(["diplomatic_grants"], h.Edicts);
        Assert.Equal(["r_ancient_sword"], h.Relics);
        Assert.Equal([new TimedModifier("galactic_market_founder", 1), new TimedModifier("FW_x", 1.15)], h.Timed);
    }

    [Fact]
    public void Reads_community_with_passed_types_and_owned_megastructures()
    {
        var (_, _, community, megas) = Scan();
        Assert.Equal([0, 1, 7], community!.Members);
        Assert.Equal([0, 1], community.Council);
        Assert.Equal(["resolution_galactic_focus_x", "resolution_mutualdefense_renegade_containment"], community.PassedResolutions);
        Assert.Equal(["interstellar_assembly_4", "interstellar_assembly_restored"], megas[0]);
        Assert.Equal(["dyson_sphere_5"], megas[7]);
        Assert.False(megas.ContainsKey(unchecked((int)4294967295)));
    }
}
```

- [ ] **Step 3: Implement**
- **Return type:** add `GamestateScanner.ScanAll(byte[])` returning `(Players, Countries, Community, Megastructures)`. Keep `Scan` as a wrapper that returns only `(Players, Countries)`, so existing tests and callers stay valid.
- **`SaveReader.Read`:** use `ScanAll` and pass the community and megastructures into the snapshot.
- **Country blocks:** add `government`, `traditions`, `ascension_perks`, `active_policies`, `edicts`, `timed_modifier` and `relics` to `CountryBlocks`, then build `CountryHoldings` from the parsed blocks:
  - civics: `government.GetBlock("civics").StringItems`;
  - origin and authority: `government.GetString("origin")` and `GetString("authority")`;
  - traditions, perks and relics: `.StringItems`;
  - policies: from `active_policies.Items.OfType<PdxBlock>()`, take `GetString("selected")`;
  - edicts: from `edicts.Items.OfType<PdxBlock>()`, take `GetString("edict")`;
  - timed: from `timed_modifier.GetBlock("items").Items.OfType<PdxBlock>()`, take `modifier`, with `multiplier` defaulting to 1 and parsed with the invariant culture.
- **Top level:**
  - `resolution`: iterate entries `id={…}` and read `type` with the byte reader. Read only the fields: skip nested blocks such as `supporters`, the same way `ScanCountry` does. Build a map from id to type.
  - `galactic_community`: parse it with `ParadoxScriptParser` (it is small). Take `members`, `council` and `passed` as integers from `StringItems`.
  - `megastructures`: iterate entries, read the `type` and `owner` fields, and keep owners that parse as int and are not 4294967295. Owners that don't fit in an int are also dropped; `int.TryParse` fails on 4294967295.
  - The passed resolution types are the `passed` ids mapped through the resolution map. Unknown ids are dropped, and the order of `passed` is kept.
  - `resolution` may appear before or after `galactic_community`, so combine the two after the scan finishes.
- [ ] **Step 4: Run the tests and the full suite.** All should PASS.
- [ ] **Step 5: Commit.** Message: `"feat: scanner reads country holdings, the Galactic Community and owned megastructures"`.

---

### Task 2: ModifierCatalog

**Files:**
- Create: `FazStellarisModmanager.Core/Diplomacy/ModifierCatalog.cs`
- Test: `FazStellarisModmanager.Tests/ModifierCatalogTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Diplomacy;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ModifierCatalogTests
{
    [Fact]
    public void Reads_each_category_container_options_and_variables()
    {
        using var t = new TempDir();
        t.Write("game/common/scripted_variables/00_vars.txt", "@global_dw = 0.4");
        t.Write("game/common/traditions/00_t.txt", """
            @local_dw = 0.2
            tr_politics_finish = { modifier = { diplo_weight_mult = @local_dw } possible = { diplo_weight_mult = 9 } }
            tr_nothing = { modifier = { unity_produces_mult = 0.1 } }
            """);
        t.Write("game/common/governments/civics/00_c.txt", "civic_galactic_sovereign = { modifier = { diplo_weight_mult = @global_dw } }");
        t.Write("game/common/policies/00_p.txt", """
            diplomatic_stance = {
                option = { name = "diplo_stance_condescending_authority_advanced" modifier = { diplo_weight_mult = 0.5 } }
                option = { name = "diplo_stance_cooperative" modifier = { diplo_weight_mult = 0.1 } }
            }
            """);
        t.Write("game/common/static_modifiers/00_s.txt", "council_member = { diplo_weight_mult = 0.2 }  galactic_market_founder = { diplo_weight_economy_mult = 0.15 }");
        t.Write("game/common/megastructures/00_m.txt", "interstellar_assembly_4 = { country_modifier = { diplo_weight_mult = 0.4 } }");
        t.Write("game/common/resolutions/00_r.txt", "resolution_mutualdefense_renegade_containment = { modifier = { diplo_weight_naval_mult = 1 } }");
        t.Write("game/common/relics/00_r.txt", "r_ancient_sword = { passive_modifier = { diplo_weight_mult = 0.1 } }");
        using var game = ContentSource.FromPath("game", Path.Combine(t.Path, "game"), isBaseGame: true);

        var c = ModifierCatalog.Load([game]);
        Assert.Equal(0.2, c.Get(DiploSource.Tradition, "tr_politics_finish")!.Overall, 6);
        Assert.Null(c.Get(DiploSource.Tradition, "tr_nothing"));
        Assert.Equal(0.4, c.Get(DiploSource.Civic, "civic_galactic_sovereign")!.Overall, 6);
        Assert.Equal(0.5, c.Get(DiploSource.Policy, "diplo_stance_condescending_authority_advanced")!.Overall, 6);
        Assert.Equal(0.2, c.Get(DiploSource.StaticModifier, "council_member")!.Overall, 6);
        Assert.Equal(0.15, c.Get(DiploSource.StaticModifier, "galactic_market_founder")!.Economy, 6);
        Assert.Equal(0.4, c.Get(DiploSource.Megastructure, "interstellar_assembly_4")!.Overall, 6);
        Assert.Equal(1, c.Get(DiploSource.Resolution, "resolution_mutualdefense_renegade_containment")!.Naval, 6);
        Assert.Equal(0.1, c.Get(DiploSource.Relic, "r_ancient_sword")!.Overall, 6);
    }

    [Fact]
    public void Later_definition_wins_even_when_it_removes_the_bonus()
    {
        using var t = new TempDir();
        t.Write("game/common/edicts/00_e.txt", "diplomatic_grants = { modifier = { diplo_weight_mult = 0.1 } }  other = { modifier = { diplo_weight_mult = 0.3 } }");
        t.Write("mod/common/edicts/zz_e.txt", "diplomatic_grants = { modifier = { diplo_weight_mult = 0.25 } }  other = { modifier = { } }");
        using var game = ContentSource.FromPath("game", Path.Combine(t.Path, "game"), isBaseGame: true);
        using var mod = ContentSource.FromPath("mod", Path.Combine(t.Path, "mod"));
        var c = ModifierCatalog.Load([game, mod]);
        Assert.Equal(0.25, c.Get(DiploSource.Edict, "diplomatic_grants")!.Overall, 6);
        Assert.Null(c.Get(DiploSource.Edict, "other"));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**

- [ ] **Step 3: Implement**

```csharp
using System.Globalization;
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Diplomacy;

/// <summary>Diplomatic weight modifiers from one source, as fractions (0.25 = +25%).</summary>
public sealed record DiploMods(double Overall, double Naval, double Economy, double Tech, double Pops)
{
    public static DiploMods Zero { get; } = new(0, 0, 0, 0, 0);
    public bool IsZero => Overall == 0 && Naval == 0 && Economy == 0 && Tech == 0 && Pops == 0;
    public DiploMods Scale(double m) => new(Overall * m, Naval * m, Economy * m, Tech * m, Pops * m);
    public static DiploMods operator +(DiploMods a, DiploMods b) =>
        new(a.Overall + b.Overall, a.Naval + b.Naval, a.Economy + b.Economy, a.Tech + b.Tech, a.Pops + b.Pops);
}

public enum DiploSource { Tradition, Perk, Civic, Authority, Policy, Edict, Tech, Relic, StaticModifier, Megastructure, Resolution }

/// <summary>
/// diplo_weight_* modifiers per definition (tradition, perk, civic, …), read from the game and mods in load order
/// (file name order, later definitions win). Only modifier containers count, never triggers or weights.
/// </summary>
public sealed class ModifierCatalog
{
    sealed record Spec(DiploSource Source, string Folder, string[]? Containers);

    static readonly Spec[] Specs =
    [
        new(DiploSource.Tradition, "common/traditions", ["modifier"]),
        new(DiploSource.Perk, "common/ascension_perks", ["modifier"]),
        new(DiploSource.Civic, "common/governments/civics", ["modifier"]),
        new(DiploSource.Authority, "common/governments/authorities", ["country_modifier", "modifier"]),
        new(DiploSource.Policy, "common/policies", null),
        new(DiploSource.Edict, "common/edicts", ["modifier"]),
        new(DiploSource.Tech, "common/technology", ["modifier"]),
        new(DiploSource.Relic, "common/relics", ["passive_modifier", "modifier"]),
        new(DiploSource.StaticModifier, "common/static_modifiers", null),
        new(DiploSource.Megastructure, "common/megastructures", ["country_modifier"]),
        new(DiploSource.Resolution, "common/resolutions", ["modifier"]),
    ];

    readonly Dictionary<DiploSource, Dictionary<string, DiploMods>> _maps = [];

    public static ModifierCatalog Empty { get; } = new();

    /// <summary>The modifiers of a definition, or null when it has none (or doesn't exist).</summary>
    public DiploMods? Get(DiploSource source, string key) =>
        _maps.TryGetValue(source, out var map) && map.TryGetValue(key, out var mods) ? mods : null;

    /// <summary>Sources in load order (game first). Unreadable files are skipped.</summary>
    public static ModifierCatalog Load(IReadOnlyList<ContentSource> sources)
    {
        var catalog = new ModifierCatalog();
        var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, rel) in Ordered(sources, "common/scripted_variables"))
            if (TryParse(source, rel, out var vars, out _))
                foreach (var e in vars.Entries.Where(e => e.Key.StartsWith('@') && e.Value is string))
                    globals[e.Key] = (string)e.Value;

        foreach (var spec in Specs)
        {
            var map = new Dictionary<string, DiploMods>(StringComparer.OrdinalIgnoreCase);
            foreach (var (source, rel) in Ordered(sources, spec.Folder))
            {
                if (!TryParse(source, rel, out var file, out var locals)) continue;
                foreach (var e in file.Entries)
                {
                    if (e.Value is not PdxBlock block || e.Key.StartsWith('@')) continue;
                    if (spec.Source == DiploSource.Policy)
                    {
                        foreach (var option in block.Entries.Where(o => o.Key == "option").Select(o => o.Value).OfType<PdxBlock>())
                            if (option.GetString("name") is { } name) Set(map, name, Read(option.GetBlock("modifier"), locals, globals));
                    }
                    else if (spec.Containers is null)
                    {
                        Set(map, e.Key, Read(block, locals, globals));
                    }
                    else
                    {
                        var sum = DiploMods.Zero;
                        foreach (var c in spec.Containers) sum += Read(block.GetBlock(c), locals, globals);
                        Set(map, e.Key, sum);
                    }
                }
            }
            catalog._maps[spec.Source] = map;
        }
        return catalog;
    }

    // A later definition replaces an earlier one, including when it has no diplomatic weight bonus.
    static void Set(Dictionary<string, DiploMods> map, string key, DiploMods mods)
    {
        if (mods.IsZero) map.Remove(key);
        else map[key] = mods;
    }

    static DiploMods Read(PdxBlock? block, IReadOnlyDictionary<string, string> locals, IReadOnlyDictionary<string, string> globals)
    {
        if (block is null) return DiploMods.Zero;
        double overall = 0, naval = 0, economy = 0, tech = 0, pops = 0;
        foreach (var e in block.Entries)
        {
            if (e.Value is not string raw) continue;
            var value = raw.StartsWith('@') ? locals.GetValueOrDefault(raw) ?? globals.GetValueOrDefault(raw) : raw;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
            switch (e.Key.ToLowerInvariant())
            {
                case "diplo_weight_mult": overall += v; break;
                case "diplo_weight_naval_mult": naval += v; break;
                case "diplo_weight_economy_mult": economy += v; break;
                case "diplo_weight_technology_mult": tech += v; break;
                case "diplo_weight_pops_mult": pops += v; break;
            }
        }
        return new DiploMods(overall, naval, economy, tech, pops);
    }

    static IEnumerable<(ContentSource Source, string Rel)> Ordered(IReadOnlyList<ContentSource> sources, string folder) =>
        sources.SelectMany((s, i) => s.Files(folder, ".txt").Select(rel => (Source: s, Index: i, Rel: rel)))
            .OrderBy(f => Path.GetFileName(f.Rel), StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Index)
            .Select(f => (f.Source, f.Rel));

    static bool TryParse(ContentSource source, string rel, out PdxBlock block, out Dictionary<string, string> locals)
    {
        locals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            block = ParadoxScriptParser.Parse(source.ReadText(rel));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            block = new PdxBlock();
            return false;
        }
        foreach (var e in block.Entries.Where(e => e.Key.StartsWith('@') && e.Value is string)) locals[e.Key] = (string)e.Value;
        return true;
    }
}
```

**Note:** check `PdxBlock`'s real API. It has a public parameterless constructor according to the parser source. If it doesn't, adapt this code.

- [ ] **Step 4: Run the tests and the full suite.** All should PASS.
- [ ] **Step 5: Commit.** Message: `"feat: modifier catalog for diplomatic weight bonuses"`.

---

### Task 3: DiploCalculator and DiploBreakdown

**Files:**
- Create: `FazStellarisModmanager.Core/Diplomacy/DiploCalculator.cs`
- Modify: `GameSnapshot`. Add the `Diplo` parameter if Task 1 did not.
- Test: `FazStellarisModmanager.Tests/DiploCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests.** The fixture rebuilds the user's 2387.09.17 tooltip.

```csharp
using FazStellarisModmanager.Core.Diplomacy;
using FazStellarisModmanager.Core.Saves;

namespace FazStellarisModmanager.Tests;

public class DiploCalculatorTests
{
    sealed class FakeCatalog
    {
        public readonly Dictionary<(DiploSource, string), DiploMods> Map = [];
        public FakeCatalog Add(DiploSource s, string k, DiploMods m) { Map[(s, k)] = m; return this; }
    }

    static DiploMods O(double v) => new(v, 0, 0, 0, 0);

    static SaveCountry Me(CountryHoldings h) =>
        new(0, "default", "Me", new Dictionary<string, string>(), null, null, 1, 0, 644875181.10937, 2479558, 1761010, 0, 0, 399579, null, [], ["tech_xeno_diplomacy"], h);

    static GameSnapshot Snap(SaveCountry c, GalacticCommunity? gc = null, Dictionary<int, IReadOnlyList<string>>? megas = null) =>
        new("S", "2387.09.17", "v", "x.sav", DateTime.UtcNow, [new SavePlayer("Me", 0)], [c], gc, megas);

    static Func<DiploSource, string, DiploMods?> Lookup(FakeCatalog c) => (s, k) => c.Map.GetValueOrDefault((s, k));

    [Fact]
    public void Rebuilds_the_tooltip_base_parts_and_overall_bonuses()
    {
        var catalog = new FakeCatalog()
            .Add(DiploSource.Perk, "ap_galactic_force_projection", new DiploMods(0, 0.10, 0, 0, 0))
            .Add(DiploSource.Perk, "ap_lord_of_war", new DiploMods(0, 0.25, 0, 0, 0))
            .Add(DiploSource.Resolution, "resolution_mutualdefense_renegade_containment", new DiploMods(0, 1.0, 0, 0, 0))
            .Add(DiploSource.Resolution, "resolution_rulesofwar_guardian_angels", new DiploMods(0, -0.20, 0, 0, 0))
            .Add(DiploSource.StaticModifier, "galactic_market_founder", new DiploMods(0, 0, 0.15, 0, 0))
            .Add(DiploSource.Tradition, "tr_gigaengineering_mega_monuments", new DiploMods(0, 0, 0.20, 0.20, 0))
            .Add(DiploSource.Resolution, "resolution_commerce_underdeveloped_system_utilization", new DiploMods(0, 0, 0.60, 0, 0))
            .Add(DiploSource.Resolution, "resolution_industry_environmental_ordinance_waivers", new DiploMods(0, 0, 0.80, 0, 0))
            .Add(DiploSource.Resolution, "resolution_customs_treaty", new DiploMods(0, 0, 0.25, 0, 0))
            .Add(DiploSource.Resolution, "resolution_emperor_imperial_bank", new DiploMods(0, 0, 0.40, 0, 0))
            .Add(DiploSource.Resolution, "resolution_galacticstudies_astral_studies_network", new DiploMods(0, 0, 0, 0.40, 0))
            .Add(DiploSource.Civic, "civic_galactic_sovereign", O(0.40))
            .Add(DiploSource.Tech, "tech_xeno_diplomacy", O(0.10))
            .Add(DiploSource.Megastructure, "interstellar_assembly_4", O(0.40))
            .Add(DiploSource.StaticModifier, "council_member", O(0.20))
            .Add(DiploSource.Relic, "r_ancient_sword", O(0.10))
            .Add(DiploSource.Relic, "r_other", O(0.05));
        var holdings = CountryHoldings.Empty with
        {
            Civics = ["civic_galactic_sovereign"],
            Perks = ["ap_galactic_force_projection", "ap_lord_of_war"],
            Traditions = ["tr_gigaengineering_mega_monuments"],
            Relics = ["r_ancient_sword", "r_other"],
            Timed = [new TimedModifier("galactic_market_founder", 1)],
        };
        var gc = new GalacticCommunity([0, 5], [0], [
            "resolution_mutualdefense_renegade_containment", "resolution_rulesofwar_guardian_angels",
            "resolution_commerce_underdeveloped_system_utilization", "resolution_industry_environmental_ordinance_waivers",
            "resolution_customs_treaty", "resolution_emperor_imperial_bank", "resolution_galacticstudies_astral_studies_network"]);
        var me = Me(holdings);
        var b = DiploCalculator.Compute(me, Snap(me, gc, new() { [0] = ["interstellar_assembly_4", "interstellar_assembly_4"] }),
            Lookup(catalog), DiploDefines.Vanilla, (s, k) => k);

        Assert.Equal(16121879.5, b.Fleet.Base, 0);
        Assert.Equal(2.15, b.Fleet.Multiplier, 6);
        Assert.Equal(34662042, b.Fleet.Total, 0);
        Assert.Equal(371933.7, b.Economy.Base, 0);
        Assert.Equal(3.40, b.Economy.Multiplier, 6);
        Assert.Equal(1.60, b.Tech.Multiplier, 6);
        // overall: sovereign .4 + xeno diplomacy .1 + 2 assemblies .8 + council .2 + one relic line .15 = 1.65
        Assert.Equal(2.65, b.OverallMultiplier, 6);
        Assert.Equal(2, b.Overall.Count(l => l.Name == "interstellar_assembly_4"));
        Assert.Single(b.Overall, l => l.Name == "From Relics");
        Assert.True(b.Approximate);
        Assert.Equal(b.Real, b.InGame);
    }

    [Fact]
    public void Resolutions_and_council_only_for_members_and_negative_total_shows_zero_in_game()
    {
        var catalog = new FakeCatalog()
            .Add(DiploSource.Resolution, "res_a", O(0.5))
            .Add(DiploSource.StaticModifier, "council_member", O(0.2))
            .Add(DiploSource.Civic, "civic_bad", new DiploMods(0, -3, 0, 0, 0));
        var me = Me(CountryHoldings.Empty with { Civics = ["civic_bad"] });
        var outsider = new GalacticCommunity([5], [5], ["res_a"]);
        var b = DiploCalculator.Compute(me, Snap(me, outsider), Lookup(catalog), DiploDefines.Vanilla, (s, k) => k);
        Assert.Empty(b.Overall);
        Assert.True(b.Real < 0);
        Assert.Equal(0, b.InGame);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**

- [ ] **Step 3: Implement**

```csharp
using FazStellarisModmanager.Core.Saves;

namespace FazStellarisModmanager.Core.Diplomacy;

/// <summary>One tooltip line: a source and its bonus as a fraction (0.25 = +25%).</summary>
public sealed record DiploBonus(string Name, double Percent);

/// <summary>A base part: Input × Factor = Base, then × (1 + Σ bonuses) = Total.</summary>
public sealed record DiploPart(double Input, double Factor, IReadOnlyList<DiploBonus> Bonuses)
{
    public double Base => Input * Factor;
    public double Multiplier => 1 + Bonuses.Sum(b => b.Percent);
    public double Total => Base * Multiplier;
}

/// <summary>Diplomatic weight laid out like the game's tooltip. Pops use the middle of the happiness range.</summary>
public sealed record DiploBreakdown(DiploPart Fleet, DiploPart Pops, DiploPart Economy, DiploPart Tech, IReadOnlyList<DiploBonus> Overall, bool Approximate)
{
    public const string NotCalculated = "Not calculated: leader, councilor and delegate bonuses, faction bonuses and other scripted sources.";
    public double BaseTotal => Fleet.Total + Pops.Total + Economy.Total + Tech.Total;
    public double OverallMultiplier => 1 + Overall.Sum(b => b.Percent);
    public double Real => BaseTotal * OverallMultiplier;
    /// <summary>The game shows a negative diplomatic weight as 0.</summary>
    public double InGame => Math.Max(0, Real);
}

public static class DiploCalculator
{
    /// <param name="lookup">The catalog's Get (injectable for tests).</param>
    /// <param name="name">Display name for a source key (localisation, or the key).</param>
    public static DiploBreakdown Compute(SaveCountry c, GameSnapshot snapshot, Func<DiploSource, string, DiploMods?> lookup,
        DiploDefines defines, Func<DiploSource, string, string> name)
    {
        var lines = new List<(string Name, DiploMods Mods)>();
        void Add(DiploSource s, string? key, double multiplier = 1)
        {
            if (key is null || lookup(s, key) is not { } m || m.IsZero) return;
            lines.Add((name(s, key), m.Scale(multiplier)));
        }

        var h = c.Holdings ?? CountryHoldings.Empty;
        foreach (var k in h.Civics) Add(DiploSource.Civic, k);
        Add(DiploSource.Civic, h.Origin);
        Add(DiploSource.Authority, h.Authority);
        foreach (var k in h.Traditions) Add(DiploSource.Tradition, k);
        foreach (var k in h.Perks) Add(DiploSource.Perk, k);
        foreach (var k in h.Policies) Add(DiploSource.Policy, k);
        foreach (var k in h.Edicts) Add(DiploSource.Edict, k);
        foreach (var k in c.Techs) Add(DiploSource.Tech, k);
        foreach (var t in h.Timed) Add(DiploSource.StaticModifier, t.Key, t.Multiplier);

        var relics = h.Relics.Select(r => lookup(DiploSource.Relic, r)).OfType<DiploMods>().Aggregate(DiploMods.Zero, (a, b) => a + b);
        if (!relics.IsZero) lines.Add(("From Relics", relics));

        if (snapshot.Megastructures?.GetValueOrDefault(c.Id) is { } megas)
            foreach (var m in megas) Add(DiploSource.Megastructure, m);

        if (snapshot.Community is { } gc && gc.Members.Contains(c.Id))
        {
            foreach (var r in gc.PassedResolutions) Add(DiploSource.Resolution, r);
            if (gc.Council.Contains(c.Id)) Add(DiploSource.StaticModifier, "council_member");
        }

        List<DiploBonus> Pick(Func<DiploMods, double> part) =>
            lines.Where(l => part(l.Mods) != 0).Select(l => new DiploBonus(l.Name, part(l.Mods))).ToList();

        return new DiploBreakdown(
            new DiploPart(c.MilitaryPower, defines.Naval, Pick(m => m.Naval)),
            new DiploPart(c.Pops, defines.PopBase + defines.PopHappiness * 0.5, Pick(m => m.Pops)),
            new DiploPart(c.EconomyPower, defines.Economy, Pick(m => m.Economy)),
            new DiploPart(c.TechPower, defines.Technology, Pick(m => m.Tech)),
            Pick(m => m.Overall),
            Approximate: true);
    }
}
```

**Note:** in the first test, `Fleet.Total` expects about 34.66M at a precision of −3. Check the expected numbers when running and fix the test arithmetic if needed. The value is 644,875,181.10937 × 0.025 × 2.15 = 34,662,041.98.

- [ ] **Step 4: Run the tests and the full suite.** All should PASS.
- [ ] **Step 5: Commit.** Message: `"feat: diplomatic weight calculator with tooltip-style breakdown"`.

---

### Task 4: Enrich snapshots and wire up the app

**Files:**
- Modify: `FazStellarisModmanager.Core/Saves/LiveGameService.cs`. Add an optional constructor parameter `Func<GameSnapshot, GameSnapshot>? enrich = null`, placed after `stableDelay`.
- Modify: `FazStellarisModmanager.Core/Saves/LiveFilter.cs`
- Create: `FazStellarisModmanager.Core/Diplomacy/DiploContext.cs`
- Modify: `FazStellarisModmanager/AppServices.cs`
- Tests: add to `LiveGameServiceTests` and `LiveFilterTests`.

- [ ] **Step 1: LiveGameService.** After `_read(path)` in `Task.Run`, if an `enrich` function is set, call it. If it throws a non-fatal exception, keep the un-enriched snapshot and set `Error` to `"Diplomatic weight could not be calculated: " + message`. Test: a fake enrich that adds a marker, for example a `Diplo` dictionary with one entry, shows up in `Current`.

- [ ] **Step 2: LiveFilter.**
  - The viewer keeps `Holdings`. Every other country gets `Holdings = null`.
  - The snapshot keeps only the viewer's entry in `Diplo`, or null when there is no viewer.
  - `Community` and `Megastructures` become null.
  - Test: these rules.

- [ ] **Step 3: DiploContext (Core).** It holds what the calculator needs for one mod list:

```csharp
using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Diplomacy;

/// <summary>Catalog, defines and display names for one mod list; Enrich computes breakdowns for the save's player countries.</summary>
public sealed class DiploContext(ModifierCatalog catalog, DiploDefines defines, Localisation? names)
{
    public static DiploContext Load(IReadOnlyList<ContentSource> sources)
    {
        var warnings = new List<string>();
        Localisation? loc = null;
        try { loc = Localisation.Load(sources, warnings); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        return new DiploContext(ModifierCatalog.Load(sources), DiploDefines.Load(sources), loc);
    }

    public GameSnapshot Enrich(GameSnapshot s)
    {
        var map = new Dictionary<int, DiploBreakdown>();
        foreach (var p in s.Players)
            if (s.Countries.FirstOrDefault(c => c.Id == p.CountryId) is { } c)
                map[c.Id] = DiploCalculator.Compute(c, s, catalog.Get, defines, Name);
        return s with { Diplo = map };
    }

    string Name(DiploSource source, string key) =>
        names?.Get(key) ?? names?.Get("modifier_" + key) ?? key.Replace('_', ' ');
}
```

Check `Localisation.Load`'s real signature, and what `Get` returns for missing keys, then adapt the code.

- [ ] **Step 4: App wiring.** In `AppServices`, the `LiveGameService` factory passes `enrich`:
  - It builds a `DiploContext` lazily: the game directory source, plus the content of each mod in `manager.ImportCurrent("live").Mods`, looked up through `manager.Library` by `DescriptorRel` (as the Live page does now).
  - It caches the context until the list of content paths changes, and disposes its sources after loading.
  - It calls `Enrich`.
  - If loading throws, it rethrows; the service catches the exception and reports it.
  - If `manager.Library` is empty, call `manager.RefreshLibrary()` first; this runs on the read's thread-pool thread.

  Put this logic in a small app-side class, `FazStellarisModmanager/Services/DiploEnricher.cs`, with a method `GameSnapshot Enrich(GameSnapshot s)`.

- [ ] **Step 5: Build the app and run the full suite.** Expect 0 errors and all tests passing.
- [ ] **Step 6: Commit.** Message: `"feat: live snapshots carry each player's diplomatic weight breakdown"`.

---

### Task 5: Sidebar breakdown

**Files:** `FazStellarisModmanager/Pages/LiveGamePage.razor` and `wwwroot/css/site.css`.

- [ ] **Step 1: Replace the diplomatic weight row and its `<details>`.** Remove the old `CachedDiploWeight` row, the `Estimate` property, `DiploDefines` loading (`LoadDefinesAsync`) and the `defines` field. Instead, use `var dw = Feed.Current?.Diplo?.GetValueOrDefault(me.Id)`, and show:
  - **Row:** "Diplomatic weight", with `@(dw.Approximate ? "≈ " : "")@dw.Real.ToString("N0")`. When `dw.Real < 0`, add a second line: `<span class="ovf">In game: 0 (negative weight is shown as 0)</span>`. When `dw` is null, show "—".
  - **`<details class="ldiplo">`** with summary "Breakdown". The contents mirror the game's tooltip:
    - **Total line:** "Total: N0", then each overall bonus line `Name +X%` (green when positive, red when negative).
    - **Base:** "Base: N0".
    - **For each part (Fleet power, Pops, Economy, Technology):**
      - a header `Name: Total`;
      - an indented `From …: Base` with `(Input × Factor)` in muted text;
      - indented bonus lines.
    - **Muted notes:**
      - `DiploBreakdown.NotCalculated`;
      - "Fleet power as stored in the save."
- **Formatting:** percentages are `(Percent * 100).ToString("+0.#;-0.#")` followed by "%".
- **CSS:** add `.ldiplo .lines { padding-left: .8rem }`, `.pos-bonus { color: #6fcf97 }` and `.neg-bonus { color: #ff8a80 }`.
- [ ] **Step 2: Build the app and run the full suite.**
- [ ] **Step 3: Commit.** Message: `"feat: Live Game sidebar shows the calculated diplomatic weight breakdown"`.

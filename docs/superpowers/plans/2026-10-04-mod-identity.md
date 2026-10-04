# Mod Identity Matching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The diff and "Match host" treat a Workshop mod and a local copy of it (same Workshop id), and local mods with the same name or identical files, as the same mod.

**Architecture:** `ModMatcher` (new, in `Core/Diff`) pairs mods by four rules in order: key, Workshop id, normalised name (only mods without a Workshop id), then file fingerprint. `ModDiffer` uses it for mods (DLCs pair by key only) and records how each pair was made. `MatchPlan` applies my paired descriptor. Keys, saved lists and the protocol are unchanged.

**Tech Stack:** .NET 10, C#, xUnit, Blazor (two small UI tags).

**Spec:** `docs/superpowers/specs/2026-10-04-mod-identity-design.md`

**Conventions for every task:**
- **Build and test** with `-c Release --artifacts-path <scratch>/art`; a running copy of the app may lock the bin folders. Never kill FazStellarisModmanager.exe.
- **Escapes:** never write `\n`, `\t` or `\u` inside C# string literals. Use `(char)10` or raw string literals.
- **Branch:** `feature/mod-identity`. Before each commit, check `git branch --show-current`; if it has changed, stop and report.
- **Commits:** end every commit message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

## File structure

| File | Responsibility |
|---|---|
| Create `FazStellarisModmanager.Core/Diff/ModMatcher.cs` | `MatchKind`, `ModIdentity`, `ModPair`, `ModMatcher` (Workshop id, name normalisation, fingerprint, pairing) |
| Modify `FazStellarisModmanager.Core/Diff/ModDiffer.cs` | `UnitDiff` gains `MineKey`, `Match`, `LocalCopyOfWorkshop`, `MineName`; `DiffUnits` pairs through `ModMatcher` |
| Modify `FazStellarisModmanager.Core/Session/MatchPlan.cs` | partners from the diff or the library; Workshop install by Workshop id; update vs local by my key |
| Modify `FazStellarisModmanager/Components/DiffView.razor` | "matched by …" and "local copy of Workshop …" tags |
| Modify `FazStellarisModmanager/Pages/SessionPage.razor` | Workshop id via `ModMatcher.WorkshopIdOf` |
| Modify `FazStellarisModmanager/Pages/Mods.razor` | "local copy of Workshop …" library badge |
| Tests: create `ModMatcherTests.cs`; add to `ModDifferTests.cs` and `MatchPlanTests.cs` | |

---

### Task 1: `ModMatcher`

**Files:**
- Create: `FazStellarisModmanager.Core/Diff/ModMatcher.cs`
- Test: `FazStellarisModmanager.Tests/ModMatcherTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Tests;

public class ModMatcherTests
{
    static ModIdentity Id(string key, string? workshop = null, string name = "", string? fingerprint = null) =>
        new(key, workshop, ModMatcher.NormalizeName(name), fingerprint);

    static List<(string Target, string Mine, MatchKind Kind)> Pair(ModIdentity[] targets, ModIdentity[] mine, MatchKind[]? rules = null) =>
        ModMatcher.Pair(targets, mine, x => x, x => x, rules).Select(p => (p.Target.Key, p.Mine.Key, p.Kind)).ToList();

    [Fact]
    public void Rules_apply_in_order_and_each_mod_pairs_once()
    {
        var targets = new[]
        {
            Id("ugc:1", "1", "One"),
            Id("ugc:2", "2", "Two"),
            Id("local:a.mod", null, "(Coll) Ethics  Fix"),
            Id("local:b.mod", null, "B", "FP"),
            Id("ugc:9", "9", "Nine"),
        };
        var mine = new[]
        {
            Id("ugc:1", "1", "One"),
            Id("local:x.mod", "2", "(Coll) Two"),
            Id("local:y.mod", "2", "Two again"),
            Id("local:e.mod", null, "ethics fix"),
            Id("local:renamed.mod", null, "Other", "FP"),
        };

        Assert.Equal(
            [("ugc:1", "ugc:1", MatchKind.Key), ("ugc:2", "local:x.mod", MatchKind.WorkshopId),
             ("local:a.mod", "local:e.mod", MatchKind.Name), ("local:b.mod", "local:renamed.mod", MatchKind.Files)],
            Pair(targets, mine));
    }

    [Fact]
    public void The_key_rule_wins_and_names_only_pair_mods_without_a_workshop_id()
    {
        Assert.Equal([("ugc:1", "ugc:1", MatchKind.Key)],
            Pair([Id("ugc:1", "1", "A")], [Id("local:copy.mod", "1", "A"), Id("ugc:1", "1", "A")]));
        Assert.Empty(Pair([Id("local:a.mod", "5", "Same")], [Id("local:b.mod", null, "Same")]));
        Assert.Empty(Pair([Id("local:a.mod", null, "Same")], [Id("local:b.mod", null, "Same")], [MatchKind.Key]));
    }

    [Theory]
    [InlineData("(Fazverse 4.4 with Flamer) DarkSpace", "darkspace")]
    [InlineData("  Ethics   Fix ", "ethics fix")]
    [InlineData("(a) (b) Name", "(b) name")]
    [InlineData("", "")]
    public void Names_lose_one_leading_collection_prefix_case_and_extra_spaces(string name, string expected) =>
        Assert.Equal(expected, ModMatcher.NormalizeName(name));

    [Fact]
    public void Workshop_ids_come_from_ugc_keys_or_remote_file_ids()
    {
        Assert.Equal("123", ModMatcher.WorkshopIdOf("ugc:123", null));
        Assert.Equal("123", ModMatcher.WorkshopIdOf("ugc:123", "999"));
        Assert.Equal("456", ModMatcher.WorkshopIdOf("local:x.mod", " 456 "));
        Assert.Null(ModMatcher.WorkshopIdOf("local:x.mod", "0"));
        Assert.Null(ModMatcher.WorkshopIdOf("local:x.mod", "abc"));
        Assert.Null(ModMatcher.WorkshopIdOf("local:x.mod", null));
    }

    [Fact]
    public void Fingerprints_ignore_file_order_and_case()
    {
        var a = ModMatcher.Fingerprint([new ModFile("common/A.txt", "AA", 1), new ModFile("b.txt", "bb", 1)]);
        var b = ModMatcher.Fingerprint([new ModFile("b.txt", "BB", 2), new ModFile("common/a.txt", "aa", 1)]);
        var c = ModMatcher.Fingerprint([new ModFile("common/a.txt", "ab", 1), new ModFile("b.txt", "bb", 1)]);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Null(ModMatcher.Fingerprint([]));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.** Run `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art> --filter ModMatcherTests`. Expected: build errors, because `ModMatcher`, `ModIdentity` and `MatchKind` don't exist.

- [ ] **Step 3: Implement `Core/Diff/ModMatcher.cs`**

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Diff;

/// <summary>Why two mods were paired.</summary>
public enum MatchKind { Key, WorkshopId, Name, Files }

/// <summary>What makes two mods the same: key, Workshop id, normalised name and (when files are known) a fingerprint of all file hashes.</summary>
public sealed record ModIdentity(string Key, string? WorkshopId, string Name, string? Fingerprint)
{
    public static ModIdentity Of(ModSnapshot s) =>
        new(s.Key, ModMatcher.WorkshopIdOf(s.Key, s.RemoteId), ModMatcher.NormalizeName(s.Name), ModMatcher.Fingerprint(s.Files));

    public static ModIdentity Of(InstalledMod m) =>
        new(m.Key, ModMatcher.WorkshopIdOf(m.Key, m.RemoteId), ModMatcher.NormalizeName(m.Name), null);

    public static ModIdentity Of(ModListEntry e) =>
        new(e.Key, ModMatcher.WorkshopIdOf(e.Key, e.RemoteId), ModMatcher.NormalizeName(e.Name), null);
}

public sealed record ModPair<TTarget, TMine>(TTarget Target, TMine Mine, MatchKind Kind);

/// <summary>
/// Pairs target (host) mods with mine by identity, rule by rule: same key, same Workshop id, same normalised name (only when
/// neither side has a Workshop id), same file fingerprint. Each mod takes part in at most one pair; each rule only looks at
/// mods still unpaired; targets are taken in order and each pairs with the first unpaired mine mod with the same value.
/// </summary>
public static class ModMatcher
{
    public static readonly MatchKind[] AllRules = [MatchKind.Key, MatchKind.WorkshopId, MatchKind.Name, MatchKind.Files];

    static readonly Regex Prefix = new(@"^\s*\([^)]*\)\s*", RegexOptions.Compiled);
    static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>The Workshop id of a ugc:&lt;id&gt; key, else of a positive numeric remote_file_id, else null.</summary>
    public static string? WorkshopIdOf(string key, string? remoteId)
    {
        if (ModKeys.WorkshopId(key) is { } fromKey) return fromKey.ToString(CultureInfo.InvariantCulture);
        return ulong.TryParse(remoteId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>Lower-case, one leading "(…)" collection prefix removed, whitespace runs collapsed, trimmed.</summary>
    public static string NormalizeName(string? name) =>
        Spaces.Replace(Prefix.Replace(name ?? "", "", 1), " ").Trim().ToLowerInvariant();

    /// <summary>SHA-1 over the sorted "path|md5" lines (lower-case, / separators); null for an empty file list.</summary>
    public static string? Fingerprint(IReadOnlyCollection<ModFile> files)
    {
        if (files.Count == 0) return null;
        var lines = files
            .Select(f => f.Path.Replace((char)92, '/').ToLowerInvariant() + "|" + f.Md5.ToLowerInvariant())
            .Order(StringComparer.Ordinal);
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(string.Join((char)10, lines))));
    }

    public static List<ModPair<TTarget, TMine>> Pair<TTarget, TMine>(IReadOnlyList<TTarget> targets, IReadOnlyList<TMine> mine,
        Func<TTarget, ModIdentity> targetIdentity, Func<TMine, ModIdentity> mineIdentity, IReadOnlyList<MatchKind>? rules = null)
    {
        var t = targets.Select(targetIdentity).ToList();
        var m = mine.Select(mineIdentity).ToList();
        var partner = new int[t.Count];
        Array.Fill(partner, -1);
        var kind = new MatchKind[t.Count];
        var used = new bool[m.Count];

        foreach (var rule in rules ?? AllRules)
        {
            var available = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);
            for (var j = 0; j < m.Count; j++)
                if (!used[j] && Value(m[j], rule) is { } v)
                {
                    if (!available.TryGetValue(v, out var queue)) available[v] = queue = new Queue<int>();
                    queue.Enqueue(j);
                }
            for (var i = 0; i < t.Count; i++)
                if (partner[i] < 0 && Value(t[i], rule) is { } v && available.TryGetValue(v, out var queue) && queue.Count > 0)
                {
                    var j = queue.Dequeue();
                    (used[j], partner[i], kind[i]) = (true, j, rule);
                }
        }

        var pairs = new List<ModPair<TTarget, TMine>>();
        for (var i = 0; i < t.Count; i++)
            if (partner[i] >= 0) pairs.Add(new ModPair<TTarget, TMine>(targets[i], mine[partner[i]], kind[i]));
        return pairs;
    }

    static string? Value(ModIdentity id, MatchKind rule) => rule switch
    {
        MatchKind.Key => id.Key,
        MatchKind.WorkshopId => id.WorkshopId,
        MatchKind.Name => id.WorkshopId is null && id.Name.Length > 0 ? id.Name : null,
        _ => id.Fingerprint,
    };
}
```

- [ ] **Step 4: Run the tests and confirm they pass.** Use the same filter. Expected: all pass. Then run the whole suite.

- [ ] **Step 5: Commit**

```bash
git add FazStellarisModmanager.Core/Diff/ModMatcher.cs FazStellarisModmanager.Tests/ModMatcherTests.cs
git commit -m "Diff: pair mods by key, Workshop id, name or identical files"
```

---

### Task 2: `ModDiffer` uses the matcher

**Files:**
- Modify: `FazStellarisModmanager.Core/Diff/ModDiffer.cs`
- Test: add to `FazStellarisModmanager.Tests/ModDifferTests.cs`

- [ ] **Step 1: Write the failing tests** (add to `ModDifferTests`)

```csharp
    static ModSnapshot Mod(string key, int order, string name, string? remoteId, params (string Path, string Md5)[] files) =>
        new(key, name, $"mod/{key}.mod", remoteId, null, null, "", order, files.Select(f => new ModFile(f.Path, f.Md5, 1)).ToList());

    [Fact]
    public void A_local_copy_of_a_workshop_mod_is_the_same_mod()
    {
        var target = Machine("v4.4", Mod("ugc:5", 1, "Mod Five", "5", ("f", "1")));
        var mine = Machine("v4.4", Mod("local:8cde_1.mod", 1, "(Coll) Mod Five", "5", ("f", "1")));

        var r = ModDiffer.Diff(target, mine);

        var u = Assert.Single(r.Mods);
        Assert.Equal((UnitStatus.Ok, "local:8cde_1.mod", MatchKind.WorkshopId, true, "(Coll) Mod Five"),
            (u.Status, u.MineKey, u.Match, u.LocalCopyOfWorkshop, u.MineName));
        Assert.True(r.IsMatch);
    }

    [Fact]
    public void Prefixed_names_and_identical_files_pair_local_mods()
    {
        var target = Machine("v4.4", Mod("local:a.mod", 1, "Ethics Fix", null, ("e", "1")), Mod("local:b.mod", 2, "Sound", null, ("s", "1")));
        var mine = Machine("v4.4", Mod("local:c.mod", 1, "(Coll) Ethics Fix", null, ("e", "2")), Mod("local:d.mod", 2, "Totally different", null, ("s", "1")));

        var r = ModDiffer.Diff(target, mine);

        Assert.Equal(
            [("local:a.mod", UnitStatus.ContentMismatch, "local:c.mod", MatchKind.Name), ("local:b.mod", UnitStatus.Ok, "local:d.mod", MatchKind.Files)],
            r.Mods.Select(m => (m.Key, m.Status, m.MineKey, m.Match)));
        Assert.All(r.Mods, m => Assert.False(m.LocalCopyOfWorkshop));
    }

    [Fact]
    public void Load_order_is_compared_across_pairs()
    {
        var target = Machine("v4.4", Mod("ugc:1", 1, "One", "1", ("a", "1")), Mod("ugc:2", 2, "Two", "2", ("b", "1")));
        var mine = Machine("v4.4", Mod("local:two.mod", 1, "Two", "2", ("b", "1")), Mod("local:one.mod", 2, "One", "1", ("a", "1")));

        var r = ModDiffer.Diff(target, mine);

        Assert.Equal(1, r.Mods.Count(m => m.OutOfOrder));
        Assert.False(r.OrderMatches);
        Assert.All(r.Mods, m => Assert.Equal(UnitStatus.Ok, m.Status));
    }

    [Fact]
    public void Dlcs_pair_only_by_key()
    {
        var target = Machine("v4.4") with { Dlcs = [Unit("dlc:dlc001", 1, ("x", "crc32:1"))] };
        var mine = Machine("v4.4") with { Dlcs = [Unit("dlc:dlc002", 1, ("x", "crc32:1"))] };

        var r = ModDiffer.Diff(target, mine);

        Assert.Equal([UnitStatus.Missing, UnitStatus.Extra], r.Dlcs.Select(d => d.Status));
    }
```

- [ ] **Step 2: Run the tests and confirm they fail.** Expected: build errors, because `UnitDiff` has no `MineKey`, `Match`, `LocalCopyOfWorkshop` or `MineName`.

- [ ] **Step 3: Implement**

3a. Extend `UnitDiff`. Its last parameter is currently `FileDiff? Files);`. Replace it with:

```csharp
    FileDiff? Files,
    string? MineKey = null,
    MatchKind Match = MatchKind.Key,
    bool LocalCopyOfWorkshop = false,
    string? MineName = null);
```

Also add this sentence to the `UnitDiff` XML summary: "MineKey/MineName name my paired mod (or the Extra one); Match says how it was paired; LocalCopyOfWorkshop marks a pair where exactly one side is the Workshop item."

3b. In `ModDiffer.Diff`, change the two calls to `DiffUnits(target.Dlcs, mine.Dlcs, mods: false)` and `DiffUnits(target.Mods, mine.Mods, mods: true)`. Keep the existing comment about DLC order.

3c. Replace the whole `DiffUnits` method with the following, and add `using FazStellarisModmanager.Core.Library;` at the top:

```csharp
    // Mods pair by identity (key, Workshop id, name, files); DLCs by key only. Order is compared only over paired units,
    // so a missing mod does not flag every later one: the minimal set of pairs outside a longest increasing subsequence
    // of my positions (in target order) is flagged.
    static List<UnitDiff> DiffUnits(List<ModSnapshot> target, List<ModSnapshot> mine, bool mods)
    {
        var t = Index(target).Values.OrderBy(u => u.LoadOrder).ToList();
        var m = Index(mine).Values.OrderBy(u => u.LoadOrder).ToList();
        var pairs = ModMatcher.Pair(t, m, ModIdentity.Of, ModIdentity.Of, mods ? ModMatcher.AllRules : [MatchKind.Key]);
        var partner = pairs.ToDictionary(p => p.Target.Key, p => p, Cmp);
        var pairedMine = new HashSet<string>(pairs.Select(p => p.Mine.Key), Cmp);

        var outOfOrder = new HashSet<string>(Cmp);
        if (mods)
        {
            var rank = new Dictionary<string, int>(Cmp);
            foreach (var u in m.Where(u => pairedMine.Contains(u.Key))) rank[u.Key] = rank.Count;
            var shared = t.Where(u => partner.ContainsKey(u.Key)).ToList();
            var keep = LisIndices(shared.Select(u => rank[partner[u.Key].Mine.Key]).ToList());
            for (int i = 0; i < shared.Count; i++)
                if (!keep.Contains(i)) outOfOrder.Add(shared[i].Key);
        }

        var result = new List<UnitDiff>();
        foreach (var x in t)
        {
            if (!partner.TryGetValue(x.Key, out var p))
            {
                result.Add(new UnitDiff(x.Key, x.Name, x.RemoteId, UnitStatus.Missing, x.LoadOrder, null, false, x.Version, null, null));
                continue;
            }
            var y = p.Mine;
            var files = DiffFiles(x.Files, y.Files);
            result.Add(new UnitDiff(x.Key, x.Name, x.RemoteId,
                files.IsEmpty ? UnitStatus.Ok : UnitStatus.ContentMismatch,
                x.LoadOrder, y.LoadOrder, outOfOrder.Contains(x.Key), x.Version, y.Version,
                files.IsEmpty ? null : files,
                y.Key, p.Kind, IsLocalCopyOfWorkshop(x, y), y.Name));
        }
        foreach (var y in m.Where(u => !pairedMine.Contains(u.Key)))
            result.Add(new UnitDiff(y.Key, y.Name, y.RemoteId, UnitStatus.Extra, null, y.LoadOrder, false, null, y.Version, null,
                y.Key, MatchKind.Key, false, y.Name));
        return result;
    }

    // Same Workshop item on both sides, but exactly one side uses the ugc_<id>.mod Workshop descriptor.
    static bool IsLocalCopyOfWorkshop(ModSnapshot x, ModSnapshot y) =>
        (ModKeys.WorkshopId(x.Key) is null) != (ModKeys.WorkshopId(y.Key) is null)
        && ModMatcher.WorkshopIdOf(x.Key, x.RemoteId) is { } id
        && id == ModMatcher.WorkshopIdOf(y.Key, y.RemoteId);
```

- [ ] **Step 4: Run all tests.** Run `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art>`. Expected: the new tests pass.
  - Existing ModDiffer, SessionHost and SessionService tests may now pair units that used to be Missing and Extra, for example when test units share file hashes. If that happens, check that the new pairing is correct under the spec's rules. Adjust the test data (give the units distinct file hashes) only where the test meant "different mods", and report each change.

- [ ] **Step 5: Commit**

```bash
git add FazStellarisModmanager.Core/Diff/ModDiffer.cs FazStellarisModmanager.Tests/ModDifferTests.cs
git commit -m "Diff: compare mods by identity; mark local copies of Workshop mods"
```

---

### Task 3: `MatchPlan` and the UI tags

**Files:**
- Modify: `FazStellarisModmanager.Core/Session/MatchPlan.cs`, `FazStellarisModmanager/Components/DiffView.razor`, `FazStellarisModmanager/Pages/SessionPage.razor`, `FazStellarisModmanager/Pages/Mods.razor`
- Test: add to `FazStellarisModmanager.Tests/MatchPlanTests.cs`

- [ ] **Step 1: Write the failing tests** (add to `MatchPlanTests`)

```csharp
    static InstalledMod Inst(string key, string rel, string name, string? remoteId) =>
        new(key, name, rel, remoteId, null, null, "", key.StartsWith("ugc:") ? ModSource.Workshop : ModSource.Local, []);

    [Fact]
    public void Uses_my_copies_and_offers_workshop_installs_for_host_copies_with_a_workshop_id()
    {
        var host = new ModList("H",
        [
            new("ugc:5", "Five", "mod/ugc_5.mod", "5"),
            new("local:hostcopy.mod", "(H) Seven", "mod/hostcopy.mod", "7"),
            new("local:eth.mod", "(H) Ethics Fix", "mod/eth.mod", null),
            new("local:only.mod", "Only host", "mod/only.mod", null),
        ], []);
        var library = new[]
        {
            Inst("local:8cde_1.mod", "mod/8cde_1.mod", "(Mine) Five", "5"),
            Inst("local:mine_eth.mod", "mod/mine_eth.mod", "Ethics Fix", null),
        };

        var plan = MatchPlan.Create(host, Diff(), library, TestSnapshots.Machine("M"));

        Assert.Equal(["local:8cde_1.mod", "local:mine_eth.mod"], plan.ToApply.Mods.Select(m => m.Key));
        Assert.Equal(["mod/8cde_1.mod", "mod/mine_eth.mod"], plan.ToApply.Mods.Select(m => m.DescriptorRel));
        Assert.Equal(["local:hostcopy.mod"], plan.NeedsWorkshopInstall.Select(e => e.Key));
        Assert.Equal(["local:only.mod"], plan.NeedsManualInstall.Select(e => e.Key));
    }

    [Fact]
    public void Differences_need_a_workshop_update_only_when_my_mod_is_the_workshop_item()
    {
        var host = new ModList("H", [new("ugc:5", "Five", "mod/ugc_5.mod", "5"), new("ugc:6", "Six", "mod/ugc_6.mod", "6")], []);
        var library = new[] { Inst("local:copy.mod", "mod/copy.mod", "Five", "5"), Inst("ugc:6", "mod/ugc_6.mod", "Six", "6") };
        var diff = Diff(
            Changed("ugc:5") with { MineKey = "local:copy.mod", Match = MatchKind.WorkshopId, LocalCopyOfWorkshop = true },
            Changed("ugc:6") with { MineKey = "ugc:6" });

        var plan = MatchPlan.Create(host, diff, library, TestSnapshots.Machine("M"));

        Assert.Equal(["local:copy.mod", "ugc:6"], plan.ToApply.Mods.Select(m => m.Key));
        Assert.Equal(["ugc:6"], plan.NeedsWorkshopUpdate.Select(u => u.Key));
        Assert.Equal(["ugc:5"], plan.DiffersLocally.Select(u => u.Key));
    }
```

- [ ] **Step 2: Run the tests and confirm they fail.** Expected: assertion failures, because today's code pairs by key only.

- [ ] **Step 3: Implement `MatchPlan.Create`.** Replace the part from the start of the method up to and including the `foreach (var e in hostList.Mods) { … }` loop with:

```csharp
        var byKey = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in library) byKey.TryAdd(m.Key, m);

        // My partner for each host mod: first the pairing the diff made (enabled mods, using files too), then a pairing
        // against my whole library (installed but not enabled) by key, Workshop id or name.
        var hostEntries = hostList.Mods.DistinctBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToList();
        var partner = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in diff.Mods)
            if (u.Status is UnitStatus.Ok or UnitStatus.ContentMismatch && u.MineKey is { } mineKey && byKey.TryGetValue(mineKey, out var paired))
                partner.TryAdd(u.Key, paired);
        var taken = new HashSet<string>(partner.Values.Select(i => i.Key), StringComparer.OrdinalIgnoreCase);
        var unpaired = hostEntries.Where(e => !partner.ContainsKey(e.Key)).ToList();
        var free = library.Where(i => !taken.Contains(i.Key)).ToList();
        foreach (var p in ModMatcher.Pair(unpaired, free, ModIdentity.Of, ModIdentity.Of, [MatchKind.Key, MatchKind.WorkshopId, MatchKind.Name]))
            partner.TryAdd(p.Target.Key, p.Mine);

        var apply = new List<ModListEntry>();
        var workshop = new List<ModListEntry>();
        var manual = new List<ModListEntry>();
        foreach (var e in hostEntries)
        {
            if (partner.TryGetValue(e.Key, out var inst)) apply.Add(new ModListEntry(inst.Key, inst.Name, inst.DescriptorRel, inst.RemoteId));
            else if (ModMatcher.WorkshopIdOf(e.Key, e.RemoteId) is not null) workshop.Add(e);
            else manual.Add(e);
        }
```

Then in the `return new MatchPlan(…)`, change the two `changed.Where(...)` lines to use my key:

```csharp
            changed.Where(u => ModKeys.WorkshopId(u.MineKey ?? u.Key) is not null).ToList(),
            changed.Where(u => ModKeys.WorkshopId(u.MineKey ?? u.Key) is null).ToList(),
```

Add `using FazStellarisModmanager.Core.Diff;` if it is missing; it is already imported. In the XML summary, replace "Missing mods and content mismatches are reported, split into Workshop (fixable by sub-project 3) and local (manual)" with "Each host mod uses my paired mod (by key, Workshop id, name or files); unpaired host mods with a Workshop id need a Workshop install, others a manual one; content mismatches need a Workshop update when my mod is the Workshop item, otherwise local attention".

- [ ] **Step 4: SessionPage.razor.** In the `NeedsWorkshopInstall` list, replace `(Workshop id @ModKeys.WorkshopId(e.Key))` with `(Workshop id @ModMatcher.WorkshopIdOf(e.Key, e.RemoteId))`.

- [ ] **Step 5: DiffView.razor.** Directly after `<span class="name">@u.Name</span> <span class="badge">@u.Key</span>`, insert:

```razor
                            @if (u.Match != MatchKind.Key && u.MineKey is { } mineKey)
                            {
                                <span class="badge">matched by @MatchText(u.Match) with @mineKey</span>
                            }
                            @if (u.LocalCopyOfWorkshop)
                            {
                                <span class="badge local" title="One side uses the Steam Workshop item, the other a local copy of it">local copy of Workshop @(ModMatcher.WorkshopIdOf(u.Key, u.RemoteId) ?? ModMatcher.WorkshopIdOf(u.MineKey ?? "", null))</span>
                            }
```

and add to its `@code` block:

```csharp
    static string MatchText(MatchKind kind) => kind switch
    {
        MatchKind.WorkshopId => "Workshop id",
        MatchKind.Name => "name",
        MatchKind.Files => "identical files",
        _ => "key",
    };
```

- [ ] **Step 6: Mods.razor.** Directly after the library row's source badge, which is `<span class="badge @m.Source.ToString().ToLowerInvariant()">…</span>`, insert:

```razor
                    @if (m.Source == ModSource.Local && ModMatcher.WorkshopIdOf(m.Key, m.RemoteId) is { } workshopId)
                    {
                        <span class="badge local" title="This local mod is a copy of a Steam Workshop item">local copy of Workshop @workshopId</span>
                    }
```

- [ ] **Step 7: Build and test.** Run `dotnet build FazStellarisModmanager.sln -c Release --artifacts-path <art>` and `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art>`. Expected: 0 errors, no new warnings, all tests pass. If the Razor files lack `@using FazStellarisModmanager.Core.Diff`, add it; `_Imports.razor` already imports it.

- [ ] **Step 8: Commit**

```bash
git add FazStellarisModmanager.Core/Session/MatchPlan.cs FazStellarisModmanager.Tests/MatchPlanTests.cs FazStellarisModmanager/Components/DiffView.razor FazStellarisModmanager/Pages/SessionPage.razor FazStellarisModmanager/Pages/Mods.razor
git commit -m "Match host: use my paired copies; Workshop installs by Workshop id; identity tags in the diff and library"
```

---

### Task 4: Real-data check

- [ ] **Step 1:** In a scratch console project outside the repo that references the Core Release DLL, take the user's real snapshot the way the app does. Find the code path in `SessionService` / `SnapshotScanner` that builds `MachineSnapshot` from the current `dlc_load.json`. The game is at `D:\SteamLibrary\steamapps\common\Stellaris`, and the user folder is `Documents\Paradox Interactive\Stellaris`.
- [ ] **Step 2:** Build a synthetic host snapshot from it. Every mod whose Workshop id is known gets key `ugc:<id>`, descriptor `mod/ugc_<id>.mod` and name without the "(…)" prefix. Keep the same files.
- [ ] **Step 3:** Run `ModDiffer.Diff(host, mine)` and report:
  - pairs by kind;
  - Missing/Extra counts (expected: 0);
  - LocalCopyOfWorkshop count;
  - `IsMatch`.

  Then run `MatchPlan.Create` with a host list built the same way, plus the real library, and report the counts of ToApply, NeedsWorkshopInstall and NeedsManualInstall.
- [ ] **Step 4:** Report anything unexpected; don't change the repo in this task.

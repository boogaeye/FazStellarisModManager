# Workshop Install & Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Clients can subscribe to and download the Workshop mods the host uses (or update outdated ones) from a pop-up, and the app then applies the host's list. The pop-up opens only from a **Workshop mods** button or from **Match host**.

**Architecture:**
- **Core** (`Core/Workshop`) defines the contract `IWorkshopService` and builds `WorkshopNeeds` from a `MatchPlan`.
- **`SessionService`** gains `PlanAsync` (preview) and `InstallFromWorkshopAndMatchAsync` (download, then the shared match core).
- **New project `FazStellarisModmanager.Steam`** implements the contract with Facepunch.Steamworks 2.3.3, connecting to Steam as Stellaris (281990) only during a call.
- **The app** registers the service, blocks Launch while it is active, and shows `WorkshopPrompt.razor` on the Session page.

**Tech Stack:** .NET 10, C#, WPF + BlazorWebView, xUnit, Facepunch.Steamworks 2.3.3 (netstandard2.0, Win64, bundles `steam_api64.dll`).

**Spec:** `docs/superpowers/specs/2026-10-04-workshop-install-design.md`

**Facepunch API, verified by reflecting over the 2.3.3 DLL:**

| Type | Members |
|---|---|
| `Steamworks.SteamClient` | `static void Init(uint appid, bool asyncCallbacks = true)`, `static void Shutdown()`, `static bool IsValid` |
| `Steamworks.Ugc.Item` (struct) | `static Task<Item?> GetAsync(PublishedFileId id, int maxageseconds = 1800)`, `Task<bool> Subscribe()`, `Task<bool> DownloadAsync(Action<float> progress = null, int milisecondsUpdateDelay = 60, CancellationToken ct = default)`, `bool IsInstalled`, `bool IsDownloading`, `bool IsSubscribed`, `bool NeedsUpdate`, `string Directory`, `long SizeBytes`, `string Title` |

**Conventions for every task:**
- **Building:** build and test with `-c Release --artifacts-path <scratch>/art`. A running copy of the app may lock the bin folders. Never kill FazStellarisModmanager.exe.
- **Escapes:** never write `\n`, `\t` or `\u` inside C# string literals.
- **Razor:**
  - A bare `<text>` element with attributes inside `@if`/`@foreach` fails with RZ1023.
  - Loops inside code blocks are written without `@`.
- **Branch:** `feature/workshop-install`. Before each commit, check `git branch --show-current`; if it has changed, stop and report.
- **Commits:** end every commit message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

## File structure

| File | Responsibility |
|---|---|
| Create `FazStellarisModmanager.Core/Workshop/WorkshopContracts.cs` | `IWorkshopService`, `WorkshopItemState`, `WorkshopProgress`, `WorkshopItemInfo`, `WorkshopItemResult`, `WorkshopUnavailableException`, `WorkshopMatchResult` |
| Create `FazStellarisModmanager.Core/Workshop/WorkshopNeeds.cs` | `WorkshopNeedKind`, `WorkshopNeed`, `WorkshopNeeds.From(MatchPlan)` |
| Create `FazStellarisModmanager.Steam/FazStellarisModmanager.Steam.csproj` and `SteamWorkshopService.cs` | the Steam adapter |
| Modify `FazStellarisModmanager.sln` | add the Steam project |
| Modify `FazStellarisModmanager.Core/Session/SessionService.cs` | workshop parameter, `PlanAsync`, `InstallFromWorkshopAndMatchAsync`, shared `MatchCoreAsync` |
| Modify `FazStellarisModmanager.Core/ModManagerService.cs` | `LaunchBlockedReason` |
| Modify `FazStellarisModmanager/FazStellarisModmanager.csproj`, `AppServices.cs`, `_Imports.razor` | reference, registration, using |
| Create `FazStellarisModmanager/Components/WorkshopPrompt.razor` | pop-up |
| Modify `FazStellarisModmanager/Pages/SessionPage.razor`, `wwwroot/css/site.css` | buttons, pop-up hosting, styles |
| Tests: create `WorkshopNeedsTests.cs`, `TestUtil/FakeWorkshop.cs`; modify `TestUtil/FakeInstall.cs`, `SessionServiceTests.cs`, `ModManagerServiceTests.cs` | |

---

### Task 1: Contract, Steam project and live spike

**Files:**
- Create: `FazStellarisModmanager.Core/Workshop/WorkshopContracts.cs`, `FazStellarisModmanager.Steam/FazStellarisModmanager.Steam.csproj`, `FazStellarisModmanager.Steam/SteamWorkshopService.cs`
- Modify: `FazStellarisModmanager.sln`

- [ ] **Step 1: Create `Core/Workshop/WorkshopContracts.cs`**

```csharp
using FazStellarisModmanager.Core.Session;

namespace FazStellarisModmanager.Core.Workshop;

public enum WorkshopItemState { Waiting, Subscribing, Downloading, Installed, Failed, Cancelled }

/// <summary>Progress of one Workshop item. Fraction is 0..1 while downloading.</summary>
public sealed record WorkshopProgress(ulong Id, WorkshopItemState State, double Fraction, string? Message = null);

/// <summary>What Steam says about an item; null fields are unknown.</summary>
public sealed record WorkshopItemInfo(ulong Id, string? Title, long? SizeBytes);

public sealed record WorkshopItemResult(ulong Id, bool Success, string? InstallFolder, string? Error);

/// <summary>Result of "install from the Workshop, then match the host".</summary>
public sealed record WorkshopMatchResult(IReadOnlyList<WorkshopItemResult> Items, MatchPlan Plan);

/// <summary>Steam could not be reached (not running, not logged in, or the account doesn't own the game).</summary>
public sealed class WorkshopUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Steam Workshop access. Implementations connect to Steam only inside a call and disconnect before returning.</summary>
public interface IWorkshopService
{
    /// <summary>True while a call is connected to Steam; the game must not be launched then.</summary>
    bool IsActive { get; }

    /// <summary>Titles and sizes. Throws <see cref="WorkshopUnavailableException"/> when Steam can't be reached.</summary>
    Task<IReadOnlyList<WorkshopItemInfo>> GetInfoAsync(IReadOnlyList<ulong> ids, CancellationToken ct);

    /// <summary>
    /// Subscribes (when needed) and downloads each item until Steam reports it installed and up to date. Per-item failures are
    /// results, not exceptions; <see cref="WorkshopUnavailableException"/> when Steam can't be reached at all.
    /// </summary>
    Task<IReadOnlyList<WorkshopItemResult>> InstallAsync(IReadOnlyList<ulong> ids, IProgress<WorkshopProgress> progress, CancellationToken ct);
}
```

- [ ] **Step 2: Create `FazStellarisModmanager.Steam/FazStellarisModmanager.Steam.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Facepunch.Steamworks" Version="2.3.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\FazStellarisModmanager.Core\FazStellarisModmanager.Core.csproj" />
  </ItemGroup>

  <!-- PackageReference ignores a package's content/ folder, so copy Steam's native library to the output and publish folders. -->
  <ItemGroup>
    <None Include="$(NuGetPackageRoot)facepunch.steamworks/2.3.3/content/steam_api64.dll" Link="steam_api64.dll"
          CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" Visible="false" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create `FazStellarisModmanager.Steam/SteamWorkshopService.cs`**

```csharp
using FazStellarisModmanager.Core.Workshop;
using Steamworks;
using Steamworks.Ugc;

namespace FazStellarisModmanager.Steam;

/// <summary>
/// Steam Workshop through Facepunch.Steamworks. Each call connects to the running Steam client as Stellaris (Steam shows
/// "Playing Stellaris" meanwhile) and disconnects before returning; calls are serialised.
/// </summary>
public sealed class SteamWorkshopService(TimeSpan? stallTimeout = null) : IWorkshopService
{
    public const uint StellarisAppId = 281990;

    readonly SemaphoreSlim _gate = new(1, 1);
    readonly TimeSpan _stall = stallTimeout ?? TimeSpan.FromMinutes(2);
    volatile bool _active;

    public bool IsActive => _active;

    public Task<IReadOnlyList<WorkshopItemInfo>> GetInfoAsync(IReadOnlyList<ulong> ids, CancellationToken ct) =>
        Connected<IReadOnlyList<WorkshopItemInfo>>(async () =>
        {
            var list = new List<WorkshopItemInfo>();
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                var item = await Item.GetAsync(id);
                list.Add(new WorkshopItemInfo(id, item?.Title, item is { } i && i.SizeBytes > 0 ? i.SizeBytes : null));
            }
            return list;
        }, ct);

    public Task<IReadOnlyList<WorkshopItemResult>> InstallAsync(IReadOnlyList<ulong> ids, IProgress<WorkshopProgress> progress, CancellationToken ct) =>
        Connected<IReadOnlyList<WorkshopItemResult>>(async () =>
        {
            foreach (var id in ids) progress.Report(new WorkshopProgress(id, WorkshopItemState.Waiting, 0));
            var results = new List<WorkshopItemResult>();
            foreach (var id in ids)
            {
                if (ct.IsCancellationRequested)
                {
                    progress.Report(new WorkshopProgress(id, WorkshopItemState.Cancelled, 0));
                    results.Add(new WorkshopItemResult(id, false, null, "Cancelled."));
                    continue;
                }
                results.Add(await InstallOne(id, progress, ct));
            }
            return results;
        }, ct);

    async Task<WorkshopItemResult> InstallOne(ulong id, IProgress<WorkshopProgress> progress, CancellationToken ct)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            progress.Report(new WorkshopProgress(id, WorkshopItemState.Subscribing, 0));
            if (await Item.GetAsync(id) is not { } item)
                return Fail(id, progress, "Not found on the Workshop, or not visible to this Steam account.");
            if (!item.IsSubscribed && !await item.Subscribe())
                return Fail(id, progress, "Steam refused the subscription.");

            stall.CancelAfter(_stall);
            var last = -1f;
            var ok = await item.DownloadAsync(fraction =>
            {
                if (fraction > last)
                {
                    last = fraction;
                    stall.CancelAfter(_stall); // progress: restart the stall timer
                }
                progress.Report(new WorkshopProgress(id, WorkshopItemState.Downloading, Math.Clamp(fraction, 0, 1)));
            }, 250, stall.Token);
            ct.ThrowIfCancellationRequested();
            if (stall.IsCancellationRequested) return Fail(id, progress, "The download stalled.");

            if (!ok || await Item.GetAsync(id, 0) is not { IsInstalled: true, NeedsUpdate: false } done)
                return Fail(id, progress, "Steam did not finish installing it.");
            progress.Report(new WorkshopProgress(id, WorkshopItemState.Installed, 1));
            return new WorkshopItemResult(id, true, done.Directory, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            progress.Report(new WorkshopProgress(id, WorkshopItemState.Cancelled, 0));
            return new WorkshopItemResult(id, false, null, "Cancelled.");
        }
        catch (OperationCanceledException)
        {
            return Fail(id, progress, "The download stalled.");
        }
    }

    static WorkshopItemResult Fail(ulong id, IProgress<WorkshopProgress> progress, string error)
    {
        progress.Report(new WorkshopProgress(id, WorkshopItemState.Failed, 0, error));
        return new WorkshopItemResult(id, false, null, error);
    }

    async Task<T> Connected<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            try { SteamClient.Init(StellarisAppId, asyncCallbacks: true); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new WorkshopUnavailableException(
                    $"Could not connect to Steam ({ex.Message}). Make sure Steam is running, you are logged in, and this account owns Stellaris.", ex);
            }
            _active = true;
            try { return await work(); }
            finally
            {
                SteamClient.Shutdown();
                _active = false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
```

- [ ] **Step 4: Add the project to the solution and build**

Run:
- `dotnet sln FazStellarisModmanager.sln add FazStellarisModmanager.Steam/FazStellarisModmanager.Steam.csproj`
- `dotnet build FazStellarisModmanager.sln -c Release --artifacts-path <art>`

Expected: 0 errors.
- If `Item.GetAsync(id)` doesn't accept a `ulong`, because `PublishedFileId` has no implicit conversion, use `new Steamworks.Data.PublishedFileId { Value = id }` and report it.
- Fix any other API mismatches minimally, and report them.

- [ ] **Step 5: Live spike (manual, read-only on Steam)**
1. Create a scratch console project outside the repo, under the session scratchpad. Reference `FazStellarisModmanager.Steam.csproj`.
2. In it, call `new SteamWorkshopService().GetInfoAsync([1419304439UL, 1UL], CancellationToken.None)` and print the results. 1419304439 is "Ancient Cache of Technologies", which the user has installed.
3. **Never call InstallAsync in the spike.**
4. Run it with Steam running.
5. Report the following:
   - The title and size printed for 1419304439. Expect a title and a size above 0.
   - The result for id 1. Expect a null title.
   - Whether `steam_api64.dll` is in the console's output folder.
   - Whether a `WorkshopUnavailableException` occurred.
   - How long the call took.

If Init fails with Steam running, capture the exception and stop. Report BLOCKED.

- [ ] **Step 6: Commit** (after the spike works)

```bash
git add FazStellarisModmanager.Core/Workshop/WorkshopContracts.cs FazStellarisModmanager.Steam FazStellarisModmanager.sln
git commit -m "Workshop: contract and Steam adapter (Facepunch.Steamworks), verified with a live read-only query"
```

---

### Task 2: `WorkshopNeeds`

**Files:**
- Create: `FazStellarisModmanager.Core/Workshop/WorkshopNeeds.cs`
- Test: `FazStellarisModmanager.Tests/WorkshopNeedsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Core.Workshop;

namespace FazStellarisModmanager.Tests;

public class WorkshopNeedsTests
{
    static UnitDiff Changed(string key, string? mineKey, string? mineName = null) =>
        new(key, key + " name", null, UnitStatus.ContentMismatch, 1, 1, false, null, null, new FileDiff(["f"], [], []),
            mineKey, MatchKind.Key, false, mineName);

    [Fact]
    public void Lists_installs_and_updates_by_workshop_id_and_names_what_cannot_be_installed()
    {
        var plan = new MatchPlan(
            new ModList("H", [], []),
            [new("ugc:5", "Five", "mod/ugc_5.mod", "5"), new("local:copy.mod", "(H) Seven", "mod/copy.mod", "7"), new("ugc:5", "Five again", "mod/ugc_5.mod", "5")],
            [new("local:only.mod", "Host only", "mod/only.mod", null)],
            [Changed("ugc:9", "ugc:9", "My Nine"), Changed("ugc:5", "ugc:5")],
            [],
            []);

        var needs = WorkshopNeeds.From(plan);

        Assert.Equal(
            [(5UL, "Five", WorkshopNeedKind.Install), (7UL, "(H) Seven", WorkshopNeedKind.Install), (9UL, "My Nine", WorkshopNeedKind.Update)],
            needs.Items.Select(i => (i.Id, i.Name, i.Kind)));
        Assert.Equal(["Host only"], needs.NotInstallable);
        Assert.True(needs.NeedsPrompt);
    }

    [Fact]
    public void Updates_use_my_key_and_a_complete_plan_needs_no_prompt()
    {
        var local = new MatchPlan(new ModList("H", [], []), [], [], [Changed("ugc:5", "local:copy.mod")], [], []);
        var none = new MatchPlan(new ModList("H", [], []), [], [], [], [], []);

        Assert.Empty(WorkshopNeeds.From(local).Items);
        Assert.False(WorkshopNeeds.From(none).NeedsPrompt);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.** Run `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art> --filter WorkshopNeedsTests`. Expected: build errors, because `WorkshopNeeds` doesn't exist.

- [ ] **Step 3: Implement `Core/Workshop/WorkshopNeeds.cs`**

```csharp
using System.Globalization;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Session;

namespace FazStellarisModmanager.Core.Workshop;

public enum WorkshopNeedKind { Install, Update }

public sealed record WorkshopNeed(ulong Id, string Name, WorkshopNeedKind Kind);

/// <summary>What the Workshop pop-up offers for a match plan: installs and updates (distinct ids, host order) and the host's own local mods.</summary>
public sealed record WorkshopNeeds(IReadOnlyList<WorkshopNeed> Items, IReadOnlyList<string> NotInstallable)
{
    /// <summary>True when there is anything to install, update or point out.</summary>
    public bool NeedsPrompt => Items.Count > 0 || NotInstallable.Count > 0;

    public static WorkshopNeeds From(MatchPlan plan)
    {
        var installs = plan.NeedsWorkshopInstall
            .Select(e => ModMatcher.WorkshopIdOf(e.Key, e.RemoteId) is { } id ? new WorkshopNeed(ulong.Parse(id, CultureInfo.InvariantCulture), e.Name, WorkshopNeedKind.Install) : null);
        // Only my Workshop items can be updated through Steam; a local copy that differs needs the user's attention instead.
        var updates = plan.NeedsWorkshopUpdate
            .Select(u => ModKeys.WorkshopId(u.MineKey ?? u.Key) is { } id ? new WorkshopNeed(id, u.MineName ?? u.Name, WorkshopNeedKind.Update) : null);
        return new WorkshopNeeds(
            installs.Concat(updates).OfType<WorkshopNeed>().DistinctBy(n => n.Id).ToList(),
            plan.NeedsManualInstall.Select(e => e.Name).ToList());
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**, then run the whole suite.

- [ ] **Step 5: Commit**

```bash
git add FazStellarisModmanager.Core/Workshop/WorkshopNeeds.cs FazStellarisModmanager.Tests/WorkshopNeedsTests.cs
git commit -m "Workshop: needs list (installs, updates, host-only mods) from a match plan"
```

---

### Task 3: Session flow and Launch guard

**Files:**
- Modify: `FazStellarisModmanager.Core/Session/SessionService.cs`, `FazStellarisModmanager.Core/ModManagerService.cs`, `FazStellarisModmanager.Tests/TestUtil/FakeInstall.cs`
- Create: `FazStellarisModmanager.Tests/TestUtil/FakeWorkshop.cs`
- Test: modify `FazStellarisModmanager.Tests/SessionServiceTests.cs`, `FazStellarisModmanager.Tests/ModManagerServiceTests.cs`

- [ ] **Step 1: Test helpers**

Add to `FakeInstall`:

```csharp
    /// <summary>A downloaded Workshop item: lib/steamapps/workshop/content/281990/&lt;id&gt; with a descriptor and one file.</summary>
    public void AddWorkshopItem(ulong id)
    {
        _tmp.Write($"lib/steamapps/workshop/content/281990/{id}/descriptor.mod", $"name=\"Workshop {id}\"" + (char)10 + "supported_version=\"v4.*\"" + (char)10);
        _tmp.Write($"lib/steamapps/workshop/content/281990/{id}/common/w{id}.txt", $"ws {id}");
    }
```

Create `TestUtil/FakeWorkshop.cs`:

```csharp
using FazStellarisModmanager.Core.Workshop;

namespace FazStellarisModmanager.Tests.TestUtil;

/// <summary>"Downloads" by calling <paramref name="install"/> (true = installed) and records what was requested.</summary>
public sealed class FakeWorkshop(Func<ulong, bool> install) : IWorkshopService
{
    public List<ulong> Requested { get; } = [];
    public bool IsActive { get; private set; }

    public Task<IReadOnlyList<WorkshopItemInfo>> GetInfoAsync(IReadOnlyList<ulong> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<WorkshopItemInfo>>(ids.Select(id => new WorkshopItemInfo(id, $"Item {id}", 1000)).ToList());

    public Task<IReadOnlyList<WorkshopItemResult>> InstallAsync(IReadOnlyList<ulong> ids, IProgress<WorkshopProgress> progress, CancellationToken ct)
    {
        IsActive = true;
        try
        {
            var results = new List<WorkshopItemResult>();
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                Requested.Add(id);
                var ok = install(id);
                progress.Report(new WorkshopProgress(id, ok ? WorkshopItemState.Installed : WorkshopItemState.Failed, ok ? 1 : 0));
                results.Add(new WorkshopItemResult(id, ok, ok ? "folder" : null, ok ? null : "Not available."));
            }
            return Task.FromResult<IReadOnlyList<WorkshopItemResult>>(results);
        }
        finally
        {
            IsActive = false;
        }
    }
}
```

- [ ] **Step 2: Write the failing tests**

In `SessionServiceTests`:
- Change the `Rig` constructor to `Rig(string playerName, string enabledModsJson, Func<FakeInstall, IWorkshopService>? workshop = null)`, and construct the session with `new SessionService(new ModManagerService(paths, _ => Fake.GameDir), workshop?.Invoke(Fake))`.
- Add `using FazStellarisModmanager.Core.Workshop;`.
- Add:

```csharp
    sealed class NoProgress : IProgress<WorkshopProgress>
    {
        public void Report(WorkshopProgress value) { }
    }

    [Fact]
    public async Task Workshop_install_downloads_missing_mods_then_matches_the_host()
    {
        await using var host = new Rig("Hosty", "[\"mod/ugc_222.mod\",\"mod/local.mod\"]");
        host.Fake.AddWorkshopItem(222);
        FakeWorkshop? workshop = null;
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]", f => workshop = new FakeWorkshop(id => { f.AddWorkshopItem(id); return true; }));
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);

        var needs = WorkshopNeeds.From(await client.Session.PlanAsync());
        Assert.Equal([222UL], needs.Items.Select(i => i.Id));
        Assert.Equal(["mod/local.mod"], DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);

        var result = await client.Session.InstallFromWorkshopAndMatchAsync([222UL], new NoProgress());

        Assert.Equal([222UL], workshop!.Requested);
        Assert.True(Assert.Single(result.Items).Success);
        Assert.True(result.Plan.IsComplete);
        Assert.Equal(["mod/ugc_222.mod", "mod/local.mod"], DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);
        Assert.True(client.Session.MyDiff!.IsMatch);
    }

    [Fact]
    public async Task A_failed_workshop_item_is_reported_and_the_rest_is_still_matched()
    {
        await using var host = new Rig("Hosty", "[\"mod/ugc_222.mod\",\"mod/local.mod\"]");
        host.Fake.AddWorkshopItem(222);
        await using var client = new Rig("Cli", "[]", _ => new FakeWorkshop(_ => false));
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);

        var result = await client.Session.InstallFromWorkshopAndMatchAsync([222UL], new NoProgress());

        var item = Assert.Single(result.Items);
        Assert.Equal((false, "Not available."), (item.Success, item.Error));
        Assert.Equal(["mod/local.mod"], DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);
        Assert.Equal(["ugc:222"], result.Plan.NeedsWorkshopInstall.Select(e => e.Key));
        Assert.False(client.Session.MyDiff!.IsMatch);
    }

    [Fact]
    public async Task Matching_without_installing_needs_no_workshop_service_but_installing_does()
    {
        await using var host = new Rig("Hosty", "[\"mod/local.mod\"]");
        await using var client = new Rig("Cli", "[]");
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);

        var result = await client.Session.InstallFromWorkshopAndMatchAsync([], new NoProgress());

        Assert.Empty(result.Items);
        Assert.Equal(["mod/local.mod"], DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Session.InstallFromWorkshopAndMatchAsync([5UL], new NoProgress()));
    }
```

In `ModManagerServiceTests`, add the following, using the file's existing usings and helpers:

```csharp
    [Fact]
    public void Launch_is_refused_while_a_reason_blocks_it()
    {
        using var fake = new FakeInstall();
        var manager = new ModManagerService(new AppPaths(fake.DataDir), _ => fake.GameDir) { LaunchBlockedReason = () => "busy downloading" };

        Assert.Equal("busy downloading", Assert.Throws<InvalidOperationException>(manager.Launch).Message);
    }
```

- [ ] **Step 3: Run the tests and confirm they fail.** Expected: build errors, because `PlanAsync`, `InstallFromWorkshopAndMatchAsync`, the `SessionService` workshop parameter and `LaunchBlockedReason` don't exist.

- [ ] **Step 4: Implement `ModManagerService.LaunchBlockedReason`.** Add the property:

```csharp
    /// <summary>When set and returning a reason, <see cref="Launch"/> refuses with that reason (e.g. while Workshop downloads run).</summary>
    public Func<string?>? LaunchBlockedReason { get; set; }
```

and make this the first statement of `Launch()`, before the lock:

```csharp
        if (LaunchBlockedReason?.Invoke() is { } reason) throw new InvalidOperationException(reason);
```

- [ ] **Step 5: Implement the session flow in `SessionService.cs`**

5a. Change the class header to `public sealed class SessionService(ModManagerService manager, IWorkshopService? workshop = null) : IAsyncDisposable`. Add the field `readonly IWorkshopService? _workshop = workshop;` and `using FazStellarisModmanager.Core.Workshop;`.

5b. Replace `MatchHostAsync` with a wrapper around a private core. Move the existing lambda body unchanged into `MatchCoreAsync`:

```csharp
    /// <summary>Client only: writes the host's list (installed mods, host order) to dlc_load.json, then rescans and reports.</summary>
    public Task<MatchPlan> MatchHostAsync(CancellationToken ct = default) => Exclusive(MatchCoreAsync, ct);

    // Runs inside Exclusive. Shared by Match host and Workshop install-then-match.
    async Task<MatchPlan> MatchCoreAsync(CancellationToken ct)
    {
        // … the former MatchHostAsync lambda body, unchanged (from "var client = _client ?? throw …" to the end of its catch) …
    }
```

5c. Add:

```csharp
    /// <summary>Client only: what Match host would do right now (the library is refreshed), without applying anything.</summary>
    public Task<MatchPlan> PlanAsync(CancellationToken ct = default) => Exclusive(async ct =>
    {
        var client = _client ?? throw new InvalidOperationException("Not connected to a host.");
        var mine = MySnapshot ?? throw new InvalidOperationException("Your mods have not been scanned yet.");
        var target = client.Target;
        SetActivity("Checking what the host's list needs…");
        await manager.RefreshLibraryAsync();
        ct.ThrowIfCancellationRequested();
        return await Task.Run(() => MatchPlan.Create(target.HostList, ModDiffer.Diff(target.HostSnapshot, mine), manager.Library, mine), CancellationToken.None);
    }, ct);

    /// <summary>
    /// Client only: downloads the given Workshop items (subscribing when needed), then matches the host like <see cref="MatchHostAsync"/>.
    /// Per-item failures don't stop the match; Steam being unavailable or a cancel does (nothing is applied then).
    /// With no ids this is a plain match. Installing needs a Workshop service.
    /// </summary>
    public Task<WorkshopMatchResult> InstallFromWorkshopAndMatchAsync(IReadOnlyList<ulong> ids, IProgress<WorkshopProgress> progress,
        CancellationToken ct = default) => Exclusive(async ct =>
    {
        var client = _client ?? throw new InvalidOperationException("Not connected to a host.");
        IReadOnlyList<WorkshopItemResult> items = [];
        if (ids.Count > 0)
        {
            var service = _workshop ?? throw new InvalidOperationException("Steam Workshop support is not available in this build.");
            SetActivity($"Downloading {ids.Count} Workshop mod(s)…");
            await client.SendBusyAsync("Downloading Workshop mods…", CancellationToken.None);
            try
            {
                items = await service.InstallAsync(ids, progress, ct);
                ct.ThrowIfCancellationRequested();
            }
            catch
            {
                // Never leave the host showing "Downloading…": put our last known snapshot back.
                if (_mySnapshot is { } last)
                {
                    try { await client.SendSnapshotAsync(last, CancellationToken.None); }
                    catch { /* best effort */ }
                }
                throw;
            }
        }
        var plan = await MatchCoreAsync(ct);
        return new WorkshopMatchResult(items, plan);
    }, ct);
```

`SetActivity`, `SendBusyAsync`, `SendSnapshotAsync`, `_mySnapshot` and `Exclusive<T>` already exist; check their exact names in the file. If `Exclusive` requires a lambda rather than a method group, use `Exclusive(ct => MatchCoreAsync(ct), ct)`.

- [ ] **Step 6: Run all tests and confirm they pass.** Expected: everything passes, including the existing Match host tests through `MatchCoreAsync`.

- [ ] **Step 7: Commit**

```bash
git add FazStellarisModmanager.Core FazStellarisModmanager.Tests
git commit -m "Session: plan preview and Workshop install-then-match; Launch can be blocked"
```

---

### Task 4: App wiring and the pop-up

**Files:**
- Modify: `FazStellarisModmanager/FazStellarisModmanager.csproj`, `AppServices.cs`, `_Imports.razor`, `Pages/SessionPage.razor`, `wwwroot/css/site.css`
- Create: `FazStellarisModmanager/Components/WorkshopPrompt.razor`

- [ ] **Step 1: Project reference.** In `FazStellarisModmanager.csproj`, add `<ProjectReference Include="..\FazStellarisModmanager.Steam\FazStellarisModmanager.Steam.csproj" />` next to the Core reference.

- [ ] **Step 2: Register the service in `AppServices.Register`.** Add `using FazStellarisModmanager.Core.Workshop;` and `using FazStellarisModmanager.Steam;`. Then replace the `ModManagerService` and `SessionService` registrations with:

```csharp
        services.AddSingleton<IWorkshopService>(_ => new SteamWorkshopService());
        services.AddSingleton(sp =>
        {
            var workshop = sp.GetRequiredService<IWorkshopService>();
            return new ModManagerService(paths)
            {
                LaunchBlockedReason = () => workshop.IsActive ? "Wait for the Workshop downloads to finish before launching Stellaris." : null,
            };
        });
        services.AddSingleton(sp => new SessionService(sp.GetRequiredService<ModManagerService>(), sp.GetRequiredService<IWorkshopService>()));
```

- [ ] **Step 3: Import the namespace.** In `_Imports.razor`, add `@using FazStellarisModmanager.Core.Workshop`.

- [ ] **Step 4: Create `Components/WorkshopPrompt.razor`**

```razor
@using System.Globalization
@implements IDisposable

<div class="modal-backdrop">
    <div class="modal workshop-modal" role="dialog" aria-modal="true" aria-labelledby="ws-title">
        <h2 id="ws-title">Workshop mods for this host</h2>

        @if (phase == Phase.Choose)
        {
            @if (Needs.Items.Count == 0)
            {
                <p class="muted">Every Workshop mod the host uses is installed and up to date.</p>
            }
            else
            {
                <p class="muted small">Ticked mods are subscribed to and downloaded through Steam (Steam shows you as playing Stellaris meanwhile), then the host's list is applied.</p>
                <div class="ws-tick">
                    <button type="button" class="link" @onclick="() => SetAll(true)">Tick all</button> ·
                    <button type="button" class="link" @onclick="() => SetAll(false)">Untick all</button>
                </div>
                <ul class="ws-list">
                    @foreach (var need in Needs.Items)
                    {
                        var n = need;
                        <li>
                            <label class="inline">
                                <input type="checkbox" checked="@selected.Contains(n.Id)" @onchange="e => Toggle(n.Id, e.Value is true)" />
                                <span class="badge @(n.Kind == WorkshopNeedKind.Install ? "workshop" : "local")">@(n.Kind == WorkshopNeedKind.Install ? "Install" : "Update")</span>
                                <span>@NameOf(n.Id)</span>
                            </label>
                            <span class="muted small">Workshop @n.Id@SizeOf(n.Id)</span>
                        </li>
                    }
                </ul>
                @if (infoError is not null)
                {
                    <p class="muted small">Couldn't get details from Steam: @infoError</p>
                }
            }
            @if (Needs.NotInstallable.Count > 0)
            {
                <h3>Get these from the host</h3>
                <p class="muted small">These are the host's own local mods; they aren't on the Workshop.</p>
                <ul>
                    @foreach (var name in Needs.NotInstallable)
                    {
                        <li>@name</li>
                    }
                </ul>
            }
            <div class="modal-actions">
                @if (Needs.Items.Count > 0)
                {
                    <button class="primary" disabled="@(selected.Count == 0)" @onclick="() => Run(Needs.Items.Select(i => i.Id).Where(selected.Contains).ToList())">Install/update selected (@selected.Count) and match</button>
                }
                <button @onclick="() => Run([])">Match without installing</button>
                <button @onclick="Close">Close</button>
            </div>
        }
        else if (phase == Phase.Working)
        {
            <p>@(Session.Activity ?? "Working…")</p>
            <ul class="ws-list">
                @foreach (var id in running)
                {
                    var p = progress.GetValueOrDefault(id);
                    <li>
                        <span>@NameOf(id)</span>
                        <span class="muted small">@StateText(p)</span>
                        <progress max="1" value="@F(p?.Fraction ?? 0)"></progress>
                    </li>
                }
            </ul>
            <div class="modal-actions">
                <button @onclick="Session.CancelCurrent">Cancel</button>
            </div>
        }
        else
        {
            @if (result is { } r)
            {
                @if (r.Items.Count > 0)
                {
                    <ul class="ws-list">
                        @foreach (var item in r.Items)
                        {
                            var it = item;
                            <li>
                                <span>@NameOf(it.Id)</span>
                                @if (it.Success)
                                {
                                    <span class="status ok">@(KindOf(it.Id) == WorkshopNeedKind.Update ? "Updated" : "Installed")</span>
                                }
                                else
                                {
                                    <span class="status error">Failed: @it.Error</span>
                                    <a href="@SteamLink(it.Id)" target="_blank">Open in Steam</a>
                                }
                            </li>
                        }
                    </ul>
                }
                <p>@MatchText(r.Plan)</p>
                @if (StillDifferent() is { Count: > 0 } diffs)
                {
                    <h3>Still different from the host</h3>
                    <ul>
                        @foreach (var d in diffs)
                        {
                            <li>@(d.MineName ?? d.Name)@(UpdatedHere(d) ? " (updated just now; the host may be on an older version)" : "")</li>
                        }
                    </ul>
                }
            }
            @if (error is not null)
            {
                <p class="status error">@error</p>
                @if (running.Count > 0)
                {
                    <p class="muted small">You can install these from Steam instead:</p>
                    <ul>
                        @foreach (var id in running)
                        {
                            <li><a href="@SteamLink(id)" target="_blank">@NameOf(id)</a></li>
                        }
                    </ul>
                }
            }
            <div class="modal-actions">
                <button class="primary" @onclick="Close">Close</button>
            </div>
        }
    </div>
</div>

@code {
    [Parameter, EditorRequired] public SessionService Session { get; set; } = default!;
    [Parameter] public IWorkshopService? Workshop { get; set; }
    [Parameter, EditorRequired] public WorkshopNeeds Needs { get; set; } = default!;
    [Parameter] public EventCallback OnClose { get; set; }

    enum Phase { Choose, Working, Done }

    Phase phase = Phase.Choose;
    readonly HashSet<ulong> selected = [];
    readonly Dictionary<ulong, WorkshopProgress> progress = [];
    Dictionary<ulong, WorkshopItemInfo> info = [];
    List<ulong> running = [];
    WorkshopMatchResult? result;
    string? error;
    string? infoError;
    WorkshopNeeds? shownNeeds;

    protected override void OnInitialized() => Session.Changed += OnSessionChanged;

    void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Session.Changed -= OnSessionChanged;

    protected override async Task OnParametersSetAsync()
    {
        if (ReferenceEquals(shownNeeds, Needs)) return;
        shownNeeds = Needs;
        (phase, result, error, infoError) = (Phase.Choose, null, null, null);
        selected.Clear();
        foreach (var need in Needs.Items) selected.Add(need.Id);
        if (Workshop is null || Needs.Items.Count == 0) return;
        try
        {
            var details = await Workshop.GetInfoAsync(Needs.Items.Select(i => i.Id).ToList(), CancellationToken.None);
            info = details.ToDictionary(d => d.Id);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            infoError = ex.Message;
        }
    }

    async Task Run(List<ulong> ids)
    {
        (phase, error, result, running) = (Phase.Working, null, null, ids);
        progress.Clear();
        try
        {
            result = await Session.InstallFromWorkshopAndMatchAsync(ids, new Sink(this));
        }
        catch (OperationCanceledException)
        {
            error = "Cancelled. The host's list was not applied.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error = ex.Message;
        }
        phase = Phase.Done;
    }

    sealed class Sink(WorkshopPrompt owner) : IProgress<WorkshopProgress>
    {
        public void Report(WorkshopProgress value) => _ = owner.InvokeAsync(() =>
        {
            owner.progress[value.Id] = value;
            owner.StateHasChanged();
        });
    }

    WorkshopNeed? NeedOf(ulong id) => Needs.Items.FirstOrDefault(i => i.Id == id);

    WorkshopNeedKind? KindOf(ulong id) => NeedOf(id)?.Kind;

    string NameOf(ulong id) =>
        info.TryGetValue(id, out var i) && i.Title is { Length: > 0 } title ? title : NeedOf(id)?.Name ?? $"Workshop {id}";

    string SizeOf(ulong id) =>
        info.TryGetValue(id, out var i) && i.SizeBytes is long bytes && bytes > 0 ? " · " + Size(bytes) : "";

    static string Size(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB"
        : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB"
        : $"{Math.Max(1, bytes / 1024)} KB";

    static string StateText(WorkshopProgress? p) => p?.State switch
    {
        null or WorkshopItemState.Waiting => "waiting",
        WorkshopItemState.Subscribing => "subscribing",
        WorkshopItemState.Downloading => $"{p.Fraction * 100:0}%",
        WorkshopItemState.Installed => "done",
        WorkshopItemState.Failed => "failed" + (p.Message is { } m ? ": " + m : ""),
        _ => "cancelled",
    };

    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    static string SteamLink(ulong id) => $"steam://url/CommunityFilePage/{id}";

    static string MatchText(MatchPlan plan) => plan.IsComplete
        ? $"Applied the host's list ({plan.ToApply.Mods.Count} mods) to dlc_load.json. Everything the host uses is installed."
        : $"Applied the host's list ({plan.ToApply.Mods.Count} installed mods) to dlc_load.json. Some mods still need attention (see below and the Session page).";

    List<UnitDiff> StillDifferent() =>
        Session.MyDiff?.Mods.Where(m => m.Status is UnitStatus.Missing or UnitStatus.ContentMismatch).ToList() ?? [];

    bool UpdatedHere(UnitDiff d) =>
        d.Status == UnitStatus.ContentMismatch && result is { } r
        && ModKeys.WorkshopId(d.MineKey ?? d.Key) is { } id && r.Items.Any(i => i.Id == id && i.Success);

    void Toggle(ulong id, bool on)
    {
        if (on) selected.Add(id);
        else selected.Remove(id);
    }

    void SetAll(bool on)
    {
        selected.Clear();
        if (on) foreach (var need in Needs.Items) selected.Add(need.Id);
    }

    Task Close() => OnClose.InvokeAsync();
}
```

- [ ] **Step 5: Wire up `Pages/SessionPage.razor`**

5a. Add `@inject IWorkshopService Workshop` after the existing `@inject`.

5b. In the client toolbar:
- Change the Match host button's `@onclick="Match"` to `@onclick="OpenOrMatch"`.
- Add this button directly after it, inside the same `@if (Session.Role == SessionRole.Client)`:

```razor
            <button @onclick="OpenWorkshop" disabled="@(Session.IsBusy || Session.MyDiff is null)">Workshop mods</button>
```

5c. At the end of the page markup, before `@code`, add:

```razor
@if (workshopNeeds is { } needs && Session.Role == SessionRole.Client)
{
    <WorkshopPrompt Session="Session" Workshop="Workshop" Needs="needs" OnClose="() => workshopNeeds = null" />
}
```

5d. In `@code`, add:

```csharp
    WorkshopNeeds? workshopNeeds;

    // Match host: when something needs installing (or is host-only), show the Workshop pop-up instead of matching straight away.
    async Task OpenOrMatch()
    {
        try
        {
            var needs = WorkshopNeeds.From(await Session.PlanAsync());
            if (needs.NeedsPrompt)
            {
                workshopNeeds = needs;
                return;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            (status, error) = (ex.Message, true);
            return;
        }
        await Match();
    }

    async Task OpenWorkshop()
    {
        try
        {
            workshopNeeds = WorkshopNeeds.From(await Session.PlanAsync());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            (status, error) = (ex.Message, true);
        }
    }
```

Check that the page's status fields are named `status` and `error`; adjust if they differ.

- [ ] **Step 6: Append the styles to `wwwroot/css/site.css`**

```css
.workshop-modal { width: min(660px, 94vw); }
.ws-tick { font-size: .85rem; }
.ws-list { list-style: none; padding: 0; margin: .3rem 0; display: flex; flex-direction: column; gap: .35rem; max-height: 45vh; overflow: auto; }
.ws-list li { display: flex; gap: .5rem; align-items: center; flex-wrap: wrap; }
.ws-list progress { flex: 1; min-width: 120px; }
```

- [ ] **Step 7: Build and test.** Build the solution and run all tests (0 errors, no new warnings). Check that `steam_api64.dll` is in the app's build output folder, under the artifacts path, next to `FazStellarisModmanager.exe`. Report the path.

- [ ] **Step 8: Commit**

```bash
git add FazStellarisModmanager
git commit -m "Session: Workshop mods pop-up from Match host or its own button; Steam service registered"
```

---

### Task 5: Manual verification (user)

- [ ] **Step 1: Check the pop-up opens only on request.** Host on one PC (or one app instance). Join from another whose user folder lacks one small Workshop mod from the host's list. The pop-up must not open by itself.
- [ ] **Step 2: Install and match.**
  1. Press **Match host**. The pop-up lists the mod as Install, with its title and size.
  2. Press **Install/update selected and match**.
  3. Watch Steam subscribe and download it, with the progress bar moving.
  4. Check that `dlc_load.json` gets the host's list and that the summary says Installed.
- [ ] **Step 3: Steam not running.** Quit Steam and open **Workshop mods**. The pop-up shows the "Couldn't get details from Steam" line, and installing shows a clear error with **Open in Steam** links.
- [ ] **Step 4: Release build.** Check that the release zip contains `steam_api64.dll`. Push a tag, or run the publish command from the release workflow.

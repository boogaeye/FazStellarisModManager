# Live Game Part 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The host's app shares live save data with every client. Each client gets a copy filtered for its own empire. A client keeps the last data after the host disconnects. The Tech tab takes researched techs from the live data, and manual "researched" marking is removed.

**Architecture:**
- **Core:**
  - `LiveFilter`, a pure function;
  - two new session messages, `LiveUpdate` and `LiveViewAs`;
  - protocol version 2;
  - per-peer live state in `SessionHost`;
  - a `Live` property on `SessionClient`;
  - `SessionService` implementing `IHostLiveSource`;
  - `LiveFeed`, which merges local saves and host data.
- **App:** the Live Game page and the Tech page both read `LiveFeed`.

**Spec:** `docs/superpowers/specs/2026-10-04-live-game-design.md`, section "Part 2 design".

**Conventions:**
- Work in `C:\Users\SCP Fazbear\source\repos\FazStellarisModmanager` on branch `feature/live-game-session`.
- Build and test with `-c Release --artifacts-path <scratchpad>/art`. Never kill FazStellarisModmanager.exe.
- Avoid backslash escapes in C# strings.

---

### Task 1: LiveFilter, messages and protocol 2

**Files:**
- Create: `FazStellarisModmanager.Core/Saves/LiveFilter.cs`
- Modify: `FazStellarisModmanager.Core/Session/SessionMessages.cs`. Add two `[JsonDerivedType]` lines and the two records.
- Modify: `FazStellarisModmanager.Core/Session/SessionProtocol.cs`. Set `Version = 2`.
- Test: `FazStellarisModmanager.Tests/LiveFilterTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Core.Session;

namespace FazStellarisModmanager.Tests;

public class LiveFilterTests
{
    static SaveCountry C(int id, int rank, IReadOnlyList<int> contacts, IReadOnlyList<string> techs) =>
        new(id, "default", "Name" + id, new Dictionary<string, string> { ["a"] = "b" }, "Adj",
            new CountryFlag("c", "f.dds", "b", "bg.dds", ["red"]), rank, 100 + id, 200, 300, 400, 500, 600, 700, 800, contacts, techs);

    static GameSnapshot S() => new("Save", "2300.01.01", "v", @"C:/Users/host/Documents/save games/x/autosave.sav".Replace('/', '/'), DateTime.UtcNow,
        [new SavePlayer("Host", 0), new SavePlayer("Guest", 1)],
        [C(0, 1, [1, 2], ["t0"]), C(1, 2, [0], ["t1", "t2"]), C(2, 3, [0], ["t9"]), C(3, 4, [], ["t3"])]);

    [Fact]
    public void Viewer_gets_own_country_in_full_others_trimmed_unknowns_blank()
    {
        var f = LiveFilter.For(S(), viewerId: 1);
        Assert.Equal("autosave.sav", f.SavePath);
        Assert.Equal(2, f.Players.Count);

        var mine = f.Countries.Single(c => c.Id == 1);
        Assert.Equal(["t1", "t2"], mine.Techs);
        Assert.Equal([0], mine.ContactedIds);
        Assert.Equal(300, mine.EconomyPower);

        var host = f.Countries.Single(c => c.Id == 0);
        Assert.Equal("Name0", host.NameKey);
        Assert.Equal(200, host.MilitaryPower);
        Assert.Empty(host.Techs);
        Assert.Empty(host.ContactedIds);

        var unknown = f.Countries.Single(c => c.Id == 2);
        Assert.Equal(3, unknown.VictoryRank);
        Assert.Equal("default", unknown.Type);
        Assert.Null(unknown.NameKey);
        Assert.Null(unknown.Flag);
        Assert.Null(unknown.Adjective);
        Assert.Empty(unknown.NameVariables);
        Assert.Equal(0, unknown.VictoryScore);
        Assert.Equal(0, unknown.MilitaryPower);
        Assert.Null(unknown.CachedDiploWeight);
        Assert.Empty(unknown.Techs);
    }

    [Fact]
    public void No_viewer_sends_players_only()
    {
        var f = LiveFilter.For(S(), viewerId: null);
        Assert.Equal(2, f.Players.Count);
        Assert.Empty(f.Countries);
        Assert.Equal("Save", f.SaveName);
    }

    [Fact]
    public async Task Live_messages_survive_framing()
    {
        var update = new LiveUpdate(LiveFilter.For(S(), 1), 1);
        using var stream = new MemoryStream();
        await Framing.WriteAsync(stream, update);
        await Framing.WriteAsync(stream, new LiveViewAs(1));
        stream.Position = 0;
        var back = Assert.IsType<LiveUpdate>(await Framing.ReadAsync(stream));
        Assert.Equal(1, back.ViewerId);
        Assert.Equal(["t1", "t2"], back.Snapshot.Countries.Single(c => c.Id == 1).Techs);
        Assert.Equal("b", back.Snapshot.Countries.Single(c => c.Id == 1).NameVariables["a"]);
        Assert.Equal(["red"], back.Snapshot.Countries.Single(c => c.Id == 0).Flag!.Colors);
        Assert.Equal(1, Assert.IsType<LiveViewAs>(await Framing.ReadAsync(stream)).CountryId);
    }
}
```

(In the `S()` helper, `@"…".Replace('/', '/')` is a harmless no-op. Write the path as a normal string containing forward slashes; `Path.GetFileName` handles them.)

- [ ] **Step 2: Run the tests.** Confirm they fail.

- [ ] **Step 3: Implement**

`LiveFilter.cs`:
```csharp
namespace FazStellarisModmanager.Core.Saves;

/// <summary>What one player may see of a save: their own country in full, contacted countries without techs and contacts, uncontacted ones as rank only.</summary>
public static class LiveFilter
{
    public static GameSnapshot For(GameSnapshot snapshot, int? viewerId)
    {
        var file = Path.GetFileName(snapshot.SavePath.Replace((char)92, '/'));
        if (viewerId is not int viewer) return snapshot with { SavePath = file, Countries = [] };

        var me = snapshot.Countries.FirstOrDefault(c => c.Id == viewer);
        var contacted = me?.ContactedIds.ToHashSet() ?? [];
        var countries = snapshot.Countries.Select(c =>
            c.Id == viewer ? c
            : contacted.Contains(c.Id) ? c with { ContactedIds = [], Techs = [] }
            : new SaveCountry(c.Id, c.Type, null, new Dictionary<string, string>(), null, null, c.VictoryRank, 0, 0, 0, 0, 0, 0, 0, null, [], []))
            .ToList();
        return snapshot with { SavePath = file, Countries = countries };
    }
}
```

`SessionMessages.cs`: add these attributes next to the others:
```csharp
[JsonDerivedType(typeof(LiveUpdate), "live")]
[JsonDerivedType(typeof(LiveViewAs), "liveViewAs")]
```
and these records, after `Bye`:
```csharp
/// <summary>Host to client: the latest save as this client may see it (see LiveFilter). ViewerId null: the host doesn't know the client's country yet (only Players are filled).</summary>
public sealed record LiveUpdate(FazStellarisModmanager.Core.Saves.GameSnapshot Snapshot, int? ViewerId) : SessionMessage;

/// <summary>Client to host: I play this country in the save.</summary>
public sealed record LiveViewAs(int CountryId) : SessionMessage;
```

Set `SessionProtocol.Version` to 2 and update its comment if needed. Check whether any test asserts the version number, and update it if so.

- [ ] **Step 4: Run the tests and the full suite.** Everything should PASS. If deserializing `IReadOnlyDictionary`/`IReadOnlyList` fails, add a `[JsonConstructor]` or switch the record property types to concrete collections, then report it.
- [ ] **Step 5: Commit.** Message: `"feat: live filter and live session messages (protocol 2)"`.

---

### Task 2: Live data in SessionHost and SessionClient

**Files:**
- Modify: `FazStellarisModmanager.Core/Session/SessionHost.cs`
- Modify: `FazStellarisModmanager.Core/Session/SessionClient.cs`
- Test: `FazStellarisModmanager.Tests/SessionLiveTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Tests.TestUtil;
using static FazStellarisModmanager.Tests.TestUtil.TestSnapshots;

namespace FazStellarisModmanager.Tests;

public class SessionLiveTests
{
    static SaveCountry C(int id, int rank, params int[] contacts) =>
        new(id, "default", "N" + id, new Dictionary<string, string>(), null, null, rank, rank * 10, 1, 1, 1, 1, 1, 1, null, contacts, ["tech_" + id]);

    static GameSnapshot Save(string date = "2300.01.01") =>
        new("Game", date, "v", "x.sav", DateTime.UtcNow, [new SavePlayer("Hosty", 0), new SavePlayer("Alice", 1), new SavePlayer("Bob", 2)],
            [C(0, 1, 1), C(1, 2, 0), C(2, 3), C(3, 4)]);

    static SessionHost StartHost()
    {
        var host = new SessionHost("Hosty", List("ugc:1"), Machine("H", "ugc:1"));
        host.Start(0, IPAddress.Loopback);
        return host;
    }

    static async Task<SessionClient> Connect(int port, string name)
    {
        var c = await SessionClient.ConnectAsync("127.0.0.1", port, name);
        c.Start();
        return c;
    }

    [Fact]
    public async Task Clients_get_their_filtered_view_by_name_and_updates()
    {
        await using var host = StartHost();
        await using var alice = await Connect(host.Port, "alice");
        host.UpdateLive(Save());
        await Wait.Until(() => alice.Live is not null, "first live update");
        Assert.Equal(1, alice.Live!.ViewerId);
        Assert.Equal(["tech_1"], alice.Live.Snapshot.Countries.Single(c => c.Id == 1).Techs);
        Assert.Null(alice.Live.Snapshot.Countries.Single(c => c.Id == 2).NameKey); // not contacted by Alice

        host.UpdateLive(Save("2300.02.01"));
        await Wait.Until(() => alice.Live!.Snapshot.Date == "2300.02.01", "second update");
    }

    [Fact]
    public async Task Late_joiner_gets_the_current_save_and_can_choose()
    {
        await using var host = StartHost();
        host.UpdateLive(Save());
        await using var carol = await Connect(host.Port, "Carol");
        await Wait.Until(() => carol.Live is not null, "update on join");
        Assert.Null(carol.Live!.ViewerId);
        Assert.Empty(carol.Live.Snapshot.Countries);
        Assert.Equal(3, carol.Live.Snapshot.Players.Count);

        await carol.SendViewAsAsync(99); // not a player country: ignored
        await carol.SendViewAsAsync(2);
        await Wait.Until(() => carol.Live!.ViewerId == 2, "chosen view");
        Assert.Equal(["tech_2"], carol.Live!.Snapshot.Countries.Single(c => c.Id == 2).Techs);
    }

    [Fact]
    public async Task No_live_data_sends_nothing()
    {
        await using var host = StartHost();
        await using var alice = await Connect(host.Port, "Alice");
        await Wait.Until(() => alice.Roster.Count == 2, "roster");
        await Task.Delay(200);
        Assert.Null(alice.Live);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**

- [ ] **Step 3: Implement**

In `SessionHost`:
- Add `using FazStellarisModmanager.Core.Saves;`.
- Add to `Peer`:
```csharp
        public int? LiveCountry { get; set; }
        public bool LiveChosen { get; set; }
```
- Add a field `GameSnapshot? _live;` guarded by `_gate`.
- Add these methods:
```csharp
    /// <summary>The newest save (or null): every peer gets its own filtered view. Never waits on peers.</summary>
    public void UpdateLive(GameSnapshot? snapshot)
    {
        lock (_gate)
        {
            _live = snapshot;
            if (snapshot is null) return;
            foreach (var p in _peers.Values) SendLive(p);
        }
    }

    // Under _gate. Keeps a valid chosen country, otherwise matches the peer's name with the save's players.
    void SendLive(Peer p)
    {
        if (_live is not { } live) return;
        if (p.LiveChosen && !live.Players.Any(pl => pl.CountryId == p.LiveCountry)) p.LiveChosen = false;
        if (!p.LiveChosen)
            p.LiveCountry = live.Players.FirstOrDefault(pl => string.Equals(pl.Name, p.Name, StringComparison.OrdinalIgnoreCase))?.CountryId;
        Enqueue(p, new LiveUpdate(LiveFilter.For(live, p.LiveCountry), p.LiveCountry));
    }
```
- In `HandleClientAsync`, inside the lock that enqueues `Welcome`, add `SendLive(p);` right after `Enqueue(p, new Welcome(...))`.
- In the message switch, add:
```csharp
                    case LiveViewAs v:
                        lock (_gate)
                        {
                            if (_live is { } live && live.Players.Any(pl => pl.CountryId == v.CountryId))
                            {
                                peer.LiveCountry = v.CountryId;
                                peer.LiveChosen = true;
                                SendLive(peer);
                            }
                        }
                        continue;
```
Use `continue`: no roster broadcast is needed. Check how the `switch`/`BroadcastRoster` loop is structured and keep it consistent.

`Peer.Name` is the cleaned name. `CleanName` may return a placeholder for empty names; check that, and the matching still works.

In `SessionClient`:
- Add the property:
```csharp
    /// <summary>The latest live save view from the host (null until the host has one).</summary>
    public LiveUpdate? Live { get; private set; }
```
- Add `case LiveUpdate l: Live = l; break;` in the receive switch.
- Add the method:
```csharp
    public Task SendViewAsAsync(int countryId, CancellationToken ct = default) => SendAsync(new LiveViewAs(countryId), ct);
```

- [ ] **Step 4: Run the tests and the full suite.** Everything should PASS.
- [ ] **Step 5: Commit.** Message: `"feat: host shares filtered live saves with each client"`.

---

### Task 3: SessionService as IHostLiveSource

**Files:**
- Create: `FazStellarisModmanager.Core/Saves/IHostLiveSource.cs`
- Modify: `FazStellarisModmanager.Core/Session/SessionService.cs`
- Modify: `FazStellarisModmanager/AppServices.cs`. Pass the `LiveGameService`. Register LiveGameService **before** SessionService, and resolve it in the factory.

- [ ] **Step 1: Create the interface**

```csharp
using FazStellarisModmanager.Core.Session;

namespace FazStellarisModmanager.Core.Saves;

/// <summary>Live data received from a session host (implemented by SessionService).</summary>
public interface IHostLiveSource
{
    /// <summary>The last update from the host; kept after the connection ends, until ForgetHostLive or a new join.</summary>
    LiveUpdate? HostLive { get; }
    DateTime? HostLiveReceivedUtc { get; }
    /// <summary>True while connected to a host as a client.</summary>
    bool IsClient { get; }
    string? HostName { get; }
    event Action? Changed;
    Task ViewAsAsync(int countryId);
    /// <summary>Drops the kept host data (ignored while connected as a client).</summary>
    void ForgetHostLive();
}
```

- [ ] **Step 2: Change SessionService**
- **Constructor:** `SessionService(ModManagerService manager, IWorkshopService? workshop = null, LiveGameService? live = null) : IAsyncDisposable, IHostLiveSource`.
- **Fields:** `volatile LiveUpdate? _hostLive; DateTime? _hostLiveReceivedUtc; LiveUpdate? _seenLive;`.
- **Members:**
  - `IsClient => _client is not null`
  - `HostName => _client?.Roster.FirstOrDefault(p => p.IsHost)?.Name`
  - `HostLive`
  - `HostLiveReceivedUtc`
- **`JoinAsync`:**
  - Before connecting, set `_hostLive = null; _hostLiveReceivedUtc = null; _seenLive = null;`.
  - In the client `Changed` handler, before `RaiseChanged()`, add:
```csharp
                if (client.Live is { } l && !ReferenceEquals(l, _seenLive))
                {
                    _seenLive = l;
                    _hostLive = l;
                    _hostLiveReceivedUtc = DateTime.UtcNow;
                }
```
- **`OnDisconnected` and `CloseAsync`:** do NOT clear `_hostLive`.
- **New methods:**
```csharp
    public Task ViewAsAsync(int countryId) => _client is { } c ? c.SendViewAsAsync(countryId) : Task.CompletedTask;

    public void ForgetHostLive()
    {
        if (_client is not null) return;
        _hostLive = null;
        _hostLiveReceivedUtc = null;
        RaiseChanged();
    }
```
- **Hosting:**
  - In `HostAsync`, after `_host = host;`, add:
```csharp
        if (live is not null)
        {
            live.Changed += PushLive;
            host.UpdateLive(live.Current);
        }
```
  - Add a method:
```csharp
    void PushLive()
    {
        if (_host is { } h && live is not null) h.UpdateLive(live.Current);
    }
```
  - In `CloseAsync`, where the host is disposed, add `if (live is not null) live.Changed -= PushLive;`.
  - `UpdateLive` is cheap and never waits, so calling it on every `Changed` (including "reading" notifications) is fine.
- **Primary constructor parameter:** SessionService uses a primary constructor (`SessionService(ModManagerService manager, IWorkshopService? workshop = null)`). Add `LiveGameService? live = null` to it.
- **AppServices:** `services.AddSingleton(sp => new SessionService(sp.GetRequiredService<ModManagerService>(), sp.GetRequiredService<IWorkshopService>(), sp.GetRequiredService<LiveGameService>()));`. Move the LiveGameService registration above it, if it isn't already.

- [ ] **Step 3: Build and run the full suite.** Existing SessionService tests must still pass, because the new parameter is optional.
- [ ] **Step 4: Commit.** Message: `"feat: session service pushes live saves when hosting and keeps host data as a client"`.

---

### Task 4: LiveFeed

**Files:**
- Create: `FazStellarisModmanager.Core/Saves/LiveFeed.cs`
- Test: `FazStellarisModmanager.Tests/LiveFeedTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class LiveFeedTests
{
    sealed class FakeHost : IHostLiveSource
    {
        public LiveUpdate? HostLive { get; set; }
        public DateTime? HostLiveReceivedUtc { get; set; }
        public bool IsClient { get; set; }
        public string? HostName { get; set; } = "Hosty";
        public event Action? Changed;
        public List<int> Sent { get; } = [];
        public Task ViewAsAsync(int countryId) { Sent.Add(countryId); return Task.CompletedTask; }
        public void ForgetHostLive() { if (!IsClient) { HostLive = null; Raise(); } }
        public void Raise() => Changed?.Invoke();
    }

    static SaveCountry C(int id, params string[] techs) =>
        new(id, "default", "N" + id, new Dictionary<string, string>(), null, null, id + 1, 1, 1, 1, 1, 1, 1, 1, null, [], techs);

    static GameSnapshot Snap(string name, params SavePlayer[] players) =>
        new(name, "2300.01.01", "v", "x.sav", DateTime.UtcNow, players, [C(0, "tech_a"), C(1, "tech_b")]);

    [Fact]
    public async Task Local_source_resolves_viewer_and_researched()
    {
        using var t = new TempDir();
        var save = t.Write("g/1.sav", "x");
        using var local = new LiveGameService(() => t.Path, _ => Snap("Solo", new SavePlayer("Me", 1)), TimeSpan.Zero);
        var host = new FakeHost();
        using var feed = new LiveFeed(local, host, new LiveViewerStore(Path.Combine(t.Path, "v.json")), () => ["Me"]);
        await local.RefreshAsync();

        Assert.Equal(LiveSource.Local, feed.Source);
        Assert.Equal(1, feed.ViewerId);
        Assert.Equal(["tech_b"], feed.ResearchedTechs.Order());
    }

    [Fact]
    public async Task Host_source_while_connected_then_kept_after_disconnect()
    {
        using var t = new TempDir();
        using var local = new LiveGameService(() => null, _ => throw new InvalidOperationException(), TimeSpan.Zero);
        var host = new FakeHost { IsClient = true, HostLive = new LiveUpdate(Snap("MP", new SavePlayer("A", 0), new SavePlayer("B", 1)), 0), HostLiveReceivedUtc = DateTime.UtcNow };
        using var feed = new LiveFeed(local, host, new LiveViewerStore(Path.Combine(t.Path, "v.json")), () => ["B"]);
        var changes = 0;
        feed.Changed += () => changes++;
        host.Raise();

        Assert.Equal(LiveSource.Host, feed.Source);
        Assert.Equal(0, feed.ViewerId); // the host decides
        Assert.Equal(["tech_a"], feed.ResearchedTechs);
        Assert.True(changes > 0);

        await feed.SetViewerAsync(1);
        Assert.Equal([1], host.Sent);

        host.IsClient = false;
        host.Raise();
        Assert.Equal(LiveSource.HostDisconnected, feed.Source);
        Assert.NotNull(feed.Current);

        feed.UseLocal();
        Assert.Equal(LiveSource.Local, feed.Source);
        Assert.Null(feed.Current);
    }

    [Fact]
    public async Task Local_viewer_choice_is_remembered()
    {
        using var t = new TempDir();
        t.Write("g/1.sav", "x");
        var store = new LiveViewerStore(Path.Combine(t.Path, "v.json"));
        using var local = new LiveGameService(() => t.Path, _ => Snap("Duo", new SavePlayer("A", 0), new SavePlayer("B", 1)), TimeSpan.Zero);
        using var feed = new LiveFeed(local, new FakeHost(), store, () => ["nobody"]);
        await local.RefreshAsync();
        Assert.Null(feed.ViewerId);
        Assert.Empty(feed.ResearchedTechs);

        await feed.SetViewerAsync(1);
        Assert.Equal(1, feed.ViewerId);
        Assert.Equal(1, store.Get("Duo"));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**

- [ ] **Step 3: Implement**

```csharp
namespace FazStellarisModmanager.Core.Saves;

public enum LiveSource
{
    /// <summary>This PC's own saves (single player, or hosting).</summary>
    Local,
    /// <summary>Connected to a session host that sent live data.</summary>
    Host,
    /// <summary>The last host data, kept after the connection ended.</summary>
    HostDisconnected,
}

/// <summary>
/// The app's one view of the live game: the host's data while connected as a client (and after it disconnects, until
/// <see cref="UseLocal"/>), otherwise this PC's saves. Knows which country is "you" and its researched techs.
/// <see cref="Changed"/> fires on any thread.
/// </summary>
public sealed class LiveFeed : IDisposable
{
    readonly LiveGameService _local;
    readonly IHostLiveSource _host;
    readonly LiveViewerStore _viewers;
    readonly Func<IEnumerable<string?>> _myNames;
    GameSnapshot? _resolvedFor;
    int? _localViewer;

    public LiveFeed(LiveGameService local, IHostLiveSource host, LiveViewerStore viewers, Func<IEnumerable<string?>> myNames)
    {
        _local = local;
        _host = host;
        _viewers = viewers;
        _myNames = myNames;
        _local.Changed += Raise;
        _host.Changed += Raise;
    }

    public event Action? Changed;

    public LiveSource Source => _host.HostLive is null ? LiveSource.Local : _host.IsClient ? LiveSource.Host : LiveSource.HostDisconnected;

    public GameSnapshot? Current => Source == LiveSource.Local ? _local.Current : _host.HostLive?.Snapshot;

    /// <summary>When the host data arrived (host sources only).</summary>
    public DateTime? ReceivedUtc => Source == LiveSource.Local ? null : _host.HostLiveReceivedUtc;

    public string? HostName => _host.HostName;
    public string? Status => Source == LiveSource.Local ? _local.Status : null;
    public string? Error => Source == LiveSource.Local ? _local.Error : null;
    public bool Reading => Source == LiveSource.Local && _local.Reading;

    public int? ViewerId => Source == LiveSource.Local ? LocalViewer() : _host.HostLive?.ViewerId;

    /// <summary>The viewer's researched tech keys (empty when unknown).</summary>
    public IReadOnlySet<string> ResearchedTechs =>
        Current is { } s && ViewerId is int id && s.Countries.FirstOrDefault(c => c.Id == id) is { } me
            ? me.Techs.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public void Start() => _local.Start();

    /// <summary>Local: remembered for this save. Host: sent to the host, which replies with a new view.</summary>
    public async Task SetViewerAsync(int? countryId)
    {
        if (Source == LiveSource.Local)
        {
            _localViewer = countryId;
            _resolvedFor = _local.Current;
            if (countryId is int id && _local.Current is { } s)
            {
                try { _viewers.Set(s.SaveName, id); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            Raise();
        }
        else if (Source == LiveSource.Host && countryId is int hostId)
        {
            await _host.ViewAsAsync(hostId);
        }
    }

    /// <summary>Stop showing kept host data and go back to this PC's saves.</summary>
    public void UseLocal() => _host.ForgetHostLive();

    int? LocalViewer()
    {
        var s = _local.Current;
        if (s is null) return null;
        if (!ReferenceEquals(s, _resolvedFor))
        {
            _resolvedFor = s;
            _localViewer = LiveViewer.Resolve(s, _myNames(), _viewers.Get(s.SaveName));
        }
        return _localViewer;
    }

    void Raise()
    {
        try { Changed?.Invoke(); }
        catch (Exception) { /* a faulty subscriber must not break the feed */ }
    }

    public void Dispose()
    {
        _local.Changed -= Raise;
        _host.Changed -= Raise;
    }
}
```

- [ ] **Step 4: Run the tests and the full suite.** Everything should PASS.
- [ ] **Step 5: Commit.** Message: `"feat: LiveFeed - one live data source for local saves and session hosts"`.

---

### Task 5: Live Game page uses LiveFeed

**Files:**
- Modify: `FazStellarisModmanager/AppServices.cs`. Register LiveFeed:
```csharp
        services.AddSingleton(sp => new LiveFeed(
            sp.GetRequiredService<LiveGameService>(),
            sp.GetRequiredService<SessionService>(),
            sp.GetRequiredService<LiveViewerStore>(),
            () => [sp.GetRequiredService<ModManagerService>().Settings.PlayerName, Environment.UserName]));
```
- Modify: `FazStellarisModmanager/Pages/LiveGamePage.razor`

- [ ] **Step 1: Switch the page to LiveFeed.** Read the current page first, then:
  - **Injects:** inject `LiveFeed Feed` instead of `LiveGameService Live` and `LiveViewerStore Viewers`.
  - **Startup:** in `OnInitialized`, subscribe to `Feed.Changed` and call `Feed.Start()`.
  - **Data and viewer:** replace every `Live.Current/Status/Error/Reading` with `Feed.…`. Delete the page's own viewer resolution (`builtFor`/`LiveViewer.Resolve`/`Viewers`), and use `Feed.ViewerId`.
  - **Rebuild:** `me = Feed.ViewerId is int v ? Feed.Current?.Countries.FirstOrDefault(c => c.Id == v) : null; rows = Feed.ViewerId is int id && Feed.Current is { } s ? LiveBoard.Build(s, id) : [];`
  - **Animation key:** use `builtFor = Feed.Current is { } s ? s.SavePath + "|" + s.Date + "|" + s.SavedUtc.Ticks : null`. Host data has only a file name, so the date is part of the key.
  - **Status bar by `Feed.Source`:**
    - **Local:** as before.
    - **Host:** the green dot, then `<b>Live from @(Feed.HostName ?? "the host")</b>` and `<span class="muted">· game date @s.Date · received @Ago(Feed.ReceivedUtc!.Value)</span>`.
    - **HostDisconnected:** a yellow `.live-bar.warn` dot (add CSS `.live-bar.warn .dot { background: #f2c94c; }`). Text: `<b>Host disconnected</b> <span class="muted">· showing @s.Date from @Ago(Feed.ReceivedUtc!.Value)</span>`. Add a button `<button class="small" @onclick="() => Feed.UseLocal()">Use my own saves</button>`.
  - **"Viewing as" picker:** call `await Feed.SetViewerAsync(id)` (or `SetViewerAsync(null)`). Show the picker when `Feed.Current.Players.Count > 1` and the source is Local or Host. A Host-source snapshot with `ViewerId == null` has no countries; show the "Choose who you are" message, as for local.
  - **Dispose:** unsubscribe from `Feed.Changed`.
- [ ] **Step 2: Build and run the full suite.** Expect 0 errors and everything passing.
- [ ] **Step 3: Commit.** Message: `"feat: Live Game tab shows host data in sessions, with a host-disconnected state"`.

---

### Task 6: Tech tab researched techs come from LiveFeed

**Files:**
- Modify: `FazStellarisModmanager/Pages/TechPage.razor`
- Modify: the components that take `OnToggleResearched`. Find them with `grep -rn "OnToggleResearched" FazStellarisModmanager/Components FazStellarisModmanager/Pages`. Expected: `TechSidebar.razor`, `TechOverviewView.razor` (and probably `OverviewLayer.razor`), and possibly `TechGraphView.razor`.

- [ ] **Step 1: Remove manual marking**
  - **Components:** delete the `OnToggleResearched` parameter and every UI that triggers it: the right-click (`@oncontextmenu`) handlers that toggle researched, and any "Mark researched" button in the sidebar. Keep the `Researched` input parameters; they still drive the ✓ marks and colours.
  - **TechPage:**
    - Delete `ToggleResearched`.
    - Inject `LiveFeed Feed`. Subscribe to `Feed.Changed` (`InvokeAsync(() => { UpdateResearched(); StateHasChanged(); })`), call `Feed.Start()`, and unsubscribe on dispose. The page already implements `IDisposable` or `IAsyncDisposable`; check which.
    - Researched = `Feed.ResearchedTechs`. Rebuild the route when it changes: the code that builds `route` from `researched` must run again.
    - When saving `ResearchState`, write `Researched` as an empty list (`new ResearchState([], [.. targets])`), and ignore `state.Researched` when loading.
    - The help text "right-click: researched ·" becomes "Researched techs come from Live Game ·".
  - **Source line** (near the RouteBar / help text), as a `<span class="small">`:
    - **With live data and a viewer:** "Researched: N techs from Live Game (@Feed.Current.Date)". N is the count present in the tree, which the existing `researchedInTree` already computes.
    - **Otherwise:** "No live game data: open the Live Game tab with a save, or join a session."
- [ ] **Step 2: Build and run the full suite.** Expect 0 errors and everything passing. Some tests might reference `ResearchState.Researched`; keep the record unchanged so they still compile.
- [ ] **Step 3: Commit.** Message: `"feat: Tech tab takes researched techs from Live Game; manual marking removed"`.

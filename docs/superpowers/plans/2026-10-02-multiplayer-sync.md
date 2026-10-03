# Multiplayer Sync Implementation Plan (Sub-project 2 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let one player host a session from the app and others join over direct IP/TCP.
- Everyone sees a roster of who matches the host.
- Clients see exactly how their mods differ from the host's.
- Clients get a **Match host** button that writes the host's mod list (in the host's order) to their `dlc_load.json`.

**Architecture:** New code goes in `FazStellarisModmanager.Core/Session/`:
- **`SessionMessages`:** message records serialized as polymorphic JSON.
- **`Framing`:** 4-byte little-endian length, then gzip JSON.
- **`SessionHost`:** a TcpListener with one task per client. It diffs every client against the host with the existing `ModDiffer` and broadcasts the roster.
- **`SessionClient`:** connects, receives the host's target, and sends its own snapshots.
- **`MatchPlan`:** pure logic that builds the list to apply from the host's list and the local library.
- **`SessionService`:** a UI-facing facade around the above, using the existing `ModManagerService`, `SnapshotScanner` and `HashCache`.

A new `Session` Razor page and a `DiffView` component show it all.

**Tech Stack:**
- .NET 10 and C#.
- `System.Net.Sockets` (TcpListener/TcpClient).
- `System.Text.Json` polymorphism (`[JsonPolymorphic]`).
- `System.IO.Compression.GZipStream`.
- `System.Buffers.Binary.BinaryPrimitives`.
- xUnit; the tests run a real loopback TCP session on port 0.

**Spec:** `docs/superpowers/specs/2026-10-02-mod-manager-design.md`, section "Sub-project 2: Multiplayer sync".

**Out of scope:** Workshop subscribe/download and forced re-download (sub-project 3). In this plan, `MatchPlan` *reports* Workshop mods that are missing or out of date, so the UI can list them. It does not install them.

**Conventions for every task:**
- Repo root: `C:\Users\SCP Fazbear\source\repos\FazStellarisModmanager`. Use Git Bash. Work on branch `feature/multiplayer-sync`, created from `master` in Task 1.
- **Always use `-c Release`** for `dotnet build`/`dotnet test`. A stuck process may lock the Debug output.
- **Never type the two characters backslash + lowercase u** in source. The editing tools mangle it, so write `(char)0xNNNN` instead.
- Commit messages end with a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Existing Core APIs this plan uses, as they are on `master`:
  - `ModManagerService(AppPaths, Func<string?, string?>? findGameDir = null)` with:
    - `Paths`, `Settings` (`AppSettings(GameDir, UserDir, PlayerName)`) and `Resolve()`, which returns `ResolvedPaths(UserDir, GameDir?, WorkshopDir?)`;
    - `RefreshLibraryAsync()`, `Library` (`IReadOnlyList<InstalledMod>`) and `ImportCurrent(name)`, which returns a `ModList`;
    - `Apply(ModList)`.
  - `SnapshotScanner.ScanAsync(userDir, gameDir, machineName, HashCache cache, IProgress<string>? progress, CancellationToken ct)` returns a `MachineSnapshot`.
  - `HashCache.Load(path)` and `.Save()`.
  - `ModDiffer.Diff(target, mine)` returns a `DiffResult` with:
    - `Mods`/`Dlcs`: lists of `UnitDiff(Key, Name, RemoteId, Status, TargetOrder, MineOrder, OutOfOrder, TargetVersion, MineVersion, Files)`;
    - `BaseFiles` (`FileDiff`);
    - `GameVersionMatches`, `IsMatch` and `IsReliable`;
    - `TargetWarnings` and `MineWarnings`.
  - `UnitStatus { Ok, Missing, Extra, ContentMismatch }`.
  - `ModList(Name, List<ModListEntry> Mods, List<string> DisabledDlcs)` and `ModListEntry(Key, Name, DescriptorRel, RemoteId)`.
  - `InstalledMod(Key, Name, DescriptorRel, RemoteId, Version, SupportedVersion, ContentPath, Source, Tags)`.
  - `ModKeys.WorkshopId(key)` returns `ulong?`.
  - Test fixtures `TestUtil/TempDir.cs` and `TestUtil/FakeInstall.cs`:
    - properties GameDir, WorkshopDir, UserDir, DataDir and Root, plus `Write(rel, content)`;
    - it lays out base game files, DLC dlc001, Workshop item 111 and `user/mod/local.mod`.

---

## File structure

```
FazStellarisModmanager.Core/Session/
  SessionMessages.cs     SessionMessage hierarchy (+ PlayerInfo, PlayerStatus, DiffSummary)
  SessionProtocol.cs     constants, ParseAddress, StatusFor, LocalAddresses
  Framing.cs             length-prefixed gzip JSON frames
  MatchPlan.cs           host list -> list to apply here + what still needs attention
  SessionHost.cs         TCP host: clients, diffs, roster broadcast
  SessionClient.cs       TCP client: welcome, roster/target updates, send snapshot/busy
  SessionService.cs      UI facade: host / join / rescan / match / leave
FazStellarisModmanager/
  Components/DiffView.razor   grouped DiffResult display
  Pages/Session.razor         Session tab
  AppServices.cs, MainLayout.razor, _Imports.razor, wwwroot/css/site.css   (modified)
FazStellarisModmanager.Tests/
  TestUtil/TestSnapshots.cs, TestUtil/Wait.cs
  SessionProtocolTests.cs, FramingTests.cs, MatchPlanTests.cs, SessionHostClientTests.cs, SessionServiceTests.cs
```

---

### Task 1: Branch, message types and protocol helpers

**Files:**
- Create: `FazStellarisModmanager.Core/Session/SessionMessages.cs`, `FazStellarisModmanager.Core/Session/SessionProtocol.cs`
- Create: `FazStellarisModmanager.Tests/TestUtil/TestSnapshots.cs`
- Test: `FazStellarisModmanager.Tests/SessionProtocolTests.cs`

- [ ] **Step 1: Create the branch**

```bash
git checkout master && git checkout -b feature/multiplayer-sync
```

- [ ] **Step 2: Create the `FazStellarisModmanager.Tests/TestUtil/TestSnapshots.cs` helper**

```csharp
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Tests.TestUtil;

/// <summary>Small hand-made snapshots and lists for session/diff tests.</summary>
public static class TestSnapshots
{
    public static MachineSnapshot Machine(string name, params string[] modKeys) =>
        new(name, "v4.4", "", DateTime.UtcNow,
            new ModSnapshot("base", "Stellaris", "checksum_manifest.txt", null, null, null, "", 0, [new ModFile("common/a.txt", "aa", 1)]),
            [],
            modKeys.Select((k, i) => new ModSnapshot(k, k, Rel(k), null, null, null, "", i + 1, [new ModFile("f.txt", "11", 1)])).ToList());

    public static ModList List(params string[] modKeys) =>
        new("Host list", modKeys.Select(k => new ModListEntry(k, k, Rel(k), null)).ToList(), []);

    /// <summary>"ugc:1" -> "mod/ugc_1.mod", matching ModKeys.</summary>
    public static string Rel(string key) => "mod/" + key.Replace(':', '_') + ".mod";
}
```

- [ ] **Step 3: Write the failing tests in `FazStellarisModmanager.Tests/SessionProtocolTests.cs`**

```csharp
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class SessionProtocolTests
{
    [Theory]
    [InlineData("1.2.3.4", "1.2.3.4", SessionProtocol.DefaultPort)]
    [InlineData(" myhost:1234 ", "myhost", 1234)]
    [InlineData("10.0.0.5:27015", "10.0.0.5", 27015)]
    public void Parses_addresses(string input, string host, int port) =>
        Assert.Equal((host, port), SessionProtocol.ParseAddress(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(":80")]
    [InlineData("host:0")]
    [InlineData("host:70000")]
    [InlineData("host:abc")]
    public void Rejects_bad_addresses(string input) =>
        Assert.Throws<ArgumentException>(() => SessionProtocol.ParseAddress(input));

    [Fact]
    public void Status_reflects_match_mismatch_and_reliability()
    {
        var host = TestSnapshots.Machine("H", "ugc:1", "ugc:2");

        Assert.Equal(PlayerStatus.Ready, SessionProtocol.StatusFor(ModDiffer.Diff(host, TestSnapshots.Machine("A", "ugc:1", "ugc:2"))));
        Assert.Equal(PlayerStatus.Mismatch, SessionProtocol.StatusFor(ModDiffer.Diff(host, TestSnapshots.Machine("B", "ugc:1"))));
        var withWarnings = TestSnapshots.Machine("C", "ugc:1", "ugc:2") with { Warnings = ["  [unreadable] x"] };
        Assert.Equal(PlayerStatus.Unreliable, SessionProtocol.StatusFor(ModDiffer.Diff(host, withWarnings)));
    }

    [Fact]
    public void Summary_counts_and_describes_differences()
    {
        var host = TestSnapshots.Machine("H", "ugc:1", "ugc:2", "ugc:3");
        var mine = TestSnapshots.Machine("M", "ugc:3", "ugc:1");

        var s = DiffSummary.From(ModDiffer.Diff(host, mine));

        Assert.Equal((1, 0, 0, 1), (s.Missing, s.Extra, s.Different, s.OutOfOrder));
        Assert.Equal("1 missing, load order differs", s.ToString());
        Assert.Equal("matches", DiffSummary.From(ModDiffer.Diff(host, host)).ToString());
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter SessionProtocolTests`
Expected: build FAILS with `The type or namespace name 'Session' does not exist`.

- [ ] **Step 5: Implement `FazStellarisModmanager.Core/Session/SessionMessages.cs`**

```csharp
using System.Text.Json.Serialization;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Session;

/// <summary>Everything sent over a session connection. Serialized as JSON with a "type" discriminator.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(Reject), "reject")]
[JsonDerivedType(typeof(SnapshotUpdate), "snapshot")]
[JsonDerivedType(typeof(HostTargetUpdate), "hostTarget")]
[JsonDerivedType(typeof(ClientBusy), "busy")]
[JsonDerivedType(typeof(Roster), "roster")]
[JsonDerivedType(typeof(Bye), "bye")]
public abstract record SessionMessage;

/// <summary>Client to host, first message.</summary>
public sealed record Hello(int ProtocolVersion, string AppVersion, string PlayerName) : SessionMessage;

/// <summary>Host to client after a valid Hello: the client's id plus the host's list and snapshot (the target to match).</summary>
public sealed record Welcome(string PlayerId, ModList HostList, MachineSnapshot HostSnapshot) : SessionMessage;

/// <summary>Host to client instead of Welcome; the connection is closed afterwards.</summary>
public sealed record Reject(string Reason) : SessionMessage;

/// <summary>Client to host: my current setup.</summary>
public sealed record SnapshotUpdate(MachineSnapshot Snapshot) : SessionMessage;

/// <summary>Host to clients: the host rescanned or changed its list.</summary>
public sealed record HostTargetUpdate(ModList HostList, MachineSnapshot HostSnapshot) : SessionMessage;

/// <summary>Client to host: I'm busy (e.g. matching the host); a SnapshotUpdate follows.</summary>
public sealed record ClientBusy(string Activity) : SessionMessage;

/// <summary>Host to clients: everyone in the session, host first.</summary>
public sealed record Roster(List<PlayerInfo> Players) : SessionMessage;

/// <summary>Either side: closing the connection on purpose.</summary>
public sealed record Bye(string? Reason) : SessionMessage;

public enum PlayerStatus { Waiting, Busy, Ready, Mismatch, Unreliable }

/// <summary>One roster row. Summary compares the player with the host (null for the host and before the first snapshot).</summary>
public sealed record PlayerInfo(string Id, string Name, bool IsHost, PlayerStatus Status, DiffSummary? Summary, string? Activity);

/// <summary>Counts from a <see cref="DiffResult"/>, small enough to broadcast to everyone.</summary>
public sealed record DiffSummary(bool GameVersionMatches, int BaseFileDiffs, int DlcDiffs, int Missing, int Extra, int Different, int OutOfOrder)
{
    public static DiffSummary From(DiffResult d) => new(
        d.GameVersionMatches,
        d.BaseFiles.Changed.Count + d.BaseFiles.OnlyInTarget.Count + d.BaseFiles.OnlyInMine.Count,
        d.Dlcs.Count(x => x.Status != UnitStatus.Ok),
        d.Mods.Count(x => x.Status == UnitStatus.Missing),
        d.Mods.Count(x => x.Status == UnitStatus.Extra),
        d.Mods.Count(x => x.Status == UnitStatus.ContentMismatch),
        d.Mods.Count(x => x.OutOfOrder));

    public override string ToString()
    {
        var parts = new List<string>();
        if (!GameVersionMatches) parts.Add("game version differs");
        if (BaseFileDiffs > 0) parts.Add($"{BaseFileDiffs} base game file(s) differ");
        if (DlcDiffs > 0) parts.Add($"{DlcDiffs} DLC difference(s)");
        if (Missing > 0) parts.Add($"{Missing} missing");
        if (Different > 0) parts.Add($"{Different} different");
        if (Extra > 0) parts.Add($"{Extra} extra");
        if (OutOfOrder > 0) parts.Add("load order differs");
        return parts.Count == 0 ? "matches" : string.Join(", ", parts);
    }
}
```

- [ ] **Step 6: Implement `FazStellarisModmanager.Core/Session/SessionProtocol.cs`**

```csharp
using System.Net;
using System.Net.Sockets;
using FazStellarisModmanager.Core.Diff;

namespace FazStellarisModmanager.Core.Session;

public static class SessionProtocol
{
    /// <summary>Bump when messages change incompatibly; hosts reject clients with a different version.</summary>
    public const int Version = 1;
    public const int DefaultPort = 27015;

    public static string AppVersion => typeof(SessionProtocol).Assembly.GetName().Version?.ToString() ?? "0";

    /// <summary>"host" or "host:port". Throws ArgumentException with a user-facing message.</summary>
    public static (string Host, int Port) ParseAddress(string input)
    {
        var s = input.Trim();
        if (s.Length == 0) throw new ArgumentException("Enter the host's address.");
        var colon = s.LastIndexOf(':');
        if (colon < 0) return (s, DefaultPort);
        var host = s[..colon].Trim();
        if (host.Length == 0 || !int.TryParse(s[(colon + 1)..], out var port) || port is < 1 or > 65535)
            throw new ArgumentException($"'{input.Trim()}' is not a valid address. Use host or host:port.");
        return (host, port);
    }

    public static PlayerStatus StatusFor(DiffResult d) =>
        !d.IsReliable ? PlayerStatus.Unreliable : d.IsMatch ? PlayerStatus.Ready : PlayerStatus.Mismatch;

    /// <summary>This PC's IPv4 addresses, to tell friends where to connect.</summary>
    public static IReadOnlyList<string> LocalAddresses()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                .Select(a => a.ToString())
                .Distinct()
                .ToList();
        }
        catch (SocketException)
        {
            return [];
        }
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter SessionProtocolTests`
Expected: `Passed!  - Failed: 0, Passed: 11`

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Session: message types and protocol helpers

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Framing

**Files:**
- Create: `FazStellarisModmanager.Core/Session/Framing.cs`
- Test: `FazStellarisModmanager.Tests/FramingTests.cs`

The wire format is a 4-byte little-endian length of the body, followed by the body: gzip-compressed UTF-8 JSON of one `SessionMessage`. The body is capped at 64 MB, and decompression is capped at 512 MB, which guards against gzip bombs.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Tests;

public class FramingTests
{
    static async Task<SessionMessage?> RoundTrip(SessionMessage m)
    {
        var ms = new MemoryStream();
        await Framing.WriteAsync(ms, m);
        ms.Position = 0;
        return await Framing.ReadAsync(ms);
    }

    static byte[] FrameOf(byte[] body)
    {
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
        body.CopyTo(frame, 4);
        return frame;
    }

    static byte[] Gzip(string text)
    {
        var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(Encoding.UTF8.GetBytes(text));
        return ms.ToArray();
    }

    [Fact]
    public async Task Round_trips_messages()
    {
        Assert.Equal(new Hello(1, "1.0", "Faz"), await RoundTrip(new Hello(1, "1.0", "Faz")));
        var roster = (Roster)(await RoundTrip(new Roster([new PlayerInfo("a", "Alice", false, PlayerStatus.Mismatch, new DiffSummary(true, 0, 0, 2, 0, 1, 0), null)])))!;
        var p = Assert.Single(roster.Players);
        Assert.Equal(("Alice", PlayerStatus.Mismatch, 2), (p.Name, p.Status, p.Summary!.Missing));
        Assert.IsType<Bye>(await RoundTrip(new Bye(null)));
    }

    [Fact]
    public async Task Round_trips_a_large_snapshot()
    {
        var files = Enumerable.Range(0, 50_000).Select(i => new ModFile($"common/file_{i}.txt", i.ToString("x32"), i)).ToList();
        var snap = new MachineSnapshot("PC", "v4.4", "C:/game", DateTime.UtcNow,
            new ModSnapshot("base", "Stellaris", "", null, null, null, "", 0, files), [], [], ["w"]);
        var list = new ModList("L", [new ModListEntry("ugc:1", "A", "mod/ugc_1.mod", "1")], []);

        var back = (Welcome)(await RoundTrip(new Welcome("id", list, snap)))!;

        Assert.Equal(50_000, back.HostSnapshot.Base.Files.Count);
        Assert.Equal(files[123], back.HostSnapshot.Base.Files[123]);
        Assert.Equal("ugc:1", back.HostList.Mods.Single().Key);
        Assert.Equal(new[] { "w" }, back.HostSnapshot.Warnings);
    }

    [Fact]
    public async Task Header_is_little_endian_length_of_a_gzip_body()
    {
        var ms = new MemoryStream();
        await Framing.WriteAsync(ms, new Bye("x"));
        var bytes = ms.ToArray();

        Assert.Equal(bytes.Length - 4, BinaryPrimitives.ReadInt32LittleEndian(bytes));
        Assert.Equal(new byte[] { 0x1f, 0x8b }, bytes[4..6]);
    }

    [Fact]
    public async Task Clean_end_of_stream_reads_as_null() =>
        Assert.Null(await Framing.ReadAsync(new MemoryStream()));

    [Fact]
    public async Task Truncated_frames_throw_end_of_stream()
    {
        var ms = new MemoryStream();
        await Framing.WriteAsync(ms, new Bye("x"));
        var bytes = ms.ToArray();

        await Assert.ThrowsAsync<EndOfStreamException>(() => Framing.ReadAsync(new MemoryStream(bytes[..^3])));
        await Assert.ThrowsAsync<EndOfStreamException>(() => Framing.ReadAsync(new MemoryStream(bytes[..2])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(Framing.MaxFrameBytes + 1)]
    public async Task Bad_lengths_throw_invalid_data(int length)
    {
        var header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);

        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadAsync(new MemoryStream(header)));
    }

    [Fact]
    public async Task Garbage_body_throws_invalid_data() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadAsync(new MemoryStream(FrameOf([1, 2, 3]))));

    [Fact]
    public async Task Unknown_message_type_throws_invalid_data() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadAsync(new MemoryStream(FrameOf(Gzip("{\"type\":\"nope\"}")))));

    [Fact]
    public async Task Malformed_json_throws_invalid_data() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadAsync(new MemoryStream(FrameOf(Gzip("{not json")))));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter FramingTests`
Expected: build FAILS with `The name 'Framing' does not exist`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/Session/Framing.cs`**

```csharp
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FazStellarisModmanager.Core.Session;

/// <summary>Wire format: 4-byte little-endian body length, then the body (gzip-compressed UTF-8 JSON of one <see cref="SessionMessage"/>).</summary>
public static class Framing
{
    public const int MaxFrameBytes = 64 * 1024 * 1024;
    public const int MaxJsonBytes = 512 * 1024 * 1024;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static byte[] Encode(SessionMessage message)
    {
        using var body = new MemoryStream();
        using (var gz = new GZipStream(body, CompressionLevel.Fastest, leaveOpen: true))
            JsonSerializer.Serialize(gz, message, Json);
        if (body.Length > MaxFrameBytes) throw new InvalidDataException($"Message too large to send ({body.Length} bytes).");
        var payload = body.ToArray();
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    public static async Task WriteAsync(Stream stream, SessionMessage message, CancellationToken ct = default)
    {
        await stream.WriteAsync(Encode(message), ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Reads one message. Returns null when the peer closed the connection cleanly between frames.</summary>
    /// <exception cref="EndOfStreamException">The connection closed mid-frame.</exception>
    /// <exception cref="InvalidDataException">The frame is oversized, not gzip, not JSON, or an unknown message type.</exception>
    public static async Task<SessionMessage?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[4];
        var got = await stream.ReadAtLeastAsync(header, 4, throwOnEndOfStream: false, ct);
        if (got == 0) return null;
        if (got < 4) throw new EndOfStreamException("Connection closed mid-frame.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxFrameBytes) throw new InvalidDataException($"Invalid frame length {length}.");

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct);
        using var json = Inflate(body);
        try
        {
            return JsonSerializer.Deserialize<SessionMessage>(json, Json) ?? throw new InvalidDataException("Empty message.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidDataException($"Malformed message: {ex.Message}", ex);
        }
    }

    static MemoryStream Inflate(byte[] body)
    {
        using var gz = new GZipStream(new MemoryStream(body), CompressionMode.Decompress);
        var output = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = gz.Read(buffer)) > 0)
        {
            if (output.Length + n > MaxJsonBytes) throw new InvalidDataException("Message too large when decompressed.");
            output.Write(buffer, 0, n);
        }
        output.Position = 0;
        return output;
    }
}
```

`GZipStream` throws `InvalidDataException` for bytes that aren't gzip, which is exactly the exception callers expect.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter FramingTests`
Expected: `Passed!  - Failed: 0, Passed: 11`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Session: length-prefixed gzip JSON framing

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: MatchPlan

**Files:**
- Create: `FazStellarisModmanager.Core/Session/MatchPlan.cs`
- Test: `FazStellarisModmanager.Tests/MatchPlanTests.cs`

`MatchPlan.Create` builds the list to apply on this machine:
- **Installed mods:** the host's mods that are installed here, in the host's order, pointing at this machine's descriptors. Mods that aren't installed are left out of the list.
- **Missing Workshop mods** (`ugc:`) go in `NeedsWorkshopInstall` (sub-project 3 will install them).
- **Missing local mods** go in `NeedsManualInstall`.
- **Content mismatches** from the diff are split the same way: Workshop mods go in `NeedsWorkshopUpdate`, local mods in `DiffersLocally`.

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Session;

namespace FazStellarisModmanager.Tests;

public class MatchPlanTests
{
    static InstalledMod Installed(string key, string rel, string name) =>
        new(key, name, rel, null, null, null, "", key.StartsWith("ugc:") ? ModSource.Workshop : ModSource.Local, []);

    static DiffResult Diff(params UnitDiff[] mods) => new("v", "v", new FileDiff([], [], []), [], mods.ToList(), [], []);

    static UnitDiff Changed(string key) =>
        new(key, key, null, UnitStatus.ContentMismatch, 1, 1, false, null, null, new FileDiff(["f.txt"], [], []));

    [Fact]
    public void Applies_installed_mods_in_host_order_and_reports_missing_ones()
    {
        var host = new ModList("Host list",
        [
            new("ugc:2", "Two", "mod/ugc_2.mod", "2"),
            new("ugc:1", "One", "mod/ugc_1.mod", "1"),
            new("local:b.mod", "B", "mod/b.mod", null),
            new("local:a.mod", "A", "mod/a.mod", null),
        ], ["dlc/dlc001_x/dlc001.dlc"]);
        var library = new[] { Installed("local:a.mod", "mod/a.mod", "My A"), Installed("ugc:1", "mod/ugc_1.mod", "My One") };

        var plan = MatchPlan.Create(host, Diff(), library);

        Assert.Equal(new[] { "mod/ugc_1.mod", "mod/a.mod" }, plan.ToApply.Mods.Select(m => m.DescriptorRel));
        Assert.Equal(new[] { "My One", "My A" }, plan.ToApply.Mods.Select(m => m.Name));
        Assert.Equal(new[] { "dlc/dlc001_x/dlc001.dlc" }, plan.ToApply.DisabledDlcs);
        Assert.Equal(new[] { "ugc:2" }, plan.NeedsWorkshopInstall.Select(m => m.Key));
        Assert.Equal(new[] { "local:b.mod" }, plan.NeedsManualInstall.Select(m => m.Key));
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void Splits_content_mismatches_by_source()
    {
        var host = new ModList("Host list", [new("ugc:1", "One", "mod/ugc_1.mod", "1"), new("local:a.mod", "A", "mod/a.mod", null)], []);
        var library = new[] { Installed("ugc:1", "mod/ugc_1.mod", "One"), Installed("local:a.mod", "mod/a.mod", "A") };

        var plan = MatchPlan.Create(host, Diff(Changed("ugc:1"), Changed("local:a.mod")), library);

        Assert.Equal(new[] { "ugc:1" }, plan.NeedsWorkshopUpdate.Select(u => u.Key));
        Assert.Equal(new[] { "local:a.mod" }, plan.DiffersLocally.Select(u => u.Key));
        Assert.Equal(2, plan.ToApply.Mods.Count);
    }

    [Fact]
    public void Fully_installed_matching_list_is_complete()
    {
        var host = new ModList("Host list", [new("ugc:1", "One", "mod/ugc_1.mod", "1")], []);

        var plan = MatchPlan.Create(host, Diff(), [Installed("UGC:1", "mod/ugc_1.mod", "One")]);

        Assert.True(plan.IsComplete);
        Assert.Single(plan.ToApply.Mods);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter MatchPlanTests`
Expected: build FAILS with `The name 'MatchPlan' does not exist`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/Session/MatchPlan.cs`**

```csharp
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;

namespace FazStellarisModmanager.Core.Session;

/// <summary>What "Match host" does on this machine, and what it could not fix.</summary>
public sealed record MatchPlan(
    ModList ToApply,
    List<ModListEntry> NeedsWorkshopInstall,
    List<ModListEntry> NeedsManualInstall,
    List<UnitDiff> NeedsWorkshopUpdate,
    List<UnitDiff> DiffersLocally)
{
    public bool IsComplete =>
        NeedsWorkshopInstall.Count == 0 && NeedsManualInstall.Count == 0 && NeedsWorkshopUpdate.Count == 0 && DiffersLocally.Count == 0;

    /// <summary>
    /// The host's list restricted to mods installed here (in the host's order, pointing at this machine's descriptors),
    /// plus the host's disabled DLCs. Missing mods and content mismatches are reported, split into Workshop (fixable by
    /// sub-project 3) and local (manual).
    /// </summary>
    public static MatchPlan Create(ModList hostList, DiffResult diff, IReadOnlyList<InstalledMod> library)
    {
        var byKey = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in library) byKey.TryAdd(m.Key, m);

        var apply = new List<ModListEntry>();
        var workshop = new List<ModListEntry>();
        var manual = new List<ModListEntry>();
        foreach (var e in hostList.Mods)
        {
            if (byKey.TryGetValue(e.Key, out var mine)) apply.Add(new ModListEntry(mine.Key, mine.Name, mine.DescriptorRel, mine.RemoteId));
            else if (ModKeys.WorkshopId(e.Key) is not null) workshop.Add(e);
            else manual.Add(e);
        }

        var changed = diff.Mods.Where(u => u.Status == UnitStatus.ContentMismatch).ToList();
        return new MatchPlan(
            new ModList(hostList.Name, apply, hostList.DisabledDlcs.ToList()),
            workshop,
            manual,
            changed.Where(u => ModKeys.WorkshopId(u.Key) is not null).ToList(),
            changed.Where(u => ModKeys.WorkshopId(u.Key) is null).ToList());
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter MatchPlanTests`
Expected: `Passed!  - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Session: MatchPlan (host list to apply + what still needs fixing)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: SessionHost and SessionClient

**Files:**
- Create: `FazStellarisModmanager.Core/Session/SessionHost.cs`, `FazStellarisModmanager.Core/Session/SessionClient.cs`
- Create: `FazStellarisModmanager.Tests/TestUtil/Wait.cs`
- Test: `FazStellarisModmanager.Tests/SessionHostClientTests.cs`

The protocol works like this:
1. The client connects and sends `Hello`.
2. The host either replies `Reject` and closes, or replies `Welcome` (id, host list, host snapshot).
3. Only after `Welcome` is sent does the client join the roster. So `Welcome` is always the first message a client receives. If the host changed its target in the meantime, the client also gets a catch-up `HostTargetUpdate`.
4. The client sends a `SnapshotUpdate`. The host diffs it, sets the player's status, and broadcasts the `Roster` to all clients.
5. `ClientBusy` marks a player as busy until their next snapshot arrives.
6. A client that fails (IO error, malformed frame) only drops itself.
7. When the host closes, it sends `Bye` to everyone.

- [ ] **Step 1: Create `FazStellarisModmanager.Tests/TestUtil/Wait.cs`**

```csharp
using System.Diagnostics;

namespace FazStellarisModmanager.Tests.TestUtil;

public static class Wait
{
    /// <summary>Polls until the condition holds (network events arrive asynchronously).</summary>
    public static async Task Until(Func<bool> condition, string what, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException($"Timed out waiting for: {what}");
            await Task.Delay(20);
        }
    }
}
```

- [ ] **Step 2: Write the failing tests in `FazStellarisModmanager.Tests/SessionHostClientTests.cs`**

```csharp
using System.Net;
using System.Net.Sockets;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Tests.TestUtil;
using static FazStellarisModmanager.Tests.TestUtil.TestSnapshots;

namespace FazStellarisModmanager.Tests;

public class SessionHostClientTests
{
    static SessionHost StartHost(params string[] modKeys)
    {
        var host = new SessionHost("Hosty", List(modKeys), Machine("H", modKeys));
        host.Start(0, IPAddress.Loopback);
        return host;
    }

    [Fact]
    public async Task Clients_get_the_target_and_the_roster_reflects_their_diffs()
    {
        await using var host = StartHost("ugc:1", "ugc:2");
        await using var alice = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Alice");
        await using var bob = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Bob");

        Assert.Equal(new[] { "ugc:1", "ugc:2" }, alice.Target.HostList.Mods.Select(m => m.Key));
        await alice.SendSnapshotAsync(Machine("A", "ugc:1", "ugc:2"));
        await bob.SendSnapshotAsync(Machine("B", "ugc:1"));
        await Wait.Until(() => alice.Roster.Count == 3 && alice.Roster.All(p => p.Status != PlayerStatus.Waiting), "roster with both diffs");

        Assert.Equal(("Hosty", true), (alice.Roster[0].Name, alice.Roster[0].IsHost));
        Assert.Equal(PlayerStatus.Ready, alice.Roster.Single(p => p.Name == "Alice").Status);
        var b = alice.Roster.Single(p => p.Name == "Bob");
        Assert.Equal((PlayerStatus.Mismatch, 1), (b.Status, b.Summary!.Missing));
        Assert.Equal(UnitStatus.Missing, host.DiffFor(b.Id)!.Mods.Single(m => m.Key == "ugc:2").Status);
        Assert.Equal(3, host.Players.Count);
    }

    [Fact]
    public async Task Busy_shows_until_the_next_snapshot()
    {
        await using var host = StartHost("ugc:1");
        await using var alice = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Alice");

        await alice.SendBusyAsync("Matching host…");
        await Wait.Until(() => host.Players.Any(p => p.Status == PlayerStatus.Busy && p.Activity == "Matching host…"), "busy status");
        await alice.SendSnapshotAsync(Machine("A", "ugc:1"));
        await Wait.Until(() => host.Players.Any(p => p.Name == "Alice" && p.Status == PlayerStatus.Ready), "ready after snapshot");
    }

    [Fact]
    public async Task Host_updates_reach_clients_and_rediff_them()
    {
        await using var host = StartHost("ugc:1");
        await using var alice = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Alice");
        await alice.SendSnapshotAsync(Machine("A", "ugc:1"));
        await Wait.Until(() => alice.Roster.Any(p => p.Name == "Alice" && p.Status == PlayerStatus.Ready), "initially ready");

        await host.UpdateHostAsync(List("ugc:1", "ugc:3"), Machine("H", "ugc:1", "ugc:3"));

        await Wait.Until(() => alice.Target.HostList.Mods.Count == 2, "new host target");
        await Wait.Until(() => alice.Roster.Any(p => p.Name == "Alice" && p.Status == PlayerStatus.Mismatch), "re-diffed as mismatch");
    }

    [Fact]
    public async Task Leaving_clients_disappear_from_the_roster()
    {
        await using var host = StartHost("ugc:1");
        await using var alice = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Alice");
        var bob = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Bob");
        await Wait.Until(() => host.Players.Count == 3, "both joined");

        await bob.DisposeAsync();

        await Wait.Until(() => host.Players.Count == 2 && alice.Roster.Count == 2, "bob removed everywhere");
    }

    [Fact]
    public async Task Closing_the_host_tells_clients_why()
    {
        var host = StartHost("ugc:1");
        await using var alice = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Alice");
        string? reason = null;
        alice.Disconnected += r => reason = r;

        await host.DisposeAsync();
        await host.DisposeAsync(); // idempotent

        await Wait.Until(() => reason is not null, "disconnect notification");
        Assert.Contains("Host closed", reason);
        Assert.False(alice.IsConnected);
    }

    [Fact]
    public async Task Wrong_protocol_version_is_rejected()
    {
        await using var host = StartHost("ugc:1");
        using var raw = new TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, host.Port);

        await Framing.WriteAsync(raw.GetStream(), new Hello(99, "x", "Old"));
        var reply = await Framing.ReadAsync(raw.GetStream());

        Assert.Contains("99", Assert.IsType<Reject>(reply).Reason);
        Assert.Single(host.Players); // only the host
    }

    [Fact]
    public async Task A_garbage_client_does_not_break_the_host()
    {
        await using var host = StartHost("ugc:1");
        using (var raw = new TcpClient())
        {
            await raw.ConnectAsync(IPAddress.Loopback, host.Port);
            await raw.GetStream().WriteAsync(new byte[] { 0xff, 0xff, 0xff, 0x7f, 1, 2, 3 });
        }

        await using var alice = await SessionClient.ConnectAsync("127.0.0.1", host.Port, "Alice");

        await Wait.Until(() => host.Players.Count == 2, "alice joined despite garbage client");
    }

    [Fact]
    public async Task Connecting_to_a_closed_port_fails()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        await Assert.ThrowsAnyAsync<SocketException>(() => SessionClient.ConnectAsync("127.0.0.1", port, "Nobody"));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter SessionHostClientTests`
Expected: build FAILS with `The type or namespace name 'SessionHost' could not be found`.

- [ ] **Step 4: Implement `FazStellarisModmanager.Core/Session/SessionHost.cs`**

```csharp
using System.Net;
using System.Net.Sockets;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Session;

/// <summary>TCP host: accepts any number of clients, diffs each against the host's snapshot and broadcasts the roster.</summary>
public sealed class SessionHost : IAsyncDisposable
{
    public static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);
    public const string HostId = "host";

    sealed class Peer(string id, TcpClient tcp)
    {
        public string Id { get; } = id;
        public TcpClient Tcp { get; } = tcp;
        public NetworkStream Stream { get; } = tcp.GetStream();
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public string Name { get; set; } = "";
        public PlayerStatus Status { get; set; } = PlayerStatus.Waiting;
        public string? Activity { get; set; }
        public MachineSnapshot? Snapshot { get; set; }
        public DiffResult? Diff { get; set; }
    }

    readonly Lock _gate = new();
    readonly Dictionary<string, Peer> _peers = new();
    readonly CancellationTokenSource _cts = new();
    readonly string _hostName;
    TcpListener? _listener;
    Task? _acceptLoop;
    ModList _hostList;
    MachineSnapshot _hostSnapshot;
    bool _disposed;

    public SessionHost(string hostName, ModList hostList, MachineSnapshot hostSnapshot)
    {
        _hostName = hostName;
        _hostList = hostList;
        _hostSnapshot = hostSnapshot;
    }

    /// <summary>The port actually listened on (useful when started with port 0).</summary>
    public int Port { get; private set; }

    /// <summary>Raised on a background thread whenever the roster changes.</summary>
    public event Action? RosterChanged;

    /// <summary>Starts listening. Throws SocketException if the port is in use.</summary>
    public void Start(int port, IPAddress? address = null)
    {
        _listener = new TcpListener(address ?? IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    /// <summary>Host first, then clients by name.</summary>
    public IReadOnlyList<PlayerInfo> Players
    {
        get { lock (_gate) return BuildRoster(); }
    }

    /// <summary>The full diff of a client against the host (null before their first snapshot).</summary>
    public DiffResult? DiffFor(string playerId)
    {
        lock (_gate) return _peers.TryGetValue(playerId, out var p) ? p.Diff : null;
    }

    /// <summary>The host rescanned or changed its list: re-diff everyone and push the new target.</summary>
    public async Task UpdateHostAsync(ModList hostList, MachineSnapshot hostSnapshot)
    {
        List<Peer> peers;
        lock (_gate)
        {
            _hostList = hostList;
            _hostSnapshot = hostSnapshot;
            foreach (var p in _peers.Values) Rediff(p);
            peers = _peers.Values.ToList();
        }
        var update = new HostTargetUpdate(hostList, hostSnapshot);
        await Task.WhenAll(peers.Select(p => SendAsync(p, update)));
        await BroadcastRosterAsync();
    }

    async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await _listener!.AcceptTcpClientAsync(ct); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException || ct.IsCancellationRequested) { break; }
            catch (SocketException) { continue; }
            _ = Task.Run(() => HandleClientAsync(tcp, ct), CancellationToken.None);
        }
    }

    async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        tcp.NoDelay = true;
        var peer = new Peer(Guid.NewGuid().ToString("N")[..8], tcp);
        try
        {
            using (var helloCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                helloCts.CancelAfter(HelloTimeout);
                if (await Framing.ReadAsync(peer.Stream, helloCts.Token) is not Hello hello) return;
                if (hello.ProtocolVersion != SessionProtocol.Version)
                {
                    await SendAsync(peer, new Reject(
                        $"Your mod manager speaks protocol {hello.ProtocolVersion}, the host speaks {SessionProtocol.Version}. Update to the same version."));
                    return;
                }
                peer.Name = string.IsNullOrWhiteSpace(hello.PlayerName) ? "Player" : hello.PlayerName.Trim();
            }

            // Welcome goes out before the peer joins _peers, so it is always the first message the client sees.
            Welcome welcome;
            lock (_gate) welcome = new Welcome(peer.Id, _hostList, _hostSnapshot);
            await SendAsync(peer, welcome);
            HostTargetUpdate? catchUp = null;
            lock (_gate)
            {
                _peers[peer.Id] = peer;
                if (!ReferenceEquals(welcome.HostSnapshot, _hostSnapshot)) catchUp = new HostTargetUpdate(_hostList, _hostSnapshot);
            }
            if (catchUp is not null) await SendAsync(peer, catchUp);
            await BroadcastRosterAsync();

            while (await Framing.ReadAsync(peer.Stream, ct) is { } message)
            {
                switch (message)
                {
                    case SnapshotUpdate s:
                        lock (_gate)
                        {
                            peer.Snapshot = s.Snapshot;
                            peer.Activity = null;
                            Rediff(peer);
                        }
                        break;
                    case ClientBusy b:
                        lock (_gate)
                        {
                            peer.Status = PlayerStatus.Busy;
                            peer.Activity = b.Activity;
                        }
                        break;
                    case Bye:
                        return;
                    default:
                        continue;
                }
                await BroadcastRosterAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // A misbehaving or vanished client only drops itself.
        }
        finally
        {
            bool removed;
            lock (_gate) removed = _peers.Remove(peer.Id);
            tcp.Dispose();
            if (removed && !ct.IsCancellationRequested) await BroadcastRosterAsync();
        }
    }

    // Caller holds _gate.
    void Rediff(Peer p)
    {
        if (p.Snapshot is null)
        {
            p.Diff = null;
            if (p.Status != PlayerStatus.Busy) p.Status = PlayerStatus.Waiting;
            return;
        }
        p.Diff = ModDiffer.Diff(_hostSnapshot, p.Snapshot);
        p.Status = SessionProtocol.StatusFor(p.Diff);
    }

    // Caller holds _gate.
    List<PlayerInfo> BuildRoster()
    {
        var hostStatus = (_hostSnapshot.Warnings?.Count ?? 0) > 0 ? PlayerStatus.Unreliable : PlayerStatus.Ready;
        var roster = new List<PlayerInfo> { new(HostId, _hostName, true, hostStatus, null, null) };
        roster.AddRange(_peers.Values
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PlayerInfo(p.Id, p.Name, false, p.Status, p.Diff is null ? null : DiffSummary.From(p.Diff), p.Activity)));
        return roster;
    }

    async Task BroadcastRosterAsync()
    {
        Roster roster;
        List<Peer> peers;
        lock (_gate)
        {
            roster = new Roster(BuildRoster());
            peers = _peers.Values.ToList();
        }
        RosterChanged?.Invoke();
        await Task.WhenAll(peers.Select(p => SendAsync(p, roster)));
    }

    async Task SendAsync(Peer p, SessionMessage message)
    {
        try
        {
            await p.WriteLock.WaitAsync(_cts.Token);
            try { await Framing.WriteAsync(p.Stream, message, _cts.Token); }
            finally { p.WriteLock.Release(); }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
            p.Tcp.Dispose(); // its reader loop notices and removes the peer
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        List<Peer> peers;
        lock (_gate) peers = _peers.Values.ToList();
        await Task.WhenAll(peers.Select(p => SendAsync(p, new Bye("Host closed the session."))));
        _cts.Cancel();
        _listener?.Stop();
        foreach (var p in peers) p.Tcp.Dispose();
        if (_acceptLoop is not null) await _acceptLoop;
    }
}
```

`_cts` is deliberately not disposed. Reader tasks that are still finishing may read its token, and reading the token of a cancelled source is safe, whereas a disposed one would throw.

- [ ] **Step 5: Implement `FazStellarisModmanager.Core/Session/SessionClient.cs`**

```csharp
using System.Net.Sockets;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Session;

public sealed class SessionRejectedException(string reason) : Exception(reason);

/// <summary>TCP client side of a session.</summary>
public sealed class SessionClient : IAsyncDisposable
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    readonly TcpClient _tcp;
    readonly NetworkStream _stream;
    readonly SemaphoreSlim _writeLock = new(1, 1);
    readonly CancellationTokenSource _cts = new();
    Task? _receiveLoop;
    bool _disposed;

    SessionClient(TcpClient tcp, Welcome welcome)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        PlayerId = welcome.PlayerId;
        Target = new HostTargetUpdate(welcome.HostList, welcome.HostSnapshot);
    }

    public string PlayerId { get; }

    /// <summary>The host's current list and snapshot (replaced as a whole on every update).</summary>
    public HostTargetUpdate Target { get; private set; }

    public IReadOnlyList<PlayerInfo> Roster { get; private set; } = [];
    public bool IsConnected { get; private set; } = true;

    /// <summary>Raised on a background thread when the roster or host target changes.</summary>
    public event Action? Changed;

    /// <summary>Raised once, on a background thread, when the connection ends; the argument says why.</summary>
    public event Action<string>? Disconnected;

    /// <exception cref="SessionRejectedException">The host refused us (e.g. protocol version).</exception>
    /// <exception cref="TimeoutException">No answer within <see cref="ConnectTimeout"/>.</exception>
    /// <exception cref="SocketException">Nothing listening / unreachable.</exception>
    public static async Task<SessionClient> ConnectAsync(string host, int port, string playerName, CancellationToken ct = default)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ConnectTimeout);
            await tcp.ConnectAsync(host, port, timeout.Token);
            var stream = tcp.GetStream();
            await Framing.WriteAsync(stream, new Hello(SessionProtocol.Version, SessionProtocol.AppVersion, playerName), timeout.Token);
            switch (await Framing.ReadAsync(stream, timeout.Token))
            {
                case Welcome welcome:
                    var client = new SessionClient(tcp, welcome);
                    client._receiveLoop = client.ReceiveLoopAsync();
                    return client;
                case Reject reject:
                    throw new SessionRejectedException(reject.Reason);
                default:
                    throw new IOException("The host closed the connection without welcoming us.");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            tcp.Dispose();
            throw new TimeoutException($"No answer from {host}:{port} within {ConnectTimeout.TotalSeconds:0} seconds.");
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    public Task SendSnapshotAsync(MachineSnapshot snapshot, CancellationToken ct = default) => SendAsync(new SnapshotUpdate(snapshot), ct);

    public Task SendBusyAsync(string activity, CancellationToken ct = default) => SendAsync(new ClientBusy(activity), ct);

    async Task SendAsync(SessionMessage message, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try { await Framing.WriteAsync(_stream, message, ct); }
        finally { _writeLock.Release(); }
    }

    async Task ReceiveLoopAsync()
    {
        var reason = "The host closed the connection.";
        try
        {
            while (await Framing.ReadAsync(_stream, _cts.Token) is { } message)
            {
                switch (message)
                {
                    case Roster r:
                        Roster = r.Players;
                        break;
                    case HostTargetUpdate t:
                        Target = t;
                        break;
                    case Bye bye:
                        reason = bye.Reason ?? "The host ended the session.";
                        return;
                    default:
                        continue;
                }
                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            reason = "You left the session.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SocketException or ObjectDisposedException)
        {
            reason = _cts.IsCancellationRequested ? "You left the session." : $"Connection lost: {ex.Message}";
        }
        finally
        {
            IsConnected = false;
            Disconnected?.Invoke(reason);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsConnected)
        {
            try { await SendAsync(new Bye("Player left."), CancellationToken.None); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException) { }
        }
        _cts.Cancel();
        _tcp.Dispose();
        if (_receiveLoop is not null) await _receiveLoop;
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter SessionHostClientTests`
Expected: `Passed!  - Failed: 0, Passed: 8`

Then run the whole suite: `dotnet test FazStellarisModmanager.Tests -c Release`. Everything should pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Session: TCP host and client with roster broadcast

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: SessionService

**Files:**
- Create: `FazStellarisModmanager.Core/Session/SessionService.cs`
- Test: `FazStellarisModmanager.Tests/SessionServiceTests.cs`

`SessionService` is the UI facade. One operation runs at a time (host, join, rescan, match, leave), guarded by a semaphore. Every operation first rescans the library and snapshots this machine through `SnapshotScanner` and the persisted `HashCache`. `Changed` can fire on any thread.

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class SessionServiceTests
{
    /// <summary>One player's machine: a fake install plus its own app data, enabled mods and player name.</summary>
    sealed class Rig : IAsyncDisposable
    {
        public FakeInstall Fake { get; } = new();
        public SessionService Session { get; }

        public Rig(string playerName, string enabledModsJson)
        {
            var paths = new AppPaths(Fake.DataDir);
            SettingsStore.Save(paths.Settings, new AppSettings(UserDir: Fake.UserDir, PlayerName: playerName));
            Fake.Write("user/dlc_load.json", "{\"disabled_dlcs\":[],\"enabled_mods\":" + enabledModsJson + "}");
            Session = new SessionService(new ModManagerService(paths, _ => Fake.GameDir));
        }

        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            Fake.Dispose();
        }
    }

    [Fact]
    public async Task Joining_shows_the_difference_and_match_host_fixes_it()
    {
        await using var host = new Rig("Hosty", "[\"mod/ugc_111.mod\",\"mod/local.mod\"]");
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]");

        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);

        Assert.Equal(SessionRole.Client, client.Session.Role);
        Assert.Equal(UnitStatus.Missing, client.Session.MyDiff!.Mods.Single(m => m.Key == "ugc:111").Status);
        await Wait.Until(() => host.Session.Players.Any(p => p.Name == "Cli" && p.Status == PlayerStatus.Mismatch), "host sees mismatch");

        var plan = await client.Session.MatchHostAsync();

        Assert.True(plan.IsComplete);
        Assert.Equal(new[] { "mod/ugc_111.mod", "mod/local.mod" }, DlcLoadFile.Read(client.Fake.UserDir).EnabledMods);
        Assert.True(client.Session.MyDiff!.IsMatch);
        await Wait.Until(() => host.Session.Players.Any(p => p.Name == "Cli" && p.Status == PlayerStatus.Ready), "host sees ready");
    }

    [Fact]
    public async Task Host_rescan_pushes_the_new_list_to_clients()
    {
        await using var host = new Rig("Hosty", "[\"mod/local.mod\"]");
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]");
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);
        Assert.True(client.Session.MyDiff!.IsMatch);

        host.Fake.Write("user/dlc_load.json", "{\"disabled_dlcs\":[],\"enabled_mods\":[\"mod/ugc_111.mod\",\"mod/local.mod\"]}");
        await host.Session.RescanAsync();

        await Wait.Until(() => client.Session.MyDiff is { IsMatch: false }, "client re-diffed against new host list");
    }

    [Fact]
    public async Task Leaving_and_stopping_reset_state()
    {
        await using var host = new Rig("Hosty", "[\"mod/local.mod\"]");
        await using var client = new Rig("Cli", "[\"mod/local.mod\"]");
        await host.Session.HostAsync(0);
        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);
        await Wait.Until(() => host.Session.Players.Count == 2, "client joined");

        await client.Session.LeaveAsync();
        Assert.Equal(SessionRole.None, client.Session.Role);
        await Wait.Until(() => host.Session.Players.Count == 1, "client gone from host roster");

        await client.Session.JoinAsync("127.0.0.1", host.Session.HostPort!.Value);
        await host.Session.LeaveAsync();
        Assert.Equal(SessionRole.None, host.Session.Role);
        await Wait.Until(() => client.Session.Role == SessionRole.None, "client notices host stopped");
        Assert.Contains("Host closed", client.Session.LastDisconnectReason);
    }

    [Fact]
    public async Task Cannot_host_twice()
    {
        await using var host = new Rig("Hosty", "[]");
        await host.Session.HostAsync(0);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Session.HostAsync(0));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter SessionServiceTests`
Expected: build FAILS with `The type or namespace name 'SessionService' could not be found`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/Session/SessionService.cs`**

```csharp
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Hashing;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Session;

public enum SessionRole { None, Host, Client }

/// <summary>
/// UI-facing multiplayer state. One operation (host, join, rescan, match, leave) runs at a time;
/// <see cref="Changed"/> may fire on any thread.
/// </summary>
public sealed class SessionService(ModManagerService manager) : IAsyncDisposable
{
    readonly SemaphoreSlim _op = new(1, 1);
    volatile SessionHost? _host;
    volatile SessionClient? _client;
    volatile DiffResult? _myDiff;
    HashCache? _cache;

    public SessionRole Role => _host is not null ? SessionRole.Host : _client is not null ? SessionRole.Client : SessionRole.None;
    public int? HostPort => _host?.Port;
    public string? HostAddress { get; private set; }
    public MachineSnapshot? MySnapshot { get; private set; }

    /// <summary>Client only: this machine compared with the host.</summary>
    public DiffResult? MyDiff => _myDiff;

    public IReadOnlyList<PlayerInfo> Players => _host?.Players ?? _client?.Roster ?? [];
    public MatchPlan? LastPlan { get; private set; }

    /// <summary>What the current operation is doing (e.g. the scan's progress); null when idle.</summary>
    public string? Activity { get; private set; }

    /// <summary>Why the last client connection ended on its own (host stopped, network lost).</summary>
    public string? LastDisconnectReason { get; private set; }

    public event Action? Changed;

    string PlayerName => string.IsNullOrWhiteSpace(manager.Settings.PlayerName) ? Environment.MachineName : manager.Settings.PlayerName.Trim();

    /// <summary>Host only: a client's full diff against the host.</summary>
    public DiffResult? DiffFor(string playerId) => _host?.DiffFor(playerId);

    public Task HostAsync(int port, CancellationToken ct = default) => Exclusive(async () =>
    {
        if (Role != SessionRole.None) throw new InvalidOperationException("Already in a session. Leave it first.");
        var snapshot = await ScanAsync(ct);
        var host = new SessionHost(PlayerName, manager.ImportCurrent("Host list"), snapshot);
        host.RosterChanged += RaiseChanged;
        try { host.Start(port); }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
        _host = host;
        LastDisconnectReason = null;
        return true;
    }, ct);

    public Task JoinAsync(string address, int port, CancellationToken ct = default) => Exclusive(async () =>
    {
        if (Role != SessionRole.None) throw new InvalidOperationException("Already in a session. Leave it first.");
        var snapshot = await ScanAsync(ct);
        SetActivity($"Connecting to {address}:{port}…");
        var client = await SessionClient.ConnectAsync(address, port, PlayerName, ct);
        client.Changed += () =>
        {
            if (!ReferenceEquals(_client, client)) return;
            RecomputeDiff();
            RaiseChanged();
        };
        client.Disconnected += reason => OnDisconnected(client, reason);
        _client = client;
        HostAddress = $"{address}:{port}";
        LastDisconnectReason = null;
        RecomputeDiff();
        if (!client.IsConnected) OnDisconnected(client, "The host closed the connection.");
        else await client.SendSnapshotAsync(snapshot, ct);
        return true;
    }, ct);

    /// <summary>Rescans this machine. As host, pushes the new list to everyone; as client, sends the new snapshot.</summary>
    public Task RescanAsync(CancellationToken ct = default) => Exclusive(async () =>
    {
        var snapshot = await ScanAsync(ct);
        if (_host is { } host) await host.UpdateHostAsync(manager.ImportCurrent("Host list"), snapshot);
        else if (_client is { } client)
        {
            RecomputeDiff();
            await client.SendSnapshotAsync(snapshot, ct);
        }
        return true;
    }, ct);

    /// <summary>Client only: writes the host's list (installed mods, host order) to dlc_load.json, then rescans and reports.</summary>
    public Task<MatchPlan> MatchHostAsync(CancellationToken ct = default) => Exclusive(async () =>
    {
        var client = _client ?? throw new InvalidOperationException("Not connected to a host.");
        var diff = _myDiff ?? throw new InvalidOperationException("Your mods have not been scanned yet.");
        await client.SendBusyAsync("Matching host…", ct);
        SetActivity("Applying the host's mod list…");
        await manager.RefreshLibraryAsync();
        var plan = MatchPlan.Create(client.Target.HostList, diff, manager.Library);
        manager.Apply(plan.ToApply);
        LastPlan = plan;
        var snapshot = await ScanAsync(ct);
        RecomputeDiff();
        await client.SendSnapshotAsync(snapshot, ct);
        return plan;
    }, ct);

    public Task LeaveAsync() => Exclusive(async () =>
    {
        await CloseAsync();
        return true;
    }, CancellationToken.None);

    async Task CloseAsync()
    {
        var host = _host;
        var client = _client;
        _host = null;
        _client = null;
        _myDiff = null;
        HostAddress = null;
        LastPlan = null;
        if (host is not null)
        {
            host.RosterChanged -= RaiseChanged;
            await host.DisposeAsync();
        }
        if (client is not null) await client.DisposeAsync();
        RaiseChanged();
    }

    void OnDisconnected(SessionClient client, string reason)
    {
        if (!ReferenceEquals(_client, client)) return; // we left on purpose
        _client = null;
        _myDiff = null;
        HostAddress = null;
        LastDisconnectReason = reason;
        RaiseChanged();
        _ = client.DisposeAsync().AsTask();
    }

    async Task<MachineSnapshot> ScanAsync(CancellationToken ct)
    {
        SetActivity("Scanning mods…");
        await manager.RefreshLibraryAsync();
        var paths = manager.Resolve();
        var gameDir = paths.GameDir ?? throw new InvalidOperationException("Stellaris install not found. Set the game folder in Settings.");
        var cache = _cache ??= HashCache.Load(manager.Paths.HashCache);
        var progress = new ActivityProgress(this);
        var snapshot = await Task.Run(() => SnapshotScanner.ScanAsync(paths.UserDir, gameDir, PlayerName, cache, progress, ct), ct);
        try { cache.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* only a cache */ }
        MySnapshot = snapshot;
        return snapshot;
    }

    async Task<T> Exclusive<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _op.WaitAsync(ct);
        try { return await action(); }
        finally
        {
            SetActivity(null);
            _op.Release();
        }
    }

    sealed class ActivityProgress(SessionService owner) : IProgress<string>
    {
        public void Report(string value) => owner.SetActivity(value.Trim());
    }

    void SetActivity(string? text)
    {
        Activity = text;
        RaiseChanged();
    }

    void RecomputeDiff() =>
        _myDiff = _client is { } client && MySnapshot is { } mine ? ModDiffer.Diff(client.Target.HostSnapshot, mine) : null;

    void RaiseChanged() => Changed?.Invoke();

    public async ValueTask DisposeAsync() => await LeaveAsync();
}
```

`Exclusive` returns a value so a single helper covers both the `Task` and the `Task<T>` operations. The `Task` methods return `true` and expose it as a plain `Task`, which works because `Task<bool>` is a `Task`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --filter SessionServiceTests`
Expected: `Passed!  - Failed: 0, Passed: 4`

Then run the whole suite: `dotnet test FazStellarisModmanager.Tests -c Release`. Everything should pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Session: SessionService facade (host/join/rescan/match/leave)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Session tab UI

**Files:**
- Create: `FazStellarisModmanager/Components/DiffView.razor`, `FazStellarisModmanager/Pages/Session.razor`
- Modify: `FazStellarisModmanager/AppServices.cs`, `FazStellarisModmanager/MainLayout.razor`, `FazStellarisModmanager/_Imports.razor`, `FazStellarisModmanager/wwwroot/css/site.css`

- [ ] **Step 1: Register the service in `FazStellarisModmanager/AppServices.cs`**

Replace the two `AddSingleton` lines so they read:

```csharp
        services.AddSingleton(paths);
        services.AddSingleton(_ => new ModManagerService(paths));
        services.AddSingleton(sp => new SessionService(sp.GetRequiredService<ModManagerService>()));
```

and add `using FazStellarisModmanager.Core.Session;` at the top.

- [ ] **Step 2: Add the tab to `FazStellarisModmanager/MainLayout.razor`**

After the Mods `NavLink`, add:

```razor
        <NavLink href="session">Session</NavLink>
```

- [ ] **Step 3: Add namespaces to `FazStellarisModmanager/_Imports.razor`**

Append:

```razor
@using FazStellarisModmanager.Components
@using FazStellarisModmanager.Core.Diff
@using FazStellarisModmanager.Core.Session
```

- [ ] **Step 4: Create `FazStellarisModmanager/Components/DiffView.razor`**

```razor
@* Shows one DiffResult grouped by kind. "Who" names the compared machine: "you" or a player's name. *@

@if (!Diff.IsReliable)
{
    <p class="status warn">Some files could not be read, so some differences below may not be real. Close programs that lock mod files and rescan.</p>
    <details>
        <summary>Scan warnings (@(Diff.TargetWarnings.Count + Diff.MineWarnings.Count))</summary>
        <ul>
            @foreach (var w in Diff.TargetWarnings) { <li>Host: @w</li> }
            @foreach (var w in Diff.MineWarnings) { <li>@Who: @w</li> }
        </ul>
    </details>
}
@if (Diff.IsMatch)
{
    <p class="status ok">Everything matches the host.</p>
}
@if (!Diff.GameVersionMatches)
{
    <p class="status error">Game version differs: host @Diff.TargetGameVersion, @Who @Diff.MineGameVersion.</p>
}
@if (!Diff.BaseFiles.IsEmpty)
{
    <p class="status error">@BaseCount base game file(s) differ. Use "Verify integrity of game files" in Steam.</p>
}

@Section($"Missing (the host has these, {Who} don't)", Diff.Mods.Where(m => m.Status == UnitStatus.Missing).ToList())
@Section("Different files", Diff.Mods.Where(m => m.Status == UnitStatus.ContentMismatch).ToList())
@Section($"Extra ({Who} have these, the host doesn't)", Diff.Mods.Where(m => m.Status == UnitStatus.Extra).ToList())
@Section("Load order differs", Diff.Mods.Where(m => m.OutOfOrder).ToList())
@Section("DLC differences", Diff.Dlcs.Where(d => d.Status != UnitStatus.Ok).ToList())

@code {
    [Parameter, EditorRequired] public DiffResult Diff { get; set; } = default!;
    [Parameter] public string Who { get; set; } = "you";

    int BaseCount => Diff.BaseFiles.Changed.Count + Diff.BaseFiles.OnlyInTarget.Count + Diff.BaseFiles.OnlyInMine.Count;

    RenderFragment Section(string title, List<UnitDiff> units) =>
        @<div class="diff-group">
            @if (units.Count > 0)
            {
                <h3>@title (@units.Count)</h3>
                <ul>
                    @foreach (var u in units)
                    {
                        <li>
                            <span class="name">@u.Name</span> <span class="badge">@u.Key</span>
                            @if (u.Files is { } f)
                            {
                                <details>
                                    <summary>@f.Changed.Count changed, @f.OnlyInTarget.Count only on host, @f.OnlyInMine.Count only on @Who</summary>
                                    <ul class="files">
                                        @foreach (var p in f.Changed.Take(50)) { <li>~ @p</li> }
                                        @foreach (var p in f.OnlyInTarget.Take(50)) { <li>- @p</li> }
                                        @foreach (var p in f.OnlyInMine.Take(50)) { <li>+ @p</li> }
                                    </ul>
                                </details>
                            }
                        </li>
                    }
                </ul>
            }
        </div>;
}
```

- [ ] **Step 5: Create `FazStellarisModmanager/Pages/Session.razor`**

```razor
@page "/session"
@implements IDisposable
@inject SessionService Session

<h1>Multiplayer session</h1>

@if (Session.Role == SessionRole.None)
{
    @if (Session.LastDisconnectReason is { } reason)
    {
        <p class="status error">@reason</p>
    }
    <div class="session-forms">
        <section class="panel pad">
            <h2>Host a session</h2>
            <p class="status">Friends join with your IP address and this port. They must be able to reach it: forward the port on your router, or use Hamachi, ZeroTier or Tailscale.</p>
            <label>Port <input type="number" min="1" max="65535" @bind="hostPort" /></label>
            <div><button class="primary" @onclick="Host" disabled="@busy">Host</button></div>
        </section>
        <section class="panel pad">
            <h2>Join a session</h2>
            <label>Host address <input @bind="joinAddress" placeholder="203.0.113.5 or 203.0.113.5:27015" /></label>
            <div><button class="primary" @onclick="Join" disabled="@busy">Join</button></div>
        </section>
    </div>
}
else
{
    <div class="toolbar">
        @if (Session.Role == SessionRole.Host)
        {
            <span>Hosting on port <b>@Session.HostPort</b>. Your addresses: @(addresses.Count == 0 ? "unknown" : string.Join(", ", addresses))</span>
        }
        else
        {
            <span>Connected to <b>@Session.HostAddress</b></span>
        }
        <button @onclick="Rescan" disabled="@busy">Rescan my mods</button>
        @if (Session.Role == SessionRole.Client)
        {
            <button class="primary" @onclick="Match" disabled="@(busy || Session.MyDiff is null || Session.MyDiff.IsMatch)">Match host</button>
        }
        <button class="danger" @onclick="Leave" disabled="@busy">@(Session.Role == SessionRole.Host ? "Stop hosting" : "Leave")</button>
    </div>

    <table class="roster">
        <thead><tr><th>Player</th><th>Status</th><th>Compared with host</th></tr></thead>
        <tbody>
            @foreach (var p in Session.Players)
            {
                var player = p;
                <tr class="@(player.Id == selectedId ? "selected" : "")" @onclick="() => selectedId = player.Id">
                    <td>@player.Name@(player.IsHost ? " (host)" : "")</td>
                    <td><span class="badge @StatusClass(player.Status)">@StatusText(player)</span></td>
                    <td>@(player.IsHost ? "" : player.Summary?.ToString() ?? "")</td>
                </tr>
            }
        </tbody>
    </table>

    @if (Session.Role == SessionRole.Client && Session.MyDiff is { } mine)
    {
        <h2>Your mods compared with the host</h2>
        <DiffView Diff="mine" Who="you" />
    }
    else if (Session.Role == SessionRole.Host && selectedId is not null && Session.DiffFor(selectedId) is { } theirs)
    {
        <h2>@(Session.Players.FirstOrDefault(p => p.Id == selectedId)?.Name ?? "Player") compared with you</h2>
        <DiffView Diff="theirs" Who="they" />
    }
    else if (Session.Role == SessionRole.Host)
    {
        <p class="status">Click a player to see how their mods differ from yours.</p>
    }

    @if (Session.LastPlan is { } plan)
    {
        <section class="panel pad">
            <h2>Match host result</h2>
            <p>Wrote the host's list (@plan.ToApply.Mods.Count installed mods, in the host's order) to dlc_load.json.</p>
            @if (plan.NeedsWorkshopInstall.Count > 0)
            {
                <p class="status error">Not installed. Subscribe to these on the Steam Workshop, then click Rescan my mods:</p>
                <ul>@foreach (var e in plan.NeedsWorkshopInstall) { <li>@e.Name (Workshop id @ModKeys.WorkshopId(e.Key))</li> }</ul>
            }
            @if (plan.NeedsWorkshopUpdate.Count > 0)
            {
                <p class="status error">Out of date. Let Steam update these (or unsubscribe and resubscribe):</p>
                <ul>@foreach (var u in plan.NeedsWorkshopUpdate) { <li>@u.Name</li> }</ul>
            }
            @if (plan.NeedsManualInstall.Count > 0)
            {
                <p class="status error">Local mods you don't have. Ask the host to share them:</p>
                <ul>@foreach (var e in plan.NeedsManualInstall) { <li>@e.Name</li> }</ul>
            }
            @if (plan.DiffersLocally.Count > 0)
            {
                <p class="status error">Local mods whose files differ from the host's:</p>
                <ul>@foreach (var u in plan.DiffersLocally) { <li>@u.Name</li> }</ul>
            }
        </section>
    }
}

@if (Session.Activity is { } activity)
{
    <p class="status">@activity</p>
}
@if (status is not null)
{
    <p class="status @(error ? "error" : "")">@status</p>
}

@code {
    int hostPort = SessionProtocol.DefaultPort;
    string joinAddress = "";
    string? selectedId;
    string? status;
    bool busy;
    bool error;
    IReadOnlyList<string> addresses = [];

    protected override void OnInitialized()
    {
        Session.Changed += OnSessionChanged;
        addresses = SessionProtocol.LocalAddresses();
    }

    void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Session.Changed -= OnSessionChanged;

    Task Host() => RunAsync(async () =>
    {
        await Session.HostAsync(hostPort);
        return $"Hosting on port {Session.HostPort}. Windows may ask to allow the app through the firewall: allow it.";
    });

    Task Join() => RunAsync(async () =>
    {
        var (h, port) = SessionProtocol.ParseAddress(joinAddress);
        await Session.JoinAsync(h, port);
        return $"Joined {h}:{port}.";
    });

    Task Rescan() => RunAsync(async () =>
    {
        await Session.RescanAsync();
        return "Rescanned and shared your mods.";
    });

    Task Match() => RunAsync(async () =>
    {
        var plan = await Session.MatchHostAsync();
        return plan.IsComplete ? "Your mod list now matches the host." : "Applied the host's list, but some mods still need attention (see below).";
    });

    Task Leave() => RunAsync(async () =>
    {
        await Session.LeaveAsync();
        selectedId = null;
        return "Left the session.";
    });

    async Task RunAsync(Func<Task<string?>> action)
    {
        busy = true;
        (status, error) = (null, false);
        try
        {
            status = await action();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException
                                   or InvalidDataException or TimeoutException or SessionRejectedException
                                   or System.Net.Sockets.SocketException or OperationCanceledException)
        {
            (status, error) = (ex.Message, true);
        }
        finally
        {
            busy = false;
        }
    }

    static string StatusClass(PlayerStatus s) => s switch
    {
        PlayerStatus.Ready => "ready",
        PlayerStatus.Mismatch => "mismatch",
        PlayerStatus.Unreliable => "unreliable",
        _ => "busy",
    };

    static string StatusText(PlayerInfo p) => p.Status switch
    {
        PlayerStatus.Ready => "✔ Ready",
        PlayerStatus.Mismatch => "✖ Differs",
        PlayerStatus.Unreliable => "⚠ Check warnings",
        PlayerStatus.Busy => p.Activity ?? "Busy…",
        _ => "Waiting for scan…",
    };
}
```

- [ ] **Step 6: Append the session styles to `FazStellarisModmanager/wwwroot/css/site.css`**

```css
.session-forms { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; }
.panel.pad { padding: 1rem; gap: .6rem; margin-top: 1rem; }
.panel.pad label { display: flex; flex-direction: column; gap: .3rem; color: var(--muted); }
.toolbar span { align-self: center; margin-right: auto; }
table.roster { width: 100%; border-collapse: collapse; margin: 1rem 0; background: var(--panel); border: 1px solid var(--border); }
table.roster th, table.roster td { text-align: left; padding: .45rem .7rem; border-bottom: 1px solid var(--border); }
table.roster tbody tr { cursor: pointer; }
table.roster tr.selected td { background: var(--panel-2); }
.badge.ready, .status.ok { color: var(--ok); }
.badge.mismatch { color: var(--danger); }
.badge.unreliable, .status.warn { color: var(--warn); }
.badge.busy { color: var(--accent); }
.diff-group h3 { font-size: .95rem; margin: 1rem 0 .3rem; }
.diff-group ul { margin: 0; padding-left: 1.2rem; }
.diff-group .files { font-family: Consolas, monospace; font-size: .8rem; color: var(--muted); }
```

- [ ] **Step 7: Build, test and smoke-run**

Run: `dotnet build FazStellarisModmanager.sln -c Release`
Expected: `Build succeeded`, 0 warnings, 0 errors.

Run: `dotnet test FazStellarisModmanager.Tests -c Release`
Expected: all tests pass.

Then smoke-run the app with an isolated data dir:
1. Start `FazStellarisModmanager/bin/Release/net10.0-windows10.0.19041.0/FazStellarisModmanager.exe --data-dir <scratch dir>` in the background.
2. Wait 10 seconds and check with `tasklist` that the process is alive.
3. Kill it with `taskkill //IM FazStellarisModmanager.exe //F`.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Session tab: host/join, roster, diff view, Match host

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Manual two-instance check (human)

This can't be automated; the user does it on their PC with two app instances.

- [ ] **Step 1: Prepare a second "player"**
  1. Copy `Documents\Paradox Interactive\Stellaris\dlc_load.json`, the `mod` folder and `logs\game.log` into `C:\Temp\StellarisCopy\`, keeping that layout.
  2. Edit `C:\Temp\StellarisCopy\dlc_load.json` to remove one mod, and swap two others.

- [ ] **Step 2: Host.** Start the app normally, open Session, keep port 27015, and click **Host**.
  - The first scan hashes the whole base game, so it takes minutes. Later scans use the cache.
  - Allow the Windows firewall prompt for private networks.
  - The roster should show you as "(host) ✔ Ready".

- [ ] **Step 3: Join.**
  1. Start a second instance: `FazStellarisModmanager.exe --data-dir C:\Temp\fsmm-b`.
  2. In its Settings, set the documents folder to `C:\Temp\StellarisCopy` and the player name to `B`.
  3. On its Session tab, join `127.0.0.1`.
  4. Instance B should list 1 Missing mod and show "Load order differs".
  5. In instance A's roster, B shows "✖ Differs". Clicking B shows the same diff.

- [ ] **Step 4: Match.** In B, click **Match host**.
  - `C:\Temp\StellarisCopy\dlc_load.json` now has A's order, and the missing mod is included if it is installed (Workshop).
  - B shows "Everything matches the host", and A's roster shows B "✔ Ready".

- [ ] **Step 5: Host change.** In A, change the list on the Mods tab and Apply, then on Session click **Rescan my mods**. B's diff updates on its own, without B doing anything.

- [ ] **Step 6: Disconnects.**
  - Click **Leave** in B; A's roster drops B.
  - Rejoin, then click **Stop hosting** in A. B returns to the forms and shows "Host closed the session."

- [ ] **Step 7: Over the network (optional).** Have a friend join through your public IP with the port forwarded, or through Hamachi/ZeroTier/Tailscale.

---

## Follow-on

`docs/superpowers/plans/<date>-workshop-install.md` (sub-project 3) will add `FazStellarisModmanager.Steam` (Steamworks.NET), `IWorkshopService`, and an extended **Match host**. That version subscribes to `MatchPlan.NeedsWorkshopInstall`, force-updates `MatchPlan.NeedsWorkshopUpdate`, waits for the downloads, then re-runs the match.

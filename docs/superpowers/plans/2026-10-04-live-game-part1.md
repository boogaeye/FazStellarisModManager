# Live Game Part 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Live Game tab that reads the newest Stellaris save. It shows the viewer's empire in a sidebar and a ranked scoreboard with an animation when ranks change. Empires the viewer hasn't contacted are hidden as ???. Overflowed values are flagged, and diplomatic weight gets an estimate breakdown. Part 1 is local only; there is no session sharing yet.

**Architecture:**
- **Core `Saves/`:**
  - a byte-level `GamestateScanner` that extracts players and countries from the 400 MB `gamestate`, using `ParadoxScriptParser` only for small sub-blocks;
  - `SaveReader` and `SaveFiles`;
  - pure logic: `LiveBoard`, `StatValue`, `CountryNames`, `DiploDefines` and `DiploWeight`;
  - `LiveGameService`, a save watcher, plus `LiveViewer` and `LiveViewerStore`.
- **App:** `LiveGamePage`, `LiveStat`, `js/livegame.js` (row-move animation) and CSS.

**Tech Stack:** .NET 10, System.IO.Compression, Blazor in BlazorWebView, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-04-live-game-design.md`

**Conventions:**
- Work in `C:\Users\SCP Fazbear\source\repos\FazStellarisModmanager`, branch `feature/live-game`.
- Build and test with `-c Release --artifacts-path <scratchpad>/art`. Never kill FazStellarisModmanager.exe.
- Avoid backslash escapes in C# strings: use `(char)92` and `(char)10`. Test fixtures use raw string literals (`"""`).

---

### Task 1: Save model and GamestateScanner

**Files:**
- Create: `FazStellarisModmanager.Core/Saves/GameSnapshot.cs`
- Create: `FazStellarisModmanager.Core/Saves/GamestateScanner.cs`
- Test: `FazStellarisModmanager.Tests/GamestateScannerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;
using FazStellarisModmanager.Core.Saves;

namespace FazStellarisModmanager.Tests;

public class GamestateScannerTests
{
    internal const string Fixture = """
        version="Cygnus v4.5.1"
        name="Test"
        date="2300.01.01"
        player=
        {
        	{
        		name="Alice"
        		country=0
        	}
        	{
        		name="Bob"
        		country=1
        	}
        }
        species_db=
        {
        	0={ name="has } brace" # comment with { brace
        	}
        }
        country=
        {
        	0=
        	{
        		flag=
        		{
        			icon={ category="special" file="the_empire.dds" }
        			background={ category="backgrounds" file="00_solid.dds" }
        			colors={ "red" "black" "null" }
        		}
        		name={ key="Imperial_Core" }
        		adjective={ key="%ADJECTIVE%" variables={ { key="adjective" value={ key="Fazbear" } } } }
        		tech_status={ technology="tech_a" level=1 technology="tech_b" level=1 }
        		victory_rank=1
        		victory_score=3547190.92253
        		military_power=3231227834.78125
        		economy_power=-1818745.755
        		tech_power=1741561.25
        		fleet_size=4755
        		empire_size=50
        		num_sapient_pops=399283
        		type="default"
        		budget={ current_month={ income={ x={ energy=5 } } } }
        		variables={ egm_cached_diplo_weight=179343485.88589 other=1 }
        		relations_manager=
        		{
        			relation={ owner=0 country=1 contact=yes communications=yes }
        			relation={ owner=0 country=2 hostile=yes }
        		}
        	}
        	1=
        	{
        		name={ key="Interstellar Battlecat Regime" }
        		victory_rank=2
        		victory_score=222820.87
        		type="default"
        	}
        	2=none
        	3=
        	{
        		name={ key="%ADJECTIVE%" variables={ { key="adjective" value={ key="SPEC_EssJaggon" } } { key="1" value={ key="Commonwealth" } } } }
        		victory_rank=3
        		type="fallen_empire"
        	}
        }
        galactic_community={ members={ 0 1 } }
        """;

    static (List<SavePlayer> Players, List<SaveCountry> Countries) Scan(string text) => GamestateScanner.Scan(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Reads_players()
    {
        var (players, _) = Scan(Fixture);
        Assert.Equal([new SavePlayer("Alice", 0), new SavePlayer("Bob", 1)], players);
    }

    [Fact]
    public void Reads_countries_skipping_none()
    {
        var (_, countries) = Scan(Fixture);
        Assert.Equal([0, 1, 3], countries.Select(c => c.Id));
    }

    [Fact]
    public void Reads_country_fields()
    {
        var c = Scan(Fixture).Countries[0];
        Assert.Equal("default", c.Type);
        Assert.Equal(1, c.VictoryRank);
        Assert.Equal(3547190.92253, c.VictoryScore, 5);
        Assert.Equal(3231227834.78125, c.MilitaryPower, 5);
        Assert.Equal(-1818745.755, c.EconomyPower, 3);
        Assert.Equal(1741561.25, c.TechPower, 2);
        Assert.Equal(4755, c.FleetSize);
        Assert.Equal(50, c.EmpireSize);
        Assert.Equal(399283, c.Pops);
        Assert.Equal(179343485.88589, c.CachedDiploWeight!.Value, 5);
        Assert.Equal("Imperial_Core", c.NameKey);
        Assert.Equal("Fazbear", c.Adjective);
        Assert.Equal(["tech_a", "tech_b"], c.Techs);
        Assert.Equal([1], c.ContactedIds);
        Assert.Equal(new CountryFlag("special", "the_empire.dds", "backgrounds", "00_solid.dds", ["red", "black", "null"]), c.Flag, new FlagComparer());
    }

    [Fact]
    public void Reads_name_variables_and_missing_fields()
    {
        var countries = Scan(Fixture).Countries;
        var c3 = countries.Single(c => c.Id == 3);
        Assert.Equal("%ADJECTIVE%", c3.NameKey);
        Assert.Equal("SPEC_EssJaggon", c3.NameVariables["adjective"]);
        Assert.Equal("Commonwealth", c3.NameVariables["1"]);
        Assert.Equal("fallen_empire", c3.Type);
        var c1 = countries.Single(c => c.Id == 1);
        Assert.Null(c1.Flag);
        Assert.Null(c1.CachedDiploWeight);
        Assert.Empty(c1.ContactedIds);
        Assert.Equal(0, c1.MilitaryPower);
    }

    [Fact]
    public void Empty_or_broken_input_does_not_throw()
    {
        Assert.Empty(Scan("").Countries);
        Assert.Empty(Scan("country={ 0={ name={ key=\"x\" ").Countries.Where(c => c.VictoryRank > 0));
    }

    sealed class FlagComparer : IEqualityComparer<CountryFlag?>
    {
        public bool Equals(CountryFlag? a, CountryFlag? b) =>
            a is not null && b is not null && a.IconCategory == b.IconCategory && a.IconFile == b.IconFile
            && a.BackgroundCategory == b.BackgroundCategory && a.BackgroundFile == b.BackgroundFile && a.Colors.SequenceEqual(b.Colors);
        public int GetHashCode(CountryFlag? f) => 0;
    }
}
```

- [ ] **Step 2: Run the tests.** Use `--filter GamestateScannerTests`. They should fail to compile.

- [ ] **Step 3: Implement**

`GameSnapshot.cs`:
```csharp
namespace FazStellarisModmanager.Core.Saves;

/// <summary>A human player in a save: their Steam name and the country they play.</summary>
public sealed record SavePlayer(string Name, int CountryId);

/// <summary>A country's flag parts as stored in the save (drawn in part 3).</summary>
public sealed record CountryFlag(string? IconCategory, string? IconFile, string? BackgroundCategory, string? BackgroundFile, IReadOnlyList<string> Colors);

/// <summary>The parts of a save's country the Live Game tab uses. Numbers are as stored (full precision, not wrapped).</summary>
public sealed record SaveCountry(
    int Id,
    string? Type,
    string? NameKey,
    IReadOnlyDictionary<string, string> NameVariables,
    string? Adjective,
    CountryFlag? Flag,
    int VictoryRank,
    double VictoryScore,
    double MilitaryPower,
    double EconomyPower,
    double TechPower,
    double FleetSize,
    double EmpireSize,
    double Pops,
    double? CachedDiploWeight,
    IReadOnlyList<int> ContactedIds,
    IReadOnlyList<string> Techs);

/// <summary>What the Live Game tab knows about one save.</summary>
public sealed record GameSnapshot(
    string SaveName,
    string Date,
    string Version,
    string SavePath,
    DateTime SavedUtc,
    IReadOnlyList<SavePlayer> Players,
    IReadOnlyList<SaveCountry> Countries);
```

`GamestateScanner.cs`:
```csharp
using System.Globalization;
using System.Text;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Saves;

/// <summary>
/// Pulls players and countries out of a save's gamestate (hundreds of MB) without parsing all of it: walks the bytes,
/// skips unneeded blocks by brace matching (quotes and # comments respected) and hands only small country sub-blocks to
/// <see cref="ParadoxScriptParser"/>. Tolerates truncated or odd input.
/// </summary>
public static class GamestateScanner
{
    static readonly HashSet<string> CountryBlocks = new(StringComparer.Ordinal)
    {
        "name", "adjective", "flag", "variables", "relations_manager", "tech_status",
    };

    public static (List<SavePlayer> Players, List<SaveCountry> Countries) Scan(byte[] data)
    {
        var players = new List<SavePlayer>();
        var countries = new List<SaveCountry>();
        var r = new Reader(data, 0, data.Length);
        while (r.NextEntry(out var key, out var value, out var body))
        {
            if (body is not { } b) continue;
            if (r.Is(key, "player")) players.AddRange(ReadPlayers(Parse(data, b)));
            else if (r.Is(key, "country")) ScanCountries(data, b, countries);
        }
        return (players, countries);
    }

    static IEnumerable<SavePlayer> ReadPlayers(PdxBlock block)
    {
        foreach (var p in block.Items.OfType<PdxBlock>())
            if (p.GetString("name") is { } name && int.TryParse(p.GetString("country"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                yield return new SavePlayer(name, id);
    }

    static void ScanCountries(byte[] data, (int Start, int End) range, List<SaveCountry> countries)
    {
        var r = new Reader(data, range.Start, range.End);
        while (r.NextEntry(out var key, out _, out var body))
        {
            if (body is not { } b || !int.TryParse(r.Text(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            countries.Add(ScanCountry(data, id, b));
        }
    }

    static SaveCountry ScanCountry(byte[] data, int id, (int Start, int End) range)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var blocks = new Dictionary<string, PdxBlock>(StringComparer.Ordinal);
        var r = new Reader(data, range.Start, range.End);
        while (r.NextEntry(out var key, out var value, out var body))
        {
            var name = r.Text(key);
            if (body is { } b)
            {
                if (CountryBlocks.Contains(name)) blocks[name] = Parse(data, b);
            }
            else if (value is { } v)
            {
                fields[name] = r.Text(v);
            }
        }
        return Build(id, fields, blocks);
    }

    static SaveCountry Build(int id, Dictionary<string, string> f, Dictionary<string, PdxBlock> b)
    {
        double Num(string k) => f.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

        var name = b.GetValueOrDefault("name");
        var adjective = b.GetValueOrDefault("adjective");
        var adjectiveKey = adjective?.GetString("key");
        var adjectiveText = adjectiveKey is not null && !adjectiveKey.Contains('%') ? adjectiveKey : Variables(adjective).GetValueOrDefault("adjective");

        CountryFlag? flag = null;
        if (b.GetValueOrDefault("flag") is { } fl)
        {
            var icon = fl.GetBlock("icon");
            var bg = fl.GetBlock("background");
            flag = new CountryFlag(icon?.GetString("category"), icon?.GetString("file"), bg?.GetString("category"), bg?.GetString("file"),
                fl.GetBlock("colors")?.StringItems.ToList() ?? []);
        }

        var contacts = new List<int>();
        if (b.GetValueOrDefault("relations_manager") is { } rm)
        {
            foreach (var rel in rm.Entries.Where(e => e.Key == "relation").Select(e => e.Value).OfType<PdxBlock>())
            {
                if (rel.GetString("contact") == "yes"
                    && int.TryParse(rel.GetString("country"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var other)
                    && !contacts.Contains(other))
                    contacts.Add(other);
            }
        }

        var techs = b.GetValueOrDefault("tech_status")?.Entries
            .Where(e => e.Key == "technology" && e.Value is string).Select(e => (string)e.Value).Distinct().ToList() ?? [];

        double? cached = b.GetValueOrDefault("variables")?.GetString("egm_cached_diplo_weight") is { } cv
                         && double.TryParse(cv, NumberStyles.Float, CultureInfo.InvariantCulture, out var cd) ? cd : null;

        return new SaveCountry(
            id,
            f.GetValueOrDefault("type"),
            name?.GetString("key"),
            Variables(name),
            adjectiveText,
            flag,
            int.TryParse(f.GetValueOrDefault("victory_rank"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank) ? rank : 0,
            Num("victory_score"),
            Num("military_power"),
            Num("economy_power"),
            Num("tech_power"),
            Num("fleet_size"),
            Num("empire_size"),
            Num("num_sapient_pops"),
            cached,
            contacts,
            techs);
    }

    // { variables={ { key="adjective" value={ key="Fazbear" } } … } } → adjective → Fazbear (first wins).
    static Dictionary<string, string> Variables(PdxBlock? block)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in block?.GetBlock("variables")?.Items.OfType<PdxBlock>() ?? [])
            if (v.GetString("key") is { } k && v.GetBlock("value")?.GetString("key") is { } val)
                result.TryAdd(k, val);
        return result;
    }

    static PdxBlock Parse(byte[] data, (int Start, int End) range) =>
        ParadoxScriptParser.Parse(Encoding.UTF8.GetString(data, range.Start, range.End - range.Start));

    enum Kind { End, Word, Quoted, Open, Close, Op }

    readonly record struct Token(Kind Kind, int Start, int End);

    /// <summary>Tokens of data[start..end). Quoted tokens exclude their quotes.</summary>
    sealed class Reader(byte[] data, int start, int end)
    {
        int _pos = start;

        /// <summary>
        /// Next "key op value" entry. For a block value, <paramref name="body"/> is the range between its braces and the
        /// reader moves past it. Bare items and anonymous blocks are skipped. False at the end of the range.
        /// </summary>
        public bool NextEntry(out Token key, out Token? value, out (int Start, int End)? body)
        {
            while (true)
            {
                value = null;
                body = null;
                key = Read();
                switch (key.Kind)
                {
                    case Kind.End:
                        return false;
                    case Kind.Open:
                        SkipBlock();
                        continue;
                    case Kind.Close:
                    case Kind.Op:
                        continue;
                }

                var save = _pos;
                var op = Read();
                if (op.Kind != Kind.Op)
                {
                    _pos = save; // a bare item: the next token starts the next entry
                    continue;
                }

                var v = Read();
                if (v.Kind == Kind.Open)
                {
                    var bodyStart = _pos;
                    var bodyEnd = SkipBlock();
                    body = (bodyStart, bodyEnd);
                    return true;
                }
                if (v.Kind is Kind.Word or Kind.Quoted)
                {
                    value = v;
                    return true;
                }
                if (v.Kind == Kind.End) return false;
            }
        }

        public bool Is(Token t, string ascii)
        {
            if (t.End - t.Start != ascii.Length) return false;
            for (var i = 0; i < ascii.Length; i++)
                if (data[t.Start + i] != ascii[i]) return false;
            return true;
        }

        public string Text(Token t) => Encoding.UTF8.GetString(data, t.Start, t.End - t.Start);

        Token Read()
        {
            while (_pos < end)
            {
                var c = data[_pos];
                if (c <= 32) { _pos++; continue; }
                if (c == (byte)'#')
                {
                    while (_pos < end && data[_pos] != 10) _pos++;
                    continue;
                }
                if (c == (byte)'"')
                {
                    var s = ++_pos;
                    while (_pos < end && data[_pos] != (byte)'"')
                    {
                        if (data[_pos] == 92) _pos++;
                        _pos++;
                    }
                    var e = Math.Min(_pos, end);
                    _pos = Math.Min(_pos + 1, end);
                    return new Token(Kind.Quoted, s, e);
                }
                if (c == (byte)'{') return new Token(Kind.Open, _pos, ++_pos);
                if (c == (byte)'}') return new Token(Kind.Close, _pos, ++_pos);
                if (c is (byte)'=' or (byte)'<' or (byte)'>' or (byte)'!' or (byte)'?')
                {
                    var s = _pos++;
                    if (_pos < end && data[_pos] == (byte)'=') _pos++;
                    return new Token(Kind.Op, s, _pos);
                }
                var ws = _pos;
                while (_pos < end)
                {
                    var d = data[_pos];
                    if (d <= 32 || d is (byte)'{' or (byte)'}' or (byte)'=' or (byte)'#' or (byte)'"' or (byte)'<' or (byte)'>' or (byte)'!' or (byte)'?') break;
                    _pos++;
                }
                return new Token(Kind.Word, ws, _pos);
            }
            return new Token(Kind.End, end, end);
        }

        /// <summary>Called just after a '{': moves past its matching '}' and returns the index of that '}' (or the range end).</summary>
        int SkipBlock()
        {
            var depth = 1;
            while (_pos < end)
            {
                var c = data[_pos];
                if (c == (byte)'"')
                {
                    _pos++;
                    while (_pos < end && data[_pos] != (byte)'"')
                    {
                        if (data[_pos] == 92) _pos++;
                        _pos++;
                    }
                    _pos++;
                    continue;
                }
                if (c == (byte)'#')
                {
                    while (_pos < end && data[_pos] != 10) _pos++;
                    continue;
                }
                if (c == (byte)'{') depth++;
                else if (c == (byte)'}' && --depth == 0)
                {
                    var close = _pos;
                    _pos++;
                    return close;
                }
                _pos++;
            }
            _pos = end;
            return end;
        }
    }
}
```

- [ ] **Step 4: Run the tests, then the full suite.** All should PASS.
- [ ] **Step 5: Commit.** Message: `"feat: save model and fast gamestate scanner"`.

---

### Task 2: SaveReader and SaveFiles

**Files:**
- Create: `FazStellarisModmanager.Core/Saves/SaveReader.cs`
- Create: `FazStellarisModmanager.Core/Saves/SaveFiles.cs`
- Test: `FazStellarisModmanager.Tests/SaveReaderTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO.Compression;
using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class SaveReaderTests
{
    internal static string WriteSave(string path, string gamestate, string name = "mp_Test Empire", string date = "2387.07.01")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var w = new StreamWriter(zip.CreateEntry("meta").Open()))
            w.Write($"version=\"Cygnus v4.5.1\"{(char)10}name=\"{name}\"{(char)10}date=\"{date}\"{(char)10}");
        using (var w = new StreamWriter(zip.CreateEntry("gamestate").Open()))
            w.Write(gamestate);
        return path;
    }

    [Fact]
    public void Reads_meta_and_gamestate()
    {
        using var t = new TempDir();
        var path = WriteSave(Path.Combine(t.Path, "x", "autosave.sav"), GamestateScannerTests.Fixture);
        var s = SaveReader.Read(path);
        Assert.Equal("mp_Test Empire", s.SaveName);
        Assert.Equal("2387.07.01", s.Date);
        Assert.Equal("Cygnus v4.5.1", s.Version);
        Assert.Equal(path, s.SavePath);
        Assert.Equal(2, s.Players.Count);
        Assert.Equal(3, s.Countries.Count);
    }

    [Fact]
    public void Not_a_save_throws_invalid_data()
    {
        using var t = new TempDir();
        var bad = t.Write("bad.sav", "nope");
        Assert.Throws<InvalidDataException>(() => SaveReader.Read(bad));
        var noMeta = Path.Combine(t.Path, "nometa.sav");
        using (var z = ZipFile.Open(noMeta, ZipArchiveMode.Create)) z.CreateEntry("gamestate");
        Assert.Throws<InvalidDataException>(() => SaveReader.Read(noMeta));
    }

    [Fact]
    public void Newest_save_across_folders()
    {
        using var t = new TempDir();
        var a = WriteSave(Path.Combine(t.Path, "a", "1.sav"), "");
        var b = WriteSave(Path.Combine(t.Path, "b", "2.sav"), "");
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(b, DateTime.UtcNow.AddMinutes(-1));
        t.Write("b/notes.txt", "x");
        Assert.Equal(b, SaveFiles.Newest(t.Path)!.FullName);
        Assert.Null(SaveFiles.Newest(Path.Combine(t.Path, "missing")));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**

- [ ] **Step 3: Implement**

`SaveReader.cs`:
```csharp
using System.IO.Compression;
using System.Text;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Saves;

/// <summary>Reads a Stellaris .sav (a zip with "meta" and "gamestate").</summary>
public static class SaveReader
{
    /// <exception cref="InvalidDataException">Not a zip, or missing meta/gamestate.</exception>
    /// <exception cref="IOException">The file can't be read (for example still being written).</exception>
    public static GameSnapshot Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var metaEntry = zip.GetEntry("meta") ?? throw new InvalidDataException("Not a Stellaris save (no meta).");
        var stateEntry = zip.GetEntry("gamestate") ?? throw new InvalidDataException("Not a Stellaris save (no gamestate).");
        if (stateEntry.Length > int.MaxValue) throw new InvalidDataException("The gamestate is too large to read.");

        string metaText;
        using (var reader = new StreamReader(metaEntry.Open(), Encoding.UTF8)) metaText = reader.ReadToEnd();
        var meta = ParadoxScriptParser.Parse(metaText);

        var data = new byte[stateEntry.Length];
        using (var stream = stateEntry.Open()) stream.ReadExactly(data);
        var (players, countries) = GamestateScanner.Scan(data);

        return new GameSnapshot(
            meta.GetString("name") ?? Path.GetFileNameWithoutExtension(path),
            meta.GetString("date") ?? "",
            meta.GetString("version") ?? "",
            path,
            File.GetLastWriteTimeUtc(path),
            players,
            countries);
    }
}
```

`SaveFiles.cs`:
```csharp
namespace FazStellarisModmanager.Core.Saves;

public static class SaveFiles
{
    /// <summary>The most recently written .sav under <paramref name="saveGamesDir"/> (any subfolder), or null if there is none.</summary>
    public static FileInfo? Newest(string saveGamesDir)
    {
        if (!Directory.Exists(saveGamesDir)) return null;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return new DirectoryInfo(saveGamesDir).EnumerateFiles("*.sav", options).MaxBy(f => f.LastWriteTimeUtc);
    }
}
```

- [ ] **Step 4: Run the tests and the full suite.** All should PASS.
- [ ] **Step 5: Commit.** Message: `"feat: save reader and newest-save lookup"`.

---

### Task 3: Board logic (LiveBoard, StatValue, CountryNames, DiploWeight)

**Files:**
- Create: `FazStellarisModmanager.Core/Saves/LiveBoard.cs`
- Create: `FazStellarisModmanager.Core/Saves/DiploWeight.cs`
- Test: `FazStellarisModmanager.Tests/LiveBoardTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class LiveBoardTests
{
    static SaveCountry C(int id, int rank, string name = "Empire", IReadOnlyList<int>? contacts = null, double score = 10,
        string? adjective = null, IReadOnlyDictionary<string, string>? vars = null) =>
        new(id, "default", name, vars ?? new Dictionary<string, string>(), adjective, null, rank, score, 1, 2, 3, 4, 5, 6, null, contacts ?? [], []);

    static GameSnapshot S(params SaveCountry[] countries) =>
        new("Save", "2300.01.01", "v", "x.sav", DateTime.UtcNow, [new SavePlayer("Alice", 0), new SavePlayer("Bob", 1)], countries);

    [Fact]
    public void Ranked_countries_in_rank_order_with_unknowns_hidden()
    {
        var rows = LiveBoard.Build(S(C(0, 2, "Mine", contacts: [1]), C(1, 1, "Friend"), C(2, 3, "Stranger"), C(3, 0, "Unranked")), viewerId: 0);
        Assert.Equal([1, 0, 2], rows.Select(r => r.Id));
        Assert.True(rows[0].Known);
        Assert.Equal("Bob", rows[0].PlayerName);
        Assert.True(rows[1].IsViewer);
        Assert.Equal("Alice", rows[1].PlayerName);
        var stranger = rows[2];
        Assert.False(stranger.Known);
        Assert.Equal("???", stranger.Name);
        Assert.Null(stranger.Score);
        Assert.Null(stranger.Flag);
        Assert.Null(stranger.PlayerName);
    }

    [Fact]
    public void Viewer_is_always_known()
    {
        var rows = LiveBoard.Build(S(C(0, 1, "Mine")), viewerId: 0);
        Assert.True(rows[0].Known);
        Assert.Equal(10, rows[0].Score!.Value.Real);
    }

    [Theory]
    [InlineData(3547190.9, false, 3547190.9)]
    [InlineData(-747776.373, true, 3547190.923)]
    public void Stat_value_flags_impossible_negatives(double raw, bool overflowed, double real)
    {
        var v = new StatValue(raw);
        Assert.Equal(overflowed, v.Overflowed);
        Assert.Equal(real, v.Real, 3);
    }

    [Theory]
    [InlineData("Interstellar Battlecat Regime", null, "Interstellar Battlecat Regime")]
    [InlineData("Imperial_Core", "Fazbear", "Fazbear Imperial Core")]
    [InlineData("EMPIRE_DESIGN_zrobots_machine_age", null, "zrobots machine age")]
    [InlineData(null, null, "Empire 7")]
    [InlineData("", null, "Empire 7")]
    public void Display_names(string? key, string? adjective, string expected)
    {
        Assert.Equal(expected, CountryNames.Display(C(7, 1, key!, adjective: adjective) with { NameKey = key }));
    }

    [Fact]
    public void Display_name_from_template_variables()
    {
        var c = C(3, 1, "%ADJECTIVE%", vars: new Dictionary<string, string> { ["adjective"] = "SPEC_EssJaggon", ["1"] = "Commonwealth" });
        Assert.Equal("EssJaggon Commonwealth", CountryNames.Display(c));
    }

    [Fact]
    public void Diplo_estimate_uses_defines()
    {
        var c = C(0, 1) with { MilitaryPower = 1000, EconomyPower = 100, TechPower = 10, Pops = 50 };
        var e = DiploWeight.Estimate(c, DiploDefines.Vanilla);
        Assert.Equal(25, e.Naval, 6);
        Assert.Equal(15, e.Economy, 6);
        Assert.Equal(1, e.Tech, 6);
        Assert.Equal(0.5, e.PopsMin, 6);
        Assert.Equal(100.5, e.PopsMax, 6);
        Assert.Equal(41.5, e.Min, 6);
        Assert.Equal(141.5, e.Max, 6);
    }

    [Fact]
    public void Diplo_defines_last_file_wins_and_defaults_fill_gaps()
    {
        using var t = new TempDir();
        t.Write("game/common/defines/00_defines.txt", "NGameplay = { DIPLOMACY_WEIGHT_NAVAL_FACTOR = 0.025 DIPLOMACY_WEIGHT_ECONOMY_FACTOR = 0.15 }");
        t.Write("mod/common/defines/zz_mod.txt", "NGameplay = { DIPLOMACY_WEIGHT_NAVAL_FACTOR = 0.05 }");
        using var game = ContentSource.FromPath("game", Path.Combine(t.Path, "game"), isBaseGame: true);
        using var mod = ContentSource.FromPath("mod", Path.Combine(t.Path, "mod"));
        var d = DiploDefines.Load([game, mod]);
        Assert.Equal(0.05, d.Naval);
        Assert.Equal(0.15, d.Economy);
        Assert.Equal(DiploDefines.Vanilla.Technology, d.Technology);
        Assert.Equal(DiploDefines.Vanilla.PopHappiness, d.PopHappiness);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**

- [ ] **Step 3: Implement**

`LiveBoard.cs`:
```csharp
namespace FazStellarisModmanager.Core.Saves;

/// <summary>
/// A stat as saved, plus overflow detection: these stats can't be negative, so a negative value means the game's
/// 32-bit (×1000) number wrapped. <see cref="Real"/> undoes one wrap.
/// </summary>
public readonly record struct StatValue(double Raw)
{
    public const double Wrap = 4294967.296;
    public bool Overflowed => Raw < 0;
    public double Real => Overflowed ? Raw + Wrap : Raw;
}

/// <summary>One scoreboard row. Unknown (not contacted) rows have Name "???" and no stats, flag or player.</summary>
public sealed record BoardRow(
    int Id, int Rank, string Name, CountryFlag? Flag, bool Known, bool IsViewer, string? PlayerName,
    StatValue? Score, StatValue? Military, StatValue? Economy, StatValue? Tech, StatValue? Fleet);

public static class LiveBoard
{
    /// <summary>Countries with a victory rank, in rank order, as seen by <paramref name="viewerId"/>.</summary>
    public static IReadOnlyList<BoardRow> Build(GameSnapshot snapshot, int viewerId)
    {
        var viewer = snapshot.Countries.FirstOrDefault(c => c.Id == viewerId);
        var contacted = viewer?.ContactedIds.ToHashSet() ?? [];
        var players = new Dictionary<int, string>();
        foreach (var p in snapshot.Players) players.TryAdd(p.CountryId, p.Name);

        return snapshot.Countries
            .Where(c => c.VictoryRank > 0)
            .OrderBy(c => c.VictoryRank).ThenBy(c => c.Id)
            .Select(c =>
            {
                var isViewer = c.Id == viewerId;
                if (!isViewer && !contacted.Contains(c.Id))
                    return new BoardRow(c.Id, c.VictoryRank, "???", null, false, false, null, null, null, null, null, null);
                return new BoardRow(c.Id, c.VictoryRank, CountryNames.Display(c), c.Flag, true, isViewer, players.GetValueOrDefault(c.Id),
                    new StatValue(c.VictoryScore), new StatValue(c.MilitaryPower), new StatValue(c.EconomyPower),
                    new StatValue(c.TechPower), new StatValue(c.FleetSize));
            })
            .ToList();
    }
}

/// <summary>Readable country names without localisation (part 3 replaces this with the game's own text).</summary>
public static class CountryNames
{
    static readonly string[] Prefixes = ["SPEC_", "EMPIRE_DESIGN_", "NAME_"];

    public static string Display(SaveCountry c)
    {
        var key = c.NameKey;
        if (string.IsNullOrWhiteSpace(key)) return $"Empire {c.Id}";
        if (key.Contains('%'))
        {
            var parts = c.NameVariables.Values.Select(Pretty).Where(p => p.Length > 0).ToList();
            return parts.Count > 0 ? string.Join(" ", parts) : $"Empire {c.Id}";
        }
        if (!key.Contains('_')) return key;
        var pretty = Pretty(key);
        return c.Adjective is { Length: > 0 } adj && !adj.Contains('%') && !adj.Contains('_') ? adj + " " + pretty : pretty;
    }

    static string Pretty(string value)
    {
        foreach (var p in Prefixes)
            if (value.StartsWith(p, StringComparison.Ordinal)) { value = value[p.Length..]; break; }
        return value.Replace('_', ' ').Trim();
    }
}
```

`DiploWeight.cs`:
```csharp
using System.Globalization;
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Saves;

/// <summary>The NGameplay DIPLOMACY_WEIGHT_* defines the game uses to compute diplomatic weight.</summary>
public sealed record DiploDefines(double Base, double Naval, double Economy, double Technology, double PopBase, double PopHappiness)
{
    public static DiploDefines Vanilla { get; } = new(0, 0.025, 0.15, 0.1, 0.01, 2.0);

    /// <summary>Reads common/defines from the sources in load order (game first): files load by file name, the last value wins; missing values stay vanilla.</summary>
    public static DiploDefines Load(IReadOnlyList<ContentSource> sources)
    {
        var files = sources
            .SelectMany((s, i) => s.Files("common/defines", ".txt").Select(rel => (Source: s, Index: i, Rel: rel)))
            .OrderBy(f => Path.GetFileName(f.Rel), StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Index)
            .ToList();
        var d = Vanilla;
        foreach (var f in files)
        {
            PdxBlock gameplay;
            try
            {
                if (ParadoxScriptParser.Parse(f.Source.ReadText(f.Rel)).GetBlock("NGameplay") is not { } g) continue;
                gameplay = g;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                continue;
            }
            d = d with
            {
                Base = Get(gameplay, "DIPLOMACY_WEIGHT_BASE") ?? d.Base,
                Naval = Get(gameplay, "DIPLOMACY_WEIGHT_NAVAL_FACTOR") ?? d.Naval,
                Economy = Get(gameplay, "DIPLOMACY_WEIGHT_ECONOMY_FACTOR") ?? d.Economy,
                Technology = Get(gameplay, "DIPLOMACY_WEIGHT_TECHNOLOGY_FACTOR") ?? d.Technology,
                PopBase = Get(gameplay, "DIPLOMACY_WEIGHT_POP_BASE") ?? d.PopBase,
                PopHappiness = Get(gameplay, "DIPLOMACY_WEIGHT_POP_HAPPINESS") ?? d.PopHappiness,
            };
        }
        return d;
    }

    static double? Get(PdxBlock b, string key) =>
        b.GetString(key) is { } v && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}

/// <summary>Diplomatic weight before percentage bonuses. Pops give between PopBase and PopBase + PopHappiness each, depending on happiness.</summary>
public sealed record DiploEstimate(double Base, double Naval, double Economy, double Tech, double PopsMin, double PopsMax)
{
    public double Min => Base + Naval + Economy + Tech + PopsMin;
    public double Max => Base + Naval + Economy + Tech + PopsMax;
}

public static class DiploWeight
{
    public static DiploEstimate Estimate(SaveCountry c, DiploDefines d) => new(
        d.Base,
        new StatValue(c.MilitaryPower).Real * d.Naval,
        new StatValue(c.EconomyPower).Real * d.Economy,
        new StatValue(c.TechPower).Real * d.Technology,
        c.Pops * d.PopBase,
        c.Pops * (d.PopBase + d.PopHappiness));
}
```

Note: `Display_names` builds the country with `with { NameKey = key }`, so a null key is possible. In `C(...)` with `name: key!` the name parameter feeds NameKey, and `with` then overrides it.

- [ ] **Step 4: Run the tests and the full suite.** All should PASS.
- [ ] **Step 5: Commit.** Message: `"feat: live board, overflow detection, display names, diplomatic weight estimate"`.

---

### Task 4: LiveGameService and the viewer choice

**Files:**
- Modify: `FazStellarisModmanager.Core/AppPaths.cs`. Add `public string LiveGame => Path.Combine(Root, "live-game.json");`.
- Create: `FazStellarisModmanager.Core/Saves/LiveGameService.cs`
- Create: `FazStellarisModmanager.Core/Saves/LiveViewer.cs`
- Test: `FazStellarisModmanager.Tests/LiveGameServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class LiveGameServiceTests
{
    static GameSnapshot Snap(string path) => new("S", "2300.01.01", "v", path, File.GetLastWriteTimeUtc(path), [], []);

    [Fact]
    public async Task Reads_newest_then_newer_and_keeps_last_good_on_failure()
    {
        using var t = new TempDir();
        var first = t.Write("g/1.sav", "x");
        File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddMinutes(-5));
        var reads = 0;
        GameSnapshot Read(string p)
        {
            reads++;
            if (p.EndsWith("bad.sav")) throw new InvalidDataException("corrupt");
            return Snap(p);
        }
        using var live = new LiveGameService(() => t.Path, Read, stableDelay: TimeSpan.Zero);

        await live.RefreshAsync();
        Assert.Equal(first, live.Current!.SavePath);

        await live.RefreshAsync();
        Assert.Equal(1, reads); // unchanged: not re-read

        var second = t.Write("g/2.sav", "y");
        await live.RefreshAsync();
        Assert.Equal(second, live.Current!.SavePath);
        Assert.Null(live.Error);

        var bad = t.Write("g/bad.sav", "z");
        File.SetLastWriteTimeUtc(bad, DateTime.UtcNow.AddMinutes(1));
        await live.RefreshAsync();
        Assert.Equal(second, live.Current!.SavePath);
        Assert.Contains("corrupt", live.Error);
        await live.RefreshAsync();
        Assert.Equal(3, reads); // the same bad file is not retried until it changes
    }

    [Fact]
    public async Task No_folder_or_no_save_sets_status()
    {
        using var t = new TempDir();
        using var none = new LiveGameService(() => null, Snap, TimeSpan.Zero);
        await none.RefreshAsync();
        Assert.Null(none.Current);
        Assert.NotNull(none.Status);

        using var empty = new LiveGameService(() => t.Path, Snap, TimeSpan.Zero);
        await empty.RefreshAsync();
        Assert.Null(empty.Current);
        Assert.Contains("Waiting", empty.Status);
    }

    static GameSnapshot WithPlayers(params SavePlayer[] players) => new("Save A", "d", "v", "p", DateTime.UtcNow, players, []);

    [Fact]
    public void Viewer_single_player_name_match_or_remembered()
    {
        Assert.Equal(4, LiveViewer.Resolve(WithPlayers(new SavePlayer("Anyone", 4)), ["me"], null));
        var mp = WithPlayers(new SavePlayer("SCP Fazbear", 0), new SavePlayer("Flamgop", 1));
        Assert.Equal(1, LiveViewer.Resolve(mp, [null, "flamgop"], null));
        Assert.Equal(0, LiveViewer.Resolve(mp, ["nobody"], 0));
        Assert.Null(LiveViewer.Resolve(mp, ["nobody"], 9));
        Assert.Null(LiveViewer.Resolve(mp, ["nobody"], null));
    }

    [Fact]
    public void Viewer_store_round_trips()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "live-game.json");
        new LiveViewerStore(path).Set("Save A", 3);
        Assert.Equal(3, new LiveViewerStore(path).Get("Save A"));
        Assert.Null(new LiveViewerStore(path).Get("Other"));
        File.WriteAllText(path, "{ broken");
        Assert.Null(new LiveViewerStore(path).Get("Save A"));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**

- [ ] **Step 3: Implement**

`LiveGameService.cs`:
```csharp
namespace FazStellarisModmanager.Core.Saves;

/// <summary>
/// Watches the save games folder and keeps <see cref="Current"/> as the newest readable save. Reads run on the thread
/// pool, one at a time. A failed read keeps the previous snapshot and is not retried until the file changes.
/// <see cref="Changed"/> fires on any thread.
/// </summary>
public sealed class LiveGameService(Func<string?> saveGamesDir, Func<string, GameSnapshot>? read = null, TimeSpan? stableDelay = null) : IDisposable
{
    readonly Func<string, GameSnapshot> _read = read ?? SaveReader.Read;
    readonly TimeSpan _stableDelay = stableDelay ?? TimeSpan.FromSeconds(2);
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly CancellationTokenSource _stop = new();
    FileSystemWatcher? _watcher;
    Timer? _poll;
    (string Path, DateTime Time)? _failed;
    bool _started;

    public GameSnapshot? Current { get; private set; }
    public string? Status { get; private set; }
    public string? Error { get; private set; }
    public bool Reading { get; private set; }
    public event Action? Changed;

    /// <summary>Starts watching (once) and reads the newest save.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        var dir = saveGamesDir();
        if (dir is not null && Directory.Exists(dir))
        {
            try
            {
                _watcher = new FileSystemWatcher(dir, "*.sav") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                _watcher.Created += (_, _) => Kick();
                _watcher.Changed += (_, _) => Kick();
                _watcher.Renamed += (_, _) => Kick();
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
            {
                _watcher = null; // polling still works
            }
        }
        _poll = new Timer(_ => Kick(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        Kick();
    }

    void Kick() => _ = RefreshAsync();

    /// <summary>Reads the newest save if it is new or changed. Safe to call any time; calls are serialised.</summary>
    public async Task RefreshAsync()
    {
        if (_stop.IsCancellationRequested) return;
        await _gate.WaitAsync();
        try
        {
            var dir = saveGamesDir();
            if (dir is null || !Directory.Exists(dir))
            {
                Status = "No save games folder found. Check the Stellaris user folder in Settings.";
                return;
            }
            var newest = SaveFiles.Newest(dir);
            if (newest is null)
            {
                Status = "Waiting for a save… (play until the first autosave, or save the game)";
                return;
            }
            var stamp = (newest.FullName, newest.LastWriteTimeUtc);
            if (Current is { } c && c.SavePath == stamp.FullName && c.SavedUtc == stamp.LastWriteTimeUtc) return;
            if (_failed == stamp) return;

            if (!await WaitUntilStableAsync(newest.FullName)) return;
            Reading = true;
            Changed?.Invoke();
            try
            {
                Current = await Task.Run(() => _read(newest.FullName));
                Error = null;
                Status = null;
                _failed = null;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Error = $"Could not read {Path.GetFileName(newest.FullName)}: {ex.Message}";
                newest.Refresh();
                _failed = (newest.FullName, newest.LastWriteTimeUtc);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
        finally
        {
            Reading = false;
            _gate.Release();
            Changed?.Invoke();
        }
    }

    // A save is written in one go; wait until its size stops changing (up to a minute).
    async Task<bool> WaitUntilStableAsync(string path)
    {
        for (var i = 0; i < 30; i++)
        {
            var before = new FileInfo(path);
            if (!before.Exists) return false;
            if (_stableDelay > TimeSpan.Zero) await Task.Delay(_stableDelay);
            var after = new FileInfo(path);
            if (after.Exists && after.Length == before.Length && after.LastWriteTimeUtc == before.LastWriteTimeUtc) return true;
        }
        return false;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _watcher?.Dispose();
        _poll?.Dispose();
    }
}
```

`LiveViewer.cs`:
```csharp
using System.Text.Json;
using FazStellarisModmanager.Core.IO;

namespace FazStellarisModmanager.Core.Saves;

public static class LiveViewer
{
    /// <summary>
    /// The country the user plays in <paramref name="snapshot"/>: the only player's country; else the player whose
    /// name matches one of <paramref name="myNames"/> (ignoring case); else <paramref name="remembered"/> if that is
    /// one of the players' countries; else null (the user must choose).
    /// </summary>
    public static int? Resolve(GameSnapshot snapshot, IEnumerable<string?> myNames, int? remembered)
    {
        if (snapshot.Players.Count == 1) return snapshot.Players[0].CountryId;
        foreach (var name in myNames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var match = snapshot.Players.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.CountryId;
        }
        return remembered is int r && snapshot.Players.Any(p => p.CountryId == r) ? r : null;
    }
}

/// <summary>Remembers which country the user picked per save name (live-game.json). Unreadable files count as empty.</summary>
public sealed class LiveViewerStore(string path)
{
    public int? Get(string saveName) => Load().TryGetValue(saveName, out var id) ? id : null;

    public void Set(string saveName, int countryId)
    {
        var map = Load();
        map[saveName] = countryId;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(map));
    }

    Dictionary<string, int> Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
```

`AtomicFile` lives in `Core/IO/AtomicFile.cs`. Check its real method name and signature, and adapt the call if it isn't `WriteAllText(path, text)`.

- [ ] **Step 4: Run the tests and the full suite.** All should PASS.
- [ ] **Step 5: Commit.** Message: `"feat: live game save watcher and viewer choice"`.

---

### Task 5: Live Game tab

**Files:**
- Modify `FazStellarisModmanager/AppServices.cs`:
  - register `new LiveGameService(() => <save games dir>)`, where the save games dir is `Path.Combine(manager.Resolve().UserDir, "save games")`. Resolve the manager from DI inside a factory lambda, and catch exceptions so it returns null;
  - register `new LiveViewerStore(paths.LiveGame)`;
  - add `using FazStellarisModmanager.Core.Saves;`.
- Modify `FazStellarisModmanager/_Imports.razor`. Add `@using FazStellarisModmanager.Core.Saves`.
- Modify `FazStellarisModmanager/MainLayout.razor`. Add `<NavLink href="live">Live Game</NavLink>` after Conflicts.
- Create `FazStellarisModmanager/Components/LiveStat.razor`.
- Create `FazStellarisModmanager/Pages/LiveGamePage.razor`.
- Create `FazStellarisModmanager/wwwroot/js/livegame.js`.
- Modify `FazStellarisModmanager/wwwroot/css/site.css`. Append the CSS below.

AppServices registration:
```csharp
        services.AddSingleton(sp =>
        {
            var manager = sp.GetRequiredService<ModManagerService>();
            return new LiveGameService(() =>
            {
                try { return Path.Combine(manager.Resolve().UserDir, "save games"); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException) { return null; }
            });
        });
        services.AddSingleton(new LiveViewerStore(paths.LiveGame));
```

- [ ] **Step 1: LiveStat.razor** (a number cell with an overflow marker)

```razor
@if (Value is not { } v)
{
    <span class="unknown">???</span>
}
else
{
    <span class="@(v.Overflowed ? "ovf" : "")" title="@(v.Overflowed ? $"Overflowed in game (shows {v.Raw:N0}); real value ≈ {v.Real:N0}" : null)">
        @v.Real.ToString("N0")@(v.Overflowed ? " ⚠" : "")
    </span>
}

@code {
    [Parameter] public StatValue? Value { get; set; }
}
```

- [ ] **Step 2: livegame.js** (the row-move animation)

```js
// Animates scoreboard rows (children with data-id and data-rank) from where they were last time to where they are
// now, and glows rows that moved up or down in rank. Positions are remembered between calls.
const last = new Map();

export function animate(board) {
    const rows = Array.from(board.querySelectorAll('[data-id]'));
    const now = new Map(rows.map(r => [r.dataset.id, { top: r.offsetTop, rank: Number(r.dataset.rank) }]));
    const first = last.size === 0;
    for (const r of rows) {
        const before = last.get(r.dataset.id);
        const after = now.get(r.dataset.id);
        if (first || !before) continue;
        const dy = before.top - after.top;
        if (dy !== 0) {
            r.style.transition = 'none';
            r.style.transform = `translateY(${dy}px)`;
        }
        r.classList.remove('moved-up', 'moved-down');
        if (after.rank < before.rank) r.classList.add('moved-up');
        else if (after.rank > before.rank) r.classList.add('moved-down');
    }
    requestAnimationFrame(() => requestAnimationFrame(() => {
        for (const r of rows) {
            r.style.transition = 'transform .7s cubic-bezier(.2,.8,.2,1)';
            r.style.transform = '';
        }
    }));
    setTimeout(() => rows.forEach(r => r.classList.remove('moved-up', 'moved-down')), 4000);
    last.clear();
    for (const [id, v] of now) last.set(id, v);
}
```

- [ ] **Step 3: LiveGamePage.razor**

```razor
@page "/live"
@implements IAsyncDisposable
@inject LiveGameService Live
@inject LiveViewerStore Viewers
@inject ModManagerService Manager
@inject IJSRuntime JS

<div class="live-page">
    <div class="live-bar @(Live.Current is null ? "" : "ok")">
        <span class="dot"></span>
        @if (Live.Current is { } s)
        {
            <b>@s.SaveName</b>
            <span class="muted">· game date @s.Date · saved @Ago(s.SavedUtc) · @Path.GetFileName(s.SavePath)</span>
        }
        else
        {
            <b>@(Live.Status ?? "Looking for saves…")</b>
        }
        @if (Live.Reading)
        {
            <span class="muted">· reading a newer save…</span>
        }
        <span class="spacer"></span>
        @if (Live.Current is { Players.Count: > 1 } multi)
        {
            <label class="inline">Viewing as
                <select value="@(viewer?.ToString() ?? "")" @onchange="PickViewer">
                    <option value="">Choose…</option>
                    @foreach (var p in multi.Players)
                    {
                        <option value="@p.CountryId">@p.Name</option>
                    }
                </select>
            </label>
        }
    </div>
    @if (Live.Error is not null)
    {
        <p class="status error">@Live.Error</p>
    }

    @if (Live.Current is { } snap && viewer is null && snap.Players.Count > 1)
    {
        <p class="status">Choose who you are in this game ("Viewing as") to see the scoreboard from your empire's point of view.</p>
    }
    else if (Live.Current is { } snap2 && me is not null)
    {
        <div class="live-grid">
            <aside class="live-side">
                <div class="head">
                    <span class="lflag big" style="background:@FlagColor(me.Flag)">@Initial(CountryNames.Display(me))</span>
                    <div>
                        <h3>@CountryNames.Display(me)</h3>
                        <div class="muted">You@(PlayerOf(snap2, me.Id) is { } pn ? " · " + pn : "")</div>
                    </div>
                    <span class="spacer"></span>
                    <span class="lrank">#@me.VictoryRank</span>
                </div>
                <div class="lstat"><span>Victory score</span><LiveStat Value="new StatValue(me.VictoryScore)" /></div>
                <div class="lstat">
                    <span>Diplomatic weight</span>
                    @if (me.CachedDiploWeight is double cached)
                    {
                        <span title="Stored by the Galactic Market Overhaul mod">@cached.ToString("N0")</span>
                    }
                    else
                    {
                        <span title="Estimate before bonuses; see the breakdown">@Estimate.Min.ToString("N0")–@Estimate.Max.ToString("N0")</span>
                    }
                </div>
                <details class="ldiplo">
                    <summary>Diplomatic weight breakdown</summary>
                    <div class="lstat"><span>Fleet power × @defines.Naval</span><span>@Estimate.Naval.ToString("N0")</span></div>
                    <div class="lstat"><span>Economy power × @defines.Economy</span><span>@Estimate.Economy.ToString("N0")</span></div>
                    <div class="lstat"><span>Tech power × @defines.Technology</span><span>@Estimate.Tech.ToString("N0")</span></div>
                    <div class="lstat"><span>Pops (by happiness)</span><span>@Estimate.PopsMin.ToString("N0")–@Estimate.PopsMax.ToString("N0")</span></div>
                    <div class="lstat"><span>Base before bonuses</span><span>@Estimate.Min.ToString("N0")–@Estimate.Max.ToString("N0")</span></div>
                    <p class="muted">The game then multiplies this by bonuses (Galactic Community resolutions and other modifiers).@(me.CachedDiploWeight is not null ? " The total above comes from the Galactic Market Overhaul mod." : "")</p>
                </details>
                <div class="lstat"><span>Military power</span><LiveStat Value="new StatValue(me.MilitaryPower)" /></div>
                <div class="lstat"><span>Economy power</span><LiveStat Value="new StatValue(me.EconomyPower)" /></div>
                <div class="lstat"><span>Tech power</span><LiveStat Value="new StatValue(me.TechPower)" /></div>
                <div class="lstat"><span>Fleet size</span><LiveStat Value="new StatValue(me.FleetSize)" /></div>
                <div class="lstat"><span>Empire size</span><LiveStat Value="new StatValue(me.EmpireSize)" /></div>
                <div class="lstat"><span>Pops</span><LiveStat Value="new StatValue(me.Pops)" /></div>
                <p class="muted">Known empires: @rows.Count(r => r.Known && !r.IsViewer) of @(rows.Count - 1)</p>
            </aside>
            <div class="live-board" @ref="boardEl">
                <div class="lhdr"><span>#</span><span></span><span>Empire</span><span>Score</span><span>Military</span><span>Economy</span><span>Tech</span><span>Fleet</span></div>
                @foreach (var r in rows)
                {
                    <div @key="r.Id" class="lrow @(r.IsViewer ? "me" : "") @(r.Known ? "" : "unknown")" data-id="@r.Id" data-rank="@r.Rank">
                        <span class="pos">@r.Rank</span>
                        <span class="lflag" style="background:@(r.Known ? FlagColor(r.Flag) : "var(--border)")">@(r.Known ? Initial(r.Name) : "?")</span>
                        <span class="who">@r.Name
                            @if (r.IsViewer) { <span class="pl">YOU</span> }
                            else if (r.PlayerName is { } player) { <span class="pl">@player</span> }
                        </span>
                        <span class="num"><LiveStat Value="r.Score" /></span>
                        <span class="num"><LiveStat Value="r.Military" /></span>
                        <span class="num"><LiveStat Value="r.Economy" /></span>
                        <span class="num"><LiveStat Value="r.Tech" /></span>
                        <span class="num"><LiveStat Value="r.Fleet" /></span>
                    </div>
                }
            </div>
        </div>
    }
</div>

@code {
    ElementReference boardEl;
    IJSObjectReference? module;
    int? viewer;
    SaveCountry? me;
    IReadOnlyList<BoardRow> rows = [];
    DiploDefines defines = DiploDefines.Vanilla;
    bool definesLoaded;
    string? animatedFor;
    string? builtFor;
    Timer? clock;

    DiploEstimate Estimate => me is null ? new DiploEstimate(0, 0, 0, 0, 0, 0) : DiploWeight.Estimate(me, defines);

    protected override void OnInitialized()
    {
        Live.Changed += OnLiveChanged;
        Live.Start();
        Rebuild();
        clock = new Timer(_ => _ = InvokeAsync(StateHasChanged), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        _ = LoadDefinesAsync();
    }

    void OnLiveChanged() => _ = InvokeAsync(() => { Rebuild(); StateHasChanged(); });

    void Rebuild()
    {
        if (Live.Current is not { } s) return;
        var key = s.SavePath + "|" + s.SavedUtc.Ticks;
        if (builtFor != key)
        {
            builtFor = key;
            viewer = LiveViewer.Resolve(s, [Manager.Settings.PlayerName, Environment.UserName], Viewers.Get(s.SaveName));
        }
        me = viewer is int v ? s.Countries.FirstOrDefault(c => c.Id == v) : null;
        rows = viewer is int id ? LiveBoard.Build(s, id) : [];
    }

    void PickViewer(ChangeEventArgs e)
    {
        if (Live.Current is not { } s) return;
        if (int.TryParse(e.Value?.ToString(), out var id))
        {
            viewer = id;
            try { Viewers.Set(s.SaveName, id); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        else viewer = null;
        me = viewer is int v ? s.Countries.FirstOrDefault(c => c.Id == v) : null;
        rows = viewer is int vid ? LiveBoard.Build(s, vid) : [];
    }

    // Defines from the game and the applied mod list; vanilla values if anything goes wrong.
    async Task LoadDefinesAsync()
    {
        if (definesLoaded) return;
        definesLoaded = true;
        try
        {
            if (Manager.Library.Count == 0) await Manager.RefreshLibraryAsync();
            var loaded = await Task.Run(() =>
            {
                var sources = new List<FazStellarisModmanager.Core.Technology.ContentSource>();
                try
                {
                    var resolved = Manager.Resolve();
                    if (resolved.GameDir is { } g) sources.Add(FazStellarisModmanager.Core.Technology.ContentSource.FromPath("game", g, isBaseGame: true));
                    var byRel = Manager.Library.GroupBy(m => m.DescriptorRel, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
                    foreach (var e in Manager.ImportCurrent("live").Mods)
                    {
                        if (!byRel.TryGetValue(e.DescriptorRel, out var mod)) continue;
                        try { sources.Add(FazStellarisModmanager.Core.Technology.ContentSource.FromPath(mod.Name, mod.ContentPath)); }
                        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
                    }
                    return DiploDefines.Load(sources);
                }
                finally
                {
                    foreach (var s in sources) s.Dispose();
                }
            });
            defines = loaded;
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException
                                   or System.Text.Json.JsonException or InvalidDataException)
        {
            defines = DiploDefines.Vanilla;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (rows.Count == 0 || builtFor is null || animatedFor == builtFor + "|" + viewer) return;
        animatedFor = builtFor + "|" + viewer;
        try
        {
            module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/livegame.js");
            await module.InvokeVoidAsync("animate", boardEl);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException or ObjectDisposedException) { }
    }

    static string? PlayerOf(GameSnapshot s, int id) => s.Players.FirstOrDefault(p => p.CountryId == id)?.Name;

    static string Initial(string name) => name.Length > 0 && name != "???" ? char.ToUpperInvariant(name[0]).ToString() : "?";

    static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 48) return $"{(int)span.TotalHours} h ago";
        return $"{(int)span.TotalDays} days ago";
    }

    static readonly Dictionary<string, string> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "#b3261e", ["dark_red"] = "#7a1a14", ["burgundy"] = "#6d1b33", ["orange"] = "#d9761e", ["yellow"] = "#d6b31f",
        ["light_green"] = "#6fbf4a", ["green"] = "#2e7d32", ["dark_green"] = "#1e4d22", ["turquoise"] = "#1fa7a0", ["teal"] = "#18736e",
        ["light_blue"] = "#4aa3df", ["blue"] = "#2b5f9e", ["dark_blue"] = "#1a3566", ["indigo"] = "#3f3a8f", ["purple"] = "#6a3d9a",
        ["pink"] = "#d0679d", ["light_grey"] = "#a6adb5", ["grey"] = "#6b737c", ["dark_grey"] = "#3c4249", ["black"] = "#1b1d21",
        ["white"] = "#e8ebee", ["brown"] = "#7a5230", ["dark_brown"] = "#4d321c", ["beige"] = "#c8b28a",
    };

    static string FlagColor(CountryFlag? flag)
    {
        var name = flag?.Colors.FirstOrDefault(c => !string.Equals(c, "null", StringComparison.OrdinalIgnoreCase));
        return name is not null && Colors.TryGetValue(name, out var hex) ? hex : "#3a4a5c";
    }

    public async ValueTask DisposeAsync()
    {
        Live.Changed -= OnLiveChanged;
        clock?.Dispose();
        if (module is not null)
        {
            try { await module.DisposeAsync(); }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException or ObjectDisposedException) { }
        }
    }
}
```

Fix one known issue in this markup before building. Inside the `@if (Live.Current is { } snap && …)` / `else if (Live.Current is { } snap2 && …)` chain, the pattern variables `snap` and `snap2` must not clash with other names; rename them if the compiler complains. `Timer` here is `System.Threading.Timer`. Add `@using System.Threading` if it isn't already imported.

- [ ] **Step 4: Append the CSS**

```css
/* Live Game */
.live-page { display: flex; flex-direction: column; gap: .6rem; height: calc(100% - 2.5rem); }
.live-bar { display: flex; gap: .5rem; align-items: center; background: var(--panel); border: 1px solid var(--border); border-radius: 8px; padding: .45rem .7rem; }
.live-bar .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--muted); }
.live-bar.ok .dot { background: #6fcf97; }
.live-bar .spacer, .live-side .spacer { flex: 1; }
.live-grid { display: grid; grid-template-columns: 290px 1fr; gap: .7rem; flex: 1; min-height: 0; }
.live-side { background: var(--panel); border: 1px solid var(--border); border-radius: 10px; padding: .8rem; overflow: auto; }
.live-side .head { display: flex; gap: .7rem; align-items: center; margin-bottom: .6rem; }
.live-side h3 { margin: 0; font-size: 1rem; }
.lrank { font-size: 1.6rem; font-weight: 700; color: #f2c94c; }
.lstat { display: flex; justify-content: space-between; gap: .5rem; padding: .3rem 0; border-bottom: 1px solid var(--panel-2); font-variant-numeric: tabular-nums; }
.ldiplo { margin: .2rem 0 .4rem; font-size: .82rem; }
.ldiplo summary { cursor: pointer; color: var(--muted); }
.lflag { width: 30px; height: 30px; border-radius: 4px; flex: none; display: inline-flex; align-items: center; justify-content: center; font-weight: 800; color: #fff; box-shadow: inset 0 0 0 1px #0006; }
.lflag.big { width: 52px; height: 52px; font-size: 1.5rem; }
.live-board { background: var(--panel); border: 1px solid var(--border); border-radius: 10px; padding: .35rem; overflow: auto; position: relative; }
.lhdr, .lrow { display: grid; grid-template-columns: 44px 36px minmax(0, 1fr) 110px 130px 110px 100px 80px; gap: .5rem; align-items: center; padding: .3rem .5rem; }
.lhdr { color: var(--muted); font-size: .7rem; text-transform: uppercase; letter-spacing: .03em; }
.lhdr span:nth-child(n+4), .lrow .num { text-align: right; font-variant-numeric: tabular-nums; }
.lrow { background: var(--panel-2); border-radius: 6px; margin-bottom: 3px; position: relative; }
.lrow.me { outline: 1px solid var(--accent); background: #1c2a3a; }
.lrow .pos { font-weight: 700; text-align: center; }
.lrow .who { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.lrow .pl { font-size: .65rem; color: var(--accent); margin-left: .4rem; border: 1px solid #2d5a85; border-radius: 3px; padding: 0 .3rem; }
.lrow.unknown { color: #5b6878; }
.lrow .ovf, .lstat .ovf { color: #ff8a80; }
.lrow.moved-up { animation: lglowup 1.8s ease-out; }
.lrow.moved-down { animation: lglowdown 1.8s ease-out; }
.lrow.moved-up .pos::after { content: " ▲"; color: #6fcf97; font-size: .65rem; }
.lrow.moved-down .pos::after { content: " ▼"; color: #ff8a80; font-size: .65rem; }
@keyframes lglowup { 0% { box-shadow: 0 0 0 2px #6fcf97; } 100% { box-shadow: 0 0 0 0 transparent; } }
@keyframes lglowdown { 0% { box-shadow: 0 0 0 2px #ff8a80; } 100% { box-shadow: 0 0 0 0 transparent; } }
```

- [ ] **Step 5: Build the app and run the full suite.** Expected: 0 errors and all tests passing.
- [ ] **Step 6: Commit.** Message: `"feat: Live Game tab - your empire, ranked scoreboard, hidden unknowns, rank animation"`.

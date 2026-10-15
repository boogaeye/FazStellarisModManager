# Launcher Playset Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On every Apply, also write the applied mod list into the Paradox launcher's `launcher-v2.sqlite` as the active playset "FazStellarisModManager". Opening the launcher then no longer replaces `dlc_load.json`.

**Architecture:** a new static `LauncherPlaysetFile` in Core/Game does the database work (backup, one transaction, warnings instead of exceptions). `ModManagerService.Apply` refuses while the launcher runs, writes `dlc_load.json` as before, then calls `LauncherPlaysetFile.Write`. It returns an `ApplyResult` that the Mods and Session pages show.

**Tech Stack:** .NET 10, Microsoft.Data.Sqlite, xUnit.

Spec: `docs/superpowers/specs/2026-10-15-launcher-playset-sync-design.md`

**Repo conventions (must follow):**
- **Build and test:** always pass `-c Release --artifacts-path C:/Users/SCPFAZ~1/AppData/Local/Temp/claude/C--Users-SCP-Fazbear-source-repos-FazStellarisModmanager/b74155d1-9762-4fc6-b5b4-9edf787ffd31/scratchpad/art`.
- **Processes:** never kill FazStellarisModmanager.exe, and never launch the app.
- **No backslash escapes in C# strings:** use `(char)10`, `(char)92`, raw string literals or verbatim strings without `\` sequences.
- **Commit trailer:** `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
- **Flaky test:** `HelperProcessWorkshopService` has a timing-flaky test; if only that one fails, rerun once.

---

### Task 1: LauncherPlaysetFile (Core) with tests

**Files:**
- Modify: `FazStellarisModmanager.Core/FazStellarisModmanager.Core.csproj` (add the Microsoft.Data.Sqlite package)
- Modify: `FazStellarisModmanager.Tests/FazStellarisModmanager.Tests.csproj` (add Microsoft.Data.Sqlite, so tests can build databases)
- Create: `FazStellarisModmanager.Core/Game/LauncherPlaysetFile.cs`
- Create: `FazStellarisModmanager.Tests/TestUtil/LauncherDb.cs`
- Create: `FazStellarisModmanager.Tests/LauncherPlaysetFileTests.cs`

- [ ] **Step 1: Add the package to both projects**

```bash
cd "C:/Users/SCP Fazbear/source/repos/FazStellarisModmanager"
dotnet add FazStellarisModmanager.Core package Microsoft.Data.Sqlite
dotnet add FazStellarisModmanager.Tests package Microsoft.Data.Sqlite
```

- [ ] **Step 2: Test helper `TestUtil/LauncherDb.cs`.** It uses the real launcher schema, copied from the user's database:

```csharp
using Microsoft.Data.Sqlite;

namespace FazStellarisModmanager.Tests.TestUtil;

/// <summary>A launcher-v2.sqlite with the Paradox launcher's real playsets / playsets_mods / mods tables.</summary>
public static class LauncherDb
{
    public const string Schema = """
        CREATE TABLE playsets (
                id char(36) not null,
                name varchar(255) not null,
                isActive boolean,
                loadOrder varchar(255),
                pdxId int,
                pdxUserId char(36),
                createdOn datetime not null,
                updatedOn datetime,
                syncedOn datetime,
                lastServerChecksum text,
                isRemoved boolean not null default false, `hasNotApprovedChanges` boolean not null default '0', `syncState` varchar(255),
                primary key (id),
                constraint uq_pdxId unique (pdxId)
              );
        CREATE TABLE `playsets_mods` (`playsetId` char(36) not null, `modId` char(36) not null, `enabled` boolean default '1', `position` integer, foreign key(`playsetId`) references `playsets`(`id`) on delete CASCADE, foreign key(`modId`) references `mods`(`id`) on delete CASCADE);
        CREATE TABLE `mods` (`id` char(36) not null, `pdxId` varchar(255), `steamId` varchar(255), `gameRegistryId` text, `name` varchar(255), `displayName` varchar(255), "descriptionDeprecated" text, `thumbnailUrl` text, `thumbnailPath` text, `version` varchar(255), `tags` json default '[]', `requiredVersion` varchar(255), `arch` text, `os` text, `repositoryPath` text, `dirPath` text, `archivePath` text, `status` text not null, `source` text not null, `cause` text null default null, `timeUpdated` integer, `isNew` boolean, `createdDate` datetime, `subscribedDate` datetime, `size` integer, `metadataId` varchar(255), `remotePdxId` varchar(255), `remoteSteamId` varchar(255), `metadataVersion` varchar(255), `isMetadataApplied` boolean not null default '0', `metadataStatus` text check (`metadataStatus` in ('not_applied', 'missing', 'parsing_error', 'applied', 'validation_error')) not null default 'not_applied', `metadataGameId` varchar(255), `descriptionPdx` varchar(255), `descriptionSteam` varchar(255), `shortDescriptionPdx` varchar(255), `keepLatest` boolean not null default '1', `userVersion` boolean null default null, primary key (`id`));
        """;

    static SqliteConnection Open(string path)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        c.Open();
        return c;
    }

    public static void Create(string path, string schema = Schema) => Exec(path, schema);

    public static void Exec(string path, string sql, params (string Name, object? Value)[] args)
    {
        using var c = Open(path);
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public static List<object?[]> Query(string path, string sql, params (string Name, object? Value)[] args)
    {
        using var c = Open(path);
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        var rows = new List<object?[]>();
        while (r.Read())
        {
            var row = new object?[r.FieldCount];
            for (var i = 0; i < r.FieldCount; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    public static void AddPlayset(string path, string id, string name, int? isActive) =>
        Exec(path, "insert into playsets (id, name, isActive, loadOrder, createdOn, updatedOn, isRemoved, hasNotApprovedChanges, syncState) values ($id, $n, $a, 'custom', 1, 1, 0, 0, 'NOT_ELIGIBLE')",
            ("$id", id), ("$n", name), ("$a", isActive));

    public static void AddMod(string path, string id, string gameRegistryId, string status = "ready_to_play") =>
        Exec(path, "insert into mods (id, gameRegistryId, displayName, status, source) values ($id, $g, $g, $s, 'steam')",
            ("$id", id), ("$g", gameRegistryId), ("$s", status));
}
```

- [ ] **Step 3: Write the failing tests in `LauncherPlaysetFileTests.cs`**

```csharp
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class LauncherPlaysetFileTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_123);

    sealed class Rig : IDisposable
    {
        readonly TempDir _tmp = new();
        public string UserDir => Path.Combine(_tmp.Path, "user");
        public string BackupDir => Path.Combine(_tmp.Path, "backups");
        public string Db => Path.Combine(UserDir, LauncherPlaysetFile.FileName);
        public Rig(bool withDb = true)
        {
            Directory.CreateDirectory(UserDir);
            if (withDb) LauncherDb.Create(Db);
        }
        public string Write(string rel, string content) => _tmp.Write("user/" + rel, content);
        public void Dispose() => _tmp.Dispose();
    }

    static string? OurPlaysetId(Rig r) =>
        LauncherDb.Query(r.Db, "select id from playsets where name = $n", ("$n", LauncherPlaysetFile.PlaysetName)).SingleOrDefault()?[0] as string;

    static List<string> PlaysetRegistryIds(Rig r, string playsetId) =>
        LauncherDb.Query(r.Db, "select m.gameRegistryId from playsets_mods pm join mods m on m.id = pm.modId where pm.playsetId = $p order by pm.position", ("$p", playsetId))
            .Select(x => (string)x[0]!).ToList();

    [Fact]
    public void No_database_does_nothing()
    {
        using var r = new Rig(withDb: false);
        var result = LauncherPlaysetFile.Write(r.UserDir, ["mod/a.mod"], r.BackupDir, Now);
        Assert.False(result.Written);
        Assert.Null(result.Warning);
        Assert.Null(result.BackupPath);
        Assert.False(Directory.Exists(r.BackupDir));
    }

    [Fact]
    public void Creates_the_active_playset_in_list_order_and_deactivates_the_others()
    {
        using var r = new Rig();
        LauncherDb.AddPlayset(r.Db, "irony", "IronyModManager", 1);
        LauncherDb.AddPlayset(r.Db, "nul", "Never active", null);
        LauncherDb.AddPlayset(r.Db, "old", "Old", 0);
        LauncherDb.AddMod(r.Db, "m1", "mod/ugc_1.mod");
        LauncherDb.AddMod(r.Db, "m2", "mod/Local.mod");

        var result = LauncherPlaysetFile.Write(r.UserDir, ["mod/local.mod", "mod/ugc_1.mod"], r.BackupDir, Now);

        Assert.True(result.Written);
        Assert.Null(result.Warning);
        Assert.Empty(result.Skipped);
        var id = OurPlaysetId(r)!;
        var row = LauncherDb.Query(r.Db, "select isActive, loadOrder, createdOn, updatedOn, isRemoved, syncState from playsets where id = $id", ("$id", id)).Single();
        Assert.Equal(1L, row[0]);
        Assert.Equal("custom", row[1]);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), row[2]);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), row[3]);
        Assert.Equal(0L, row[4]);
        Assert.Equal("NOT_ELIGIBLE", row[5]);
        Assert.Equal(0L, LauncherDb.Query(r.Db, "select isActive from playsets where id = 'irony'").Single()[0]);
        Assert.Null(LauncherDb.Query(r.Db, "select isActive from playsets where id = 'nul'").Single()[0]);
        Assert.Equal(0L, LauncherDb.Query(r.Db, "select isActive from playsets where id = 'old'").Single()[0]);
        Assert.Equal(["mod/Local.mod", "mod/ugc_1.mod"], PlaysetRegistryIds(r, id));
        Assert.Equal(new object?[] { 0L, 1L }, LauncherDb.Query(r.Db, "select position from playsets_mods where playsetId = $p order by position", ("$p", id)).Select(x => x[0]));
        Assert.All(LauncherDb.Query(r.Db, "select enabled from playsets_mods"), x => Assert.Equal(1L, x[0]));
        Assert.Equal(2L, LauncherDb.Query(r.Db, "select count(*) from mods").Single()[0]); // matched, none inserted
    }

    [Fact]
    public void Reuses_its_playset_and_replaces_the_rows()
    {
        using var r = new Rig();
        LauncherDb.AddMod(r.Db, "m1", "mod/ugc_1.mod");
        LauncherDb.AddMod(r.Db, "m2", "mod/ugc_2.mod");

        LauncherPlaysetFile.Write(r.UserDir, ["mod/ugc_1.mod", "mod/ugc_2.mod"], r.BackupDir, Now);
        var first = OurPlaysetId(r);
        LauncherPlaysetFile.Write(r.UserDir, ["mod/ugc_2.mod"], r.BackupDir, Now.AddMinutes(1));

        Assert.Equal(first, OurPlaysetId(r));
        Assert.Equal(["mod/ugc_2.mod"], PlaysetRegistryIds(r, first!));
        Assert.Equal(Now.AddMinutes(1).ToUnixTimeMilliseconds(), LauncherDb.Query(r.Db, "select updatedOn from playsets where id = $id", ("$id", first)).Single()[0]);
    }

    [Fact]
    public void Inserts_rows_for_mods_the_launcher_does_not_know_and_skips_missing_descriptors()
    {
        using var r = new Rig();
        r.Write("mod/ugc_222.mod", "name=\"Two\"" + (char)10 + "version=\"1.2\"" + (char)10 + "supported_version=\"v4.5.*\"" + (char)10
            + "tags={" + (char)10 + "\"Gameplay\"" + (char)10 + "\"UI\"" + (char)10 + "}" + (char)10 + "path=\"D:/ws/222\"" + (char)10 + "remote_file_id=\"222\"" + (char)10);
        r.Write("mod/zipped.mod", "name=\"Zipped\"" + (char)10 + "archive=\"mod/zipped.zip\"" + (char)10);

        var result = LauncherPlaysetFile.Write(r.UserDir, ["mod/ugc_222.mod", "mod/gone.mod", "mod/zipped.mod"], r.BackupDir, Now);

        Assert.True(result.Written);
        Assert.Equal(["mod/gone.mod"], result.Skipped);
        Assert.Equal(["mod/ugc_222.mod", "mod/zipped.mod"], PlaysetRegistryIds(r, OurPlaysetId(r)!));

        var two = LauncherDb.Query(r.Db, "select steamId, displayName, version, requiredVersion, tags, dirPath, archivePath, status, source, isNew, createdDate from mods where gameRegistryId = 'mod/ugc_222.mod'").Single();
        Assert.Equal("222", two[0]);
        Assert.Equal("Two", two[1]);
        Assert.Equal("1.2", two[2]);
        Assert.Equal("v4.5.*", two[3]);
        Assert.Equal("[\"Gameplay\",\"UI\"]", two[4]);
        Assert.Equal(Path.GetFullPath("D:/ws/222"), two[5]);
        Assert.Null(two[6]);
        Assert.Equal("ready_to_play", two[7]);
        Assert.Equal("steam", two[8]);
        Assert.Equal(0L, two[9]);
        Assert.Equal(Now.ToUnixTimeSeconds(), two[10]);

        var zip = LauncherDb.Query(r.Db, "select steamId, dirPath, archivePath, source from mods where gameRegistryId = 'mod/zipped.mod'").Single();
        Assert.Null(zip[0]);
        Assert.Null(zip[1]);
        Assert.Equal(Path.GetFullPath(Path.Combine(r.UserDir, "mod/zipped.zip")), zip[2]);
        Assert.Equal("local", zip[3]);
    }

    [Fact]
    public void Prefers_a_ready_to_play_row_when_several_match()
    {
        using var r = new Rig();
        LauncherDb.AddMod(r.Db, "broken", "mod/ugc_1.mod", "corrupted");
        LauncherDb.AddMod(r.Db, "good", "mod/ugc_1.mod");

        LauncherPlaysetFile.Write(r.UserDir, ["mod/ugc_1.mod"], r.BackupDir, Now);

        Assert.Equal("good", LauncherDb.Query(r.Db, "select modId from playsets_mods").Single()[0]);
    }

    [Fact]
    public void Backs_up_the_database_and_keeps_the_newest_20()
    {
        using var r = new Rig();
        Directory.CreateDirectory(r.BackupDir);
        for (var i = 0; i < 25; i++) File.WriteAllText(Path.Combine(r.BackupDir, $"launcher-v2_20200101_000000_{i:000}.sqlite"), "old");
        File.WriteAllText(Path.Combine(r.BackupDir, "dlc_load_20200101_000000_000.json"), "{}"); // not ours: kept

        var result = LauncherPlaysetFile.Write(r.UserDir, [], r.BackupDir, Now);

        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        var backups = Directory.GetFiles(r.BackupDir, "launcher-v2_*.sqlite");
        Assert.Equal(20, backups.Length);
        Assert.Contains(result.BackupPath, backups);
        Assert.True(File.Exists(Path.Combine(r.BackupDir, "dlc_load_20200101_000000_000.json")));
    }

    [Fact]
    public void A_corrupt_database_gives_a_warning_and_is_left_alone()
    {
        using var r = new Rig(withDb: false);
        var junk = Enumerable.Range(0, 4096).Select(i => (byte)(i * 7)).ToArray();
        File.WriteAllBytes(r.Db, junk);

        var result = LauncherPlaysetFile.Write(r.UserDir, ["mod/a.mod"], r.BackupDir, Now);

        Assert.False(result.Written);
        Assert.NotNull(result.Warning);
        Assert.Equal(junk, File.ReadAllBytes(r.Db));
    }

    [Fact]
    public void An_unexpected_layout_gives_a_warning_and_changes_nothing()
    {
        using var r = new Rig(withDb: false);
        LauncherDb.Create(r.Db, "CREATE TABLE playsets (id char(36) not null, name varchar(255) not null);");

        var result = LauncherPlaysetFile.Write(r.UserDir, ["mod/a.mod"], r.BackupDir, Now);

        Assert.False(result.Written);
        Assert.Contains("unexpected", result.Warning);
        Assert.Empty(LauncherDb.Query(r.Db, "select * from playsets"));
    }
}
```

`TempDir` already exists in `FazStellarisModmanager.Tests/TestUtil`. Check that it has `Path` and `Write(relative, content)` (FakeInstall uses both); adapt if the API differs.

- [ ] **Step 4: Run the tests and confirm they fail** (the type doesn't exist yet)

```bash
dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art> --filter "FullyQualifiedName~LauncherPlaysetFileTests"
```
Expected: compile error, because LauncherPlaysetFile doesn't exist yet.

- [ ] **Step 5: Implement `Core/Game/LauncherPlaysetFile.cs`**

```csharp
using System.Diagnostics;
using System.Text.Json;
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Library;
using Microsoft.Data.Sqlite;

namespace FazStellarisModmanager.Core.Game;

/// <summary>What <see cref="LauncherPlaysetFile.Write"/> did. Skipped lists descriptors that are neither known to the launcher nor on disk.</summary>
public sealed record LauncherSyncResult(bool Written, string? BackupPath, string? Warning, IReadOnlyList<string> Skipped);

/// <summary>
/// The Paradox launcher's database (launcher-v2.sqlite in the user dir). Whenever the launcher starts (and on Play) it
/// rewrites dlc_load.json from its active playset, so the applied list is also stored there as the active playset.
/// </summary>
public static class LauncherPlaysetFile
{
    public const string FileName = "launcher-v2.sqlite";
    public const string PlaysetName = "FazStellarisModManager";
    const int KeepBackups = 20;
    const string BackupPrefix = "launcher-v2_";

    static readonly string[] LauncherProcesses = ["Paradox Launcher", "dowser"];

    static readonly (string Table, string[] Columns)[] Required =
    [
        ("playsets", ["id", "name", "isActive", "loadOrder", "createdOn", "updatedOn", "isRemoved", "hasNotApprovedChanges", "syncState"]),
        ("playsets_mods", ["playsetId", "modId", "enabled", "position"]),
        ("mods", ["id", "steamId", "gameRegistryId", "displayName", "version", "tags", "requiredVersion", "dirPath", "archivePath", "status", "source", "isNew", "createdDate"]),
    ];

    public static bool IsLauncherRunning()
    {
        foreach (var name in LauncherProcesses)
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var p in processes) p.Dispose();
            if (processes.Length > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Makes <paramref name="descriptorRels"/> (dlc_load.json paths, in load order) the launcher's active playset.
    /// No database: nothing happens. Database problems come back as a warning; the transaction leaves the file unchanged.
    /// </summary>
    public static LauncherSyncResult Write(string userDir, IReadOnlyList<string> descriptorRels, string backupDir, DateTimeOffset now)
    {
        var db = Path.Combine(userDir, FileName);
        if (!File.Exists(db)) return new(false, null, null, []);
        string? backup = null;
        try
        {
            backup = Backup(db, backupDir, now);
            var skipped = WriteDatabase(db, userDir, descriptorRels, now);
            return new(true, backup, null, skipped);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(false, backup, "Could not update the Paradox launcher's playset: " + ex.Message, []);
        }
    }

    static List<string> WriteDatabase(string db, string userDir, IReadOnlyList<string> descriptorRels, DateTimeOffset now)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 5 }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        RequireSchema(conn);
        using var tx = conn.BeginTransaction();
        var ms = now.ToUnixTimeMilliseconds();

        var playset = Scalar(conn, "select id from playsets where name = $n and isRemoved = 0 order by createdOn limit 1", ("$n", PlaysetName)) as string;
        if (playset is null)
        {
            playset = Guid.NewGuid().ToString();
            Exec(conn, "insert into playsets (id, name, isActive, loadOrder, createdOn, updatedOn, isRemoved, hasNotApprovedChanges, syncState) values ($id, $n, 1, 'custom', $now, $now, 0, 0, 'NOT_ELIGIBLE')",
                ("$id", playset), ("$n", PlaysetName), ("$now", ms));
        }
        Exec(conn, "update playsets set isActive = 0 where isActive = 1 and id <> $id", ("$id", playset));
        Exec(conn, "update playsets set isActive = 1, updatedOn = $now where id = $id", ("$id", playset), ("$now", ms));
        Exec(conn, "delete from playsets_mods where playsetId = $id", ("$id", playset));

        var known = KnownMods(conn);
        var skipped = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        foreach (var raw in descriptorRels)
        {
            var rel = raw.Replace((char)92, '/');
            if (!seen.Add(rel)) continue;
            if (!known.TryGetValue(rel, out var modId))
            {
                modId = InsertMod(conn, userDir, rel, now);
                if (modId is null) { skipped.Add(rel); continue; }
                known[rel] = modId;
            }
            Exec(conn, "insert into playsets_mods (playsetId, modId, enabled, position) values ($p, $m, 1, $pos)",
                ("$p", playset), ("$m", modId), ("$pos", position++));
        }
        tx.Commit();
        return skipped;
    }

    // gameRegistryId -> mod id; a ready_to_play row wins over others for the same id.
    static Dictionary<string, string> KnownMods(SqliteConnection conn)
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ready = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select id, gameRegistryId, status from mods where gameRegistryId is not null";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var key = r.GetString(1).Replace((char)92, '/');
            var isReady = !r.IsDBNull(2) && r.GetString(2) == "ready_to_play";
            if (!known.ContainsKey(key) || (isReady && !ready.Contains(key)))
            {
                known[key] = r.GetString(0);
                if (isReady) ready.Add(key);
            }
        }
        return known;
    }

    // A minimal row the launcher completes on its next scan; null when the descriptor isn't on disk.
    static string? InsertMod(SqliteConnection conn, string userDir, string rel, DateTimeOffset now)
    {
        var file = Path.Combine(userDir, rel);
        if (!File.Exists(file)) return null;
        var d = ModDescriptor.Load(file);
        var key = ModKeys.For(rel);
        var id = Guid.NewGuid().ToString();
        Exec(conn, """
            insert into mods (id, steamId, gameRegistryId, displayName, version, tags, requiredVersion, dirPath, archivePath, status, source, isNew, createdDate)
            values ($id, $steam, $reg, $name, $version, $tags, $req, $dir, $archive, 'ready_to_play', $source, 0, $created)
            """,
            ("$id", id),
            ("$steam", ModMatcher.WorkshopIdOf(key, d.RemoteFileId)),
            ("$reg", rel),
            ("$name", d.Name ?? Path.GetFileNameWithoutExtension(rel)),
            ("$version", d.Version),
            ("$tags", JsonSerializer.Serialize(d.Tags)),
            ("$req", d.SupportedVersion),
            ("$dir", d.Path is { Length: > 0 } p ? Full(userDir, p) : null),
            ("$archive", d.Path is not { Length: > 0 } && d.Archive is { Length: > 0 } a ? Full(userDir, a) : null),
            ("$source", ModKeys.WorkshopId(key) is not null ? "steam" : "local"),
            ("$created", now.ToUnixTimeSeconds()));
        return id;
    }

    static string Full(string userDir, string p) => Path.IsPathRooted(p) ? Path.GetFullPath(p) : Path.GetFullPath(Path.Combine(userDir, p));

    static void RequireSchema(SqliteConnection conn)
    {
        foreach (var (table, columns) in Required)
        {
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"pragma table_info({table})";
            using (var r = cmd.ExecuteReader())
                while (r.Read()) have.Add(r.GetString(1));
            var missing = columns.FirstOrDefault(c => !have.Contains(c));
            if (have.Count == 0 || missing is not null)
                throw new InvalidDataException($"{FileName} has an unexpected layout (missing {table}{(have.Count == 0 ? "" : "." + missing)}).");
        }
    }

    static string Backup(string db, string backupDir, DateTimeOffset now)
    {
        Directory.CreateDirectory(backupDir);
        var stem = BackupPrefix + now.UtcDateTime.ToString("yyyyMMdd_HHmmss_fff");
        var target = Path.Combine(backupDir, stem + ".sqlite");
        for (var i = 1; File.Exists(target); i++) target = Path.Combine(backupDir, $"{stem}_{i}.sqlite");
        File.Copy(db, target);
        foreach (var old in Directory.GetFiles(backupDir, BackupPrefix + "*.sqlite").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(KeepBackups))
            if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase)) File.Delete(old);
        return target;
    }

    static object? Scalar(SqliteConnection conn, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(conn, sql, args);
        return cmd.ExecuteScalar();
    }

    static void Exec(SqliteConnection conn, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(conn, sql, args);
        cmd.ExecuteNonQuery();
    }

    static SqliteCommand Command(SqliteConnection conn, string sql, (string Name, object? Value)[] args)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }
}
```

Notes for the implementer:
- In Microsoft.Data.Sqlite, commands on a connection with an open transaction automatically use it; no `cmd.Transaction` assignment is needed.
- Backup pruning sorts by file name, which sorts by time because of the timestamp format.
- The newly written backup sorts last when the pre-existing names are older; the explicit guard keeps it anyway.

- [ ] **Step 6: Run the tests and confirm they pass**

```bash
dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art> --filter "FullyQualifiedName~LauncherPlaysetFileTests"
```
Expected: all 8 pass.

- [ ] **Step 7: Commit**

```bash
git add -A FazStellarisModmanager.Core FazStellarisModmanager.Tests
git commit -m "feat: LauncherPlaysetFile writes the applied list into launcher-v2.sqlite as the active playset"
```

---

### Task 2: Apply integration (ModManagerService, Mods page, Session)

**Files:**
- Modify: `FazStellarisModmanager.Core/ModManagerService.cs` (Apply, IsLauncherRunning, ApplyResult)
- Modify: `FazStellarisModmanager/Pages/Mods.razor` (Apply / ApplyAndLaunch messages, around lines 393-428)
- Modify: `FazStellarisModmanager.Core/Session/SessionService.cs` (`_lastBackupPath` use at line ~199; add LastLauncherWarning)
- Modify: `FazStellarisModmanager/Pages/SessionPage.razor` (Match host result paragraph, line ~92)
- Modify: `FazStellarisModmanager.Tests/ModManagerServiceTests.cs`, `FazStellarisModmanager.Tests/SessionServiceTests.cs`

- [ ] **Step 1: Failing tests in `ModManagerServiceTests.cs`**

In `Create(...)`, set `IsLauncherRunning = () => false`:
```csharp
return new ModManagerService(paths, _ => fake.GameDir) { IsLauncherRunning = () => false };
```
Change the existing `Apply_writes_dlc_load_and_backs_up` test to `var result = svc.Apply(list);`, then `Assert.NotNull(result.BackupPath); Assert.StartsWith(..., result.BackupPath);` and `Assert.False(result.LauncherPlaysetSet); Assert.Null(result.LauncherWarning);`. FakeInstall has no launcher DB. Add:

```csharp
[Fact]
public void Apply_refuses_while_the_launcher_runs_and_writes_nothing()
{
    using var fake = new FakeInstall();
    var svc = Create(fake);
    svc.IsLauncherRunning = () => true;
    var before = File.ReadAllText(Path.Combine(fake.UserDir, "dlc_load.json"));

    var ex = Assert.Throws<InvalidOperationException>(() => svc.Apply(new ModList("L", [new("local:local.mod", "Local Mod", "mod/local.mod", null)], [])));

    Assert.Contains("Paradox launcher", ex.Message);
    Assert.Equal(before, File.ReadAllText(Path.Combine(fake.UserDir, "dlc_load.json")));
}

[Fact]
public void Apply_sets_the_launcher_playset_when_the_launcher_database_exists()
{
    using var fake = new FakeInstall();
    var svc = Create(fake);
    var db = Path.Combine(fake.UserDir, LauncherPlaysetFile.FileName);
    LauncherDb.Create(db);
    LauncherDb.AddMod(db, "m1", "mod/local.mod");

    var result = svc.Apply(new ModList("L", [new("local:local.mod", "Local Mod", "mod/local.mod", null)], []));

    Assert.True(result.LauncherPlaysetSet);
    Assert.Null(result.LauncherWarning);
    Assert.Equal("m1", LauncherDb.Query(db, "select modId from playsets_mods").Single()[0]);
}
```

In `SessionServiceTests.Rig`, construct with `new ModManagerService(paths, _ => Fake.GameDir) { IsLauncherRunning = () => false }`. Search all test files for other `new ModManagerService(` calls whose tests reach `Apply` or Match host, and give them the same initializer. EventGraph and TechTree tests don't apply, so leave them.

- [ ] **Step 2: Run them and confirm they fail**

- [ ] **Step 3: Implement in `ModManagerService.cs`**

Add, next to `ResolvedPaths`:
```csharp
/// <summary>What Apply did: the dlc_load.json backup, whether the launcher playset was set, and a launcher problem to show (null if none).</summary>
public sealed record ApplyResult(string? BackupPath, bool LauncherPlaysetSet, string? LauncherWarning);
```
Replace `Apply`:
```csharp
/// <summary>Checked by <see cref="Apply"/>: while the Paradox launcher runs it would overwrite dlc_load.json from its own playset.</summary>
public Func<bool> IsLauncherRunning { get; set; } = LauncherPlaysetFile.IsLauncherRunning;

/// <summary>
/// Writes the list to dlc_load.json (after backing up the old file), then makes it the Paradox launcher's active playset,
/// so opening the launcher keeps it. Refuses while the launcher runs. Launcher database problems are returned as a warning.
/// </summary>
public ApplyResult Apply(ModList list)
{
    if (IsLauncherRunning()) throw new InvalidOperationException("Close the Paradox launcher first, or it will overwrite your mod list.");
    lock (_gate)
    {
        var user = Resolve().UserDir;
        var load = list.ToDlcLoad();
        var backup = DlcLoadFile.Write(user, load, _paths.Backups);
        var sync = LauncherPlaysetFile.Write(user, load.EnabledMods, _paths.Backups, DateTimeOffset.UtcNow);
        var warnings = new List<string>();
        if (sync.Warning is { } w) warnings.Add(w);
        if (sync.Skipped.Count > 0) warnings.Add("Not added to the launcher playset (descriptor missing): " + string.Join(", ", sync.Skipped) + ".");
        return new ApplyResult(backup, sync.Written, warnings.Count == 0 ? null : string.Join(" ", warnings));
    }
}
```

- [ ] **Step 4: Mods.razor.** Replace the bodies' Apply calls:

In `Apply()`:
```csharp
var result = Manager.Apply(Build());
var message = $"Applied {current.Count} mods to dlc_load.json" + (result.BackupPath is null ? "." : $" (backup: {Path.GetFileName(result.BackupPath)}).")
    + (result.LauncherPlaysetSet ? " Set as the active Paradox launcher playset." : "");
if (result.LauncherWarning is { } warning)
{
    (status, error) = (message + " " + warning, true);
    return null;
}
return message;
```
In `ApplyAndLaunch()`, replace `Manager.Apply(Build());` with `var result = Manager.Apply(Build());`. Then replace the final `return "Applied and launched Stellaris." + steamWarning;` with:
```csharp
if (result.LauncherWarning is { } warning)
{
    (status, error) = ("Applied and launched Stellaris." + steamWarning + " " + warning, true);
    return null;
}
return "Applied and launched Stellaris." + steamWarning;
```

- [ ] **Step 5: SessionService and SessionPage.** In SessionService:
- add `volatile string? _lastLauncherWarning;` and `public string? LastLauncherWarning => _lastLauncherWarning;` next to `_lastBackupPath` and `LastBackupPath`;
- set `_lastLauncherWarning = null;` everywhere `_lastBackupPath = null;` is set;
- replace `_lastBackupPath = manager.Apply(plan.ToApply);` with:
```csharp
var result = manager.Apply(plan.ToApply);
_lastBackupPath = result.BackupPath;
_lastLauncherWarning = result.LauncherWarning;
```
In SessionPage.razor, directly after the `<p>Wrote the host's list …</p>` line, add:
```razor
@if (Session.LastLauncherWarning is { } launcherWarning)
{
    <p class="status error">@launcherWarning</p>
}
```

- [ ] **Step 6: Build the whole solution and run all tests**

```bash
dotnet build FazStellarisModmanager.sln -c Release --artifacts-path <art>
dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art>
```
Expected: the build succeeds and all tests pass. The only failure that can be ignored is the known flaky HelperProcessWorkshopService test: rerun once. Use the `.sln` or `.slnx` file the repo actually has.

- [ ] **Step 7: Commit**

```bash
git add -A FazStellarisModmanager FazStellarisModmanager.Core FazStellarisModmanager.Tests
git commit -m "feat: Apply sets the Paradox launcher's active playset and refuses while the launcher is open"
```

---

### Task 3: Real-data check (controller)
- Copy `C:/Users/SCP Fazbear/Documents/Paradox Interactive/Stellaris/launcher-v2.sqlite` into the scratchpad, along with a scratch user dir that has `mod/` descriptors (only the copied database is written).
- Call `LauncherPlaysetFile.Write` with the current `dlc_load.json` enabled_mods.
- Check:
  - the playset "FazStellarisModManager" exists and is active;
  - IronyModManager is now inactive;
  - there are 58 rows in order, with each `gameRegistryId` matching;
  - no new mods rows were inserted.

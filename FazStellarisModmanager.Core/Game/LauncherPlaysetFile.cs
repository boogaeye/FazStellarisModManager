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

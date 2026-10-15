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

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
        r.Write("mod/ugc_222.mod", """
            name="Two"
            version="1.2"
            supported_version="v4.5.*"
            tags={
            "Gameplay"
            "UI"
            }
            path="D:/ws/222"
            remote_file_id="222"
            """);
        r.Write("mod/zipped.mod", """
            name="Zipped"
            archive="mod/zipped.zip"
            """);

        var result = LauncherPlaysetFile.Write(r.UserDir, ["mod/ugc_222.mod", "mod/gone.mod", "mod/zipped.mod"], r.BackupDir, Now);

        Assert.True(result.Written);
        Assert.Equal(["mod/gone.mod"], result.Skipped);
        Assert.Equal(["mod/ugc_222.mod", "mod/zipped.mod"], PlaysetRegistryIds(r, OurPlaysetId(r)!));

        var two = LauncherDb.Query(r.Db, "select steamId, displayName, version, requiredVersion, tags, dirPath, archivePath, status, source, isNew, createdDate from mods where gameRegistryId = 'mod/ugc_222.mod'").Single();
        Assert.Equal("222", two[0]);
        Assert.Equal("Two", two[1]);
        Assert.Equal("1.2", two[2]);
        Assert.Equal("v4.5.*", two[3]);
        Assert.Equal("""["Gameplay","UI"]""", two[4]);
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

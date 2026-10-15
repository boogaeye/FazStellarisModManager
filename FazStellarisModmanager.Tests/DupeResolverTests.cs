using System.Text;
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class DupeResolverTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_123);
    static readonly string Crlf = (char)13 + "" + (char)10;
    static readonly string Lf = (char)10 + "";

    sealed class Rig : IDisposable
    {
        readonly TempDir _tmp = new();
        public string UserDir => Path.Combine(_tmp.Path, "user");
        public string BackupDir => Path.Combine(_tmp.Path, "backups");
        public string Db => Path.Combine(UserDir, LauncherPlaysetFile.FileName);
        public Rig() => Directory.CreateDirectory(Path.Combine(UserDir, "mod"));
        public string PathOf(string rel) => Path.Combine(UserDir, rel);
        public byte[] Write(string rel, byte[] bytes)
        {
            File.WriteAllBytes(PathOf(rel), bytes);
            return bytes;
        }
        public byte[] Read(string rel) => File.ReadAllBytes(PathOf(rel));
        public void Dispose() => _tmp.Dispose();
    }

    static InstalledMod Mod(string rel, string name, bool workshop = false) =>
        new(ModKeys.For(rel), name, rel, null, "1", "v4.5.*", "C:/x/" + rel, workshop ? ModSource.Workshop : ModSource.Local, []);

    static byte[] Bytes(string text, string newline, bool bom)
    {
        var body = Encoding.UTF8.GetBytes(text.ReplaceLineEndings(newline));
        return bom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    const string LoserText = """
        version="2.1"
        tags={
        	"Interface"
        }
        name="UI Overhaul Dynamic"
        supported_version="v4.5.*"
        path="D:/mods/uiod"
        """;

    const string KeeperText = """
        name="UI Overhaul Dynamic"
        version="2.1"
        remote_file_id="1"
        path="D:/ws/1"
        """;

    // The usual pair: keep the Workshop copy (mod/ugc_1.mod), rename the local one (mod/8cde_a.mod).
    static (byte[] Keeper, byte[] Loser, DupeChoice Choice) Uiod(Rig r)
    {
        var keeper = r.Write("mod/ugc_1.mod", Bytes(KeeperText, Lf, bom: false));
        var loser = r.Write("mod/8cde_a.mod", Bytes(LoserText, Crlf, bom: true));
        var group = DupeConflicts.Find([Mod("mod/ugc_1.mod", "UI Overhaul Dynamic", workshop: true), Mod("mod/8cde_a.mod", "UI Overhaul Dynamic")]).Single();
        return (keeper, loser, new DupeChoice(group, group.Mods.Single(m => m.DescriptorRel == "mod/ugc_1.mod")));
    }

    [Fact]
    public void Renames_the_losers_and_leaves_the_keeper_and_every_other_byte_alone()
    {
        using var r = new Rig();
        var (keeper, loser, choice) = Uiod(r);

        var result = DupeResolver.Resolve(r.UserDir, [choice], r.BackupDir, Now);

        var expected = Bytes(LoserText.Replace("UI Overhaul Dynamic", "UI Overhaul Dynamic (duplicate: 8cde_a)"), Crlf, bom: true);
        Assert.Equal(expected, r.Read("mod/8cde_a.mod"));
        Assert.Equal(keeper, r.Read("mod/ugc_1.mod"));
        Assert.Equal([new DupeRename("mod/8cde_a.mod", "UI Overhaul Dynamic", "UI Overhaul Dynamic (duplicate: 8cde_a)")], result.Renamed);
        Assert.Null(result.LauncherWarning);

        Assert.NotNull(result.BackupDir);
        Assert.Equal(Path.Combine(r.BackupDir, "descriptors_" + Now.UtcDateTime.ToString("yyyyMMdd_HHmmss_fff")), result.BackupDir);
        Assert.Equal(["8cde_a.mod"], Directory.GetFiles(result.BackupDir).Select(Path.GetFileName));
        Assert.Equal(loser, File.ReadAllBytes(Path.Combine(result.BackupDir, "8cde_a.mod")));
        Assert.False(File.Exists(r.PathOf("mod/8cde_a.mod.tmp")));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void A_descriptor_without_a_name_line_gets_one_at_the_top(bool bom, bool crlf)
    {
        using var r = new Rig();
        var newline = crlf ? Crlf : Lf;
        const string text = """
            version="1"
            path="D:/mods/f"
            """;
        r.Write("mod/f.mod", Bytes(text, newline, bom));
        r.Write("mod/ugc_1.mod", Bytes(KeeperText, Lf, bom: false));
        var group = DupeConflicts.Find([Mod("mod/f.mod", "X"), Mod("mod/ugc_1.mod", "X", workshop: true)]).Single();

        var result = DupeResolver.Resolve(r.UserDir, [new DupeChoice(group, group.Mods[1])], r.BackupDir, Now);

        var expected = Bytes("name=" + '"' + "X (duplicate: f)" + '"' + newline + text.ReplaceLineEndings(newline), newline, bom);
        Assert.Equal(expected, r.Read("mod/f.mod"));
        Assert.Equal("X (duplicate: f)", result.Renamed.Single().NewName);
    }

    [Fact]
    public void SetName_changes_only_the_top_level_name_line_after_blocks()
    {
        const string text = """
            version="1"
            tags={
            	"name"
            	"Gameplay"
            }
            dependencies = { "name" }
            name="A"
            path="D:/mods/a"
            """;
        const string expected = """
            version="1"
            tags={
            	"name"
            	"Gameplay"
            }
            dependencies = { "name" }
            name="B (duplicate: a)"
            path="D:/mods/a"
            """;

        Assert.Equal(expected, DupeResolver.SetName(text, "B (duplicate: a)"));
    }

    [Fact]
    public void SetName_keeps_spacing_around_the_equals_sign()
    {
        Assert.Equal("  name = " + '"' + "B" + '"' + Lf + "version=" + '"' + "1" + '"',
            DupeResolver.SetName("  name = " + '"' + "A" + '"' + Lf + "version=" + '"' + "1" + '"', "B"));
    }

    [Fact]
    public void Updates_the_launcher_display_name_of_renamed_mods()
    {
        using var r = new Rig();
        var (_, _, choice) = Uiod(r);
        LauncherDb.Create(r.Db);
        LauncherDb.AddMod(r.Db, "id1", "mod/ugc_1.mod");
        LauncherDb.AddMod(r.Db, "id8", "mod/8cde_a.mod");
        LauncherDb.AddMod(r.Db, "id8win", "mod" + (char)92 + "8CDE_a.mod");

        var result = DupeResolver.Resolve(r.UserDir, [choice], r.BackupDir, Now);

        Assert.Null(result.LauncherWarning);
        string? DisplayName(string id) => LauncherDb.Query(r.Db, "select displayName from mods where id = $id", ("$id", id)).Single()[0] as string;
        Assert.Equal("UI Overhaul Dynamic (duplicate: 8cde_a)", DisplayName("id8"));
        Assert.Equal("UI Overhaul Dynamic (duplicate: 8cde_a)", DisplayName("id8win"));
        Assert.Equal("mod/ugc_1.mod", DisplayName("id1"));
        Assert.Single(Directory.GetFiles(r.BackupDir, "launcher-v2_*.sqlite"));
    }

    [Fact]
    public void No_launcher_database_means_no_warning()
    {
        using var r = new Rig();
        var (_, _, choice) = Uiod(r);

        var result = DupeResolver.Resolve(r.UserDir, [choice], r.BackupDir, Now);

        Assert.Null(result.LauncherWarning);
        Assert.Single(result.Renamed);
        Assert.False(File.Exists(r.Db));
        Assert.Empty(Directory.GetFiles(r.BackupDir, "launcher-v2_*.sqlite"));
    }

    [Fact]
    public void A_corrupt_launcher_database_gives_a_warning_but_the_descriptors_are_still_renamed()
    {
        using var r = new Rig();
        var (_, _, choice) = Uiod(r);
        var junk = Enumerable.Range(0, 4096).Select(i => (byte)(i * 7)).ToArray();
        File.WriteAllBytes(r.Db, junk);

        var result = DupeResolver.Resolve(r.UserDir, [choice], r.BackupDir, Now);

        Assert.NotNull(result.LauncherWarning);
        Assert.Equal(junk, File.ReadAllBytes(r.Db));
        Assert.Single(result.Renamed);
        Assert.Contains("UI Overhaul Dynamic (duplicate: 8cde_a)", Encoding.UTF8.GetString(r.Read("mod/8cde_a.mod")));
    }
}

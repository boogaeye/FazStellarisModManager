using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class DlcLoadFileTests
{
    [Fact]
    public void Missing_file_reads_as_empty()
    {
        using var tmp = new TempDir();

        var load = DlcLoadFile.Read(tmp.Path);

        Assert.Empty(load.EnabledMods);
        Assert.Empty(load.DisabledDlcs);
    }

    [Fact]
    public void Reads_real_format()
    {
        using var tmp = new TempDir();
        tmp.Write("dlc_load.json", "{\"disabled_dlcs\":[\"dlc/dlc001_x/dlc001.dlc\"],\"enabled_mods\":[\"mod/ugc_1.mod\",\"mod/EthicsFix.mod\"]}");

        var load = DlcLoadFile.Read(tmp.Path);

        Assert.Equal(new[] { "mod/ugc_1.mod", "mod/EthicsFix.mod" }, load.EnabledMods);
        Assert.Equal(new[] { "dlc/dlc001_x/dlc001.dlc" }, load.DisabledDlcs);
    }

    [Fact]
    public void Write_backs_up_previous_file_and_round_trips()
    {
        using var tmp = new TempDir();
        var original = "{\"disabled_dlcs\":[],\"enabled_mods\":[\"mod/old.mod\"]}";
        tmp.Write("user/dlc_load.json", original);
        var userDir = Path.Combine(tmp.Path, "user");
        var backups = Path.Combine(tmp.Path, "backups");

        var backup = DlcLoadFile.Write(userDir, new DlcLoad(["mod/ugc_2.mod", "mod/a&b.mod"], []), backups);

        Assert.NotNull(backup);
        Assert.Equal(original, File.ReadAllText(backup));
        Assert.Equal(new[] { "mod/ugc_2.mod", "mod/a&b.mod" }, DlcLoadFile.Read(userDir).EnabledMods);
        Assert.Contains("\"mod/a&b.mod\"", File.ReadAllText(Path.Combine(userDir, "dlc_load.json")));
    }

    [Fact]
    public void Write_without_existing_file_returns_null_backup()
    {
        using var tmp = new TempDir();

        var backup = DlcLoadFile.Write(tmp.Path, new DlcLoad(["mod/x.mod"], []), Path.Combine(tmp.Path, "b"));

        Assert.Null(backup);
        Assert.Single(DlcLoadFile.Read(tmp.Path).EnabledMods);
    }

    [Fact]
    public void Write_leaves_no_temp_file()
    {
        using var tmp = new TempDir();

        DlcLoadFile.Write(tmp.Path, new DlcLoad(["mod/x.mod"], []), Path.Combine(tmp.Path, "b"));

        Assert.Empty(Directory.GetFiles(tmp.Path, "*.tmp"));
        Assert.Equal(new[] { "mod/x.mod" }, DlcLoadFile.Read(tmp.Path).EnabledMods);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" 	 ")]
    public void Empty_or_whitespace_file_reads_as_empty(string content)
    {
        using var tmp = new TempDir();
        tmp.Write("dlc_load.json", content);

        var load = DlcLoadFile.Read(tmp.Path);

        Assert.Empty(load.EnabledMods);
        Assert.Empty(load.DisabledDlcs);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("{\"enabled_mods\":[1]}")]
    public void Malformed_file_throws_InvalidDataException_naming_path(string content)
    {
        using var tmp = new TempDir();
        tmp.Write("dlc_load.json", content);

        var ex = Assert.Throws<InvalidDataException>(() => DlcLoadFile.Read(tmp.Path));

        Assert.Contains(Path.Combine(tmp.Path, "dlc_load.json"), ex.Message);
    }

    [Fact]
    public void Null_or_missing_properties_read_as_empty_lists()
    {
        using var tmp = new TempDir();
        tmp.Write("dlc_load.json", "{\"enabled_mods\":null}");

        var load = DlcLoadFile.Read(tmp.Path);

        Assert.Empty(load.EnabledMods);
        Assert.Empty(load.DisabledDlcs);
    }

    [Fact]
    public void Utf8_bom_file_reads_fine()
    {
        using var tmp = new TempDir();
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes("{\"enabled_mods\":[\"mod/a.mod\"]}")).ToArray();
        File.WriteAllBytes(Path.Combine(tmp.Path, "dlc_load.json"), bytes);

        Assert.Equal(new[] { "mod/a.mod" }, DlcLoadFile.Read(tmp.Path).EnabledMods);
    }

    [Fact]
    public void Back_to_back_writes_create_distinct_backups()
    {
        using var tmp = new TempDir();
        var original = "{\"disabled_dlcs\":[],\"enabled_mods\":[\"mod/old.mod\"]}";
        tmp.Write("dlc_load.json", original);
        var backups = Path.Combine(tmp.Path, "backups");

        var b1 = DlcLoadFile.Write(tmp.Path, new DlcLoad(["mod/1.mod"], []), backups);
        var b2 = DlcLoadFile.Write(tmp.Path, new DlcLoad(["mod/2.mod"], []), backups);

        Assert.NotNull(b1);
        Assert.NotNull(b2);
        Assert.NotEqual(b1, b2);
        Assert.Equal(2, Directory.GetFiles(backups).Length);
        Assert.Equal(original, File.ReadAllText(b1));
    }
}

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
}

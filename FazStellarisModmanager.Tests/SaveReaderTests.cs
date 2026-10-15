using System.IO.Compression;
using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class SaveReaderTests
{
    internal static string WriteSave(string path, string gamestate, string name = "mp_Test Empire", string date = "2387.07.01", string extraMeta = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var w = new StreamWriter(zip.CreateEntry("meta").Open()))
            w.Write($"version=\"Cygnus v4.5.1\"{(char)10}name=\"{name}\"{(char)10}date=\"{date}\"{(char)10}{extraMeta}");
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
        Assert.Throws<InvalidDataException>(() => SaveReader.ReadTechs(bad));
        var noMeta = Path.Combine(t.Path, "nometa.sav");
        using (var z = ZipFile.Open(noMeta, ZipArchiveMode.Create)) z.CreateEntry("gamestate");
        Assert.Throws<InvalidDataException>(() => SaveReader.Read(noMeta));
        Assert.Throws<InvalidDataException>(() => SaveReader.ReadTechs(noMeta));
    }

    [Fact]
    public void Full_read_is_not_tech_only()
    {
        using var t = new TempDir();
        var path = WriteSave(Path.Combine(t.Path, "x", "autosave.sav"), GamestateScannerTests.Fixture);

        Assert.False(SaveReader.Read(path).TechOnly);
    }

    [Fact]
    public void ReadTechs_reads_meta_players_and_player_techs_only()
    {
        using var t = new TempDir();
        var path = WriteSave(Path.Combine(t.Path, "x", "autosave.sav"), GamestateScannerTests.Fixture,
            extraMeta: "required_dlcs={ " + '"' + "Utopia" + '"' + " }" + (char)10);

        var s = SaveReader.ReadTechs(path);

        Assert.True(s.TechOnly);
        Assert.Equal("mp_Test Empire", s.SaveName);
        Assert.Equal("2387.07.01", s.Date);
        Assert.Equal("Cygnus v4.5.1", s.Version);
        Assert.Equal(path, s.SavePath);
        Assert.Equal(File.GetLastWriteTimeUtc(path), s.SavedUtc);
        Assert.Equal(["Utopia"], s.Dlcs);
        Assert.Equal(2, s.Players.Count);
        Assert.Equal([0, 1], s.Countries.Select(c => c.Id));
        Assert.Equal(["tech_a", "tech_b"], s.Countries[0].Techs);
        Assert.Equal(SaveReader.Read(path).Countries[0].Techs, s.Countries[0].Techs);
        Assert.Null(s.Community);
        Assert.Null(s.Megastructures);
        Assert.Null(s.GlobalFlags);
        Assert.Null(s.Galaxy);
        Assert.Null(s.Diplo);
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

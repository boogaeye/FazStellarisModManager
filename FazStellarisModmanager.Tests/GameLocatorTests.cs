using FazStellarisModmanager.Core.Paths;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class GameLocatorTests
{
    [Fact]
    public void Parses_library_folders_vdf()
    {
        var vdf = """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            		"apps" { "228980" "1" }
            	}
            	"1"
            	{
            		"path"		"D:\\SteamLibrary"
            	}
            }
            """;

        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, GameLocator.ParseLibraryFolders(vdf));
    }

    [Fact]
    public void Finds_game_in_second_library()
    {
        using var tmp = new TempDir();
        var lib1 = tmp.Mkdir("lib1");
        var lib2 = tmp.Mkdir("lib2");
        tmp.Write("lib2/steamapps/common/Stellaris/checksum_manifest.txt", "");

        var found = GameLocator.FindGameDir(null, [lib1, lib2]);

        Assert.Equal(Path.Combine(lib2, "steamapps", "common", "Stellaris"), found);
    }

    [Fact]
    public void Explicit_dir_is_used_only_if_valid()
    {
        using var tmp = new TempDir();
        var game = tmp.Mkdir("game");

        Assert.Null(GameLocator.FindGameDir(game, []));
        tmp.Write("game/checksum_manifest.txt", "");
        Assert.Equal(game, GameLocator.FindGameDir(game, []));
    }

    [Fact]
    public void Workshop_dir_is_in_same_library_as_game()
    {
        var dir = GameLocator.WorkshopDirFor(@"D:\SteamLibrary\steamapps\common\Stellaris");

        Assert.Equal(@"D:\SteamLibrary\steamapps\workshop\content\281990", dir);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void BuildLibraries_is_empty_without_steam_path(string? steamPath)
    {
        Assert.Empty(GameLocator.BuildLibraries(steamPath, "\"path\"  \"D:\\\\SteamLibrary\""));
    }

    [Fact]
    public void BuildLibraries_normalizes_dedupes_and_skips_blank()
    {
        var vdf = "\"path\"  \"C:\\\\Program Files (x86)\\\\Steam\"\n\"path\"  \"D:\\\\SteamLibrary\"\n\"path\"  \"\"\n";

        var libs = GameLocator.BuildLibraries("c:/program files (x86)/steam", vdf);

        Assert.Equal(
            new[] { @"c:\program files (x86)\steam", @"D:\SteamLibrary" },
            libs,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildLibraries_skips_paths_with_invalid_chars()
    {
        var vdf = "\"path\"  \"C:\\\\bad\0name\"\n\"path\"  \"D:\\\\SteamLibrary\"";

        var libs = GameLocator.BuildLibraries("C:/Steam", vdf);

        Assert.Equal(new[] { @"C:\Steam", @"D:\SteamLibrary" }, libs, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseLibraryFolders_of_empty_text_is_empty()
    {
        Assert.Empty(GameLocator.ParseLibraryFolders(""));
    }
}

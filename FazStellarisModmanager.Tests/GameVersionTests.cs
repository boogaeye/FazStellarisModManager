using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class GameVersionTests
{
    // As the game ships it: tabs, a leading-comma line and an array after the version.
    const string Real = """
        {
        	"gameId": "stellaris",
        	"version": "Cygnus v4.5.2 (9776)",
        	"modsCompatibilityVersion": "4.5",
        	"rawVersion": "v4.5.2",
        	"distPlatform": "steam",
        	"exeArgs": [ "-gdpr-compliant" ]
        		,"ingameSettingsLayoutPath": "./settings-layout.json"
        }
        """;

    [Fact]
    public void Reads_the_raw_version_from_launcher_settings()
    {
        using var tmp = new TempDir();
        tmp.Write("launcher-settings.json", Real);

        Assert.Equal("v4.5.2", GameVersion.Read(tmp.Path));
    }

    [Fact]
    public void Tolerates_comments_and_trailing_commas()
    {
        using var tmp = new TempDir();
        tmp.Write("launcher-settings.json", """
            {
            	// patched
            	"rawVersion": "v4.6.0",
            }
            """);

        Assert.Equal("v4.6.0", GameVersion.Read(tmp.Path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("""{ "version": "Cygnus v4.5.2 (9776)" }""")]
    [InlineData("""{ "rawVersion": 452 }""")]
    [InlineData("""{ "rawVersion": "  " }""")]
    public void Missing_or_unusable_files_give_null(string? content)
    {
        using var tmp = new TempDir();
        if (content is not null) tmp.Write("launcher-settings.json", content);

        Assert.Null(GameVersion.Read(tmp.Path));
    }

    [Fact]
    public void No_game_dir_gives_null()
    {
        using var tmp = new TempDir();

        Assert.Null(GameVersion.Read(null));
        Assert.Null(GameVersion.Read(Path.Combine(tmp.Path, "missing")));
    }
}

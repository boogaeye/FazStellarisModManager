using System.IO.Compression;

namespace FazStellarisModmanager.Tests.TestUtil;

/// <summary>
/// lib/steamapps/common/Stellaris          game: manifest, common/*.txt, one DLC zip
/// lib/steamapps/workshop/content/281990   workshop item 111 (no mod/ugc_111.mod yet)
/// user/                                   logs/game.log, mod/local.mod (+content), dlc_load.json
/// </summary>
public sealed class FakeInstall : IDisposable
{
    readonly TempDir _tmp = new();

    public string Root => _tmp.Path;
    public string Library => Path.Combine(Root, "lib");
    public string GameDir => Path.Combine(Library, "steamapps", "common", "Stellaris");
    public string WorkshopDir => Path.Combine(Library, "steamapps", "workshop", "content", "281990");
    public string UserDir => Path.Combine(Root, "user");
    public string DataDir => Path.Combine(Root, "appdata");

    public FakeInstall()
    {
        _tmp.Write("lib/steamapps/common/Stellaris/checksum_manifest.txt",
            "directory = {\n\tname = \"common\"\n\tsub_directories = yes\n\tfile_extension = \".txt\"\n}\n");
        _tmp.Write("lib/steamapps/common/Stellaris/common/a.txt", "base a");
        _tmp.Write("lib/steamapps/common/Stellaris/common/sub/b.txt", "base b");
        _tmp.Write("lib/steamapps/common/Stellaris/common/icon.dds", "not hashed: wrong extension");
        _tmp.Write("lib/steamapps/common/Stellaris/dlc/dlc001_test/dlc001.dlc",
            "name=\"Test DLC\"\narchive=\"dlc/dlc001_test/dlc001.zip\"\nsteam_id=\"999\"\n");
        using (var zip = ZipFile.Open(Path.Combine(GameDir, "dlc", "dlc001_test", "dlc001.zip"), ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("music/song.ogg").Open()))
            w.Write("la la la");

        _tmp.Write("lib/steamapps/workshop/content/281990/111/descriptor.mod",
            "name=\"Workshop One\"\nsupported_version=\"v4.*\"\ntags={\n\t\"Gameplay\"\n}\n");
        _tmp.Write("lib/steamapps/workshop/content/281990/111/common/x.txt", "ws one");

        _tmp.Write("user/logs/game.log", "[00:00:00][game.cpp:100]: Game Version: Pegasus v4.4.6\r\n");
        _tmp.Write("user/mod/local.mod", "name=\"Local Mod\"\npath=\"mod/local\"\n");
        _tmp.Write("user/mod/local/common/y.txt", "local y");
        _tmp.Write("user/dlc_load.json", "{\"disabled_dlcs\":[],\"enabled_mods\":[\"mod/local.mod\"]}");
    }

    /// <summary>A downloaded Workshop item: lib/steamapps/workshop/content/281990/&lt;id&gt; with a descriptor and one file.</summary>
    public void AddWorkshopItem(ulong id)
    {
        _tmp.Write($"lib/steamapps/workshop/content/281990/{id}/descriptor.mod", $"name=\"Workshop {id}\"" + (char)10 + "supported_version=\"v4.*\"" + (char)10);
        _tmp.Write($"lib/steamapps/workshop/content/281990/{id}/common/w{id}.txt", $"ws {id}");
    }

    /// <summary>The base game's strategic resources (energy on the market, nanites off it) and launcher-settings.json saying v4.5.2, for Faz Addons builds.</summary>
    public void AddMarket()
    {
        _tmp.Write("lib/steamapps/common/Stellaris/launcher-settings.json", """{ "gameId": "stellaris", "rawVersion": "v4.5.2" }""");
        _tmp.Write("lib/steamapps/common/Stellaris/" + MarketFile, MarketFixture.LfText("""
            energy = {
                tradable = yes
                category = basic
                market_amount = 100
                market_price = 100
                max = 25000
            }
            nanites = {
                tradable = no
                category = rare
            }

            """));
    }

    /// <summary>The strategic resources file <see cref="AddMarket"/> writes, relative to the game (or a mod's content) folder.</summary>
    public const string MarketFile = "common/strategic_resources/00_strategic_resources.txt";

    public string Write(string relative, string content) => _tmp.Write(relative, content);

    public void Dispose() => _tmp.Dispose();
}

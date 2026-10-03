using System.IO.Compression;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Tests.TestUtil;

/// <summary>
/// Base game + Mod A (folder) + Mod B (zip) exercising every override rule:
/// - tech_b: base 00_phys.txt, overridden by Mod A zz_moda.txt (later filename wins)
/// - tech_c: base wins over Mod B's 000_early.txt (that file sorts first)
/// - tech_d: base 00_eng.txt replaced at file level by Mod B's 00_eng.txt
/// - tech_e (Mod A), tech_f (Mod B) are new
/// </summary>
public sealed class TechFixture : IDisposable
{
    readonly TempDir _tmp = new();
    public List<ContentSource> Sources { get; } = [];

    public TechFixture()
    {
        _tmp.Write("base/common/scripted_variables/00_vars.txt", "@tier1cost1 = 1000\n@globalTier = 2\n");
        _tmp.Write("base/common/technology/00_phys.txt", """
            @local_cost = 500
            tech_a = {
            	area = physics
            	tier = 0
            	category = { particles }
            	cost = @local_cost
            	start_tech = yes
            }
            tech_b = {
            	area = physics
            	tier = 1
            	category = { particles }
            	cost = @tier1cost1
            	prerequisites = { "tech_a" }
            	potential = { NOT = { host_has_dlc = "Apocalypse" } }
            }
            tech_c = {
            	area = society
            	tier = @globalTier
            	category = { biology }
            	cost = 300
            	prerequisites = { "tech_b" }
            	is_rare = yes
            }
            """);
        _tmp.Write("base/common/technology/00_eng.txt", """
            tech_d = {
            	area = engineering
            	tier = 1
            	category = { voidcraft }
            	cost = 400
            	prerequisites = { "tech_a" }
            	levels = -1
            	icon = "tech_a"
            }
            """);
        _tmp.Write("base/common/technology/category/00_category.txt", "particles = { icon = x }\n");
        _tmp.Write("base/localisation/english/tech_l_english.yml", "l_english:\n tech_a:0 \"§YAlpha§! Tech\"\n tech_a_desc:0 \"Starts $tech_b$ £energy£ research.\"\n tech_b:0 \"Beta\"\n");

        _tmp.Write("moda/common/technology/zz_moda.txt", """
            tech_b = { area = physics tier = 2 category = { particles } cost = 999 prerequisites = { "tech_a" } }
            tech_e = { area = physics tier = 3 category = { particles } cost = 50 prerequisites = { "tech_b" "tech_c" } }
            """);
        _tmp.Write("moda/localisation/english/moda_l_english.yml", "l_english:\n tech_e:0 \"Epsilon\"\n");
        _tmp.Write("moda/localisation/replace/english/r_l_english.yml", "l_english:\n tech_b:0 \"Beta Prime\"\n");

        var zipPath = Path.Combine(_tmp.Path, "modb.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            Entry(zip, "common/technology/00_eng.txt", "tech_d = { area = engineering tier = 1 category = { voidcraft } cost = 1 prerequisites = { \"tech_a\" } }\ntech_f = { area = engineering tier = 1 category = { voidcraft } cost = 2 }\n");
            Entry(zip, "common/technology/000_early.txt", "tech_c = { area = society tier = 9 cost = 1 }\n");
        }

        Sources.Add(ContentSource.FromPath("Base game", Path.Combine(_tmp.Path, "base"), isBaseGame: true));
        Sources.Add(ContentSource.FromPath("Mod A", Path.Combine(_tmp.Path, "moda")));
        Sources.Add(ContentSource.FromPath("Mod B", zipPath));
    }

    static void Entry(ZipArchive zip, string name, string text)
    {
        using var w = new StreamWriter(zip.CreateEntry(name).Open());
        w.Write(text);
    }

    public void Dispose()
    {
        foreach (var s in Sources) s.Dispose();
        _tmp.Dispose();
    }
}

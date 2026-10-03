using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class LocalisationTests
{
    [Fact]
    public void Parses_lines_and_cleans_text()
    {
        var loc = new Localisation();
        loc.AddText("""
            l_english:
             tech_a:0 "§YAlpha§! Tech"
             tech_a_desc:1 "Starts $tech_b$ £energy£ research.\nNext line" # trailing comment
             tech_b: "Beta"
             tech_c:0 "Uses $missing_key$ and $tech_a$"
            """);

        Assert.Equal("Alpha Tech", loc.Get("tech_a"));
        Assert.Equal("Starts Beta research.\nNext line", loc.Get("tech_a_desc"));
        Assert.Equal("Uses missing_key and Alpha Tech", loc.Get("tech_c"));
        Assert.Null(loc.Get("nope"));
        Assert.Equal(4, loc.Count);
    }

    [Fact]
    public void Later_sources_and_replace_folders_win()
    {
        using var tmp = new TempDir();
        tmp.Write("base/localisation/english/a_l_english.yml", "l_english:\n tech_a:0 \"Base A\"\n tech_b:0 \"Base B\"\n");
        tmp.Write("base/localisation/french/a_l_french.yml", "l_french:\n tech_a:0 \"Français\"\n");
        tmp.Write("mod1/localisation/replace/english/r_l_english.yml", "l_english:\n tech_a:0 \"Replaced A\"\n");
        tmp.Write("mod2/localisation/english/m_l_english.yml", "l_english:\n tech_a:0 \"Mod2 A\"\n tech_b:0 \"Mod2 B\"\n");
        using var b = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "base"), isBaseGame: true);
        using var m1 = ContentSource.FromPath("Mod1", Path.Combine(tmp.Path, "mod1"));
        using var m2 = ContentSource.FromPath("Mod2", Path.Combine(tmp.Path, "mod2"));
        var warnings = new List<string>();

        var loc = Localisation.Load([b, m1, m2], warnings);

        Assert.Equal("Replaced A", loc.Get("tech_a"));
        Assert.Equal("Mod2 B", loc.Get("tech_b"));
        Assert.Empty(warnings);
    }
}

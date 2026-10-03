using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class TechSourcesTests
{
    [Fact]
    public void Builds_unique_names_and_warns_on_unusable_mods()
    {
        using var fx = new FakeInstall();
        fx.Write("user/mod/missingdir.mod", "name=\"Gone\"\npath=\"mod/nothere\"\n");
        fx.Write("user/mod/corrupt.mod", "name=\"Corrupt\"\narchive=\"mod/bad.zip\"\n");
        fx.Write("user/mod/bad.zip", "garbage bytes that are not a zip");
        fx.Write("user/mod/other/common/z.txt", "z");
        fx.Write("user/mod/other.mod", "name=\"Local Mod\"\npath=\"mod/other\"\n");

        var list = new ModList("t",
        [
            new("mod/local.mod", "Local Mod", "mod/local.mod", null),
            new("mod/nodesc.mod", "No Descriptor", "mod/nodesc.mod", null),
            new("mod/missingdir.mod", "Gone", "mod/missingdir.mod", null),
            new("mod/corrupt.mod", "Corrupt", "mod/corrupt.mod", null),
            new("mod/other.mod", "Local Mod", "mod/other.mod", null),
        ], []);

        var warnings = new List<string>();
        var sources = TechSources.Build(fx.GameDir, fx.UserDir, list, warnings);
        try
        {
            Assert.Equal(new[] { "Base game", "Local Mod", "Local Mod (2)" }, sources.Select(s => s.Name));
            Assert.Equal(3, warnings.Count);
            Assert.Contains(warnings, w => w.Contains("not found"));
        }
        finally { foreach (var s in sources) s.Dispose(); }
    }

    [Fact]
    public void Mod_named_like_base_game_gets_a_suffix()
    {
        using var fx = new FakeInstall();
        fx.Write("user/mod/b.mod", "name=\"base game\"\npath=\"mod/local\"\n");
        var list = new ModList("t", [new("mod/b.mod", "x", "mod/b.mod", null)], []);
        var sources = TechSources.Build(fx.GameDir, fx.UserDir, list, new List<string>());
        try { Assert.Equal(new[] { "Base game", "base game (2)" }, sources.Select(s => s.Name)); }
        finally { foreach (var s in sources) s.Dispose(); }
    }
}

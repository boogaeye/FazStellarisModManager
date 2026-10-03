using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class TechDatabaseTests
{
    [Fact]
    public void Applies_file_and_object_overrides_and_records_sources()
    {
        using var fx = new TechFixture();

        var db = TechDatabase.Build(fx.Sources);

        Assert.Equal(new[] { "tech_a", "tech_b", "tech_c", "tech_d", "tech_e", "tech_f" }, db.Techs.Keys.Order(StringComparer.Ordinal));

        var b = db.Techs["tech_b"];
        Assert.Equal(("Mod A", "common/technology/zz_moda.txt", 2, "999"), (b.Source.SourceName, b.Source.File, b.Tier, b.Cost));
        Assert.Equal(new[] { "Base game" }, b.Overridden.Select(o => o.SourceName));
        Assert.Empty(b.Dlcs);
        Assert.True(b.ChangedByMods);

        var c = db.Techs["tech_c"];
        Assert.Equal(("Base game", 2), (c.Source.SourceName, c.Tier));
        Assert.Equal(new[] { "Mod B" }, c.Overridden.Select(o => o.SourceName));
        Assert.False(c.ChangedByMods);

        var d = db.Techs["tech_d"];
        Assert.Equal(("Mod B", "common/technology/00_eng.txt", "1", "tech_d"), (d.Source.SourceName, d.Source.File, d.Cost, d.IconKey));
        Assert.Equal(new[] { "Base game" }, d.Overridden.Select(o => o.SourceName));
        Assert.Empty(db.Techs["tech_f"].Overridden);

        Assert.False(db.Techs.ContainsKey("particles"));
    }

    [Fact]
    public void Resolves_variables_names_and_flags()
    {
        using var fx = new TechFixture();

        var db = TechDatabase.Build(fx.Sources);

        var a = db.Techs["tech_a"];
        Assert.Equal(("Alpha Tech", "Starts Beta Prime research.", "500", 0, TechArea.Physics), (a.Name, a.Description, a.Cost, a.Tier, a.Area));
        Assert.True(a.IsStart);
        Assert.Equal("Beta Prime", db.Techs["tech_b"].Name);
        Assert.Equal("Epsilon", db.Techs["tech_e"].Name);
        Assert.Equal("tech_f", db.Techs["tech_f"].Name);
        Assert.True(db.Techs["tech_c"].IsRare);
        Assert.Equal(TechArea.Society, db.Techs["tech_c"].Area);
    }

    [Fact]
    public void Indexes_dependents_and_finds_shortest_path_from_start()
    {
        using var fx = new TechFixture();

        var db = TechDatabase.Build(fx.Sources);

        Assert.Equal(new[] { "tech_b", "tech_d" }, db.Dependents("tech_a"));
        Assert.Equal(new[] { "tech_c", "tech_e" }, db.Dependents("tech_b"));
        Assert.Empty(db.Dependents("tech_e"));
        Assert.Equal(new[] { "tech_a", "tech_b", "tech_e" }, db.PathFromStart("tech_e").Select(t => t.Key));
        Assert.Equal(new[] { "tech_a" }, db.PathFromStart("tech_a").Select(t => t.Key));
        Assert.Equal(new[] { "tech_f" }, db.PathFromStart("tech_f").Select(t => t.Key));
        Assert.Empty(db.PathFromStart("nope"));
    }

    [Fact]
    public void Queries_filter_and_sort()
    {
        using var fx = new TechFixture();
        var db = TechDatabase.Build(fx.Sources);
        string[] Keys(TechFilter f) => db.Query(f).Select(t => t.Key).ToArray();

        Assert.Equal(new[] { "tech_a", "tech_b", "tech_e" }, Keys(new TechFilter(Area: TechArea.Physics)));
        Assert.Equal(new[] { "tech_b" }, Keys(new TechFilter(Search: "prime")));
        Assert.Equal(new[] { "tech_e" }, Keys(new TechFilter(Search: "TECH_E")));
        Assert.Equal(new[] { "tech_b", "tech_e", "tech_d", "tech_f" }, Keys(new TechFilter(ChangedByModsOnly: true)));
        Assert.Equal(new[] { "tech_d", "tech_f" }, Keys(new TechFilter(Source: "mod b")));
        Assert.Equal(new[] { "tech_b", "tech_c" }, Keys(new TechFilter(Tier: 2)));
        Assert.Equal(new[] { "tech_c" }, Keys(new TechFilter(RareOnly: true)));
        Assert.Equal(new[] { "tech_a" }, Keys(new TechFilter(StartOnly: true)));
        Assert.Equal(new[] { "tech_c" }, Keys(new TechFilter(Category: "BIOLOGY")));
        Assert.Equal(6, db.Count(null));
        Assert.Equal(3, db.Count(TechArea.Physics));
        Assert.Equal(new[] { "Base game", "Mod A", "Mod B" }, db.SourceNames);
        Assert.Equal(new[] { "biology", "particles", "voidcraft" }, db.Categories);
        Assert.Equal(new[] { 0, 1, 2, 3 }, db.Tiers);
    }

    [Fact]
    public void Unreadable_files_become_warnings()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("base/common/technology/00_t.txt", "tech_a = { area = physics }");
        tmp.Write("base/common/technology/01_t.txt", "tech_b = { area = physics }");
        using var source = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "base"), isBaseGame: true);
        using var hold = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var db = TechDatabase.Build([source], ["earlier warning"]);

        Assert.Equal(new[] { "tech_b" }, db.Techs.Keys);
        Assert.Equal(2, db.Warnings.Count);
        Assert.Equal("earlier warning", db.Warnings[0]);
        Assert.Contains("00_t.txt", db.Warnings[1]);
    }
}

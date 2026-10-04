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

    [Fact]
    public void Duplicate_warnings_are_kept_once_in_order()
    {
        using var tmp = new TempDir();
        tmp.Write("base/common/technology/00_t.txt", "tech_a = { area = physics }");
        // Both the unlock scan and the grant scan read common/buildings, so both would warn about it.
        var file = tmp.Write("base/common/buildings/00_b.txt", "building_x = { prerequisites = { tech_a } }");
        using var source = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "base"), isBaseGame: true);
        using var hold = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var db = TechDatabase.Build([source], ["dup", "earlier", "dup"]);

        Assert.Equal(["dup", "earlier"], db.Warnings.Take(2));
        Assert.Single(db.Warnings, w => w.Contains("00_b.txt"));
        Assert.Equal(3, db.Warnings.Count);
    }

    static (TempDir Tmp, List<ContentSource> Sources) Dirs(params (string Name, bool Base, (string Rel, string Text)[] Files)[] defs)
    {
        var tmp = new TempDir();
        var list = new List<ContentSource>();
        for (var i = 0; i < defs.Length; i++)
        {
            foreach (var (rel, text) in defs[i].Files) tmp.Write($"s{i}/common/technology/{rel}", text);
            tmp.Mkdir($"s{i}/common/technology");
            list.Add(ContentSource.FromPath(defs[i].Name, Path.Combine(tmp.Path, $"s{i}"), defs[i].Base));
        }
        return (tmp, list);
    }

    [Fact]
    public void Load_order_is_ascii_on_lowercased_paths()
    {
        const string def = "tech_a = { area = physics tier = 1 cost = {0} } ";
        var (tmp, sources) = Dirs(
            ("Base game", true, [("00_a.txt", def.Replace("{0}", "1"))]),
            ("Mod", false, [("Zz_late.txt", def.Replace("{0}", "2"))]));
        using (tmp) {
            var db = TechDatabase.Build(sources);
            Assert.Equal(("Mod", "2"), (db.Techs["tech_a"].Source.SourceName, db.Techs["tech_a"].Cost));
            foreach (var s in sources) s.Dispose();
        }

        var (tmp2, sources2) = Dirs(
            ("Base game", true, [("a_x.txt", "tech_q = { area = physics tier = 1 cost = 1 } ")]),
            ("Mod", false, [("Aa.txt", "tech_q = { area = physics tier = 1 cost = 2 } ")]));
        using (tmp2) {
            var db = TechDatabase.Build(sources2);
            Assert.Equal("Mod", db.Techs["tech_q"].Source.SourceName);
            foreach (var s in sources2) s.Dispose();
        }
    }

    [Fact]
    public void Redefinition_in_same_file_is_not_an_override()
    {
        var (tmp, sources) = Dirs(("Base game", true, [("00_z.txt",
            "tech_z = { area = physics tier = 1 cost = 1 } tech_z = { area = physics tier = 1 cost = 2 } ")]));
        using (tmp) {
            var db = TechDatabase.Build(sources);
            Assert.Equal("2", db.Techs["tech_z"].Cost);
            Assert.Empty(db.Techs["tech_z"].Overridden);
            foreach (var s in sources) s.Dispose();
        }
    }

    [Fact]
    public void PathFromStart_prefers_real_start_techs()
    {
        var (tmp, sources) = Dirs(("Base game", true, [("00_p.txt", """
            t1 = { area = physics tier = 0 cost = 1 start_tech = yes }
            t2 = { area = physics tier = 1 cost = 1 prerequisites = { "t1" } }
            orphan = { area = physics tier = 1 cost = 1 prerequisites = { "missing_mod_tech" } }
            t3 = { area = physics tier = 2 cost = 1 prerequisites = { "t2" "orphan" } }
            """)]));
        using (tmp) {
            var db = TechDatabase.Build(sources);
            Assert.Equal(new[] { "t1", "t2", "t3" }, db.PathFromStart("t3").Select(t => t.Key));
            foreach (var s in sources) s.Dispose();
        }
    }

    [Fact]
    public void Grant_sources_and_events_are_part_of_the_database()
    {
        using var tmp = new TempDir();
        tmp.Write("g/common/technology/00_t.txt", "tech_a = { area = physics tier = 1 }");
        tmp.Write("g/events/e.txt", "country_event = { id = e.1 title = e.1.name option = { name = OK give_technology = { tech = TECH_A } } }");
        using var source = ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true);

        var db = TechDatabase.Build([source]);

        var grant = Assert.Single(db.GrantSources("tech_a"));
        Assert.Equal(("e.1", true), (grant.Id, grant.IsEvent));
        Assert.Equal("e.1.name", db.Event("e.1")!.Title);
        Assert.Empty(db.GrantSources("tech_missing"));
        Assert.Null(db.Event("nope"));
    }
}

using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class GrantScannerTests
{
    const string BaseEvents = """
        namespace = test
        country_event = {
            id = test.1
            title = test.1.name
            desc = test.1.desc
            picture = GFX_evt_one
            immediate = { add_research_option = tech_a }
            option = { name = test.1.a give_technology = { tech = tech_a } }
            option = { name = test.1.b trigger = { is_gestalt = yes } }
        }
        fleet_event = {
            id = test.2
            title = { trigger = { always = yes } text = test.2.name }
            desc = { trigger = { always = yes } text = test.2.desc }
            desc = test.2.desc
            picture = { trigger = { always = yes } picture = GFX_evt_two }
            hide_window = yes
            option = { name = OK }
            after = { if = { limit = { is_gestalt = no } add_tech_progress = { tech = tech_a progress = 0.25 } } }
        }
        country_event = {
            id = test.3
            title = test.3.name
            inline_script = { script = events/tech_option TECH = tech_b }
            option = { name = test.3.a give_technology = { tech = tech_unknown } }
        }
        country_event = { id = test.4 title = test.4.name option = { name = test.4.a } }
        country_event = { id = test.5 title = test.5.name option = { name = test.5.a } }
        """;

    sealed record Setup(TempDir Tmp, List<ContentSource> Sources, Localisation Loc) : IDisposable
    {
        public void Dispose()
        {
            foreach (var s in Sources) s.Dispose();
            Tmp.Dispose();
        }
    }

    static Setup Create()
    {
        var tmp = new TempDir();
        tmp.Write("g/events/test_events.txt", BaseEvents);
        tmp.Write("g/common/inline_scripts/events/tech_option.txt", "option = { name = pick_$TECH$ give_technology = { tech = $TECH$ } }");
        tmp.Write("g/common/traditions/00_traditions.txt", "tr_x = { on_enabled = { give_technology = { tech = tech_b } } }");
        tmp.Write("g/localisation/english/t_l_english.yml",
            "l_english:" + (char)10 + " test.1.name: \"First Contact\"" + (char)10 + " test.1.desc: \"Hello [Root.GetName]\"" + (char)10
            + " test.1.a: \"Accept\"" + (char)10 + " test.1.b: \"Refuse\"" + (char)10 + " test.2.name: \"Second\"" + (char)10 + " tr_x: \"Tradition X\"" + (char)10);
        // Events: the FIRST definition of an id wins. "!override" sorts before "test_events", "zz_late" after.
        tmp.Write("m/events/!override.txt", "country_event = { id = test.4 title = test.4.name option = { name = mod.a add_research_option = tech_b } }");
        tmp.Write("m/events/zz_late.txt", "country_event = { id = test.1 option = { give_technology = { tech = tech_b } } }");
        tmp.Write("m/common/ascension_perks/mod_perks.txt", "ap_mod = { on_enabled = { add_tech_progress = { tech = tech_b progress = 1 } } }");
        var sources = new List<ContentSource>
        {
            ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true),
            ContentSource.FromPath("Mod", Path.Combine(tmp.Path, "m")),
        };
        return new Setup(tmp, sources, Localisation.Load(sources, new List<string>()));
    }

    static GrantIndex Scan(Setup s)
    {
        var warnings = new List<string>();
        var techs = new HashSet<string>(["tech_a", "tech_b"], StringComparer.OrdinalIgnoreCase);
        var index = GrantScanner.Scan(s.Sources, s.Loc, ScriptLibrary.Load(s.Sources, warnings), k => techs.TryGetValue(k, out var t) ? t : null, warnings);
        Assert.Empty(warnings);
        return index;
    }

    [Fact]
    public void Events_record_where_each_grant_happens()
    {
        using var s = Create();
        var index = Scan(s);

        var sources = index.For("TECH_A");
        Assert.Equal(["test.1", "test.2"], sources.Select(x => x.Id));
        Assert.All(sources, x => Assert.True(x.IsEvent));
        Assert.Equal(
            [(GrantKind.ResearchOption, EventPart.Immediate, (int?)null), (GrantKind.Gives, EventPart.Option, 0)],
            sources[0].Grants.Select(g => (g.Kind, g.Part, g.OptionIndex)));
        var after = Assert.Single(sources[1].Grants);
        Assert.Equal((GrantKind.Progress, 0.25, EventPart.After, "not Gestalt"), (after.Kind, after.Progress!.Value, after.Part, after.Condition));
    }

    [Fact]
    public void Event_windows_get_title_text_picture_and_options()
    {
        using var s = Create();
        var index = Scan(s);

        var first = index.Event("test.1")!;
        Assert.Equal(("country_event", "First Contact", "Hello [Root.GetName]", false, "GFX_evt_one", false, false),
            (first.Type, first.Title, first.Description, first.DescriptionVaries, first.Picture, first.PictureVaries, first.Hidden));
        Assert.Equal([new EventOption("Accept", null), new EventOption("Refuse", "Gestalt")], first.Options);
        Assert.Equal(("Base game", "events/test_events.txt"), (first.Source.SourceName, first.Source.File));

        var second = index.Event("test.2")!;
        Assert.Equal(("Second", "test.2.desc", true, "GFX_evt_two", true, true),
            (second.Title, second.Description, second.DescriptionVaries, second.Picture, second.PictureVaries, second.Hidden));
        Assert.Equal(["OK"], second.Options.Select(o => o.Name));
        Assert.Null(index.Event("test.5"));
    }

    [Fact]
    public void Injected_options_mods_first_definition_and_other_sources()
    {
        using var s = Create();
        var index = Scan(s);

        var sources = index.For("tech_b");
        Assert.Equal(["test.3", "test.4", "ap_mod", "tr_x"], sources.Select(x => x.Id));
        Assert.Equal(["pick_tech_b", "test.3.a"], index.Event("test.3")!.Options.Select(o => o.Name));
        Assert.Equal((EventPart.Option, 0), (sources[0].Grants[0].Part, sources[0].Grants[0].OptionIndex!.Value));
        Assert.Equal("Mod", index.Event("test.4")!.Source.SourceName);
        Assert.DoesNotContain(index.For("tech_b"), x => x.Id == "test.1");
        Assert.Equal(("Ascension perks", "ascension_perks", "ap_mod", 1.0), (sources[2].Kind, sources[2].KindFolder, sources[2].Name, sources[2].Grants[0].Progress!.Value));
        Assert.Equal(("Traditions", "Tradition X", GrantKind.Gives), (sources[3].Kind, sources[3].Name, sources[3].Grants[0].Kind));
        Assert.Empty(index.For("tech_unknown"));
    }
}

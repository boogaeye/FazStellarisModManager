using FazStellarisModmanager.Core.Events;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class EventGraphScannerTests
{
    const string Events = """
        namespace = ev
        country_event = {
            id = ev.1
            title = ev.1.name
            desc = ev.1.desc
            picture = GFX_evt_x
            is_triggered_only = yes
            trigger = { has_country_flag = flag_a }
            immediate = { country_event = { id = ev.2 days = 10 } }
            option = {
                name = ev.1.a
                trigger = { is_gestalt = yes }
                country_event = { id = ev.3 days = 90 random = 30 }
            }
            option = {
                name = ev.1.b
                allow = { has_country_flag = flag_b }
                if = { limit = { has_technology = tech_x } ship_event = { id = ev.4 } }
                else = { fire_it = yes }
            }
            after = { hidden_effect = { country_event = { id = ev.5 scopes = { from = root } } } }
        }
        country_event = { id = ev.2 is_triggered_only = yes option = { name = ok country_event = { id = ev.1 } } }
        country_event = {
            id = ev.3
            is_triggered_only = yes
            immediate = {
                random_country = {
                    limit = { is_country_type = global_event }
                    random_list = {
                        10 = { modifier = { factor = 0 has_global_flag = g } country_event = { id = ev.6 } }
                        30 = { }
                    }
                }
            }
            option = { name = x random = { chance = 25 country_event = { id = ev.7 } } }
        }
        country_event = { id = ev.4 option = { name = y } }
        country_event = { id = ev.8 base = ev.1 option_clear = yes desc_clear = yes option = { name = own } }
        country_event = { id = ev.9 is_triggered_only = yes inline_script = { script = events/opt TARGET = ev.10 } }
        country_event = { id = ev.11 is_triggered_only = yes option = { name = ev.11.a } }
        country_event = { id = ev.22 mean_time_to_happen = { months = 12 } }
        """;

    sealed record Setup(TempDir Tmp, List<ContentSource> Sources, Localisation Loc, ScriptLibrary Library) : IDisposable
    {
        public void Dispose()
        {
            foreach (var s in Sources) s.Dispose();
            Tmp.Dispose();
        }
    }

    static Setup Create()
    {
        const char N = (char)10;
        var tmp = new TempDir();
        tmp.Write("g/events/ev_events.txt", Events);
        tmp.Write("g/common/scripted_effects/00_effects.txt", "fire_it = { country_event = { id = ev.12 } }");
        tmp.Write("g/common/inline_scripts/events/opt.txt", "option = { name = inl country_event = { id = $TARGET$ } }");
        tmp.Write("g/common/on_actions/00_on_actions.txt", "on_yearly_pulse = { events = { ev.13 } random_events = { 100 = 0 50 = ev.14 } }");
        tmp.Write("m/common/on_actions/mod_on_actions.txt", "on_yearly_pulse = { events = { ev.15 } random_events = { 50 = ev.16 } }");
        tmp.Write("g/common/decisions/00_decisions.txt",
            "decision_x = { potential = { country_event = { id = ev.never } } effect = { country_event = { id = ev.17 } } }");
        tmp.Write("g/common/anomalies/00_anomalies.txt",
            "anom_x = { on_success = { 1 = ev.18  3 = { modifier = { factor = 2 has_country_flag = f } ship_event = { id = ev.19 } } } }");
        // Only a scripted effect that fires an event: still read (the pre-filter follows scripted effects).
        tmp.Write("g/common/edicts/00_edicts.txt", "edict_x = { on_activation = { fire_it = yes } }");
        // No event words: its objects are definitions only, and they replace the mod's earlier definition (load order by file name).
        tmp.Write("g/common/policies/zz_policies.txt", "policy_x = { on_enabled = { add_x = 1 } }");
        tmp.Write("m/common/policies/00_mod.txt", "policy_x = { on_enabled = { country_event = { id = ev.20 } } } policy_y = { on_enabled = { country_event = { id = ev.21 } } }");
        tmp.Write("m/events/mod_events.txt", "namespace = modev" + N + "country_event = { id = modev.1 title = modev.1.name is_triggered_only = yes }");
        tmp.Write("g/localisation/english/ev_l_english.yml",
            "l_english:" + N + " ev.1.name: \"First Contact\"" + N + " ev.1.desc: \"Hello there\"" + N + " ev.1.a: \"Go\"" + N
            + " ev.1.b: \"Wait\"" + N + " decision_x: \"Decision X\"" + N + " modev.1.name: \"Mod Story\"" + N);
        var sources = new List<ContentSource>
        {
            ContentSource.FromPath("Base game", Path.Combine(tmp.Path, "g"), isBaseGame: true),
            ContentSource.FromPath("Mod", Path.Combine(tmp.Path, "m")),
        };
        var warnings = new List<string>();
        var setup = new Setup(tmp, sources, Localisation.Load(sources, warnings), ScriptLibrary.Load(sources, warnings));
        Assert.Empty(warnings);
        return setup;
    }

    static EventGraph Build(Setup s)
    {
        var graph = EventGraphScanner.Build(s.Sources, s.Loc, s.Library);
        Assert.Empty(graph.Warnings);
        return graph;
    }

    [Fact]
    public void Events_get_texts_conditions_and_options()
    {
        using var s = Create();
        var ev = Build(s).Event("EV.1")!;

        Assert.Equal(("country_event", "First Contact", "Hello there", "GFX_evt_x", true, false, false),
            (ev.Type, ev.Title, ev.Description, ev.Picture, ev.TriggeredOnly, ev.Hidden, ev.HasMtth));
        Assert.Equal("has_country_flag = flag_a", ev.TriggerScript);
        Assert.Equal("ev", ev.Namespace);
        Assert.Equal(["Go", "Wait"], ev.Options.Select(o => o.Name));
        Assert.Equal(("Gestalt", "is_gestalt = yes", (string?)null), (ev.Options[0].TriggerText, ev.Options[0].TriggerScript, ev.Options[0].AllowScript));
        Assert.Equal("has_country_flag = flag_b", ev.Options[1].AllowScript);
        Assert.Equal("(no title) y", Build(s).Event("ev.4")!.ListTitle);
    }

    [Fact]
    public void Calls_from_immediate_options_and_after_carry_part_and_delay()
    {
        using var s = Create();
        var graph = Build(s);

        var immediate = Assert.Single(graph.CallersOf("ev.2"), c => c.CallerId == "ev.1");
        Assert.Equal((CallerKind.Event, CallPart.Immediate, "country_event", "after 10 days", "First Contact"),
            (immediate.CallerKind, immediate.Part, immediate.Effect, immediate.Delay, immediate.CallerName));

        var option = Assert.Single(graph.CallersOf("ev.3"));
        Assert.Equal((CallPart.Option, (int?)0, "Go", 90, 120, "after 90–120 days", "option “Go”"),
            (option.Part, option.OptionIndex, option.OptionName, option.DaysMin!.Value, option.DaysMax!.Value, option.Delay, option.Where));
        Assert.Equal(["ev.3"], graph.Event("ev.1")!.Options[0].Calls.Select(c => c.TargetId));

        var after = Assert.Single(graph.CallersOf("ev.5"), c => c.CallerId == "ev.1");
        Assert.Equal((CallPart.After, "immediately", "from = root"), (after.Part, after.Delay, after.Scopes));
        Assert.Equal(["ev.5"], graph.Event("ev.1")!.AfterCalls.Select(c => c.TargetId));
    }

    [Fact]
    public void If_else_and_scripted_effects_give_conditions_and_via()
    {
        using var s = Create();
        var graph = Build(s);

        var ship = Assert.Single(graph.CallersOf("ev.4"));
        Assert.Equal(("ship_event", "Technology: tech_x", "has_technology = tech_x", (string?)null),
            (ship.Effect, ship.Condition, ship.ConditionScript, ship.Via));

        var viaEvent = Assert.Single(graph.CallersOf("ev.12"), c => c.CallerKind == CallerKind.Event);
        Assert.Equal(("ev.1", "fire_it", "otherwise"), (viaEvent.CallerId, viaEvent.Via, viaEvent.Condition));
        // The else runs when the if's limit failed.
        Assert.Equal(string.Join((char)10, "NOR = {", "    AND = {", "        has_technology = tech_x", "    }", "}"), viaEvent.ConditionScript);
    }

    [Fact]
    public void Random_lists_record_weights_and_modifiers_in_scope_and_random_blocks_their_chance()
    {
        using var s = Create();
        var graph = Build(s);

        var call = Assert.Single(graph.CallersOf("ev.6"));
        Assert.Equal(["random_country: Country type: global_event", "by chance: weight 10 of 40"], call.Conditions.Select(c => c.Text));
        Assert.Equal(string.Join((char)10, "random_country = {", "    is_country_type = global_event", "}"), call.ConditionScript);
        var pick = call.Weight!;
        Assert.Equal((0, 0.25), (pick.Index, pick.BaseChance!.Value));
        Assert.Equal([10.0, 30.0], pick.Branches.Select(b => b.Weight!.Value));
        var modifier = Assert.Single(pick.Branch.Modifiers);
        Assert.Equal((0.0, string.Join((char)10, "random_country = {", "    has_global_flag = g", "}")), (modifier.Factor!.Value, modifier.Script));
        Assert.Empty(pick.Branches[1].Modifiers);

        var chance = Assert.Single(graph.CallersOf("ev.7"));
        Assert.Equal((25.0, "25 % chance"), (chance.Chance!.Value, chance.Condition));
    }

    [Fact]
    public void Inheritance_and_inline_scripts_are_resolved()
    {
        using var s = Create();
        var graph = Build(s);

        Assert.Equal(["ev.1", "ev.8"], graph.CallersOf("ev.2").Select(c => c.CallerId));
        Assert.Equal(["own"], graph.Event("ev.8")!.Options.Select(o => o.Name));
        Assert.Equal("ev.1", graph.Event("ev.8")!.BaseId);

        var inline = Assert.Single(graph.CallersOf("ev.10"));
        Assert.Equal(("ev.9", "inl"), (inline.CallerId, inline.OptionName));
    }

    [Fact]
    public void A_cycle_is_just_two_calls()
    {
        using var s = Create();
        var graph = Build(s);

        Assert.Equal(["ev.2"], graph.CallersOf("ev.1").Select(c => c.CallerId));
        Assert.Contains(graph.CallersOf("ev.2"), c => c.CallerId == "ev.1");
    }

    [Fact]
    public void On_actions_merge_events_and_random_events_across_files()
    {
        using var s = Create();
        var graph = Build(s);

        foreach (var id in new[] { "ev.13", "ev.15" })
        {
            var c = Assert.Single(graph.CallersOf(id));
            Assert.Equal((CallerKind.OnAction, "on_yearly_pulse", CallPart.OnActionEvents, true), (c.CallerKind, c.CallerId, c.Part, c.IsRoot));
        }
        var random = Assert.Single(graph.CallersOf("ev.14"));
        Assert.Equal(CallPart.OnActionRandom, random.Part);
        Assert.Equal([(100.0, (string?)null), (50.0, "ev.14"), (50.0, "ev.16")], random.Weight!.Branches.Select(b => (b.Weight!.Value, b.EventId)));
        Assert.Equal((1, 0.25), (random.Weight.Index, random.Weight.BaseChance!.Value));
        Assert.Equal(2, Assert.Single(graph.CallersOf("ev.16")).Weight!.Index);
    }

    [Fact]
    public void Common_objects_are_roots_with_kind_name_and_block()
    {
        using var s = Create();
        var graph = Build(s);

        var decision = Assert.Single(graph.CallersOf("ev.17"));
        Assert.Equal((CallerKind.Object, "decision_x", "Decision X", "Decisions", "decisions", "effect", CallPart.Effect),
            (decision.CallerKind, decision.CallerId, decision.CallerName, decision.CallerKindName, decision.CallerFolder, decision.Block, decision.Part));
        Assert.Empty(graph.CallersOf("ev.never"));

        var edict = Assert.Single(graph.CallersOf("ev.12"), c => c.CallerKind == CallerKind.Object);
        Assert.Equal(("edict_x", "fire_it", "on_activation"), (edict.CallerId, edict.Via, edict.Block));

        Assert.Empty(graph.CallersOf("ev.20"));
        Assert.Equal("policy_y", Assert.Single(graph.CallersOf("ev.21")).CallerId);
    }

    [Fact]
    public void Anomaly_outcomes_are_a_weighted_pick()
    {
        using var s = Create();
        var graph = Build(s);

        var plain = Assert.Single(graph.CallersOf("ev.18"));
        Assert.Equal(("on_success", 0, 0.25), (plain.Block, plain.Weight!.Index, plain.Weight.BaseChance!.Value));
        var modified = Assert.Single(graph.CallersOf("ev.19"));
        Assert.Equal((1, 2.0), (modified.Weight!.Index, modified.Weight.Branch.Modifiers.Single().Factor!.Value));
    }

    [Fact]
    public void Uncalled_events_fire_on_their_own_unless_triggered_only()
    {
        using var s = Create();
        var graph = Build(s);

        Assert.Equal(EventOrigin.Called, graph.Origin("ev.2"));
        Assert.Equal(EventOrigin.FiresOnItsOwn, graph.Origin("ev.22"));
        Assert.True(graph.Event("ev.22")!.HasMtth);
        Assert.Equal("months = 12", graph.Event("ev.22")!.MtthScript);
        Assert.Equal(EventOrigin.NoKnownCaller, graph.Origin("ev.11"));
    }

    [Fact]
    public void Search_matches_id_title_and_description_with_filters()
    {
        using var s = Create();
        var graph = Build(s);

        Assert.Equal(["ev.1", "ev.8"], graph.Search("contact").Select(e => e.Id));
        Assert.Equal(["ev.1"], graph.Search("hello").Select(e => e.Id));
        // Exact id, then id prefix, then id contains.
        Assert.Equal(["ev.1", "ev.11", "modev.1"], graph.Search("ev.1").Select(e => e.Id));
        Assert.Equal(["ev.1", "ev.2", "ev.3", "ev.4", "ev.8", "ev.9", "ev.11", "ev.22"], graph.Search("", ns: "ev").Select(e => e.Id));
        Assert.Equal(["modev.1"], graph.Search(null, source: "Mod").Select(e => e.Id));
        Assert.Equal(["ev", "modev"], graph.Namespaces);
        Assert.Equal(["Base game", "Mod"], graph.SourceNames);
    }
}

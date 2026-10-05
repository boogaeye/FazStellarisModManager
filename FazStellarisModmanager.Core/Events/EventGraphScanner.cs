using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Events;

/// <summary>
/// Builds the <see cref="EventGraph"/>: every event (events/, first definition per id wins, <c>base = id</c> and inline scripts
/// resolved like the tech grant scanner), the calls in their immediate / option / after blocks, on_actions (<c>events</c> and
/// <c>random_events</c>, merged across files per key), and the effect blocks of common/ objects (last (folder, id) wins).
/// Base-game common/ files are only parsed when they mention an event effect, inline_script or a scripted effect that
/// (transitively) does.
/// </summary>
public static class EventGraphScanner
{
    const string OnActionsFolder = "on_actions";

    static readonly HashSet<string> ExcludedCommon = new(StringComparer.OrdinalIgnoreCase)
    {
        "scripted_effects", "inline_scripts", "scripted_triggers", "scripted_variables", "script_values", "scripted_loc",
        "defines", "random_names", "name_lists", OnActionsFolder,
    };

    public static EventGraph Build(IReadOnlyList<ContentSource> sources, Localisation loc, ScriptLibrary library,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        string Describe(PdxBlock? b) => TriggerSummary.Describe(b, loc.Get, n => loc.ScriptedTriggers.TryGetValue(n, out var t) ? t : null);
        var finder = new EventCallFinder(library, Describe);

        progress?.Report("Reading event files…");
        var eventFiles = ScriptFiles.ParseAll(sources, "events", _ => true, null, warnings, ct);
        progress?.Report("Reading on_actions…");
        var onActionFiles = ScriptFiles.ParseAll(sources, "common", rel => Folder(rel) is { } f && f.Equals(OnActionsFolder, StringComparison.OrdinalIgnoreCase),
            null, warnings, ct);
        progress?.Report("Reading common/ objects that fire events…");
        var prefilter = ScriptFiles.Prefilter(library, EventScripts.IsEventKey, ["_event", "event =", "event="]);
        var commonFiles = ScriptFiles.ParseAll(sources, "common", rel => Folder(rel) is { } f && !ExcludedCommon.Contains(f), prefilter, warnings, ct);

        progress?.Report("Resolving events…");
        var resolved = EventScripts.Resolve(eventFiles, library, ct);
        progress?.Report($"Finding the calls of {resolved.Count} events…");
        var infos = new EventInfo[resolved.Count];
        Parallel.For(0, resolved.Count, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, i =>
            infos[i] = Analyse(resolved[i], loc, finder, Describe));
        var events = new Dictionary<string, EventInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in infos) events[info.Id] = info;

        var calls = new List<EventCall>();
        foreach (var info in infos) calls.AddRange(info.AllCalls);

        progress?.Report("Reading on_actions…");
        calls.AddRange(OnActions(onActionFiles));

        progress?.Report("Finding objects that fire events…");
        var objects = new Dictionary<string, List<EventCall>?>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in commonFiles)
        {
            ct.ThrowIfCancellationRequested();
            var folder = Folder(file.Src.File)!;
            if (file.DefinedOnly is { } defined)
            {
                foreach (var id in defined) objects[folder + "|" + id] = null;
                continue;
            }
            foreach (var e in file.Root.Entries)
            {
                if (e.Value is not PdxBlock block || e.Key.StartsWith('@')) continue;
                var id = block.GetString("key") ?? e.Key;
                var found = ObjectCalls(block, folder, id, file.Src, loc, finder);
                objects[folder + "|" + id] = found.Count == 0 ? null : found;
            }
        }
        foreach (var o in objects.Values)
            if (o is not null) calls.AddRange(o);

        return new EventGraph(events, calls, loc.ScriptedTriggers, warnings.Distinct().ToList());
    }

    static string? Folder(string rel) => rel.Split('/') is { Length: >= 3 } p ? p[1] : null;

    static EventInfo Analyse(ResolvedEvent ev, Localisation loc, EventCallFinder finder, Func<PdxBlock?, string> describe)
    {
        var block = ev.Block;
        var texts = EventScripts.Texts(block, loc);
        var caller = new EventCall
        {
            TargetId = "",
            Effect = "",
            CallerKind = CallerKind.Event,
            CallerId = ev.Id,
            CallerName = texts.Title ?? ev.Id,
            CallerKindName = "Event",
            Part = CallPart.Immediate,
            Source = ev.Src,
        };
        var options = EventScripts.Options(block).Select((o, i) =>
        {
            var name = EventScripts.OptionName(o, i, loc);
            var trigger = o.GetBlock("exclusive_trigger") ?? o.GetBlock("trigger");
            var allow = o.GetBlock("allow");
            return new EventOptionInfo(i, name, trigger, Text(trigger, describe), allow, Text(allow, describe),
                finder.Find(o, caller with { Part = CallPart.Option, OptionIndex = i, OptionName = name }));
        }).ToList();
        var trigger = block.GetBlock("trigger");
        var mtth = block.GetBlock("mean_time_to_happen");
        return new EventInfo
        {
            Id = ev.Id,
            Type = ev.Type,
            Title = texts.Title,
            Description = texts.Description,
            DescriptionVaries = texts.DescriptionVaries,
            Picture = texts.Picture,
            PictureVaries = texts.PictureVaries,
            Hidden = EventScripts.Yes(block, "hide_window"),
            TriggeredOnly = EventScripts.Yes(block, "is_triggered_only"),
            HasMtth = mtth is not null,
            Trigger = trigger,
            TriggerText = Text(trigger, describe),
            MeanTimeToHappen = mtth,
            Options = options,
            ImmediateCalls = block.GetBlock("immediate") is { } immediate ? finder.Find(immediate, caller) : [],
            AfterCalls = block.GetBlock("after") is { } after ? finder.Find(after, caller with { Part = CallPart.After }) : [],
            BaseId = block.GetString("base"),
            Source = ev.Src,
        };
    }

    // "always" (an empty or always-true trigger) reads as no condition.
    static string? Text(PdxBlock? trigger, Func<PdxBlock?, string> describe) =>
        trigger is not null && describe(trigger) is var text && text != "always" ? text : null;

    /// <summary>
    /// The calls of on_actions: <c>events = { id … }</c> and <c>random_events = { weight = id … }</c>, merged per key across files
    /// (in load order). Each random event's pick lists every random_events entry of its key; weight 0 entries ("= 0") fire nothing.
    /// </summary>
    static IEnumerable<EventCall> OnActions(List<ParsedFile> files)
    {
        var plain = new List<(string Key, string Id, TechSourceRef Src)>();
        var random = new Dictionary<string, List<(PdxEntry Entry, TechSourceRef Src)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
            foreach (var e in file.Root.Entries)
            {
                if (e.Value is not PdxBlock block) continue;
                foreach (var list in block.Entries.Where(x => x.Value is PdxBlock && x.Key.Equals("events", StringComparison.OrdinalIgnoreCase)))
                    foreach (var id in ((PdxBlock)list.Value).StringItems)
                        plain.Add((e.Key, id, file.Src));
                foreach (var list in block.Entries.Where(x => x.Value is PdxBlock && x.Key.Equals("random_events", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!random.TryGetValue(e.Key, out var entries)) random[e.Key] = entries = [];
                    entries.AddRange(((PdxBlock)list.Value).Entries.Where(x => x.Value is string).Select(x => (x, file.Src)));
                }
            }

        EventCall Call(string key, string id, CallPart part, TechSourceRef src) => new()
        {
            TargetId = id,
            Effect = "on_action",
            CallerKind = CallerKind.OnAction,
            CallerId = key,
            CallerName = key,
            CallerKindName = "On action",
            Part = part,
            DaysMin = 0,
            DaysMax = 0,
            Source = src,
        };

        foreach (var (key, id, src) in plain) yield return Call(key, id, CallPart.OnActionEvents, src);
        foreach (var (key, entries) in random)
        {
            var branches = entries.Select(x => new WeightBranch(x.Entry.Key, EventCallFinder.Number(x.Entry.Key), [],
                (string)x.Entry.Value is var id && id != "0" ? id : null)).ToList();
            for (var i = 0; i < entries.Count; i++)
            {
                if (branches[i].EventId is not { } id) continue;
                var pick = new WeightedPick(branches, i);
                yield return Call(key, id, CallPart.OnActionRandom, entries[i].Src) with
                {
                    Weight = pick,
                    Conditions = [new CallCondition(pick.BaseChance is { } c
                        ? $"by chance: weight {branches[i].WeightText} ({EventCallFinder.Format(c * 100)} %)"
                        : $"by chance: weight {branches[i].WeightText}", null)],
                };
            }
        }
    }

    /// <summary>
    /// The calls in an object's top-level blocks (not its triggers). A block whose keys are all numbers (anomaly on_success
    /// outcomes) is a weighted pick like random_list; its <c>weight = id</c> entries fire that event.
    /// </summary>
    static List<EventCall> ObjectCalls(PdxBlock obj, string folder, string id, TechSourceRef src, Localisation loc, EventCallFinder finder)
    {
        var caller = new EventCall
        {
            TargetId = "",
            Effect = "",
            CallerKind = CallerKind.Object,
            CallerId = id,
            CallerName = UnlockScanner.UnlockName(loc, folder, id),
            CallerKindName = UnlockScanner.KindName(folder),
            CallerFolder = folder,
            Part = CallPart.Effect,
            Source = src,
        };
        var calls = new List<EventCall>();
        foreach (var e in obj.Entries)
        {
            if (EffectWalker.IsTriggerBlock(e.Key)) continue;
            if (EventScripts.IsEventKey(e.Key))
            {
                var single = new PdxBlock();
                single.Entries.Add(e);
                calls.AddRange(finder.Find(single, caller));
                continue;
            }
            if (e.Value is not PdxBlock block) continue;
            var at = caller with { Block = e.Key };
            if (block.Entries.Count > 0 && block.Entries.All(x => EventCallFinder.Number(x.Key) is not null))
            {
                var branches = new PdxBlock();
                foreach (var x in block.Entries)
                {
                    // A branch that is not an event still takes its share of the weight.
                    var body = x.Value as PdxBlock;
                    if (body is null)
                    {
                        body = new PdxBlock();
                        if (EventCallFinder.IsEventId((string)x.Value)) body.Entries.Add(new PdxEntry("event", "=", x.Value));
                    }
                    branches.Entries.Add(new PdxEntry(x.Key, "=", body));
                }
                var list = new PdxBlock();
                list.Entries.Add(new PdxEntry("random_list", "=", branches));
                calls.AddRange(finder.Find(list, at));
            }
            else calls.AddRange(finder.Find(block, at));
        }
        return calls;
    }
}

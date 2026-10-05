using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>An event's winning definition with inline scripts expanded and <c>base = id</c> inheritance applied.</summary>
internal sealed record ResolvedEvent(string Id, string Type, PdxBlock Block, TechSourceRef Src);

/// <summary>The window texts of an event: the first title, description and picture (Varies = chosen by conditions in game).</summary>
internal sealed record EventTexts(string? Title, string? Description, bool DescriptionVaries, string? Picture, bool PictureVaries);

/// <summary>Event definitions shared by the tech grant scanner and the event graph.</summary>
internal static class EventScripts
{
    /// <summary>How many base events deep inheritance is followed.</summary>
    const int MaxInheritance = 5;

    public static bool IsEventKey(string key) => Is(key, "event") || key.EndsWith("_event", StringComparison.OrdinalIgnoreCase);

    public static bool Is(string key, string name) => key.Equals(name, StringComparison.OrdinalIgnoreCase);

    public static bool Yes(PdxBlock block, string key) => string.Equals(block.GetString(key), "yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The events of the event files, first definition per id winning, in that order. Event-level inline scripts are expanded and
    /// <c>base = id</c> inheritance is resolved across files (up to <see cref="MaxInheritance"/> deep, cycle-safe).
    /// </summary>
    public static List<ResolvedEvent> Resolve(IEnumerable<ParsedFile> files, ScriptLibrary library, CancellationToken ct)
    {
        var definitions = new Dictionary<string, (string Type, PdxBlock Block, TechSourceRef Src)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
            foreach (var e in file.Root.Entries)
                if (e.Value is PdxBlock block && IsEventKey(e.Key) && block.GetString("id") is { } id)
                    definitions.TryAdd(id, (e.Key, block, file.Src));

        var resolved = new Dictionary<string, PdxBlock>(StringComparer.OrdinalIgnoreCase);
        PdxBlock Resolve(string id, int depth, HashSet<string> resolving)
        {
            if (resolved.TryGetValue(id, out var done)) return done;
            var own = ExpandInline(definitions[id].Block, library, 0);
            var result = own;
            if (own.GetString("base") is { } baseId && depth < MaxInheritance && !Is(baseId, id)
                && definitions.ContainsKey(baseId) && !resolving.Contains(baseId))
            {
                resolving.Add(id);
                result = Inherit(own, Resolve(baseId, depth + 1, resolving));
                resolving.Remove(id);
            }
            return resolved[id] = result;
        }

        var list = new List<ResolvedEvent>(definitions.Count);
        foreach (var (id, d) in definitions)
        {
            ct.ThrowIfCancellationRequested();
            list.Add(new ResolvedEvent(id, d.Type, Resolve(id, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase)), d.Src));
        }
        return list;
    }

    /// <summary>
    /// A derived event (<c>base = id</c>) on top of its resolved base: title, desc (unless desc_clear = yes), picture, immediate and
    /// after come from the base when the derived event has none; the base's options come first unless option_clear = yes.
    /// </summary>
    static PdxBlock Inherit(PdxBlock own, PdxBlock baseBlock)
    {
        bool Has(string key) => own.Entries.Any(e => Is(e.Key, key));
        var inherit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "title", "picture", "immediate", "after" })
            if (!Has(key)) inherit.Add(key);
        if (!Has("desc") && !Yes(own, "desc_clear")) inherit.Add("desc");
        if (!Yes(own, "option_clear")) inherit.Add("option");

        var merged = new PdxBlock();
        merged.Entries.AddRange(baseBlock.Entries.Where(e => inherit.Contains(e.Key)));
        merged.Entries.AddRange(own.Entries);
        merged.Items.AddRange(own.Items);
        return merged;
    }

    /// <summary>Replaces event-level inline_script entries by the script's entries (parameters substituted), recursively.</summary>
    static PdxBlock ExpandInline(PdxBlock block, ScriptLibrary library, int depth)
    {
        if (depth > 5 || !block.Entries.Any(e => Is(e.Key, "inline_script"))) return block;
        var copy = new PdxBlock();
        foreach (var e in block.Entries)
        {
            if (!Is(e.Key, "inline_script")) { copy.Entries.Add(e); continue; }
            var body = e.Value switch
            {
                string path => library.Inline(path, new Dictionary<string, string>()),
                PdxBlock b when b.GetString("script") is { } path => library.Inline(path,
                    b.Entries.Where(x => x.Value is string && !Is(x.Key, "script"))
                        .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => (string)g.Last().Value, StringComparer.OrdinalIgnoreCase)),
                _ => null,
            };
            if (body is not null) copy.Entries.AddRange(ExpandInline(body, library, depth + 1).Entries);
        }
        copy.Items.AddRange(block.Items);
        return copy;
    }

    public static List<PdxBlock> Options(PdxBlock block) =>
        block.Entries.Where(e => Is(e.Key, "option") && e.Value is PdxBlock).Select(e => (PdxBlock)e.Value).ToList();

    public static EventTexts Texts(PdxBlock block, Localisation loc)
    {
        var titles = Entries(block, "title");
        var descs = Entries(block, "desc");
        var pictures = Entries(block, "picture");
        return new EventTexts(
            titles.Count > 0 ? FirstText(titles[0].Value, loc) : null,
            descs.Count > 0 ? FirstText(descs[0].Value, loc) : null,
            descs.Count > 1 || descs.Any(d => d.Value is PdxBlock),
            pictures.Count > 0 ? (pictures[0].Value is PdxBlock pb ? pb.GetString("picture") : (string)pictures[0].Value) : null,
            pictures.Count > 1 || pictures.Any(p => p.Value is PdxBlock));
    }

    /// <summary>The option's localised name, or "Option N" (1-based) when it has none.</summary>
    public static string OptionName(PdxBlock option, int index, Localisation loc) =>
        Entries(option, "name") is { Count: > 0 } names ? FirstText(names[0].Value, loc) ?? $"Option {index + 1}" : $"Option {index + 1}";

    static List<PdxEntry> Entries(PdxBlock block, string key) => block.Entries.Where(e => Is(e.Key, key)).ToList();

    /// <summary>
    /// A key's localised text, or the first "text" found depth-first in a block: first_valid, random_valid, nested desc, and the
    /// <c>trigger = { text = …  if = { limit = { … } text = … } }</c> form. Limit blocks are skipped.
    /// </summary>
    public static string? FirstText(object value, Localisation loc)
    {
        if (value is string key) return loc.Get(key) ?? key;
        var block = (PdxBlock)value;
        if (block.GetString("text") is { } text) return loc.Get(text) ?? text;
        foreach (var e in block.Entries)
            if (e.Value is PdxBlock child && !Is(e.Key, "limit") && FirstText(child, loc) is { } found)
                return found;
        return null;
    }
}

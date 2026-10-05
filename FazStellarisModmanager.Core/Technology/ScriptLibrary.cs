using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>
/// Scripted effects (common/scripted_effects, last definition per name wins) and inline scripts (common/inline_scripts,
/// raw text by path without .txt), with the game's $PARAM$ / $PARAM|default$ substitution.
/// </summary>
public sealed class ScriptLibrary
{
    const string InlineFolder = "common/inline_scripts";

    static readonly Regex Param = new(@"\$([A-Za-z0-9_]+)(?:\|([^$]*))?\$", RegexOptions.Compiled);

    readonly Dictionary<string, PdxBlock> _effects = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _inline = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, bool> _hasParameters = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, PdxBlock> Effects => _effects;

    public static ScriptLibrary Load(IReadOnlyList<ContentSource> sources, ICollection<string> warnings, CancellationToken ct = default)
    {
        var library = new ScriptLibrary();
        foreach (var (name, body) in CommonDefinitions.Load(sources, "common/scripted_effects", warnings, ct)) library._effects[name] = body;

        var files = new Dictionary<string, (ContentSource Source, string Rel)>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            foreach (var rel in source.Files(InlineFolder, ".txt"))
                files[rel] = (source, rel);
        foreach (var (source, rel) in files.Values)
        {
            ct.ThrowIfCancellationRequested();
            try { library.AddInlineScript(rel[(InlineFolder.Length + 1)..^4], source.ReadText(rel)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
            {
                warnings.Add($"{source.Name}: {rel}: {ex.Message}");
            }
        }
        return library;
    }

    /// <summary>Adds (or replaces) the scripted effects defined in <paramref name="text"/>.</summary>
    public void AddEffects(string text)
    {
        foreach (var e in ParadoxScriptParser.Parse(text).Entries)
            if (e.Value is PdxBlock b) _effects[e.Key] = b;
        _hasParameters.Clear();
    }

    /// <summary>True when the scripted effect mentions a $PARAM$ anywhere (so calls must substitute); false for unknown names.</summary>
    internal bool HasParameters(string name) =>
        _hasParameters.GetOrAdd(name, n => _effects.TryGetValue(n, out var b) && Mentions(b));

    static bool Mentions(PdxBlock b) =>
        b.Entries.Any(e => e.Key.Contains('$') || (e.Value is PdxBlock inner ? Mentions(inner) : ((string)e.Value).Contains('$')))
        || b.Items.Any(i => i is PdxBlock inner ? Mentions(inner) : ((string)i).Contains('$'));

    /// <summary>Adds an inline script; <paramref name="path"/> is relative to common/inline_scripts, without .txt.</summary>
    public void AddInlineScript(string path, string text) => _inline[Normalize(path)] = text;

    /// <summary>The inline script with parameters substituted, parsed; null when there is no such script.</summary>
    public PdxBlock? Inline(string path, IReadOnlyDictionary<string, string> parameters) =>
        _inline.TryGetValue(Normalize(path), out var text) ? ParadoxScriptParser.Parse(Substitute(text, parameters)) : null;

    static string Normalize(string path) => path.Trim().Trim('"').Replace((char)92, '/');

    public static string Substitute(string text, IReadOnlyDictionary<string, string> parameters) =>
        !text.Contains('$') ? text
        : Param.Replace(text, m => parameters.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Groups[2].Success ? m.Groups[2].Value : m.Value);

    /// <summary>A copy of <paramref name="block"/> with parameters substituted in keys, values and items.</summary>
    public static PdxBlock Substitute(PdxBlock block, IReadOnlyDictionary<string, string> parameters)
    {
        var copy = new PdxBlock();
        foreach (var e in block.Entries)
            copy.Entries.Add(new PdxEntry(Substitute(e.Key, parameters), e.Op,
                e.Value is PdxBlock b ? Substitute(b, parameters) : Substitute((string)e.Value, parameters)));
        foreach (var item in block.Items)
            copy.Items.Add(item is PdxBlock b ? Substitute(b, parameters) : Substitute((string)item, parameters));
        return copy;
    }
}

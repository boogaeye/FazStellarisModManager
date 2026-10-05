using System.Buffers;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>A parsed script file, or (DefinedOnly set, Root empty) the object ids of a base file a pre-filter ruled out.</summary>
internal sealed record ParsedFile(TechSourceRef Src, PdxBlock Root, IReadOnlyList<string>? DefinedOnly);

/// <summary>Reading script folders for scanners: per-path override, parallel parsing, and a word pre-filter for base-game files.</summary>
internal static class ScriptFiles
{
    static readonly Regex Word = new("[A-Za-z0-9_]+", RegexOptions.Compiled);

    /// <summary>
    /// Words whose presence means a file may contain an effect of interest: <paramref name="needles"/> (substrings), inline_script, and
    /// the scripted effects that (transitively) mention a word for which <paramref name="interesting"/> is true. A superset filter:
    /// a match inside a longer word counts too.
    /// </summary>
    public static SearchValues<string> Prefilter(ScriptLibrary library, Func<string, bool> interesting, IEnumerable<string> needles)
    {
        var words = library.Effects.ToDictionary(kv => kv.Key,
            kv => new HashSet<string>(Word.Matches(PdxScriptPrinter.Print(kv.Value)).Select(m => m.Value), StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var matching = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, w) in words)
            if (w.Any(interesting) || w.Contains("inline_script")) matching.Add(name);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (name, w) in words)
                if (!matching.Contains(name) && w.Any(matching.Contains)) { matching.Add(name); changed = true; }
        }
        return SearchValues.Create([.. needles, "inline_script", .. matching], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Winning files of a top folder (per-path override), parsed in parallel, returned in load order. With a pre-filter, base
    /// files that mention none of its words are not parsed: only their object ids are read, as definitions without effects.
    /// Mod files are always parsed.
    /// </summary>
    public static List<ParsedFile> ParseAll(IReadOnlyList<ContentSource> sources, string folder, Func<string, bool> include, SearchValues<string>? prefilter,
        ICollection<string> warnings, CancellationToken ct)
    {
        var files = new Dictionary<string, (ContentSource Source, string Rel)>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            foreach (var rel in source.Files(folder, ".txt"))
                if (include(rel)) files[rel] = (source, rel);

        var ordered = files.Values.OrderBy(f => f.Rel, TechDatabase.LoadOrder).ToArray();
        var parsed = new ParsedFile?[ordered.Length];
        var warned = new ConcurrentQueue<string>();
        Parallel.ForEach(Enumerable.Range(0, ordered.Length), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, i =>
        {
            var (source, rel) = ordered[i];
            string text;
            try { text = source.ReadText(rel); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
            {
                warned.Enqueue($"{source.Name}: {rel}: {ex.Message}");
                return;
            }
            var src = new TechSourceRef(source.Name, source.IsBaseGame, rel);
            parsed[i] = prefilter is null || !source.IsBaseGame || text.AsSpan().ContainsAny(prefilter)
                ? new ParsedFile(src, ParadoxScriptParser.Parse(text), null)
                : new ParsedFile(src, new PdxBlock(), TopLevelIds(text));
        });
        ct.ThrowIfCancellationRequested();
        foreach (var w in warned) warnings.Add(w);
        return parsed.OfType<ParsedFile>().ToList();
    }

    /// <summary>
    /// The ids of the top-level <c>name = { … }</c> objects of a script file, without a full parse: the name, or the object's own
    /// <c>key = …</c> when it has one. Skips comments and quoted strings; <c>@variables</c> are not objects.
    /// </summary>
    public static List<string> TopLevelIds(string text)
    {
        const char Newline = (char)10;
        var ids = new List<string>();
        var depth = 0;
        string? id = null, word = null;
        var afterEquals = false;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#')
            {
                while (i < text.Length && text[i] != Newline) i++;
                continue;
            }
            if (c == '{')
            {
                if (depth == 0 && afterEquals && word is not null && !word.StartsWith('@')) id = word;
                depth++;
                (word, afterEquals) = (null, false);
                i++;
                continue;
            }
            if (c == '}')
            {
                if (depth > 0 && --depth == 0 && id is not null) { ids.Add(id); id = null; }
                (word, afterEquals) = (null, false);
                i++;
                continue;
            }
            if (c == '=') { afterEquals = word is not null; i++; continue; }
            if (c is '<' or '>' or '!') { (word, afterEquals) = (null, false); i++; continue; }

            string token;
            if (c == '"')
            {
                var end = text.IndexOf('"', i + 1);
                if (end < 0) end = text.Length;
                token = text[(i + 1)..end];
                i = end + 1;
            }
            else
            {
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '=' or '#' or '"' or '<' or '>' or '!')) i++;
                token = text[start..i];
            }
            if (afterEquals)
            {
                if (depth == 1 && id is not null && word!.Equals("key", StringComparison.OrdinalIgnoreCase)) id = token;
                (word, afterEquals) = (null, false);
            }
            else word = token;
        }
        return ids;
    }
}

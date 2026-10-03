using System.Text.RegularExpressions;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>English localisation merged from the base game and mods (later wins; "replace" folders win over everything).</summary>
public sealed class Localisation
{
    static readonly Regex Line = new(@"^\s*([\w.\-]+):\d*\s*""(.*)""[^""]*$", RegexOptions.Compiled);
    static readonly Regex ColorCode = new(@"§.", RegexOptions.Compiled);
    static readonly Regex IconTag = new(@"£[^£]*£", RegexOptions.Compiled);
    static readonly Regex IconName = new(@"£([^£]+)£", RegexOptions.Compiled);
    static readonly Regex Reference = new(@"\$([^$\s]+)\$", RegexOptions.Compiled);
    static readonly Regex Spaces = new(@"[ \t]{2,}", RegexOptions.Compiled);

    readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _map.Count;

    public static Localisation Load(IReadOnlyList<ContentSource> sources, ICollection<string> warnings)
    {
        var loc = new Localisation();
        var replace = new List<(ContentSource Source, string Rel)>();
        foreach (var source in sources)
            foreach (var rel in source.Files("localisation", "_l_english.yml"))
            {
                if (rel.Split('/').Any(part => part.Equals("replace", StringComparison.OrdinalIgnoreCase))) replace.Add((source, rel));
                else loc.AddFile(source, rel, warnings);
            }
        foreach (var (source, rel) in replace) loc.AddFile(source, rel, warnings);
        return loc;
    }

    void AddFile(ContentSource source, string rel, ICollection<string> warnings)
    {
        try { AddText(source.ReadText(rel)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
        {
            warnings.Add($"{source.Name}: {rel}: {ex.Message}");
        }
    }

    public void AddText(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } raw)
        {
            var trimmed = raw.AsSpan().TrimStart();
            if (trimmed.IsEmpty || trimmed[0] == '#') continue;
            var m = Line.Match(raw);
            if (m.Success) _map[m.Groups[1].Value] = m.Groups[2].Value;
        }
    }

    /// <summary>Cleaned text for a key (colour codes and icons removed, $refs$ resolved up to three levels), or null.</summary>
    public string? Get(string key) => _map.TryGetValue(key, out var value) ? Clean(value, 0) : null;

    /// <summary>Name of the first inline icon tag (the name in a "£name£" pair) of a key's raw text, after expanding $refs$ up to three levels, or null.</summary>
    public string? FirstIconTag(string key)
    {
        if (!_map.TryGetValue(key, out var value)) return null;
        var m = IconName.Match(Expand(value, 0));
        return m.Success ? m.Groups[1].Value : null;
    }

    string Expand(string text, int depth) => Reference.Replace(text, m =>
    {
        var key = m.Groups[1].Value.Split('|')[0];
        return depth < 3 && _map.TryGetValue(key, out var v) ? Expand(v, depth + 1) : key;
    });

    string Clean(string text, int depth)
    {
        text = ColorCode.Replace(text, "");
        text = IconTag.Replace(text, "");
        text = Reference.Replace(text, m =>
        {
            var key = m.Groups[1].Value.Split('|')[0];
            return depth < 3 && _map.TryGetValue(key, out var v) ? Clean(v, depth + 1) : key;
        });
        text = text.Replace("\\n", "\n").Replace("\\\"", "\"");
        return Spaces.Replace(text, " ").Trim();
    }
}

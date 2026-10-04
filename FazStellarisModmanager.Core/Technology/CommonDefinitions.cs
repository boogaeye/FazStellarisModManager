using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>
/// Top-level "name = { … }" definitions of a common/ folder (recursive *.txt) after the usual override rules: a later source's file
/// at the same path replaces the earlier one; remaining files are read in load order and a later definition of a name wins.
/// </summary>
public static class CommonDefinitions
{
    public static Dictionary<string, PdxBlock> Load(IReadOnlyList<ContentSource> sources, string folder, ICollection<string> warnings,
        CancellationToken ct = default)
    {
        var files = new Dictionary<string, (ContentSource Source, string Rel)>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            foreach (var rel in source.Files(folder, ".txt"))
                files[rel] = (source, rel);

        var definitions = new Dictionary<string, PdxBlock>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, rel) in files.Values.OrderBy(f => f.Rel, TechDatabase.LoadOrder))
        {
            ct.ThrowIfCancellationRequested();
            string text;
            try { text = source.ReadText(rel); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
            {
                warnings.Add($"{source.Name}: {rel}: {ex.Message}");
                continue;
            }
            foreach (var e in ParadoxScriptParser.Parse(text).Entries)
                if (e.Value is PdxBlock b) definitions[e.Key] = b;
        }
        return definitions;
    }
}

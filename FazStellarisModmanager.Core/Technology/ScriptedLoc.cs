using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>One "text = { trigger = { … } localization_key = K }" branch of a defined_text.</summary>
public sealed record ScriptedLocBranch(string LocKey, PdxBlock? Trigger);

/// <summary>A defined_text: conditional branches in file order plus the optional default key.</summary>
public sealed record ScriptedLocFunction(string Name, IReadOnlyList<ScriptedLocBranch> Branches, string? Default)
{
    /// <summary>The key shown when no empire is known: the default, else the last branch.</summary>
    public string? DefaultKey => Default ?? Branches.LastOrDefault()?.LocKey;
}

/// <summary>Scripted localisation functions ([GetX] in localisation) from common/scripted_loc. A later source's file at the same path replaces the earlier one; remaining files are read in load order, later wins per name.</summary>
public sealed class ScriptedLoc
{
    public const string Folder = "common/scripted_loc";

    // Translation-only files (00_scripted_loc_fr.txt, scripted_loc_deloc.txt, …) define the same names with other text.
    static readonly Regex OtherLanguage = new(@"(?:_(?:fr|de|es|ru|pl|pt|br|por|ja|ko|zh|ch)|(?:^|[_/])(?:fr|de|es|ru|pl|por|br|ja|ko|zh)loc)\.txt$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    readonly Dictionary<string, ScriptedLocFunction> _functions = new(StringComparer.OrdinalIgnoreCase);

    public static ScriptedLoc Empty { get; } = new();

    public int Count => _functions.Count;

    public ScriptedLocFunction? Find(string name) => _functions.TryGetValue(name, out var f) ? f : null;

    public static ScriptedLoc Build(IReadOnlyList<ContentSource> sources, ICollection<string> warnings, CancellationToken ct = default)
    {
        var files = new Dictionary<string, (ContentSource Source, string Rel)>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            foreach (var rel in source.Files(Folder, ".txt"))
                if (!OtherLanguage.IsMatch(rel)) files[rel] = (source, rel);

        var index = new ScriptedLoc();
        foreach (var (source, rel) in files.Values.OrderBy(f => f.Rel, TechDatabase.LoadOrder))
        {
            ct.ThrowIfCancellationRequested();
            try { index.AddText(source.ReadText(rel)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
            {
                warnings.Add($"{source.Name}: {rel}: {ex.Message}");
            }
        }
        return index;
    }

    public void AddText(string text)
    {
        foreach (var e in ParadoxScriptParser.Parse(text).Entries)
        {
            if (!e.Key.Equals("defined_text", StringComparison.OrdinalIgnoreCase) || e.Value is not PdxBlock b) continue;
            if (b.GetString("name") is not { } name) continue;
            var branches = b.Entries
                .Where(x => x.Key.Equals("text", StringComparison.OrdinalIgnoreCase) && x.Value is PdxBlock)
                .Select(x => (PdxBlock)x.Value)
                .Where(t => t.GetString("localization_key") is not null)
                .Select(t => new ScriptedLocBranch(t.GetString("localization_key")!, t.GetBlock("trigger")))
                .ToList();
            var fallback = b.GetString("default");
            if (branches.Count == 0 && fallback is null) continue;
            _functions[name] = new ScriptedLocFunction(name, branches, fallback);
        }
    }
}

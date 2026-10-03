using System.Text;

namespace FazStellarisModmanager.Core.Descriptors;

/// <summary>A mod/*.mod (or a Workshop descriptor.mod) file.</summary>
public sealed record ModDescriptor(
    string? Name,
    string? Path,
    string? Archive,
    string? RemoteFileId,
    string? Version,
    string? SupportedVersion,
    string? Picture,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Dependencies)
{
    public static ModDescriptor Parse(string text)
    {
        var b = ParadoxScriptParser.Parse(text);
        return new ModDescriptor(
            b.GetString("name"),
            b.GetString("path"),
            b.GetString("archive"),
            b.GetString("remote_file_id"),
            b.GetString("version"),
            b.GetString("supported_version"),
            b.GetString("picture"),
            b.GetBlock("tags")?.StringItems.ToList() ?? [],
            b.GetBlock("dependencies")?.StringItems.ToList() ?? []);
    }

    public static ModDescriptor Load(string file) => Parse(File.ReadAllText(file));

    /// <summary>Writes the descriptor in the layout the Paradox launcher uses for mod/*.mod files.</summary>
    public string Serialize()
    {
        var sb = new StringBuilder();

        void Str(string key, string? value)
        {
            if (value is not null) sb.Append(key).Append("=\"").Append(Escape(value)).Append("\"\n");
        }

        void List(string key, IReadOnlyList<string> items)
        {
            if (items.Count == 0) return;
            sb.Append(key).Append("={\n");
            foreach (var item in items) sb.Append("\t\"").Append(Escape(item)).Append("\"\n");
            sb.Append("}\n");
        }

        Str("version", Version);
        List("tags", Tags);
        Str("name", Name);
        Str("picture", Picture);
        Str("supported_version", SupportedVersion);
        Str("path", Path);
        Str("archive", Archive);
        Str("remote_file_id", RemoteFileId);
        List("dependencies", Dependencies);
        return sb.ToString();
    }

    static string Escape(string s) => s.Replace("\"", "\\\"");
}

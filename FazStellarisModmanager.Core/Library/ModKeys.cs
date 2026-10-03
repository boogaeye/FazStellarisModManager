using System.Text.RegularExpressions;

namespace FazStellarisModmanager.Core.Library;

public static class ModKeys
{
    static readonly Regex Ugc = new(@"^ugc_(\d+)\.mod$", RegexOptions.IgnoreCase);

    /// <summary>
    /// "ugc:&lt;id&gt;" for Steam Workshop descriptors (mod/ugc_&lt;id&gt;.mod), otherwise "local:&lt;file&gt;".
    /// Local copies that keep a remote_file_id (e.g. Irony merged collections) stay local,
    /// so they never collide with the real Workshop item.
    /// </summary>
    public static string For(string descriptorRel)
    {
        var file = Path.GetFileName(descriptorRel.Replace('\\', '/'));
        var m = Ugc.Match(file);
        return m.Success ? $"ugc:{m.Groups[1].Value}" : $"local:{file}";
    }

    public static ulong? WorkshopId(string key) =>
        key.StartsWith("ugc:", StringComparison.OrdinalIgnoreCase) && ulong.TryParse(key[4..], out var id) ? id : null;
}

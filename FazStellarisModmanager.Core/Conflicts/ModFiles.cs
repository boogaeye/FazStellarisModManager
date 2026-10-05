using System.IO.Compression;

namespace FazStellarisModmanager.Core.Conflicts;

/// <summary>A mod's content files: paths (forward slashes) inside subfolders of its folder or zip. Root files (descriptor, thumbnail, readme) and icons are not content.</summary>
public static class ModFiles
{
    /// <summary>Icons (resources, techs, buildings, …) are not counted: overriding them is cosmetic.</summary>
    public const string IconFolder = "gfx/interface/icons/";

    /// <summary>Content paths sorted case-insensitively without case-insensitive duplicates (first spelling kept); empty when the path is neither a folder nor a .zip. Throws on IO and zip errors.</summary>
    public static IReadOnlyList<string> List(string contentPath)
    {
        IEnumerable<string> raw;
        if (contentPath.Length > 0 && Directory.Exists(contentPath))
        {
            var root = Path.GetFullPath(contentPath);
            raw = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f));
        }
        else if (contentPath.Length > 0 && File.Exists(contentPath) && contentPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(contentPath);
            raw = zip.Entries.Select(e => e.FullName).ToList();
        }
        else
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var r in raw)
        {
            var p = r.Replace((char)92, '/').TrimStart('/');
            if (p.Length == 0 || p.EndsWith('/') || !p.Contains('/')) continue;
            if (p.StartsWith(IconFolder, StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(p)) result.Add(p);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }
}

namespace FazStellarisModmanager.Core.Hashing;

public static class FileCollector
{
    /// <summary>
    /// Relative path ("common/a.txt") -> absolute path for files matched by the manifest rules.
    /// Later roots override earlier ones, matching how Stellaris layers mods over the base game.
    /// </summary>
    public static SortedDictionary<string, string> Collect(IEnumerable<ManifestEntry> entries, IReadOnlyList<string> roots)
    {
        var rules = entries.ToList();
        var files = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            foreach (var entry in rules)
            {
                var dir = Path.Combine(root, entry.Name);
                if (!Directory.Exists(dir)) continue;
                var opt = entry.SubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var f in Directory.EnumerateFiles(dir, "*", opt))
                {
                    if (!f.EndsWith(entry.FileExtension, StringComparison.OrdinalIgnoreCase)) continue;
                    files[Path.GetRelativePath(root, f).Replace('\\', '/')] = f;
                }
            }
        }
        return files;
    }
}

using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Library;

public static class ModLibrary
{
    /// <summary>
    /// Creates mod/ugc_&lt;id&gt;.mod for every Workshop folder that lacks one, the way the Paradox launcher does.
    /// Returns the descriptors created (relative to the user dir).
    /// </summary>
    public static List<string> EnsureWorkshopDescriptors(string userDir, string workshopDir)
    {
        var created = new List<string>();
        if (!Directory.Exists(workshopDir)) return created;
        var modDir = Path.Combine(userDir, "mod");
        Directory.CreateDirectory(modDir);

        foreach (var dir in Directory.EnumerateDirectories(workshopDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileName(dir);
            if (!ulong.TryParse(id, out _)) continue;
            var target = Path.Combine(modDir, $"ugc_{id}.mod");
            if (File.Exists(target)) continue;
            var inner = Path.Combine(dir, "descriptor.mod");
            if (!File.Exists(inner)) continue;

            var full = Path.GetFullPath(dir).Replace('\\', '/');
            var d = ModDescriptor.Load(inner);
            d = d.Archive is { Length: > 0 } archive
                ? d with { Path = null, Archive = $"{full}/{Path.GetFileName(archive)}", RemoteFileId = id }
                : d with { Path = full, Archive = null, RemoteFileId = id };
            File.WriteAllText(target, d.Serialize());
            created.Add($"mod/ugc_{id}.mod");
        }
        return created;
    }

    /// <summary>Reads every mod/*.mod descriptor in the user dir, sorted by file name.</summary>
    public static List<InstalledMod> Scan(string userDir)
    {
        var result = new List<InstalledMod>();
        var modDir = Path.Combine(userDir, "mod");
        if (!Directory.Exists(modDir)) return result;

        foreach (var file in Directory.EnumerateFiles(modDir, "*.mod").OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
        {
            var rel = "mod/" + Path.GetFileName(file);
            var d = ModDescriptor.Load(file);
            var key = ModKeys.For(rel);
            result.Add(new InstalledMod(
                key,
                d.Name ?? Path.GetFileNameWithoutExtension(file),
                rel,
                d.RemoteFileId,
                d.Version,
                d.SupportedVersion,
                ResolveContent(userDir, d),
                key.StartsWith("ugc:", StringComparison.Ordinal) ? ModSource.Workshop : ModSource.Local,
                d.Tags));
        }
        return result;
    }

    /// <summary>Absolute content folder (path=) or archive (archive=); relative values are relative to the user dir.</summary>
    public static string ResolveContent(string userDir, ModDescriptor d)
    {
        var p = d.Path ?? d.Archive ?? "";
        if (p.Length == 0) return "";
        return Path.IsPathRooted(p) ? Path.GetFullPath(p) : Path.GetFullPath(Path.Combine(userDir, p));
    }
}

using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Library;

public static class ModLibrary
{
    /// <summary>
    /// Creates mod/ugc_&lt;id&gt;.mod for every Workshop folder that lacks one, the way the Paradox launcher does.
    /// Returns the descriptors created (relative to the user dir).
    /// </summary>
    public static List<string> EnsureWorkshopDescriptors(string userDir, string workshopDir, ICollection<string>? errors = null)
    {
        var created = new List<string>();
        if (!Directory.Exists(workshopDir)) return created;
        var modDir = Path.Combine(userDir, "mod");
        List<string> workshopDirs;
        try
        {
            Directory.CreateDirectory(modDir);
            workshopDirs = Directory.EnumerateDirectories(workshopDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            errors?.Add($"Workshop descriptors: {e.Message}");
            return created;
        }

        foreach (var dir in workshopDirs)
        {
            var id = Path.GetFileName(dir);
            if (!ulong.TryParse(id, out _)) continue;
            var target = Path.Combine(modDir, $"ugc_{id}.mod");
            if (File.Exists(target)) continue;
            var inner = Path.Combine(dir, "descriptor.mod");
            if (!File.Exists(inner)) continue;

            var tmp = target + ".tmp";
            try
            {
                var full = Path.GetFullPath(dir).Replace('\\', '/');
                var d = ModDescriptor.Load(inner);
                d = d.Archive is { Length: > 0 } archive
                    ? d with { Path = null, Archive = $"{full}/{Path.GetFileName(archive)}", RemoteFileId = id }
                    : d with { Path = full, Archive = null, RemoteFileId = id };
                File.WriteAllText(tmp, d.Serialize());
                File.Move(tmp, target, overwrite: false);
                created.Add($"mod/ugc_{id}.mod");
            }
            catch (IOException) when (File.Exists(target) && File.Exists(tmp))
            {
                // the launcher created the descriptor meanwhile: keep theirs
                TryDelete(tmp);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                TryDelete(tmp);
                errors?.Add($"mod/ugc_{id}.mod: {e.Message}");
            }
        }
        return created;
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Reads every mod/*.mod descriptor in the user dir, sorted by file name; unreadable ones are skipped and reported in <paramref name="errors"/>.</summary>
    public static List<InstalledMod> Scan(string userDir, ICollection<string>? errors = null)
    {
        var result = new List<InstalledMod>();
        var modDir = Path.Combine(userDir, "mod");
        if (!Directory.Exists(modDir)) return result;

        foreach (var file in Directory.EnumerateFiles(modDir, "*.mod").OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
        {
            var rel = "mod/" + Path.GetFileName(file);
            try
            {
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
                    ModKeys.WorkshopId(key) is not null ? ModSource.Workshop : ModSource.Local,
                    d.Tags));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                errors?.Add($"{rel}: {e.Message}");
            }
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

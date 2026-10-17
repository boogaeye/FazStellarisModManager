using System.Text;
using FazStellarisModmanager.Core.IO;

namespace FazStellarisModmanager.Core.Addons;

/// <summary>
/// Faz Addons on disk: the content in &lt;user dir&gt;/mod/faz_addons and the descriptor mod/faz_addons.mod. It is not a mod of the manager's
/// lists; Apply appends it last when addons are on.
/// </summary>
public static class AddonMod
{
    public const string DescriptorRel = "mod/faz_addons.mod";
    public const string ContentRel = "mod/faz_addons";
    public const string Name = "Faz Addons";

    /// <summary>True for Faz Addons' descriptor as dlc_load.json or a list names it (either slash, any case).</summary>
    public static bool IsAddon(string descriptorRel) =>
        string.Equals(descriptorRel.Replace((char)92, '/'), DescriptorRel, StringComparison.OrdinalIgnoreCase);

    public static string ContentFolder(string userDir) => Path.GetFullPath(Path.Combine(userDir, ContentRel));

    public static string DescriptorFile(string userDir) => Path.GetFullPath(Path.Combine(userDir, DescriptorRel));

    /// <summary>
    /// Writes <paramref name="output"/> into the content folder (each changed file atomically; identical files are left untouched), deletes the
    /// files there that aren't in the output (and folders left empty), then writes mod/faz_addons.mod: the output's descriptor plus
    /// path="mod/faz_addons". Nothing else is touched.
    /// </summary>
    /// <exception cref="ArgumentException">An output path is rooted or leaves the content folder; nothing is written.</exception>
    public static void Write(string userDir, AddonOutput output)
    {
        var folder = ContentFolder(userDir);
        var targets = output.Files.Select(f => (Full: Contain(folder, f.Path), f.Bytes)).ToList();

        Directory.CreateDirectory(folder);
        foreach (var (full, bytes) in targets)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            WriteIfChanged(full, bytes);
        }

        var keep = targets.Select(t => t.Full).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList())
            if (!keep.Contains(Path.GetFullPath(file))) File.Delete(file);
        foreach (var dir in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);

        WriteIfChanged(DescriptorFile(userDir), Descriptor(output));
    }

    /// <summary>True when the content folder holds exactly the output's files with the same bytes, and mod/faz_addons.mod matches too.</summary>
    public static bool IsUpToDate(string userDir, AddonOutput output)
    {
        try
        {
            var folder = ContentFolder(userDir);
            if (!Directory.Exists(folder) || !SameBytes(DescriptorFile(userDir), Descriptor(output))) return false;
            var expected = output.Files.ToDictionary(f => Contain(folder, f.Path), f => f.Bytes, StringComparer.OrdinalIgnoreCase);
            var actual = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(Path.GetFullPath).ToList();
            return actual.Count == expected.Count && actual.All(f => expected.TryGetValue(f, out var bytes) && SameBytes(f, bytes));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    /// <summary>The content of mod/faz_addons.mod for an output: its descriptor.mod plus path="mod/faz_addons".</summary>
    public static byte[] Descriptor(AddonOutput output)
    {
        var text = output.Get(AddonBuilder.DescriptorPath) is { } bytes ? Encoding.UTF8.GetString(bytes) : "";
        if (text.Length > 0 && !text.EndsWith((char)10)) text += (char)10;
        return Encoding.UTF8.GetBytes(text + "path=" + (char)34 + ContentRel + (char)34 + (char)10);
    }

    // Full path of an output file, which must stay inside the content folder.
    static string Contain(string folder, string relativePath)
    {
        var full = Path.IsPathRooted(relativePath) ? null : Path.GetFullPath(Path.Combine(folder, relativePath));
        var prefix = folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (full is null || !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Addon file outside {ContentRel}: {relativePath}", nameof(relativePath));
        return full;
    }

    static bool SameBytes(string path, byte[] bytes) =>
        File.Exists(path) && new FileInfo(path).Length == bytes.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);

    static void WriteIfChanged(string path, byte[] bytes)
    {
        if (!SameBytes(path, bytes)) AtomicFile.WriteAllBytes(path, bytes);
    }
}

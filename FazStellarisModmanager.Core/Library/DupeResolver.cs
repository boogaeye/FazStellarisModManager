using System.Text;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.IO;

namespace FazStellarisModmanager.Core.Library;

public sealed record DupeChoice(DupeGroup Group, InstalledMod Keep);
public sealed record DupeRename(string DescriptorRel, string OldName, string NewName);
public sealed record DupeResolution(IReadOnlyList<DupeRename> Renamed, string? BackupDir, string? LauncherWarning);

/// <summary>Renames every member of a dupe group except the kept one, in its mod/*.mod file and in the launcher database.</summary>
public static class DupeResolver
{
    // The first top-level name line: optional indentation, name = "value".
    static readonly Regex NameLine = new(@"^([ \t]*name[ \t]*=[ \t]*"")([^""\r\n]*)("")", RegexOptions.Multiline);

    /// <summary>
    /// Backs up each renamed descriptor to &lt;backupDir&gt;/descriptors_&lt;timestamp&gt;/, rewrites only its name value, then
    /// sets the new names in launcher-v2.sqlite. A database problem becomes <see cref="DupeResolution.LauncherWarning"/>.
    /// </summary>
    public static DupeResolution Resolve(string userDir, IReadOnlyList<DupeChoice> choices, string backupDir, DateTimeOffset now)
    {
        var renames = new List<DupeRename>();
        string? dir = null;
        foreach (var (group, keep) in choices)
            foreach (var m in group.Mods)
            {
                if (string.Equals(m.DescriptorRel, keep.DescriptorRel, StringComparison.OrdinalIgnoreCase)) continue;
                var file = Path.Combine(userDir, m.DescriptorRel);
                if (!File.Exists(file)) continue;
                dir ??= Directory.CreateDirectory(Path.Combine(backupDir, "descriptors_" + now.UtcDateTime.ToString("yyyyMMdd_HHmmss_fff"))).FullName;
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
                var newName = DupeConflicts.DuplicateName(group.Name, m.DescriptorRel);
                var bytes = File.ReadAllBytes(file);
                var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                var text = Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
                var output = Encoding.UTF8.GetBytes(SetName(text, newName));
                AtomicFile.WriteAllBytes(file, bom ? [0xEF, 0xBB, 0xBF, .. output] : output);
                renames.Add(new DupeRename(m.DescriptorRel, m.Name, newName));
            }
        var warning = LauncherPlaysetFile.RenameMods(userDir, renames.Select(r => (r.DescriptorRel, r.NewName)).ToList(), backupDir, now);
        return new DupeResolution(renames, dir, warning);
    }

    /// <summary>The descriptor text with its first top-level name value replaced (or a name line added at the top).</summary>
    public static string SetName(string text, string newName)
    {
        var m = NameLine.Match(text);
        if (m.Success) return string.Concat(text.AsSpan(0, m.Groups[2].Index), newName, text.AsSpan(m.Groups[2].Index + m.Groups[2].Length));
        var newline = text.Contains((char)13 + "" + (char)10) ? (char)13 + "" + (char)10 : (char)10 + "";
        return "name=" + '"' + newName + '"' + newline + text;
    }
}

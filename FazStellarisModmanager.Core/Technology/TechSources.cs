using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;

namespace FazStellarisModmanager.Core.Technology;

public static class TechSources
{
    /// <summary>
    /// Base game first, then each mod of the list in load order. Mods whose descriptor or content is missing are skipped with a warning.
    /// Source names are unique (case-insensitive): later duplicates get " (2)", " (3)", ... appended.
    /// A missing <paramref name="gameDir"/> makes <see cref="ContentSource.FromPath"/> throw DirectoryNotFoundException; that is fatal and intended.
    /// </summary>
    public static List<ContentSource> Build(string gameDir, string userDir, ModList list, ICollection<string> warnings)
    {
        var sources = new List<ContentSource> { ContentSource.FromPath("Base game", gameDir, isBaseGame: true) };
        foreach (var entry in list.Mods)
        {
            try
            {
                var descriptorPath = Path.Combine(userDir, entry.DescriptorRel);
                if (!File.Exists(descriptorPath))
                {
                    warnings.Add($"{entry.Name}: descriptor {entry.DescriptorRel} not found, skipped.");
                    continue;
                }
                var descriptor = ModDescriptor.Load(descriptorPath);
                var name = UniqueName(descriptor.Name ?? entry.Name, sources);
                sources.Add(ContentSource.FromPath(name, ModLibrary.ResolveContent(userDir, descriptor)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                warnings.Add($"{entry.Name}: {ex.Message}");
            }
        }
        return sources;
    }

    static string UniqueName(string name, List<ContentSource> existing)
    {
        var candidate = name;
        for (var n = 2; existing.Any(s => string.Equals(s.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
            candidate = $"{name} ({n})";
        return candidate;
    }
}

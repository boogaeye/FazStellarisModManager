using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;

namespace FazStellarisModmanager.Core.Technology;

public static class TechSources
{
    /// <summary>Base game first, then each mod of the list in load order. Mods whose descriptor or content is missing are skipped with a warning.</summary>
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
                sources.Add(ContentSource.FromPath(descriptor.Name ?? entry.Name, ModLibrary.ResolveContent(userDir, descriptor)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                warnings.Add($"{entry.Name}: {ex.Message}");
            }
        }
        return sources;
    }
}

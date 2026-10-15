namespace FazStellarisModmanager.Core.Library;

/// <summary>Installed mods sharing one descriptor name. Stellaris skips every mod whose name another mod/*.mod also uses.</summary>
public sealed record DupeGroup(string Name, IReadOnlyList<InstalledMod> Mods);

public static class DupeConflicts
{
    /// <summary>Groups of two or more mods whose names match (trimmed, case-insensitive), ordered by name; members by descriptor path.</summary>
    public static IReadOnlyList<DupeGroup> Find(IReadOnlyList<InstalledMod> library) =>
        library
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .GroupBy(m => m.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var mods = g.OrderBy(m => m.DescriptorRel, StringComparer.OrdinalIgnoreCase).ToList();
                return new DupeGroup(mods[0].Name.Trim(), mods);
            })
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The member that keeps the name by default: the only one in the list; else, among those in the list (or all), the first
    /// Workshop one; else the first. Pass <paramref name="inListRels"/> with a case-insensitive comparer.
    /// </summary>
    public static InstalledMod DefaultKeep(DupeGroup group, IReadOnlySet<string> inListRels)
    {
        var listed = group.Mods.Where(m => inListRels.Contains(m.DescriptorRel)).ToList();
        if (listed.Count == 1) return listed[0];
        var pool = listed.Count > 0 ? listed : group.Mods;
        return pool.FirstOrDefault(m => m.Source == ModSource.Workshop) ?? pool[0];
    }

    /// <summary>"&lt;name&gt; (duplicate: &lt;descriptor file stem&gt;)", e.g. "UI Overhaul Dynamic (duplicate: 8cde_cf2c63b4)".</summary>
    public static string DuplicateName(string name, string descriptorRel) =>
        $"{name} (duplicate: {Path.GetFileNameWithoutExtension(descriptorRel)})";
}

using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Library;

namespace FazStellarisModmanager.Core.Lists;

public sealed record ModListEntry(string Key, string Name, string DescriptorRel, string? RemoteId);

/// <summary>A named, ordered mod list owned by the app. Mods are in load order.</summary>
public sealed record ModList(string Name, List<ModListEntry> Mods, List<string> DisabledDlcs)
{
    public DlcLoad ToDlcLoad() => new(Mods.Select(m => m.DescriptorRel).ToList(), DisabledDlcs.ToList());

    public static ModList FromDlcLoad(string name, DlcLoad load, IReadOnlyList<InstalledMod> library)
    {
        var byRel = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in library) byRel.TryAdd(m.DescriptorRel, m);

        var mods = load.EnabledMods.Select(raw =>
        {
            var rel = raw.Replace('\\', '/');
            return byRel.TryGetValue(rel, out var m)
                ? new ModListEntry(m.Key, m.Name, m.DescriptorRel, m.RemoteId)
                : new ModListEntry(ModKeys.For(rel), Path.GetFileNameWithoutExtension(rel), rel, null);
        }).ToList();

        return new ModList(name, mods, load.DisabledDlcs.ToList());
    }
}

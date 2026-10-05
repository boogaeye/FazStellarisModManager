namespace FazStellarisModmanager.Core.Lists;

/// <summary>The mod list being edited on the Mods page, shared with other pages (Conflicts). Used from the UI thread only. <see cref="Version"/> changes on every edit.</summary>
public sealed class EditedModList
{
    public string Name { get; set; } = "Current";
    public List<ModListEntry> Mods { get; private set; } = [];
    public List<string> DisabledDlcs { get; private set; } = [];
    public bool Loaded { get; private set; }
    public int Version { get; private set; }

    /// <summary>Replaces the list with a copy of <paramref name="list"/>.</summary>
    public void Set(ModList list)
    {
        Name = list.Name;
        Mods = list.Mods.ToList();
        DisabledDlcs = list.DisabledDlcs.ToList();
        Loaded = true;
        Version++;
    }

    /// <summary>Call after changing <see cref="Mods"/> in place.</summary>
    public void Touch() => Version++;

    public ModList ToModList() => new(Name.Trim(), Mods.ToList(), DisabledDlcs.ToList());
}

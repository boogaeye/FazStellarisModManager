using FazStellarisModmanager.Core.Library;

namespace FazStellarisModmanager.Core.Lists;

/// <summary>Edits on an ordered (load order) mod list.</summary>
public static class ModListEditor
{
    /// <summary>Moves the item at <paramref name="from"/> to the 1-based <paramref name="position"/> (clamped to the list). Returns its new index, or -1 when <paramref name="from"/> is out of range.</summary>
    public static int MoveTo<T>(List<T> list, int from, int position)
    {
        if (from < 0 || from >= list.Count) return -1;
        var to = Math.Clamp(position, 1, list.Count) - 1;
        Move(list, from, to);
        return to;
    }

    /// <summary>Drag and drop: the item at <paramref name="from"/> takes the place of the item at <paramref name="to"/>, landing after it when moving down and before it when moving up. Returns false (no change) for bad or equal indexes.</summary>
    public static bool Move<T>(List<T> list, int from, int to)
    {
        if (from < 0 || from >= list.Count || to < 0 || to >= list.Count || from == to) return false;
        var item = list[from];
        list.RemoveAt(from);
        list.Insert(to, item);
        return true;
    }

    /// <summary>Appends mods whose key (case-insensitive) is not in the list yet, in the given order. Returns how many were added.</summary>
    public static int AddRange(List<ModListEntry> list, IEnumerable<InstalledMod> mods)
    {
        var keys = new HashSet<string>(list.Select(e => e.Key), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var m in mods)
        {
            if (!keys.Add(m.Key)) continue;
            list.Add(new ModListEntry(m.Key, m.Name, m.DescriptorRel, m.RemoteId));
            added++;
        }
        return added;
    }
}

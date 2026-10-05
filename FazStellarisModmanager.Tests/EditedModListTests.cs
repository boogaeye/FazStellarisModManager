using FazStellarisModmanager.Core.Lists;

namespace FazStellarisModmanager.Tests;

public class EditedModListTests
{
    [Fact]
    public void Set_and_touch_bump_version_and_copy()
    {
        var list = new EditedModList();
        Assert.False(list.Loaded);
        var source = new ModList("L", [new ModListEntry("mod:a", "A", "mod/a.mod", null)], ["dlc1"]);
        list.Set(source);
        Assert.True(list.Loaded);
        Assert.Equal(1, list.Version);
        Assert.Equal("L", list.Name);
        list.Mods.Add(new ModListEntry("mod:b", "B", "mod/b.mod", null));
        Assert.Single(source.Mods);
        list.Touch();
        Assert.Equal(2, list.Version);
        var copy = list.ToModList();
        Assert.Equal(2, copy.Mods.Count);
        copy.Mods.Clear();
        Assert.Equal(2, list.Mods.Count);
        Assert.Equal(["dlc1"], copy.DisabledDlcs);
    }
}

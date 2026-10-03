using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ModListTests
{
    static InstalledMod Installed(string rel, string name) =>
        new(ModKeys.For(rel), name, rel, null, null, null, "", ModSource.Local, []);

    [Fact]
    public void FromDlcLoad_uses_library_names_and_keeps_unknown_entries()
    {
        var load = new DlcLoad(["mod/ugc_5.mod", "mod/gone.mod"], ["dlc/x.dlc"]);
        var library = new[] { Installed("mod/ugc_5.mod", "Five") };

        var list = ModList.FromDlcLoad("Current", load, library);

        Assert.Equal("Current", list.Name);
        Assert.Equal(new[] { ("ugc:5", "Five"), ("local:gone.mod", "gone") }, list.Mods.Select(m => (m.Key, m.Name)));
        Assert.Equal(new[] { "dlc/x.dlc" }, list.DisabledDlcs);
    }

    [Fact]
    public void ToDlcLoad_preserves_order()
    {
        var list = new ModList("L", [new("ugc:2", "B", "mod/ugc_2.mod", "2"), new("ugc:1", "A", "mod/ugc_1.mod", "1")], []);

        Assert.Equal(new[] { "mod/ugc_2.mod", "mod/ugc_1.mod" }, list.ToDlcLoad().EnabledMods);
    }

    [Fact]
    public void Store_saves_loads_lists_and_deletes()
    {
        using var tmp = new TempDir();
        var store = new ModListStore(Path.Combine(tmp.Path, "lists"));
        var b = new ModList("b: weird/name?", [new("ugc:1", "A", "mod/ugc_1.mod", "1")], []);
        var a = new ModList("a list", [], ["dlc/x.dlc"]);

        store.Save(b);
        store.Save(a);

        Assert.Equal(new[] { "a list", "b: weird/name?" }, store.LoadAll().Select(l => l.Name));
        var loaded = store.Load("b: weird/name?")!;
        Assert.Equal("mod/ugc_1.mod", loaded.Mods.Single().DescriptorRel);
        store.Delete("a list");
        Assert.Null(store.Load("a list"));
        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void Store_rejects_blank_name()
    {
        using var tmp = new TempDir();
        var store = new ModListStore(tmp.Path);

        Assert.Throws<ArgumentException>(() => store.Save(new ModList("  ", [], [])));
    }
}

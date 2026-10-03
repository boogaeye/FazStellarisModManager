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

    [Fact]
    public void Save_rejects_a_different_list_that_sanitizes_to_the_same_file()
    {
        using var tmp = new TempDir();
        var store = new ModListStore(tmp.Path);
        store.Save(new ModList("a/b", [], []));

        Assert.Throws<InvalidOperationException>(() => store.Save(new ModList("a?b", [], [])));
        Assert.NotNull(store.Load("a/b"));
        Assert.Null(store.Load("a?b"));
    }

    [Fact]
    public void Save_allows_case_only_rename_of_the_same_list()
    {
        using var tmp = new TempDir();
        var store = new ModListStore(tmp.Path);
        store.Save(new ModList("Foo", [], []));
        store.Save(new ModList("foo", [], []));

        Assert.Equal(new[] { "foo" }, store.LoadAll().Select(l => l.Name));
    }

    [Fact]
    public void Reserved_device_names_round_trip()
    {
        using var tmp = new TempDir();
        var store = new ModListStore(tmp.Path);
        store.Save(new ModList("CON", [], []));
        store.Save(new ModList("con.txt", [], []));

        Assert.Equal("CON", store.Load("CON")!.Name);
        Assert.Equal("con.txt", store.Load("con.txt")!.Name);
    }

    [Fact]
    public void Delete_leaves_a_different_list_with_the_same_file_name()
    {
        using var tmp = new TempDir();
        var store = new ModListStore(tmp.Path);
        store.Save(new ModList("a/b", [], []));

        store.Delete("a?b");
        Assert.NotNull(store.Load("a/b"));
        store.Delete("A/B");
        Assert.Null(store.Load("a/b"));
    }

    [Fact]
    public void Corrupt_files_are_skipped_and_corrupt_target_can_be_deleted()
    {
        using var tmp = new TempDir();
        var store = new ModListStore(tmp.Path);
        store.Save(new ModList("good", [], []));
        File.WriteAllText(Path.Combine(tmp.Path, "bad.json"), "{not json");
        File.WriteAllText(Path.Combine(tmp.Path, "empty.json"), "{}");

        Assert.Equal(new[] { "good" }, store.LoadAll().Select(l => l.Name));
        store.Delete("bad");
        Assert.False(File.Exists(Path.Combine(tmp.Path, "bad.json")));
    }

    [Fact]
    public void Missing_directory_and_missing_list_are_empty()
    {
        using var tmp = new TempDir();
        var store = new ModListStore(Path.Combine(tmp.Path, "nope"));

        Assert.Empty(store.LoadAll());
        Assert.Null(store.Load("x"));
    }

    [Fact]
    public void Round_trip_preserves_all_fields()
    {
        using var tmp = new TempDir();
        var store = new ModListStore(tmp.Path);
        store.Save(new ModList("L", [new("ugc:1", "A", "mod/ugc_1.mod", "1")], ["dlc/x.dlc"]));

        var m = store.Load("L")!;
        var mod = m.Mods.Single();
        Assert.Equal(("ugc:1", "A", "mod/ugc_1.mod", "1"), (mod.Key, mod.Name, mod.DescriptorRel, mod.RemoteId));
        Assert.Equal(new[] { "dlc/x.dlc" }, m.DisabledDlcs);
    }

    [Fact]
    public void Delete_reports_whether_a_file_was_removed()
    {
        using var tmp = new TempDir();
        var store = new ModListStore(tmp.Path);
        store.Save(new ModList("x", [], []));

        Assert.False(store.Delete("missing"));
        Assert.True(store.Delete("x"));
        Assert.False(store.Delete("x"));
    }

    [Fact]
    public void ReadFile_drops_null_and_descriptorless_mod_entries()
    {
        using var tmp = new TempDir();
        var store = new ModListStore(tmp.Path);
        File.WriteAllText(Path.Combine(tmp.Path, "l.json"),
            "{\"name\":\"L\",\"mods\":[null, {\"key\":\"ugc:1\",\"name\":\"A\",\"descriptorRel\":\"mod/ugc_1.mod\",\"remoteId\":\"1\"}, {\"key\":\"x\",\"name\":\"B\",\"descriptorRel\":null,\"remoteId\":null}],\"disabledDlcs\":[null]}");

        var list = store.Load("L")!;

        Assert.Equal("ugc:1", list.Mods.Single().Key);
        Assert.Empty(list.DisabledDlcs);
    }
}

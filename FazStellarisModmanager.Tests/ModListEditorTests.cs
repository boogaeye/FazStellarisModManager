using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;

namespace FazStellarisModmanager.Tests;

public class ModListEditorTests
{
    static List<string> L(params string[] items) => items.ToList();

    [Theory]
    [InlineData(1, 3, "a,c,b,d", 2)]   // down: lands at position 3
    [InlineData(3, 1, "d,a,b,c", 0)]   // up to the top
    [InlineData(0, 99, "b,c,d,a", 3)]  // clamped to the end
    [InlineData(2, 0, "c,a,b,d", 0)]   // clamped to the start
    [InlineData(2, 3, "a,b,c,d", 2)]   // same place
    public void MoveTo_moves_to_one_based_position(int from, int position, string expected, int newIndex)
    {
        var list = L("a", "b", "c", "d");
        Assert.Equal(newIndex, ModListEditor.MoveTo(list, from, position));
        Assert.Equal(expected, string.Join(",", list));
    }

    [Fact]
    public void MoveTo_ignores_bad_index()
    {
        var list = L("a", "b");
        Assert.Equal(-1, ModListEditor.MoveTo(list, 5, 1));
        Assert.Equal("a,b", string.Join(",", list));
    }

    [Theory]
    [InlineData(1, 3, "a,c,d,b,e", true)]  // moving down lands after the target
    [InlineData(3, 1, "a,d,b,c,e", true)]  // moving up lands before the target
    [InlineData(2, 2, "a,b,c,d,e", false)]
    [InlineData(0, 9, "a,b,c,d,e", false)]
    public void Move_takes_the_targets_place(int from, int to, string expected, bool moved)
    {
        var list = L("a", "b", "c", "d", "e");
        Assert.Equal(moved, ModListEditor.Move(list, from, to));
        Assert.Equal(expected, string.Join(",", list));
    }

    static InstalledMod Mod(string key, string name) =>
        new(key, name, "mod/" + key + ".mod", null, null, null, "", ModSource.Local, []);

    [Fact]
    public void AddRange_appends_new_mods_in_order_and_skips_duplicates()
    {
        var list = new List<ModListEntry> { new("mod:a", "A", "mod/a.mod", null) };
        var added = ModListEditor.AddRange(list, [Mod("MOD:A", "A again"), Mod("mod:c", "C"), Mod("mod:b", "B"), Mod("mod:c", "C twice")]);
        Assert.Equal(2, added);
        Assert.Equal(["mod:a", "mod:c", "mod:b"], list.Select(e => e.Key));
        Assert.Equal("mod/mod:c.mod", list[1].DescriptorRel);
    }
}

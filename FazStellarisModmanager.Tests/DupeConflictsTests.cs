using FazStellarisModmanager.Core.Library;

namespace FazStellarisModmanager.Tests;

public class DupeConflictsTests
{
    static InstalledMod Mod(string rel, string name, bool workshop = false) =>
        new(ModKeys.For(rel), name, rel, null, "1", "v4.5.*", "C:/x/" + rel, workshop ? ModSource.Workshop : ModSource.Local, []);

    static HashSet<string> Rels(params string[] rels) => new(rels, StringComparer.OrdinalIgnoreCase);

    static DupeGroup Uiod(params InstalledMod[] extra) =>
        DupeConflicts.Find([Mod("mod/ugc_1.mod", "UI Overhaul Dynamic", workshop: true), Mod("mod/8cde_a.mod", "UI Overhaul Dynamic"), .. extra]).Single();

    [Fact]
    public void Find_groups_names_ignoring_case_and_whitespace_and_skips_singletons()
    {
        var groups = DupeConflicts.Find(
        [
            Mod("mod/ugc_1.mod", "UI Overhaul Dynamic", workshop: true),
            Mod("mod/other.mod", "Other"),
            Mod("mod/8cde_a.mod", " ui overhaul dynamic "),
            Mod("mod/z.mod", "Alpha"),
            Mod("mod/Y.mod", "alpha"),
        ]);

        // Ordered by name; each name is the first member's (by descriptor), trimmed.
        Assert.Equal(["alpha", "ui overhaul dynamic"], groups.Select(g => g.Name));
        Assert.Equal(["mod/Y.mod", "mod/z.mod"], groups[0].Mods.Select(m => m.DescriptorRel));
        Assert.Equal(["mod/8cde_a.mod", "mod/ugc_1.mod"], groups[1].Mods.Select(m => m.DescriptorRel));
    }

    [Fact]
    public void Find_ignores_blank_names()
    {
        Assert.Empty(DupeConflicts.Find([Mod("mod/a.mod", ""), Mod("mod/b.mod", "  ")]));
    }

    [Fact]
    public void DefaultKeep_is_the_only_member_in_the_list_even_when_local()
    {
        var keep = DupeConflicts.DefaultKeep(Uiod(), Rels("MOD/8cde_a.mod"));
        Assert.Equal("mod/8cde_a.mod", keep.DescriptorRel);
    }

    [Fact]
    public void DefaultKeep_is_the_workshop_member_when_none_is_in_the_list()
    {
        var keep = DupeConflicts.DefaultKeep(Uiod(), Rels("mod/unrelated.mod"));
        Assert.Equal("mod/ugc_1.mod", keep.DescriptorRel);
    }

    [Fact]
    public void DefaultKeep_is_the_workshop_member_among_several_in_the_list()
    {
        var group = Uiod(Mod("mod/ugc_2.mod", "UI Overhaul Dynamic", workshop: true));
        var keep = DupeConflicts.DefaultKeep(group, Rels("mod/8cde_a.mod", "mod/ugc_2.mod"));
        Assert.Equal("mod/ugc_2.mod", keep.DescriptorRel);
    }

    [Fact]
    public void DefaultKeep_is_the_first_by_descriptor_without_workshop_members_or_list_members()
    {
        var group = DupeConflicts.Find([Mod("mod/c.mod", "Same"), Mod("mod/b.mod", "Same"), Mod("mod/d.mod", "Same")]).Single();
        var keep = DupeConflicts.DefaultKeep(group, Rels());
        Assert.Equal("mod/b.mod", keep.DescriptorRel);
    }

    [Fact]
    public void DuplicateName_appends_the_descriptor_file_stem()
    {
        Assert.Equal("UI Overhaul Dynamic (duplicate: 8cde_cf2c63b4)", DupeConflicts.DuplicateName("UI Overhaul Dynamic", "mod/8cde_cf2c63b4.mod"));
    }
}

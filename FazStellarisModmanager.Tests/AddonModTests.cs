using System.Text;
using FazStellarisModmanager.Core.Addons;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class AddonModTests
{
    static readonly string Lf = MarketFixture.Lf;

    static readonly string Descriptor = MarketFixture.LfText("""
        version="1"
        name="Faz Addons"
        """) + Lf;

    static AddonOutput Output(params (string Path, string Text)[] extra) => new(
        new (string Path, string Text)[] { ("descriptor.mod", Descriptor), ("README.txt", "") }
            .Concat(extra)
            .Select(f => (f.Path, Encoding.UTF8.GetBytes(f.Text)))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList(),
        HasChanges: extra.Length > 0);

    static string Content(TempDir user, string rel) => File.ReadAllText(Path.Combine(user.Path, "mod", "faz_addons", rel));

    [Fact]
    public void Names_are_fixed()
    {
        Assert.Equal("mod/faz_addons.mod", AddonMod.DescriptorRel);
        Assert.Equal("mod/faz_addons", AddonMod.ContentRel);
        Assert.Equal("Faz Addons", AddonMod.Name);
    }

    [Fact]
    public void Writes_the_content_folder_and_the_descriptor_with_its_path()
    {
        using var user = new TempDir();

        AddonMod.Write(user.Path, Output(("common/defines/zzzzz_faz_addons.txt", "NEconomy = { }")));

        Assert.Equal("NEconomy = { }", Content(user, "common/defines/zzzzz_faz_addons.txt"));
        Assert.Equal(Descriptor, Content(user, "descriptor.mod"));
        Assert.Equal("", Content(user, "README.txt"));
        Assert.Equal(Descriptor + "path=" + (char)34 + "mod/faz_addons" + (char)34 + Lf, File.ReadAllText(Path.Combine(user.Path, "mod", "faz_addons.mod")));
    }

    [Fact]
    public void Removes_stale_files_and_leaves_everything_outside_the_folder_alone()
    {
        using var user = new TempDir();
        var stale = user.Write("mod/faz_addons/common/strategic_resources/00_old.txt", "old");
        var outside = new[]
        {
            user.Write("mod/other.mod", "name=Other"),
            user.Write("mod/faz_addons_backup/README.txt", "keep"),
            user.Write("mod/other/common/defines/zzzzz_faz_addons.txt", "keep"),
            user.Write("faz_addons/README.txt", "keep"),
        };
        var before = outside.Select(File.ReadAllText).ToList();

        AddonMod.Write(user.Path, Output(("common/defines/zzzzz_faz_addons.txt", "x")));

        Assert.False(File.Exists(stale));
        Assert.False(Directory.Exists(Path.Combine(user.Path, "mod", "faz_addons", "common", "strategic_resources")));
        Assert.Equal(before, outside.Select(File.ReadAllText));
        Assert.Equal(["README.txt", "common/defines/zzzzz_faz_addons.txt", "descriptor.mod"],
            Directory.EnumerateFiles(Path.Combine(user.Path, "mod", "faz_addons"), "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(Path.Combine(user.Path, "mod", "faz_addons"), f).Replace((char)92, '/'))
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Unchanged_files_are_not_rewritten()
    {
        using var user = new TempDir();
        var output = Output(("common/defines/zzzzz_faz_addons.txt", "x"));
        AddonMod.Write(user.Path, output);
        var file = Path.Combine(user.Path, "mod", "faz_addons", "common", "defines", "zzzzz_faz_addons.txt");
        var descriptor = Path.Combine(user.Path, "mod", "faz_addons.mod");
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, old);
        File.SetLastWriteTimeUtc(descriptor, old);

        AddonMod.Write(user.Path, output);

        Assert.Equal(old, File.GetLastWriteTimeUtc(file));
        Assert.Equal(old, File.GetLastWriteTimeUtc(descriptor));
    }

    [Fact]
    public void Is_up_to_date_only_when_the_files_and_bytes_match()
    {
        using var user = new TempDir();
        var output = Output(("common/defines/zzzzz_faz_addons.txt", "x"));
        var folder = Path.Combine(user.Path, "mod", "faz_addons");

        Assert.False(AddonMod.IsUpToDate(user.Path, output));
        AddonMod.Write(user.Path, output);
        Assert.True(AddonMod.IsUpToDate(user.Path, output));

        File.WriteAllText(Path.Combine(folder, "README.txt"), "edited");
        Assert.False(AddonMod.IsUpToDate(user.Path, output));
        AddonMod.Write(user.Path, output);

        user.Write("mod/faz_addons/extra.txt", "extra");
        Assert.False(AddonMod.IsUpToDate(user.Path, output));
        AddonMod.Write(user.Path, output);

        File.Delete(Path.Combine(user.Path, "mod", "faz_addons.mod"));
        Assert.False(AddonMod.IsUpToDate(user.Path, output));
        AddonMod.Write(user.Path, output);

        Assert.True(AddonMod.IsUpToDate(user.Path, output));
        Assert.False(AddonMod.IsUpToDate(user.Path, Output(("common/defines/zzzzz_faz_addons.txt", "y"))));
        Assert.False(AddonMod.IsUpToDate(user.Path, Output()));
    }

    [Fact]
    public void A_path_outside_the_folder_is_refused_before_anything_is_written()
    {
        using var user = new TempDir();

        Assert.Throws<ArgumentException>(() => AddonMod.Write(user.Path, Output(("../escaped.txt", "x"))));

        Assert.False(File.Exists(Path.Combine(user.Path, "mod", "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(user.Path, "mod", "faz_addons.mod")));
    }
}

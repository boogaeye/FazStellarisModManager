using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ModLibraryTests
{
    [Theory]
    [InlineData("mod/ugc_3005307317.mod", "ugc:3005307317")]
    [InlineData("mod/UGC_42.mod", "ugc:42")]
    [InlineData("mod/EthicsFix.mod", "local:EthicsFix.mod")]
    [InlineData("mod/8cde_010c1b4b.mod", "local:8cde_010c1b4b.mod")]
    public void Keys_follow_descriptor_file_name(string rel, string expected) =>
        Assert.Equal(expected, ModKeys.For(rel));

    [Fact]
    public void WorkshopId_parses_only_ugc_keys()
    {
        Assert.Equal(42UL, ModKeys.WorkshopId("ugc:42"));
        Assert.Null(ModKeys.WorkshopId("local:x.mod"));
    }

    [Fact]
    public void Creates_missing_workshop_descriptor_once()
    {
        using var fake = new FakeInstall();

        var created = ModLibrary.EnsureWorkshopDescriptors(fake.UserDir, fake.WorkshopDir);
        var again = ModLibrary.EnsureWorkshopDescriptors(fake.UserDir, fake.WorkshopDir);

        Assert.Equal(new[] { "mod/ugc_111.mod" }, created);
        Assert.Empty(again);
        var d = ModDescriptor.Load(Path.Combine(fake.UserDir, "mod", "ugc_111.mod"));
        Assert.Equal("Workshop One", d.Name);
        Assert.Equal("111", d.RemoteFileId);
        Assert.Equal(Path.Combine(fake.WorkshopDir, "111").Replace('\\', '/'), d.Path);
        Assert.Equal(new[] { "Gameplay" }, d.Tags);
    }

    [Fact]
    public void Missing_workshop_dir_creates_nothing()
    {
        using var fake = new FakeInstall();

        Assert.Empty(ModLibrary.EnsureWorkshopDescriptors(fake.UserDir, Path.Combine(fake.Root, "nope")));
    }

    [Fact]
    public void Scan_lists_workshop_and_local_mods()
    {
        using var fake = new FakeInstall();
        ModLibrary.EnsureWorkshopDescriptors(fake.UserDir, fake.WorkshopDir);
        // Irony-style local copy that keeps the remote id: must stay local
        fake.Write("user/mod/8cde_copy.mod", "name=\"Copy\"\npath=\"mod/copy\"\nremote_file_id=\"111\"\n");

        var mods = ModLibrary.Scan(fake.UserDir);

        Assert.Equal(new[] { "local:8cde_copy.mod", "local:local.mod", "ugc:111" }, mods.Select(m => m.Key));
        var ws = mods.Single(m => m.Key == "ugc:111");
        Assert.Equal(ModSource.Workshop, ws.Source);
        Assert.Equal("mod/ugc_111.mod", ws.DescriptorRel);
        Assert.Equal(Path.Combine(fake.WorkshopDir, "111"), ws.ContentPath);
        var local = mods.Single(m => m.Key == "local:local.mod");
        Assert.Equal(ModSource.Local, local.Source);
        Assert.Equal(Path.Combine(fake.UserDir, "mod", "local"), local.ContentPath);
        Assert.Equal("111", mods.Single(m => m.Key == "local:8cde_copy.mod").RemoteId);
    }

    [Fact]
    public void Scan_skips_locked_descriptor_and_reports_it()
    {
        using var fake = new FakeInstall();
        var locked = fake.Write("user/mod/locked.mod", "name=\"Locked\"\npath=\"mod/locked\"\n");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var errors = new List<string>();

        var mods = ModLibrary.Scan(fake.UserDir, errors);

        Assert.Equal(new[] { "local:local.mod" }, mods.Select(m => m.Key));
        var error = Assert.Single(errors);
        Assert.StartsWith("mod/locked.mod:", error);
    }

    [Fact]
    public void Ensure_continues_past_locked_item_and_leaves_no_tmp_files()
    {
        using var fake = new FakeInstall();
        var inner = fake.Write("lib/steamapps/workshop/content/281990/050/descriptor.mod", "name=\"Locked\"\n");
        fake.Write("lib/steamapps/workshop/content/281990/333/descriptor.mod", "name=\"Third\"\n");
        using var hold = new FileStream(inner, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var errors = new List<string>();

        var created = ModLibrary.EnsureWorkshopDescriptors(fake.UserDir, fake.WorkshopDir, errors);

        Assert.Equal(new[] { "mod/ugc_111.mod", "mod/ugc_333.mod" }, created);
        var error = Assert.Single(errors);
        Assert.StartsWith("mod/ugc_050.mod:", error);
        Assert.Empty(Directory.GetFiles(Path.Combine(fake.UserDir, "mod"), "*.tmp"));
    }

    [Fact]
    public void Zipped_workshop_item_gets_archive_descriptor()
    {
        using var fake = new FakeInstall();
        fake.Write("lib/steamapps/workshop/content/281990/222/descriptor.mod", "name=\"Zipped\"\narchive=\"old.zip\"\n");
        fake.Write("lib/steamapps/workshop/content/281990/222/old.zip", "zip");

        ModLibrary.EnsureWorkshopDescriptors(fake.UserDir, fake.WorkshopDir);

        var d = ModDescriptor.Load(Path.Combine(fake.UserDir, "mod", "ugc_222.mod"));
        var expected = Path.GetFullPath(Path.Combine(fake.WorkshopDir, "222")).Replace('\\', '/') + "/old.zip";
        Assert.Equal(expected, d.Archive);
        Assert.Null(d.Path);
        var mod = ModLibrary.Scan(fake.UserDir).Single(m => m.Key == "ugc:222");
        Assert.Equal(Path.GetFullPath(expected), mod.ContentPath);
    }

    [Fact]
    public void EnsureWorkshopDescriptors_reports_an_unusable_mod_dir_instead_of_throwing()
    {
        using var tmp = new TempDir();
        var user = Path.Combine(tmp.Path, "user");
        var workshop = Path.Combine(tmp.Path, "ws");
        Directory.CreateDirectory(user);
        Directory.CreateDirectory(workshop);
        File.WriteAllText(Path.Combine(user, "mod"), "not a directory");
        var errors = new List<string>();

        var created = ModLibrary.EnsureWorkshopDescriptors(user, workshop, errors);

        Assert.Empty(created);
        Assert.Single(errors);
    }
}

using FazStellarisModmanager.Core.Workshop;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class FolderStampTests
{
    static readonly DateTime T0 = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    // A mod folder with 3 + 5 bytes in two files, one in a subfolder, last written at T0 and a minute later.
    static string Mod(TempDir t)
    {
        File.SetLastWriteTimeUtc(t.Write("m/descriptor.mod", "abc"), T0);
        File.SetLastWriteTimeUtc(t.Write("m/common/a.txt", "12345"), T0.AddMinutes(1));
        return Path.Combine(t.Path, "m");
    }

    [Fact]
    public void A_missing_or_unnamed_folder_has_no_stamp()
    {
        using var t = new TempDir();

        Assert.Null(FolderStamp.Of(Path.Combine(t.Path, "nope")));
        Assert.Null(FolderStamp.Of(null));
        Assert.Null(FolderStamp.Of(""));
    }

    [Fact]
    public void Counts_the_files_and_bytes_in_subfolders_too_and_takes_the_newest_write_time()
    {
        using var t = new TempDir();

        Assert.Equal(new FolderStamp(2, 8, T0.AddMinutes(1)), FolderStamp.Of(Mod(t)));
    }

    [Fact]
    public void An_unchanged_folder_stamps_the_same_twice()
    {
        using var t = new TempDir();
        var folder = Mod(t);

        Assert.Equal(FolderStamp.Of(folder), FolderStamp.Of(folder));
    }

    [Fact]
    public void New_content_a_new_write_time_or_a_new_file_each_change_the_stamp()
    {
        using var t = new TempDir();
        var folder = Mod(t);
        var descriptor = Path.Combine(folder, "descriptor.mod");
        List<FolderStamp?> stamps = [FolderStamp.Of(folder)];

        File.WriteAllText(descriptor, "abcdef"); // new content, same write time
        File.SetLastWriteTimeUtc(descriptor, T0);
        stamps.Add(FolderStamp.Of(folder));

        File.SetLastWriteTimeUtc(descriptor, T0.AddHours(1)); // only the write time
        stamps.Add(FolderStamp.Of(folder));

        File.SetLastWriteTimeUtc(t.Write("m/common/b.txt", ""), T0); // an empty, old file
        stamps.Add(FolderStamp.Of(folder));

        Assert.Equal(4, stamps.Distinct().Count());
        Assert.Equal(new FolderStamp(3, 11, T0.AddHours(1)), stamps[^1]);
    }
}

using System.IO.Compression;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ContentSourceTests
{
    static void Zip(string path, params (string Name, string Text)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open());
            w.Write(text);
        }
    }

    [Fact]
    public void Directory_source_lists_reads_and_stamps_files()
    {
        using var tmp = new TempDir();
        tmp.Write("mod/common/technology/b.txt", "B");
        tmp.Write("mod/common/technology/A.txt", "A");
        tmp.Write("mod/common/technology/sub/c.txt", "C");
        tmp.Write("mod/common/technology/notes.txtx", "no");

        using var s = ContentSource.FromPath("Mod", Path.Combine(tmp.Path, "mod"));

        Assert.Equal(new[] { "common/technology/A.txt", "common/technology/b.txt", "common/technology/sub/c.txt" }, s.Files("common/technology", ".txt"));
        Assert.Equal("B", s.ReadText("common/technology/b.txt"));
        Assert.True(s.Exists("COMMON/technology/a.txt"));
        Assert.False(s.Exists("common/technology/zzz.txt"));
        Assert.Matches(@"^1:\d+$", s.Stamp("common/technology/b.txt"));
        Assert.Empty(s.Files("common/missing", ".txt"));
        Assert.False(s.IsBaseGame);
        Assert.Equal("Mod", s.Name);
    }

    [Fact]
    public void Directory_source_rejects_paths_that_escape_the_root()
    {
        using var tmp = new TempDir();
        tmp.Write("outside.txt", "secret");
        tmp.Write("mod/a.txt", "A");
        using var s = ContentSource.FromPath("Mod", Path.Combine(tmp.Path, "mod"));

        Assert.False(s.Exists("../outside.txt"));
        Assert.Throws<UnauthorizedAccessException>(() => s.Open("../outside.txt"));
        Assert.Throws<UnauthorizedAccessException>(() => s.Stamp("../outside.txt"));
        Assert.False(s.Exists(@"C:\Windows\win.ini"));
    }

    [Fact]
    public void Zip_source_behaves_like_a_directory()
    {
        using var tmp = new TempDir();
        var zipPath = Path.Combine(tmp.Path, "old.zip");
        Zip(zipPath, ("common/technology/x.txt", "X"), ("common/technology/sub/y.txt", "Y"), ("descriptor.mod", "name=\"Old\""));

        using var s = ContentSource.FromPath("Old", zipPath);

        Assert.Equal(new[] { "common/technology/sub/y.txt", "common/technology/x.txt" }, s.Files("common/technology", ".txt"));
        Assert.Equal("X", s.ReadText("common/technology/X.TXT"));
        Assert.True(s.Exists("descriptor.mod"));
        Assert.Matches(@"^1:\d+$", s.Stamp("common/technology/x.txt"));
    }

    [Fact]
    public void Missing_content_throws_directory_not_found()
    {
        using var tmp = new TempDir();

        Assert.Throws<DirectoryNotFoundException>(() => ContentSource.FromPath("Gone", Path.Combine(tmp.Path, "nope")));
        Assert.Throws<DirectoryNotFoundException>(() => ContentSource.FromPath("Empty", ""));
    }

    [Fact]
    public void Base_game_flag_is_kept()
    {
        using var tmp = new TempDir();

        using var s = ContentSource.FromPath("Base game", tmp.Path, isBaseGame: true);

        Assert.True(s.IsBaseGame);
    }
}

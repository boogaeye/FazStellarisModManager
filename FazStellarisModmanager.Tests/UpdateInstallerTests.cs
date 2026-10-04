using System.Diagnostics;
using System.IO.Compression;
using FazStellarisModmanager.Core.Updates;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class UpdateInstallerTests
{
    sealed class Collect : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value) => Values.Add(value);
    }

    public static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in entries)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(text);
            }
        return ms.ToArray();
    }

    static UpdateInfo Info(string? url, long size) =>
        new(new Version(0, 2, 0), "v0.2.0", "Release v0.2.0", null, "https://github.com/owner/repo/releases/tag/v0.2.0", url, size);

    [Fact]
    public async Task Downloads_the_zip_and_reports_progress()
    {
        using var tmp = new TempDir();
        var bytes = new byte[200_000];
        new Random(1).NextBytes(bytes);
        var installer = new UpdateInstaller(new HttpClient(new FakeHttp(_ => FakeHttp.Bytes(bytes))));
        var progress = new Collect();

        var zip = await installer.DownloadAsync(Info("https://x/app.zip", bytes.Length), Path.Combine(tmp.Path, "work"), progress);

        Assert.Equal(bytes, File.ReadAllBytes(zip));
        Assert.Equal(1, progress.Values[^1]);
        Assert.True(progress.Values.Count > 1);
    }

    [Fact]
    public async Task A_wrong_size_or_a_missing_download_fails()
    {
        using var tmp = new TempDir();
        var installer = new UpdateInstaller(new HttpClient(new FakeHttp(_ => FakeHttp.Bytes([1, 2, 3]))));

        await Assert.ThrowsAsync<InvalidDataException>(() => installer.DownloadAsync(Info("https://x/app.zip", 4), tmp.Path));
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.DownloadAsync(Info(null, 0), tmp.Path));
    }

    [Fact]
    public void Extract_finds_the_exe_at_the_root_or_in_a_single_folder()
    {
        using var tmp = new TempDir();
        var root = Path.Combine(tmp.Path, "root.zip");
        File.WriteAllBytes(root, Zip((UpdateInstaller.ExeName, "exe"), ("lib/a.dll", "a")));
        var wrapped = Path.Combine(tmp.Path, "wrapped.zip");
        File.WriteAllBytes(wrapped, Zip(("FazApp/" + UpdateInstaller.ExeName, "exe")));
        var missing = Path.Combine(tmp.Path, "missing.zip");
        File.WriteAllBytes(missing, Zip(("readme.txt", "no exe")));

        Assert.Equal(Path.Combine(tmp.Path, "w1", "files"), UpdateInstaller.Extract(root, Path.Combine(tmp.Path, "w1")));
        Assert.Equal(Path.Combine(tmp.Path, "w2", "files", "FazApp"), UpdateInstaller.Extract(wrapped, Path.Combine(tmp.Path, "w2")));
        Assert.Throws<InvalidDataException>(() => UpdateInstaller.Extract(missing, Path.Combine(tmp.Path, "w3")));
    }

    [Fact]
    public void Developer_builds_cannot_update_themselves_but_a_writable_folder_can()
    {
        using var tmp = new TempDir();
        var dev = Directory.CreateDirectory(Path.Combine(tmp.Path, "src", "bin", "Release")).FullName;
        var app = Directory.CreateDirectory(Path.Combine(tmp.Path, "app")).FullName;

        Assert.Contains("developer", UpdateInstaller.CannotInstallReason(dev));
        Assert.Null(UpdateInstaller.CannotInstallReason(app));
        Assert.Empty(Directory.GetFiles(app));
    }

    [Fact]
    public void The_start_info_runs_the_script_hidden_with_its_arguments()
    {
        var psi = UpdateInstaller.ScriptStart(@"C:\w\apply-update.ps1", 42, @"C:\w\files", @"C:\My Apps\Faz", @"C:\My Apps\Faz\FazStellarisModmanager.exe");

        Assert.Equal("powershell.exe", psi.FileName);
        Assert.True(psi.CreateNoWindow);
        Assert.Equal(
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", @"C:\w\apply-update.ps1",
             "-ProcessId", "42", "-Source", @"C:\w\files", "-Target", @"C:\My Apps\Faz", "-Restart", @"C:\My Apps\Faz\FazStellarisModmanager.exe"],
            psi.ArgumentList);
        Assert.DoesNotContain("-Restart", UpdateInstaller.ScriptStart("s.ps1", 1, "a", "b", null).ArgumentList);
    }

    [Fact]
    public void The_script_copies_the_new_files_over_the_app_and_removes_the_source()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var tmp = new TempDir();
        var work = Path.Combine(tmp.Path, "work");
        var source = Path.Combine(work, "files");
        var target = Path.Combine(tmp.Path, "app");
        tmp.Write("work/files/" + UpdateInstaller.ExeName, "new exe");
        tmp.Write("work/files/sub/b.txt", "new b");
        tmp.Write("app/" + UpdateInstaller.ExeName, "old exe");
        tmp.Write("app/old-only.txt", "kept");
        int finishedId;
        using (var finished = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!)
        {
            finished.WaitForExit();
            finishedId = finished.Id;
        }
        var script = UpdateInstaller.WriteScript(work);

        using var run = Process.Start(UpdateInstaller.ScriptStart(script, finishedId, source, target, restartExe: null))!;
        Assert.True(run.WaitForExit(120_000));

        Assert.Equal("new exe", File.ReadAllText(Path.Combine(target, UpdateInstaller.ExeName)));
        Assert.Equal("new b", File.ReadAllText(Path.Combine(target, "sub", "b.txt")));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(target, "old-only.txt")));
        Assert.False(Directory.Exists(source));
        Assert.True(File.Exists(Path.Combine(work, "update.log")));
    }
}

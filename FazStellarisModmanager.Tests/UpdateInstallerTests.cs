using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
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

    static UpdateInfo Info(string? url, long size, string? sha256 = null) =>
        new(new Version(0, 2, 0), "v0.2.0", "Release v0.2.0", null, "https://github.com/owner/repo/releases/tag/v0.2.0", url, size, sha256);

    public static string PowerShellExe => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

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
        Assert.Equal(0, progress.Values[0]);
        Assert.Equal(1, progress.Values[^1]);
        Assert.True(progress.Values.Count > 2);
        Assert.Equal(progress.Values.Distinct().Order(), progress.Values);
        Assert.All(progress.Values, v => Assert.Equal(Math.Round(v * 100), v * 100, 6));
    }

    [Fact]
    public async Task A_matching_checksum_passes_and_a_wrong_one_fails()
    {
        using var tmp = new TempDir();
        byte[] bytes = [1, 2, 3, 4, 5];
        var installer = new UpdateInstaller(new HttpClient(new FakeHttp(_ => FakeHttp.Bytes(bytes))));
        var good = Convert.ToHexStringLower(SHA256.HashData(bytes));

        var zip = await installer.DownloadAsync(Info("https://x/app.zip", bytes.Length, good), Path.Combine(tmp.Path, "ok"));
        Assert.Equal(bytes, File.ReadAllBytes(zip));

        var bad = await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.DownloadAsync(Info("https://x/app.zip", bytes.Length, new string('0', 64)), Path.Combine(tmp.Path, "bad")));
        Assert.Equal("The download is damaged (checksum mismatch).", bad.Message);
    }

    [Fact]
    public async Task A_stalled_download_times_out_but_caller_cancellation_stays_a_cancellation()
    {
        using var tmp = new TempDir();
        var stalled = new UpdateInstaller(new HttpClient(new FakeHttp(_ => FakeHttp.Stalling([1, 2, 3]))), TimeSpan.FromMilliseconds(200));
        var watch = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => stalled.DownloadAsync(Info("https://x/app.zip", 10), Path.Combine(tmp.Path, "a")));
        Assert.Equal("The download stalled.", ex.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));

        var patient = new UpdateInstaller(new HttpClient(new FakeHttp(_ => FakeHttp.Stalling([1, 2, 3]))), TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            patient.DownloadAsync(Info("https://x/app.zip", 10), Path.Combine(tmp.Path, "b"), ct: cts.Token));
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
    public void Only_a_folder_with_the_release_marker_can_update_itself()
    {
        using var tmp = new TempDir();
        var dev = Directory.CreateDirectory(Path.Combine(tmp.Path, "src", "app")).FullName;
        var bin = Directory.CreateDirectory(Path.Combine(tmp.Path, "Games", "bin", "Faz")).FullName;
        File.WriteAllText(Path.Combine(bin, UpdateInstaller.ReleaseMarker), "0.1.0");
        var app = Directory.CreateDirectory(Path.Combine(tmp.Path, "app")).FullName;
        File.WriteAllText(Path.Combine(app, UpdateInstaller.ReleaseMarker), "0.1.0");

        Assert.Equal("This copy wasn't installed from a release zip (developer build); update it from source.",
            UpdateInstaller.CannotInstallReason(dev));
        Assert.Null(UpdateInstaller.CannotInstallReason(bin));
        Assert.Null(UpdateInstaller.CannotInstallReason(app));
        Assert.Equal([Path.Combine(app, UpdateInstaller.ReleaseMarker)], Directory.GetFiles(app));
    }

    [Fact]
    public void The_start_info_runs_the_script_hidden_with_its_arguments()
    {
        var psi = UpdateInstaller.ScriptStart(@"C:\w\apply-update.ps1", 42, @"C:\w\files\", @"C:\My Apps\Faz\", @"C:\My Apps\Faz\FazStellarisModmanager.exe");

        Assert.Equal(PowerShellExe, psi.FileName);
        Assert.True(psi.CreateNoWindow);
        Assert.Equal(
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", @"C:\w\apply-update.ps1",
             "-ProcessId", "42", "-Source", @"C:\w\files", "-Target", @"C:\My Apps\Faz", "-Restart", @"C:\My Apps\Faz\FazStellarisModmanager.exe"],
            psi.ArgumentList);
        Assert.DoesNotContain("-Restart", UpdateInstaller.ScriptStart("s.ps1", 1, "a", "b", null).ArgumentList);
        Assert.DoesNotContain("-WaitSeconds", psi.ArgumentList);
        Assert.Equal(["-WaitSeconds", "5"], UpdateInstaller.ScriptStart("s.ps1", 1, "a", "b", null, waitSeconds: 5).ArgumentList.TakeLast(2));
    }

    // Runs the start info with the target argument given a trailing separator again, to prove the script trims it too
    // (PowerShell would otherwise quote it as "C:\My App\" and robocopy would read the backslash-quote as a literal quote).
    static Process RunScript(string script, int processId, string source, string target, int waitSeconds = 60)
    {
        var psi = UpdateInstaller.ScriptStart(script, processId, source, target, restartExe: null, waitSeconds: waitSeconds);
        var i = psi.ArgumentList.IndexOf("-Target");
        psi.ArgumentList[i + 1] += Path.DirectorySeparatorChar;
        return Process.Start(psi)!;
    }

    [Fact]
    public void The_script_backs_up_copies_the_new_files_over_the_app_and_cleans_up()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var tmp = new TempDir();
        var work = Path.Combine(tmp.Path, "work");
        var source = Path.Combine(work, "files");
        var target = Path.Combine(tmp.Path, "My App");
        tmp.Write("work/files/" + UpdateInstaller.ExeName, "new exe");
        tmp.Write("work/files/sub/b.txt", "new b");
        tmp.Write("work/update.zip", "zip");
        tmp.Write("My App/" + UpdateInstaller.ExeName, "old exe");
        tmp.Write("My App/old-only.txt", "kept");
        int finishedId;
        using (var finished = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!)
        {
            finished.WaitForExit();
            finishedId = finished.Id;
        }
        var script = UpdateInstaller.WriteScript(work);

        using var run = RunScript(script, finishedId, source, target);
        Assert.True(run.WaitForExit(120_000));

        var log = File.ReadAllText(Path.Combine(work, "update.log"));
        Assert.True(File.Exists(Path.Combine(target, "sub", "b.txt")), log);
        Assert.Equal("new exe", File.ReadAllText(Path.Combine(target, UpdateInstaller.ExeName)));
        Assert.Equal("new b", File.ReadAllText(Path.Combine(target, "sub", "b.txt")));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(target, "old-only.txt")));
        Assert.False(Directory.Exists(source));
        Assert.False(Directory.Exists(Path.Combine(work, "backup")));
        Assert.False(File.Exists(Path.Combine(work, "update.zip")));
        Assert.False(File.Exists(Path.Combine(tmp.Path, "update-failed.txt")));
    }

    [Fact]
    public void The_script_changes_nothing_when_the_app_does_not_close()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var tmp = new TempDir();
        var work = Path.Combine(tmp.Path, "0.2.0-20261003120000");
        var source = Path.Combine(work, "files");
        var target = Path.Combine(tmp.Path, "My App");
        tmp.Write("0.2.0-20261003120000/files/" + UpdateInstaller.ExeName, "new exe");
        tmp.Write("My App/" + UpdateInstaller.ExeName, "old exe");
        var script = UpdateInstaller.WriteScript(work);
        using var running = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            using var run = RunScript(script, running.Id, source, target, waitSeconds: 1);
            Assert.True(run.WaitForExit(60_000));

            Assert.False(running.HasExited);
            Assert.Equal("old exe", File.ReadAllText(Path.Combine(target, UpdateInstaller.ExeName)));
            Assert.True(Directory.Exists(source));
            Assert.False(Directory.Exists(Path.Combine(work, "backup")));
            var lines = File.ReadAllLines(Path.Combine(tmp.Path, "update-failed.txt")).Where(l => l.Length > 0).ToList();
            var marker = Assert.Single(lines);
            Assert.Contains("0.2.0", marker);
            Assert.Contains(Path.Combine(work, "update.log"), marker);
        }
        finally
        {
            running.Kill(entireProcessTree: true);
        }
    }
}

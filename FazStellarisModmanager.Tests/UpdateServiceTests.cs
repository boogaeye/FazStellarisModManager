using System.ComponentModel;
using System.Diagnostics;
using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Updates;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class UpdateServiceTests
{
    sealed record Setup(UpdateService Service, FakeHttp Http, List<ProcessStartInfo> Started, string AppDir);

    static Setup Create(TempDir tmp, Func<HttpRequestMessage, HttpResponseMessage> respond, bool checkAtStartup = true,
        Action<ProcessStartInfo>? startAndExit = null) =>
        Create(tmp, new FakeHttp(respond), checkAtStartup, startAndExit);

    static Setup Create(TempDir tmp, FakeHttp http, bool checkAtStartup = true, Action<ProcessStartInfo>? startAndExit = null)
    {
        var paths = new AppPaths(Path.Combine(tmp.Path, "data"));
        SettingsStore.Save(paths.Settings, new AppSettings(CheckForUpdates: checkAtStartup));
        var started = new List<ProcessStartInfo>();
        var appDir = Directory.CreateDirectory(Path.Combine(tmp.Path, "app")).FullName;
        File.WriteAllText(Path.Combine(appDir, UpdateInstaller.ReleaseMarker), "0.1.0");
        var service = new UpdateService(new ModManagerService(paths), new HttpClient(http), new Version(0, 1, 0), appDir, startAndExit ?? started.Add,
            "owner/repo", startupDelay: TimeSpan.Zero, workRoot: WorkRoot(tmp));
        return new Setup(service, http, started, appDir);
    }

    static string WorkRoot(TempDir tmp) => Path.Combine(tmp.Path, "work");

    static string Newer(long size = 0) =>
        UpdateCheckerTests.Release("v0.2.0", ("FazStellarisModManager-win-x64.zip", "https://download.test/app.zip", size));

    [Fact]
    public void Old_settings_files_keep_update_checks_on()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("settings.json", "{ \"PlayerName\": \"Faz\" }");

        Assert.True(SettingsStore.Load(file).CheckForUpdates);
    }

    [Fact]
    public async Task The_startup_check_runs_once_and_offers_a_newer_release()
    {
        using var tmp = new TempDir();
        var s = Create(tmp, _ => FakeHttp.Json(Newer()));

        await s.Service.CheckAtStartupAsync();
        await s.Service.CheckAtStartupAsync();

        Assert.Single(s.Http.Requests);
        Assert.Equal(new Version(0, 2, 0), s.Service.Offer!.Version);
        Assert.Equal(UpdateStatus.Available, s.Service.LastCheck!.Status);
    }

    [Fact]
    public async Task The_startup_check_is_skipped_when_turned_off_and_not_now_clears_the_offer()
    {
        using var tmp = new TempDir();
        var off = Create(tmp, _ => FakeHttp.Json(Newer()), checkAtStartup: false);
        await off.Service.CheckAtStartupAsync();
        Assert.Empty(off.Http.Requests);
        Assert.Null(off.Service.Offer);

        await off.Service.CheckNowAsync();
        Assert.NotNull(off.Service.Offer);
        off.Service.Dismiss();
        Assert.Null(off.Service.Offer);
    }

    [Fact]
    public async Task Installing_downloads_unpacks_and_hands_over_to_the_script()
    {
        using var tmp = new TempDir();
        var zip = UpdateInstallerTests.Zip((UpdateInstaller.ExeName, "new exe"));
        var s = Create(tmp, r => r.RequestUri!.Host == "api.github.com" ? FakeHttp.Json(Newer(zip.Length)) : FakeHttp.Bytes(zip));
        await s.Service.CheckNowAsync();
        Assert.Null(s.Service.InstallBlocker());

        await s.Service.InstallAsync();

        var psi = Assert.Single(s.Started);
        Assert.Equal(UpdateInstallerTests.PowerShellExe, psi.FileName);
        var args = psi.ArgumentList.ToList();
        Assert.Equal(Environment.ProcessId.ToString(), args[args.IndexOf("-ProcessId") + 1]);
        Assert.Equal(s.AppDir, args[args.IndexOf("-Target") + 1]);
        var source = args[args.IndexOf("-Source") + 1];
        Assert.True(File.Exists(Path.Combine(source, UpdateInstaller.ExeName)));
        Assert.Matches(@"^0\.2\.0-[0-9]{14}$", Path.GetFileName(Path.GetDirectoryName(source)));
        Assert.Equal(WorkRoot(tmp), Path.GetDirectoryName(Path.GetDirectoryName(source)));
        Assert.Null(s.Service.InstallError);
        Assert.True(s.Service.IsInstalling);
    }

    [Fact]
    public async Task A_broken_update_reports_the_error_and_keeps_the_app_running()
    {
        using var tmp = new TempDir();
        var zip = UpdateInstallerTests.Zip(("readme.txt", "no exe"));
        var s = Create(tmp, r => r.RequestUri!.Host == "api.github.com" ? FakeHttp.Json(Newer(zip.Length)) : FakeHttp.Bytes(zip));
        await s.Service.CheckNowAsync();

        await s.Service.InstallAsync();

        Assert.Empty(s.Started);
        Assert.Contains(UpdateInstaller.ExeName, s.Service.InstallError);
        Assert.False(s.Service.IsInstalling);
        Assert.NotNull(s.Service.Offer);
    }

    [Fact]
    public async Task A_release_without_a_windows_zip_points_to_the_release_page()
    {
        using var tmp = new TempDir();
        var s = Create(tmp, _ => FakeHttp.Json(UpdateCheckerTests.Release("v0.2.0")));
        await s.Service.CheckNowAsync();

        Assert.Contains("no Windows download", s.Service.InstallBlocker());
        await s.Service.InstallAsync();
        Assert.Empty(s.Started);
    }

    [Fact]
    public async Task Cancelling_the_install_reports_it_and_keeps_the_offer()
    {
        using var tmp = new TempDir();
        var s = Create(tmp, r => r.RequestUri!.Host == "api.github.com" ? FakeHttp.Json(Newer(100)) : FakeHttp.Stalling([1, 2, 3]));
        await s.Service.CheckNowAsync();

        var install = s.Service.InstallAsync();
        Assert.True(s.Service.IsInstalling);
        s.Service.CancelInstall();
        await install.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("Update cancelled.", s.Service.InstallError);
        Assert.False(s.Service.IsInstalling);
        Assert.Empty(s.Started);
        Assert.NotNull(s.Service.Offer);
    }

    [Fact]
    public async Task Caller_cancellation_is_reported_as_cancelled_too()
    {
        using var tmp = new TempDir();
        var s = Create(tmp, r => r.RequestUri!.Host == "api.github.com" ? FakeHttp.Json(Newer(100)) : FakeHttp.Stalling([1, 2, 3]));
        await s.Service.CheckNowAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await s.Service.InstallAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("Update cancelled.", s.Service.InstallError);
        Assert.False(s.Service.IsInstalling);
    }

    [Fact]
    public async Task A_script_that_cannot_start_is_reported_and_the_app_keeps_running()
    {
        using var tmp = new TempDir();
        var zip = UpdateInstallerTests.Zip((UpdateInstaller.ExeName, "new exe"));
        var s = Create(tmp, r => r.RequestUri!.Host == "api.github.com" ? FakeHttp.Json(Newer(zip.Length)) : FakeHttp.Bytes(zip),
            startAndExit: _ => throw new Win32Exception(2, "The system cannot find the file specified."));
        await s.Service.CheckNowAsync();

        await s.Service.InstallAsync();

        Assert.Contains("The system cannot find the file specified.", s.Service.InstallError);
        Assert.False(s.Service.IsInstalling);
        Assert.Null(s.Service.DownloadProgress);
    }

    [Fact]
    public async Task Overlapping_checks_share_one_request()
    {
        using var tmp = new TempDir();
        var gate = new TaskCompletionSource();
        var http = FakeHttp.Async(async _ =>
        {
            await gate.Task;
            return FakeHttp.Json(Newer());
        });
        var s = Create(tmp, http);

        var first = s.Service.CheckNowAsync();
        var second = s.Service.CheckNowAsync();
        gate.SetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Single(http.Requests);
        Assert.Same(results[0], results[1]);
        Assert.False(s.Service.IsChecking);

        await s.Service.CheckNowAsync();
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void Startup_keeps_the_two_newest_work_folders_and_picks_up_a_failure_marker()
    {
        using var tmp = new TempDir();
        var root = WorkRoot(tmp);
        var names = new[] { "0.1.0-20260101000000", "0.1.5-20260201000000", "0.2.0-20260301000000", "0.2.0-20260401000000" };
        for (var i = 0; i < names.Length; i++)
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, names[i]));
            File.WriteAllText(Path.Combine(dir.FullName, "update.log"), "log");
            Directory.SetCreationTimeUtc(dir.FullName, new DateTime(2026, 1 + i, 1, 0, 0, 0, DateTimeKind.Utc));
        }
        File.WriteAllText(Path.Combine(root, UpdateInstaller.FailureMarker), "Update to 0.2.0-20260401000000: copying failed. Log: x");

        var s = Create(tmp, _ => FakeHttp.Json(Newer()));

        Assert.Equal("Update to 0.2.0-20260401000000: copying failed. Log: x", s.Service.PreviousFailure);
        Assert.False(File.Exists(Path.Combine(root, UpdateInstaller.FailureMarker)));
        Assert.Equal(names[2..], Directory.GetDirectories(root).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Startup_without_a_work_folder_has_no_previous_failure()
    {
        using var tmp = new TempDir();
        var s = Create(tmp, _ => FakeHttp.Json(Newer()));

        Assert.Null(s.Service.PreviousFailure);
    }
}

using System.Diagnostics;
using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Updates;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class UpdateServiceTests
{
    sealed record Setup(UpdateService Service, FakeHttp Http, List<ProcessStartInfo> Started, string AppDir);

    static Setup Create(TempDir tmp, Func<HttpRequestMessage, HttpResponseMessage> respond, bool checkAtStartup = true)
    {
        var paths = new AppPaths(Path.Combine(tmp.Path, "data"));
        SettingsStore.Save(paths.Settings, new AppSettings(CheckForUpdates: checkAtStartup));
        var http = new FakeHttp(respond);
        var started = new List<ProcessStartInfo>();
        var appDir = Directory.CreateDirectory(Path.Combine(tmp.Path, "app")).FullName;
        File.WriteAllText(Path.Combine(appDir, UpdateInstaller.ReleaseMarker), "0.1.0");
        var service = new UpdateService(new ModManagerService(paths), new HttpClient(http), new Version(0, 1, 0), appDir, started.Add,
            "owner/repo", startupDelay: TimeSpan.Zero, workRoot: Path.Combine(tmp.Path, "work"));
        return new Setup(service, http, started, appDir);
    }

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
        Assert.True(File.Exists(Path.Combine(args[args.IndexOf("-Source") + 1], UpdateInstaller.ExeName)));
        Assert.Null(s.Service.InstallError);
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
}

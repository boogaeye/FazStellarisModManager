# Auto-update from GitHub Releases Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The app checks the latest GitHub release at startup and offers to update. The user can refuse. Accepting downloads the release zip, swaps the files and restarts the app.

**Architecture:** Core gets three classes in `FazStellarisModmanager.Core/Updates`:
- `UpdateChecker`: GitHub API and version comparison.
- `UpdateInstaller`: download, extract, plus a PowerShell swap script.
- `UpdateService`: state and orchestration, with no UI types.

The app registers `UpdateService` and shows `UpdatePrompt` (a modal in `MainLayout`) and an Updates section in Settings. A tag-triggered GitHub workflow builds the self-contained release zip.

**Tech Stack:** .NET 10, C#, WPF + BlazorWebView, xUnit, GitHub Actions, PowerShell 5.1 and robocopy (both ship with Windows).

**Spec:** `docs/superpowers/specs/2026-10-03-auto-update-design.md`

**Conventions for every task:**
- **Building:** a running copy of the app may lock the normal bin folders, so build and test with `-c Release --artifacts-path <scratch>/art`. Never kill FazStellarisModmanager.exe.
- **No backslash escapes in C#:** the tools mangle `\n`, `\t` and `\u` inside C# strings. Use `(char)10` or a raw string literal (`"""…"""`) instead.
- **Branch:** `feature/auto-update` (already checked out).
- **Commits:** end every commit message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

## File structure

| File | Responsibility |
|---|---|
| Create `FazStellarisModmanager.Core/Updates/UpdateChecker.cs` | `UpdateInfo`, `UpdateStatus`, `UpdateCheckResult`, `UpdateChecker` (GitHub latest release, version parsing) |
| Create `FazStellarisModmanager.Core/Updates/UpdateInstaller.cs` | download, extract, can-install check, swap script and its start info |
| Create `FazStellarisModmanager.Core/Updates/UpdateService.cs` | startup/now checks, offer and dismiss, install orchestration, `Changed` |
| Modify `FazStellarisModmanager.Core/AppSettings.cs` | `CheckForUpdates = true` |
| Create `FazStellarisModmanager.Tests/TestUtil/FakeHttp.cs` | fake `HttpMessageHandler` |
| Create tests `UpdateCheckerTests.cs`, `UpdateInstallerTests.cs`, `UpdateServiceTests.cs` | |
| Modify `FazStellarisModmanager/AppServices.cs` | register `UpdateService` with start-and-exit |
| Create `FazStellarisModmanager/Components/UpdatePrompt.razor` | modal prompt |
| Modify `FazStellarisModmanager/MainLayout.razor` | host the prompt |
| Modify `FazStellarisModmanager/Pages/Settings.razor` | Updates section; keep `CheckForUpdates` on save |
| Modify `FazStellarisModmanager/_Imports.razor` | `@using FazStellarisModmanager.Core.Updates` |
| Modify `FazStellarisModmanager/wwwroot/css/site.css` | modal styles |
| Create `.github/workflows/release.yml` | tag → self-contained zip → GitHub Release |
| Modify `README.md` | "Releasing a new version" |

---

### Task 1: `UpdateChecker`

**Files:**
- Create: `FazStellarisModmanager.Tests/TestUtil/FakeHttp.cs`
- Create: `FazStellarisModmanager.Core/Updates/UpdateChecker.cs`
- Test: `FazStellarisModmanager.Tests/UpdateCheckerTests.cs`

- [ ] **Step 1: Create the fake HTTP handler** (`TestUtil/FakeHttp.cs`)

```csharp
using System.Net;
using System.Text;

namespace FazStellarisModmanager.Tests.TestUtil;

/// <summary>An HttpMessageHandler that answers every request with a function and records the requests.</summary>
public sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
}
```

- [ ] **Step 2: Write the failing tests** (`UpdateCheckerTests.cs`)

```csharp
using System.Net;
using System.Text.Json;
using FazStellarisModmanager.Core.Updates;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class UpdateCheckerTests
{
    public static string Release(string tag, params (string Name, string Url, long Size)[] assets) =>
        JsonSerializer.Serialize(new
        {
            tag_name = tag,
            name = "Release " + tag,
            body = "Notes",
            html_url = "https://github.com/owner/repo/releases/tag/" + tag,
            assets = assets.Select(a => new { name = a.Name, browser_download_url = a.Url, size = a.Size }),
        });

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("V2.0.0-beta+abc", "2.0.0")]
    [InlineData("v3", "3.0.0")]
    [InlineData(" v0.1.0 ", "0.1.0")]
    [InlineData("0.1.0+9f1c2d", "0.1.0")]
    public void Reads_version_tags(string tag, string expected)
    {
        Assert.True(UpdateChecker.TryParseVersion(tag, out var v));
        Assert.Equal(Version.Parse(expected), v);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("1.x")]
    public void Rejects_tags_that_are_not_versions(string tag) => Assert.False(UpdateChecker.TryParseVersion(tag, out _));

    [Fact]
    public void Parses_a_release_and_picks_the_windows_zip()
    {
        var info = UpdateChecker.Parse(Release("v0.2.0",
            ("Source.zip", "https://x/source.zip", 5),
            ("FazStellarisModManager-WIN-X64.zip", "https://x/app.zip", 123)))!;

        Assert.Equal(new Version(0, 2, 0), info.Version);
        Assert.Equal(("v0.2.0", "Release v0.2.0", "Notes"), (info.Tag, info.Name, info.Notes));
        Assert.Equal("https://github.com/owner/repo/releases/tag/v0.2.0", info.PageUrl);
        Assert.Equal(("https://x/app.zip", 123L), (info.DownloadUrl, info.Size));
    }

    [Fact]
    public void A_release_without_the_zip_has_no_download_and_bad_documents_give_null()
    {
        var info = UpdateChecker.Parse(Release("v0.2.0"))!;
        Assert.Null(info.DownloadUrl);
        Assert.Equal(0, info.Size);
        Assert.Null(UpdateChecker.Parse(Release("nightly")));
        Assert.Null(UpdateChecker.Parse("[]"));
    }

    static UpdateChecker Checker(FakeHttp http) => new(new HttpClient(http), "owner/repo");

    [Fact]
    public async Task A_newer_release_is_available_and_the_request_identifies_the_app()
    {
        var http = new FakeHttp(_ => FakeHttp.Json(Release("v0.2.0")));

        var result = await Checker(http).CheckAsync(new Version(0, 1, 0, 0));

        Assert.Equal(UpdateStatus.Available, result.Status);
        Assert.Equal(new Version(0, 2, 0), result.Update!.Version);
        Assert.Equal(new Version(0, 1, 0), result.Current);
        var request = Assert.Single(http.Requests);
        Assert.Equal("https://api.github.com/repos/owner/repo/releases/latest", request.RequestUri!.ToString());
        Assert.Contains("FazStellarisModManager", request.Headers.UserAgent.ToString());
    }

    [Theory]
    [InlineData("v0.1.0")]
    [InlineData("v0.0.9")]
    public async Task The_same_or_an_older_release_is_up_to_date(string tag)
    {
        var result = await Checker(new FakeHttp(_ => FakeHttp.Json(Release(tag)))).CheckAsync(new Version(0, 1, 0));

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task Not_found_means_no_releases_and_other_problems_fail_quietly()
    {
        var current = new Version(0, 1, 0);
        Assert.Equal(UpdateStatus.NoReleases,
            (await Checker(new FakeHttp(_ => FakeHttp.Json("{}", HttpStatusCode.NotFound))).CheckAsync(current)).Status);

        var server = await Checker(new FakeHttp(_ => FakeHttp.Json("{}", HttpStatusCode.InternalServerError))).CheckAsync(current);
        Assert.Equal(UpdateStatus.Failed, server.Status);
        Assert.Contains("500", server.Error);

        var offline = await Checker(new FakeHttp(_ => throw new HttpRequestException("offline"))).CheckAsync(current);
        Assert.Equal((UpdateStatus.Failed, "offline"), (offline.Status, offline.Error));

        Assert.Equal(UpdateStatus.Failed, (await Checker(new FakeHttp(_ => FakeHttp.Json("not json"))).CheckAsync(current)).Status);
        Assert.Equal(UpdateStatus.Failed, (await Checker(new FakeHttp(_ => FakeHttp.Json(Release("nightly")))).CheckAsync(current)).Status);
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art> --filter UpdateCheckerTests`
Expected: the build fails because the type or namespace `UpdateChecker` / `FazStellarisModmanager.Core.Updates` does not exist.

- [ ] **Step 4: Implement `Core/Updates/UpdateChecker.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FazStellarisModmanager.Core.Updates;

/// <summary>A published release. <see cref="DownloadUrl"/> is null when the release has no Windows zip.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Name, string? Notes, string PageUrl, string? DownloadUrl, long Size);

public enum UpdateStatus { UpToDate, Available, NoReleases, Failed }

public sealed record UpdateCheckResult(UpdateStatus Status, Version Current, UpdateInfo? Update = null, string? Error = null);

/// <summary>Asks GitHub for the latest release (drafts and pre-releases excluded) and compares it with the running version.</summary>
public sealed class UpdateChecker(HttpClient http, string repository = UpdateChecker.DefaultRepository)
{
    public const string DefaultRepository = "boogaeye/FazStellarisModManager";

    /// <summary>The release asset the installer uses: a self-contained Windows x64 build.</summary>
    public const string AssetSuffix = "-win-x64.zip";

    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Never throws for network or GitHub problems: those come back as <see cref="UpdateStatus.Failed"/> with a message.</summary>
    public async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken ct = default)
    {
        current = Normalize(current);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("FazStellarisModManager", current.ToString()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateCheckResult(UpdateStatus.NoReleases, current);
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(UpdateStatus.Failed, current, Error: $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            var release = Parse(await response.Content.ReadAsStringAsync(timeout.Token), repository);
            if (release is null)
                return new UpdateCheckResult(UpdateStatus.Failed, current, Error: "The latest release has no version tag like v1.2.3.");
            return release.Version > current
                ? new UpdateCheckResult(UpdateStatus.Available, current, release)
                : new UpdateCheckResult(UpdateStatus.UpToDate, current);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateStatus.Failed, current, Error: "GitHub did not answer in time.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return new UpdateCheckResult(UpdateStatus.Failed, current, Error: ex.Message);
        }
    }

    /// <summary>The release in a GitHub release JSON document, or null when it isn't an object or its tag is not a version.</summary>
    public static UpdateInfo? Parse(string json, string repository = DefaultRepository)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (Str(root, "tag_name") is not { } tag || !TryParseVersion(tag, out var version)) return null;
        string? url = null;
        long size = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var asset in assets.EnumerateArray())
                if (Str(asset, "name") is { } name && name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase)
                    && Str(asset, "browser_download_url") is { } link)
                {
                    url = link;
                    size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                    break;
                }
        return new UpdateInfo(version, tag,
            Str(root, "name") is { Length: > 0 } title ? title : tag,
            Str(root, "body"),
            Str(root, "html_url") ?? $"https://github.com/{repository}/releases/latest",
            url, size);
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>"v1.2.3", "1.2", "V2.0.0-beta+abc" read as 1.2.3, 1.2.0, 2.0.0: a leading v and any -/+ suffix are ignored, missing parts are 0.</summary>
    public static bool TryParseVersion(string text, out Version version)
    {
        version = new Version(0, 0, 0);
        var t = text.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        var cut = t.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) t = t[..cut];
        if (!t.Contains('.')) t += ".0";
        if (!Version.TryParse(t, out var parsed)) return false;
        version = Normalize(parsed);
        return true;
    }

    /// <summary>major.minor.patch only, so 0.1.0 and 0.1.0.0 compare equal.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art> --filter UpdateCheckerTests`
Expected: all pass (10 test cases from the theories and 5 facts).

- [ ] **Step 6: Commit**

```bash
git add FazStellarisModmanager.Core/Updates/UpdateChecker.cs FazStellarisModmanager.Tests/UpdateCheckerTests.cs FazStellarisModmanager.Tests/TestUtil/FakeHttp.cs
git commit -m "Updates: check the latest GitHub release against the running version"
```

---

### Task 2: `UpdateInstaller`

**Files:**
- Create: `FazStellarisModmanager.Core/Updates/UpdateInstaller.cs`
- Test: `FazStellarisModmanager.Tests/UpdateInstallerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art> --filter UpdateInstallerTests`
Expected: the build fails because `UpdateInstaller` does not exist.

- [ ] **Step 3: Implement `Core/Updates/UpdateInstaller.cs`**

```csharp
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;

namespace FazStellarisModmanager.Core.Updates;

/// <summary>Downloads and unpacks a release, then prepares a script that swaps the files once the app has exited.</summary>
public sealed class UpdateInstaller(HttpClient http)
{
    public const string ExeName = "FazStellarisModmanager.exe";

    // Waits for the app to exit, copies the new files over the old ones (files only the old version had stay),
    // restarts the app and removes the unpacked files. robocopy exit codes below 8 mean success.
    const string Script = """
        param([int]$ProcessId, [string]$Source, [string]$Target, [string]$Restart = '')
        $log = Join-Path $PSScriptRoot 'update.log'
        try { Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction Stop } catch { }
        robocopy $Source $Target /E /IS /IT /R:20 /W:1 /NP /LOG+:$log | Out-Null
        $code = $LASTEXITCODE
        Add-Content -LiteralPath $log -Value ('robocopy exit code ' + $code)
        if ($Restart -ne '') { Start-Process -FilePath $Restart -WorkingDirectory $Target }
        if ($code -lt 8) { Remove-Item -LiteralPath $Source -Recurse -Force -ErrorAction SilentlyContinue }
        """;

    /// <summary>Downloads the release zip into <paramref name="workDir"/>\update.zip; progress goes from 0 to 1. A size different from the release's fails.</summary>
    public async Task<string> DownloadAsync(UpdateInfo update, string workDir, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (update.DownloadUrl is null) throw new InvalidOperationException("This release has no Windows download.");
        Directory.CreateDirectory(workDir);
        var zip = Path.Combine(workDir, "update.zip");
        using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
        request.Headers.UserAgent.ParseAdd("FazStellarisModManager");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? update.Size;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = File.Create(zip))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report(Math.Min(1, (double)done / total));
            }
        }
        var length = new FileInfo(zip).Length;
        if (update.Size > 0 && length != update.Size)
            throw new InvalidDataException($"The download is {length:N0} bytes but the release says {update.Size:N0}.");
        return zip;
    }

    /// <summary>Unpacks into <paramref name="workDir"/>\files and returns the folder holding the exe (the zip may wrap everything in one folder).</summary>
    public static string Extract(string zipPath, string workDir)
    {
        var target = Path.Combine(workDir, "files");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, target);
        if (File.Exists(Path.Combine(target, ExeName))) return target;
        var dirs = Directory.GetDirectories(target);
        if (dirs.Length == 1 && Directory.GetFiles(target).Length == 0 && File.Exists(Path.Combine(dirs[0], ExeName))) return dirs[0];
        throw new InvalidDataException($"The update does not contain {ExeName}.");
    }

    /// <summary>Null when the app in <paramref name="appDir"/> can replace its own files; otherwise why not.</summary>
    public static string? CannotInstallReason(string appDir)
    {
        var parts = Path.GetFullPath(appDir).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase)))
            return "This is a developer build; update it from source instead.";
        try
        {
            var probe = Path.Combine(appDir, $".update-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "The app's folder can't be written to; download the new version from the release page instead.";
        }
    }

    /// <summary>Writes the swap script into <paramref name="workDir"/> and returns its path.</summary>
    public static string WriteScript(string workDir)
    {
        Directory.CreateDirectory(workDir);
        var path = Path.Combine(workDir, "apply-update.ps1");
        File.WriteAllText(path, Script);
        return path;
    }

    /// <summary>Runs the script hidden. Without <paramref name="restartExe"/> nothing is started afterwards.</summary>
    public static ProcessStartInfo ScriptStart(string scriptPath, int processId, string source, string target, string? restartExe)
    {
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", scriptPath,
                     "-ProcessId", processId.ToString(CultureInfo.InvariantCulture), "-Source", source, "-Target", target,
                 })
            psi.ArgumentList.Add(arg);
        if (restartExe is not null)
        {
            psi.ArgumentList.Add("-Restart");
            psi.ArgumentList.Add(restartExe);
        }
        return psi;
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art> --filter UpdateInstallerTests`
Expected: 6 passed. The script test takes a few seconds.

- [ ] **Step 5: Commit**

```bash
git add FazStellarisModmanager.Core/Updates/UpdateInstaller.cs FazStellarisModmanager.Tests/UpdateInstallerTests.cs
git commit -m "Updates: download, unpack and swap files with a script after exit"
```

---

### Task 3: `UpdateService` and the setting

**Files:**
- Modify: `FazStellarisModmanager.Core/AppSettings.cs`
- Create: `FazStellarisModmanager.Core/Updates/UpdateService.cs`
- Test: `FazStellarisModmanager.Tests/UpdateServiceTests.cs`

- [ ] **Step 1: Add the setting.** In `AppSettings.cs`, replace the record line with:

```csharp
/// <summary>User overrides. Null means auto-detect (game dir, user dir) or machine name (player name). CheckForUpdates defaults to on.</summary>
public sealed record AppSettings(string? GameDir = null, string? UserDir = null, string? PlayerName = null, bool CheckForUpdates = true);
```

- [ ] **Step 2: Write the failing tests** (`UpdateServiceTests.cs`)

```csharp
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
        Assert.Equal("powershell.exe", psi.FileName);
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
```

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art> --filter UpdateServiceTests`
Expected: the build fails because `UpdateService` does not exist.

- [ ] **Step 4: Implement `Core/Updates/UpdateService.cs`**

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace FazStellarisModmanager.Core.Updates;

/// <summary>
/// Startup check, the "update available" offer and installing. Holds no UI types: <see cref="Changed"/> may fire on any thread.
/// Installing ends by handing the swap script's start info to the start-and-exit callback, which must start it and close the app.
/// </summary>
public sealed class UpdateService
{
    readonly ModManagerService _manager;
    readonly UpdateChecker _checker;
    readonly UpdateInstaller _installer;
    readonly string _appDir;
    readonly string _workRoot;
    readonly Action<ProcessStartInfo> _startAndExit;
    readonly TimeSpan _startupDelay;
    int _startupChecked;
    long _lastProgressTick;
    UpdateInfo? _blockerFor;
    string? _blocker;

    public UpdateService(ModManagerService manager, HttpClient http, Version current, string appDir, Action<ProcessStartInfo> startAndExit,
        string repository = UpdateChecker.DefaultRepository, TimeSpan? startupDelay = null, string? workRoot = null)
    {
        _manager = manager;
        _checker = new UpdateChecker(http, repository);
        _installer = new UpdateInstaller(http);
        Current = UpdateChecker.Normalize(current);
        _appDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDir));
        _startAndExit = startAndExit;
        _startupDelay = startupDelay ?? TimeSpan.FromSeconds(3);
        _workRoot = workRoot ?? Path.Combine(Path.GetTempPath(), "FazStellarisModmanager-update");
    }

    public Version Current { get; }
    public UpdateCheckResult? LastCheck { get; private set; }

    /// <summary>The release the prompt offers; null when there is nothing newer or the user chose Not now.</summary>
    public UpdateInfo? Offer { get; private set; }

    public bool IsChecking { get; private set; }
    public bool IsInstalling { get; private set; }

    /// <summary>0..1 while downloading, else null.</summary>
    public double? DownloadProgress { get; private set; }

    public string? InstallError { get; private set; }
    public event Action? Changed;

    /// <summary>The app version from an assembly's informational version (a "+commit" suffix is ignored), else its assembly version.</summary>
    public static Version VersionOf(Assembly assembly)
    {
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (info is not null && UpdateChecker.TryParseVersion(info, out var version)) return version;
        return UpdateChecker.Normalize(assembly.GetName().Version ?? new Version(0, 0, 0));
    }

    /// <summary>Once per run, after a short delay, unless turned off in settings.</summary>
    public async Task CheckAtStartupAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _startupChecked, 1) == 1 || !_manager.Settings.CheckForUpdates) return;
        try { await Task.Delay(_startupDelay, ct); }
        catch (OperationCanceledException) { return; }
        await CheckNowAsync(ct);
    }

    public async Task<UpdateCheckResult> CheckNowAsync(CancellationToken ct = default)
    {
        IsChecking = true;
        Raise();
        try
        {
            var result = await _checker.CheckAsync(Current, ct);
            LastCheck = result;
            if (!IsInstalling) Offer = result.Status == UpdateStatus.Available ? result.Update : null;
            return result;
        }
        finally
        {
            IsChecking = false;
            Raise();
        }
    }

    /// <summary>"Not now": hides the offer until the next check.</summary>
    public void Dismiss()
    {
        if (IsInstalling) return;
        Offer = null;
        InstallError = null;
        Raise();
    }

    /// <summary>Null when Update now can install in place; otherwise why the release page must be used instead. Worked out once per offer.</summary>
    public string? InstallBlocker()
    {
        if (Offer is not { } offer) return null;
        if (!ReferenceEquals(offer, _blockerFor))
        {
            _blockerFor = offer;
            _blocker = offer.DownloadUrl is null ? "This release has no Windows download." : UpdateInstaller.CannotInstallReason(_appDir);
        }
        return _blocker;
    }

    /// <summary>Download, unpack, write the script, then start it and exit. Failures land in <see cref="InstallError"/> and the app keeps running.</summary>
    public async Task InstallAsync(CancellationToken ct = default)
    {
        if (Offer is not { } offer || IsInstalling || InstallBlocker() is not null) return;
        (IsInstalling, InstallError, DownloadProgress) = (true, null, 0);
        Raise();
        try
        {
            var work = Path.Combine(_workRoot, offer.Version.ToString());
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            var zip = await _installer.DownloadAsync(offer, work, new ProgressSink(this), ct);
            var files = UpdateInstaller.Extract(zip, work);
            var script = UpdateInstaller.WriteScript(work);
            _startAndExit(UpdateInstaller.ScriptStart(script, Environment.ProcessId, files, _appDir, Path.Combine(_appDir, UpdateInstaller.ExeName)));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException
                                   or InvalidOperationException or Win32Exception or OperationCanceledException)
        {
            (InstallError, IsInstalling, DownloadProgress) = (ex.Message, false, null);
            Raise();
        }
    }

    // Progress arrives for every 80 KB; tell the UI at most every 100 ms (and always at 100 %).
    sealed class ProgressSink(UpdateService owner) : IProgress<double>
    {
        public void Report(double value)
        {
            owner.DownloadProgress = value;
            var now = Environment.TickCount64;
            if (value < 1 && now - Interlocked.Read(ref owner._lastProgressTick) < 100) return;
            Interlocked.Exchange(ref owner._lastProgressTick, now);
            owner.Raise();
        }
    }

    void Raise()
    {
        if (Changed is not { } handlers) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try { handler(); }
            catch (Exception) { /* a faulty subscriber must not break updating */ }
        }
    }
}
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art>`
Expected: all pass, including the 6 new service tests.

- [ ] **Step 6: Commit**

```bash
git add FazStellarisModmanager.Core/AppSettings.cs FazStellarisModmanager.Core/Updates/UpdateService.cs FazStellarisModmanager.Tests/UpdateServiceTests.cs
git commit -m "Updates: service with startup check, offer, dismiss and install"
```

---

### Task 4: App wiring, prompt and Settings section

**Files:**
- Modify: `FazStellarisModmanager/AppServices.cs`
- Modify: `FazStellarisModmanager/_Imports.razor`
- Create: `FazStellarisModmanager/Components/UpdatePrompt.razor`
- Modify: `FazStellarisModmanager/MainLayout.razor`
- Modify: `FazStellarisModmanager/Pages/Settings.razor`
- Modify: `FazStellarisModmanager/wwwroot/css/site.css`

- [ ] **Step 1: Register the service** in `AppServices.cs`.

Add these usings at the top:

```csharp
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using FazStellarisModmanager.Core.Updates;
```

At the end of `Register` add:

```csharp
        services.AddSingleton(sp => new UpdateService(
            sp.GetRequiredService<ModManagerService>(),
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            UpdateService.VersionOf(typeof(AppServices).Assembly),
            AppContext.BaseDirectory,
            StartAndExit));
```

Then add this method to the class:

```csharp
    // Starts the update script, then closes the app so the script can replace its files.
    static void StartAndExit(ProcessStartInfo script)
    {
        Process.Start(script)?.Dispose();
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => System.Windows.Application.Current.Shutdown());
    }
```

- [ ] **Step 2: Import the namespace.** In `_Imports.razor`, add after `@using FazStellarisModmanager.Core.Technology`:

```razor
@using FazStellarisModmanager.Core.Updates
```

- [ ] **Step 3: Create `Components/UpdatePrompt.razor`**

```razor
@implements IDisposable
@inject UpdateService Updates

@if (Updates.Offer is { } offer)
{
    <div class="modal-backdrop">
        <div class="modal" role="dialog" aria-labelledby="update-title">
            <h2 id="update-title">Version @offer.Version is available</h2>
            <p class="muted">You have version @Updates.Current. @offer.Name</p>
            @if (!string.IsNullOrWhiteSpace(offer.Notes))
            {
                <pre class="release-notes">@offer.Notes</pre>
            }
            @if (Updates.IsInstalling)
            {
                <p>Downloading the update… @((int)Math.Round((Updates.DownloadProgress ?? 0) * 100))%</p>
                <progress max="1" value="@((Updates.DownloadProgress ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture))"></progress>
                <p class="muted small">The app will close and restart when the update is ready.</p>
            }
            else
            {
                @if (Updates.InstallError is { } err)
                {
                    <p class="status error">Update failed: @err</p>
                }
                @if (Updates.InstallBlocker() is { } reason)
                {
                    <p class="muted small">@reason</p>
                }
                <div class="modal-actions">
                    @if (Updates.InstallBlocker() is null)
                    {
                        <button class="primary" @onclick="() => Updates.InstallAsync()">Update now</button>
                    }
                    else
                    {
                        <a class="button primary" href="@offer.PageUrl" target="_blank">Open release page</a>
                    }
                    <button @onclick="Updates.Dismiss">Not now</button>
                </div>
            }
        </div>
    </div>
}

@code {
    protected override void OnInitialized() => Updates.Changed += OnChanged;

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender) _ = Updates.CheckAtStartupAsync();
    }

    void OnChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Updates.Changed -= OnChanged;
}
```

- [ ] **Step 4: Host the prompt in `MainLayout.razor`.** Directly after the closing `</main>` tag (still inside `<div class="shell">`), add:

```razor
    <UpdatePrompt />
```

- [ ] **Step 5: Add the Updates section to `Pages/Settings.razor`.**

5a. After `@inject ModManagerService Manager` add:

```razor
@inject UpdateService Updates
@implements IDisposable
```

5b. After the closing `}` of the `@if (message is not null) { … }` block (before `@code`), add:

```razor
<h2>Updates</h2>
<div class="form">
    <p class="status">Version @Updates.Current@UpdateStatusText</p>
    <label class="inline">
        <input type="checkbox" @bind="checkForUpdates" @bind:after="SaveUpdateSetting" />
        Check for updates at startup
    </label>
    <div><button @onclick="CheckNow" disabled="@Updates.IsChecking">@(Updates.IsChecking ? "Checking…" : "Check now")</button></div>
</div>
```

5c. In `@code`, add a field `bool checkForUpdates;` next to the other fields. In `OnInitialized`, add these lines at the end:

```csharp
        checkForUpdates = Manager.Settings.CheckForUpdates;
        Updates.Changed += OnUpdatesChanged;
```

5d. In `Save()`, replace `Manager.UpdateSettings(new AppSettings(Blank(gameDir), Blank(userDir), Blank(playerName)));` with:

```csharp
            Manager.UpdateSettings(Manager.Settings with { GameDir = Blank(gameDir), UserDir = Blank(userDir), PlayerName = Blank(playerName) });
```

5e. Add these members to `@code`:

```csharp
    string UpdateStatusText => Updates.LastCheck switch
    {
        null => "",
        { Status: UpdateStatus.UpToDate } => " is the latest.",
        { Status: UpdateStatus.Available, Update: { } u } => $" — version {u.Version} is available.",
        { Status: UpdateStatus.NoReleases } => " — no releases have been published yet.",
        { Error: var e } => $" — the last check failed: {e}",
    };

    void SaveUpdateSetting()
    {
        try
        {
            Manager.UpdateSettings(Manager.Settings with { CheckForUpdates = checkForUpdates });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            (error, message) = (true, ex.Message);
        }
    }

    Task CheckNow() => Updates.CheckNowAsync();

    void OnUpdatesChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Updates.Changed -= OnUpdatesChanged;
```

- [ ] **Step 6: Append the styles to `wwwroot/css/site.css`**

```css
.modal-backdrop { position: fixed; inset: 0; background: #000a; display: flex; align-items: center; justify-content: center; z-index: 100; }
.modal { background: var(--panel); border: 1px solid var(--border); border-radius: 10px; padding: 1.2rem; width: min(560px, 92vw); max-height: 80vh; display: flex; flex-direction: column; gap: .6rem; }
.modal h2 { margin: 0; }
.modal progress { width: 100%; }
.release-notes { white-space: pre-wrap; overflow: auto; max-height: 40vh; background: var(--bg); border: 1px solid var(--border); border-radius: 6px; padding: .6rem; margin: 0; font-family: inherit; }
.modal-actions { display: flex; gap: .5rem; align-items: center; justify-content: flex-end; flex-wrap: wrap; }
a.button { display: inline-block; background: var(--panel-2); color: var(--text); border: 1px solid var(--border); border-radius: 6px; padding: .4rem .8rem; text-decoration: none; }
a.button.primary { background: var(--accent); border-color: var(--accent); color: #fff; }
```

- [ ] **Step 7: Build and test**

Run: `dotnet build FazStellarisModmanager.sln -c Release --artifacts-path <art>` and then `dotnet test FazStellarisModmanager.Tests -c Release --artifacts-path <art>`.
Expected: 0 errors, no new warnings, and all tests pass.

- [ ] **Step 8: Commit**

```bash
git add FazStellarisModmanager/AppServices.cs FazStellarisModmanager/_Imports.razor FazStellarisModmanager/Components/UpdatePrompt.razor FazStellarisModmanager/MainLayout.razor FazStellarisModmanager/Pages/Settings.razor FazStellarisModmanager/wwwroot/css/site.css
git commit -m "Updates: startup prompt with Update now / Not now, Updates section in Settings"
```

---

### Task 5: Release workflow and README

**Files:**
- Create: `.github/workflows/release.yml`
- Modify: `README.md`

- [ ] **Step 1: Create `.github/workflows/release.yml`**

```yaml
name: Release

on:
  push:
    tags: ['v*']

permissions:
  contents: write

jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Version from tag
        id: version
        shell: bash
        run: echo "version=${GITHUB_REF_NAME#v}" >> "$GITHUB_OUTPUT"

      - name: Test
        run: dotnet test FazStellarisModmanager.Tests -c Release

      - name: Publish (self-contained, win-x64)
        run: dotnet publish FazStellarisModmanager/FazStellarisModmanager.csproj -c Release -r win-x64 --self-contained true -p:Version=${{ steps.version.outputs.version }} -o publish

      - name: Zip
        shell: pwsh
        run: Compress-Archive -Path publish/* -DestinationPath FazStellarisModManager-win-x64.zip

      - uses: softprops/action-gh-release@v2
        with:
          files: FazStellarisModManager-win-x64.zip
          generate_release_notes: true
```

- [ ] **Step 2: Add a section to `README.md`**, before "## Project layout":

````markdown
## Installing

Download `FazStellarisModManager-win-x64.zip` from the [latest release](https://github.com/boogaeye/FazStellarisModManager/releases/latest). Unzip it into a folder you own, such as `Documents\FazStellarisModManager`; avoid `Program Files`, where the app can't update itself. Then run `FazStellarisModmanager.exe`. No .NET install is needed.

At startup the app checks for a newer release and asks before updating; you can say **Not now**. Turn the check off, or check by hand, in **Settings → Updates**.

## Releasing a new version

Tag the commit with the new version and push the tag:

```bash
git tag v0.2.0
git push origin v0.2.0
```

The **Release** workflow tests the code, builds a self-contained Windows x64 app stamped with that version, and publishes it as a GitHub Release with `FazStellarisModManager-win-x64.zip` attached. Installed copies offer the update the next time they start.
````

- [ ] **Step 3: Validate the YAML**

Run: `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/release.yml')); print('ok')"`. If PyYAML isn't installed, check the indentation by eye against Step 1 instead.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/release.yml README.md
git commit -m "Release workflow: tag v* builds a self-contained zip and publishes a GitHub Release"
```

---

### Task 6: Manual verification (after merging and pushing)

- [ ] **Step 1: Publish the first release.** Push tag `v0.1.0`. Check that the Release run succeeds and that the release has `FazStellarisModManager-win-x64.zip` attached.
- [ ] **Step 2: Install it.** Unzip into a folder outside `bin`, run it, and open Settings: it should say "Version 0.1.0 is the latest." No prompt appears.
- [ ] **Step 3: Release an update.** Push tag `v0.1.1`. Start the installed 0.1.0: within a few seconds the prompt shows "Version 0.1.1 is available". **Not now** closes it.
- [ ] **Step 4: Install the update.** Restart the app, then click **Update now**. The download progress shows, the app closes and restarts, and Settings shows version 0.1.1. `%TEMP%\FazStellarisModmanager-update\0.1.1\update.log` shows a robocopy exit code below 8.

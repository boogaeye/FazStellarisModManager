using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace FazStellarisModmanager.Core.Updates;

/// <summary>Downloads and unpacks a release, then prepares a script that swaps the files once the app has exited.</summary>
/// <param name="http">Used for the download; its own timeout should be infinite, the stall timeout guards the download instead.</param>
/// <param name="stallTimeout">The download fails when no bytes arrive for this long (default 30 s).</param>
public sealed class UpdateInstaller(HttpClient http, TimeSpan? stallTimeout = null)
{
    public const string ExeName = "FazStellarisModmanager.exe";

    /// <summary>The release workflow puts this file next to the exe; only such copies update themselves.</summary>
    public const string ReleaseMarker = "FazStellarisModmanager.release";

    /// <summary>Written by the script into the parent of its work folder (the updater's work root) when an update fails.</summary>
    public const string FailureMarker = "update-failed.txt";

    readonly TimeSpan _stall = stallTimeout ?? TimeSpan.FromSeconds(30);

    // Must run on Windows PowerShell 5.1. Steps:
    // 1. Wait for the app to exit (twice $WaitSeconds at most). If it, or another copy running from the app folder, is still
    //    running, write the failure marker and stop without touching anything or restarting.
    // 2. Back up the app folder (without the WebView2 cache) into backup\. If that fails, nothing is changed.
    // 3. Copy the new files over the old ones (files only the old version had stay). If that fails, restore the backup.
    // 4. Restart the app (new or restored version). On success remove the unpacked files, the backup and the zip.
    // robocopy exit codes below 8 mean success. Paths lose a trailing backslash (except drive roots like C:\), because
    // PowerShell would quote "C:\My App\" and robocopy would read the backslash-quote as a literal quote.
    const string Script = """
        param([int]$ProcessId, [string]$Source, [string]$Target, [string]$Restart = '', [int]$WaitSeconds = 60)

        function Trim-Dir([string]$Path) {
            if ($Path -match '^[A-Za-z]:\\$') { return $Path }
            return $Path.TrimEnd('\')
        }
        $Source = Trim-Dir $Source
        $Target = Trim-Dir $Target
        $log = Join-Path $PSScriptRoot 'update.log'
        $backup = Join-Path $PSScriptRoot 'backup'
        $failed = Join-Path (Split-Path -Parent $PSScriptRoot) 'update-failed.txt'
        $version = Split-Path -Leaf $PSScriptRoot

        function Write-Log([string]$Text) {
            Add-Content -LiteralPath $log -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $Text)
        }
        function Write-Failure([string]$Reason) {
            Write-Log ('FAILED: ' + $Reason)
            Set-Content -LiteralPath $failed -Encoding UTF8 -Value ('Update to ' + $version + ': ' + $Reason + ' Log: ' + $log)
        }
        function Start-App {
            if ($Restart -ne '') { Start-Process -FilePath $Restart -WorkingDirectory $Target }
        }

        try { Wait-Process -Id $ProcessId -Timeout $WaitSeconds -ErrorAction Stop } catch { }
        if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
            Write-Log ('process ' + $ProcessId + ' is still running; waiting up to ' + $WaitSeconds + ' s more')
            try { Wait-Process -Id $ProcessId -Timeout $WaitSeconds -ErrorAction Stop } catch { }
            if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
                Write-Failure 'the app did not close, so nothing was changed.'
                exit 1
            }
        }

        # Another copy running from the same folder would keep its files locked and make every copy retry; stop early instead.
        $prefix = $Target + '\'
        $others = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
            try { $_.Path -and $_.Path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) } catch { $false }
        })
        if ($others.Count -gt 0) {
            Write-Failure ('another copy of the app is still running from ' + $Target + ', so nothing was changed. Close it and update again.')
            exit 1
        }

        robocopy $Target $backup /E /XD '*.WebView2' /R:2 /W:1 /NP "/LOG+:$log" | Out-Null
        $code = $LASTEXITCODE
        Write-Log ('backup robocopy exit code ' + $code)
        if ($code -ge 8) {
            Write-Failure 'the current version could not be backed up, so nothing was changed.'
            Start-App
            exit 1
        }

        robocopy $Source $Target /E /IS /IT /R:3 /W:1 /NP "/LOG+:$log" | Out-Null
        $code = $LASTEXITCODE
        Write-Log ('copy robocopy exit code ' + $code)
        if ($code -ge 8) {
            robocopy $backup $Target /E /IS /IT /R:3 /W:1 /NP "/LOG+:$log" | Out-Null
            $restore = $LASTEXITCODE
            Write-Log ('restore robocopy exit code ' + $restore)
            if ($restore -ge 8) {
                Write-Failure ('copying the new files failed and restoring the previous version also failed; the backup is kept in ' + $backup + '.')
            } else {
                Write-Failure 'copying the new files failed; the previous version was restored.'
            }
            Start-App
            exit 1
        }

        Start-App
        Remove-Item -LiteralPath $Source, $backup, (Join-Path $PSScriptRoot 'update.zip') -Recurse -Force -ErrorAction SilentlyContinue
        """;

    /// <summary>
    /// Downloads the release zip into <paramref name="workDir"/>\update.zip; progress goes from 0 to 1 in whole percents.
    /// A size or SHA-256 different from the release's fails with <see cref="InvalidDataException"/>; no data for the stall
    /// timeout fails with <see cref="TimeoutException"/>.
    /// </summary>
    public async Task<string> DownloadAsync(UpdateInfo update, string workDir, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (update.DownloadUrl is null) throw new InvalidOperationException("This release has no Windows download.");
        Directory.CreateDirectory(workDir);
        var zip = Path.Combine(workDir, "update.zip");
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(_stall);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
            request.Headers.UserAgent.ParseAdd("FazStellarisModManager");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? update.Size;
            progress?.Report(0);
            await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
            await using var target = File.Create(zip);
            var buffer = new byte[81920];
            long done = 0;
            var percent = 0;
            int read;
            stall.CancelAfter(_stall);
            while ((read = await source.ReadAsync(buffer, stall.Token)) > 0)
            {
                stall.CancelAfter(_stall);
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total <= 0) continue;
                var now = (int)Math.Min(100, done * 100 / total);
                if (now == percent) continue;
                percent = now;
                progress?.Report(now / 100.0);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("The download stalled.");
        }
        var length = new FileInfo(zip).Length;
        if (update.Size > 0 && length != update.Size)
            throw new InvalidDataException($"The download is {length:N0} bytes but the release says {update.Size:N0}.");
        if (update.Sha256 is { } expected)
        {
            await using var file = File.OpenRead(zip);
            var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The download is damaged (checksum mismatch).");
        }
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
        if (!File.Exists(Path.Combine(appDir, ReleaseMarker)))
            return "This copy wasn't installed from a release zip (developer build); update it from source.";
        if (ScriptsBlockedByPolicy())
            return "PowerShell scripts are blocked by policy on this PC; download the new version from the release page.";
        var probe = Path.Combine(appDir, $".update-probe-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "The app's folder can't be written to; download the new version from the release page instead.";
        }
        try { File.Delete(probe); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the write worked, which is what counts */ }
        return null;
    }

    // Group Policy "Turn on Script Execution": EnableScripts = 0 turns scripts off, and the Restricted or AllSigned
    // policies stop our unsigned script even with -ExecutionPolicy Bypass (policy settings win over the command line).
    static bool ScriptsBlockedByPolicy()
    {
        if (!OperatingSystem.IsWindows()) return false;
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\PowerShell");
                if (key is null) continue;
                if (key.GetValue("EnableScripts") is int enabled && enabled == 0) return true;
                if (key.GetValue("ExecutionPolicy") is string policy
                    && (policy.Equals("Restricted", StringComparison.OrdinalIgnoreCase) || policy.Equals("AllSigned", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                // Unreadable policy: assume scripts may run, as they do on almost every PC.
            }
        }
        return false;
    }

    /// <summary>Writes the swap script into <paramref name="workDir"/> and returns its path.</summary>
    public static string WriteScript(string workDir)
    {
        Directory.CreateDirectory(workDir);
        var path = Path.Combine(workDir, "apply-update.ps1");
        File.WriteAllText(path, Script);
        return path;
    }

    /// <summary>
    /// Runs the script hidden with Windows PowerShell. Without <paramref name="restartExe"/> nothing is started afterwards.
    /// <paramref name="waitSeconds"/> overrides how long the script waits for the app to exit (60 s, twice).
    /// </summary>
    public static ProcessStartInfo ScriptStart(string scriptPath, int processId, string source, string target, string? restartExe, int? waitSeconds = null)
    {
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var psi = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", scriptPath,
                     "-ProcessId", processId.ToString(CultureInfo.InvariantCulture),
                     "-Source", Path.TrimEndingDirectorySeparator(source), "-Target", Path.TrimEndingDirectorySeparator(target),
                 })
            psi.ArgumentList.Add(arg);
        if (restartExe is not null)
        {
            psi.ArgumentList.Add("-Restart");
            psi.ArgumentList.Add(restartExe);
        }
        if (waitSeconds is { } wait)
        {
            psi.ArgumentList.Add("-WaitSeconds");
            psi.ArgumentList.Add(wait.ToString(CultureInfo.InvariantCulture));
        }
        return psi;
    }
}

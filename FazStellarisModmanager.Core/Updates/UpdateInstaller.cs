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

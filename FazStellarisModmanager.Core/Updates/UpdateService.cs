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

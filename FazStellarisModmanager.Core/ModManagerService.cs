using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Paths;

namespace FazStellarisModmanager.Core;

public sealed record ResolvedPaths(string UserDir, string? GameDir, string? WorkshopDir);

/// <summary>Stateful facade the UI talks to: settings, installed-mod library, saved lists, apply and launch. Mutating calls are serialized; property reads return the last published state.</summary>
public sealed class ModManagerService
{
    readonly Lock _gate = new();
    readonly AppPaths _paths;
    readonly Func<string?, string?> _findGameDir;
    HashSet<string> _installed = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="findGameDir">Maps the configured game dir (or null) to a valid install; defaults to <see cref="GameLocator.FindGameDir"/>.</param>
    public ModManagerService(AppPaths paths, Func<string?, string?>? findGameDir = null)
    {
        _paths = paths;
        _findGameDir = findGameDir ?? (dir => GameLocator.FindGameDir(dir));
        Settings = SettingsStore.Load(paths.Settings);
        Lists = new ModListStore(paths.Lists);
    }

    public AppPaths Paths => _paths;
    public AppSettings Settings { get; private set; }
    public ModListStore Lists { get; }
    public IReadOnlyList<InstalledMod> Library { get; private set; } = [];

    /// <summary>Problems (unreadable descriptors, etc.) from the last <see cref="RefreshLibrary"/>.</summary>
    public IReadOnlyList<string> LibraryErrors { get; private set; } = [];

    public ResolvedPaths Resolve()
    {
        var user = string.IsNullOrWhiteSpace(Settings.UserDir) ? GameLocator.DefaultUserDir() : Settings.UserDir;
        var game = _findGameDir(Settings.GameDir);
        return new ResolvedPaths(user, game, game is null ? null : GameLocator.WorkshopDirFor(game));
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_gate)
        {
            SettingsStore.Save(_paths.Settings, settings);
            Settings = settings;
        }
    }

    /// <summary>Creates missing mod/ugc_&lt;id&gt;.mod descriptors for downloaded Workshop items, then rescans mod/*.mod.</summary>
    public IReadOnlyList<InstalledMod> RefreshLibrary()
    {
        lock (_gate)
        {
            var p = Resolve();
            var errors = new List<string>();
            if (p.WorkshopDir is not null) ModLibrary.EnsureWorkshopDescriptors(p.UserDir, p.WorkshopDir, errors);
            var lib = ModLibrary.Scan(p.UserDir, errors);
            var installed = new HashSet<string>(lib.Select(m => m.DescriptorRel), StringComparer.OrdinalIgnoreCase);
            Library = lib;
            LibraryErrors = errors;
            _installed = installed;
            return lib;
        }
    }

    /// <summary><see cref="RefreshLibrary"/> on a background thread. UI code should prefer it so the window doesn't freeze on large mod folders.</summary>
    public Task<IReadOnlyList<InstalledMod>> RefreshLibraryAsync() => Task.Run(RefreshLibrary);

    public bool IsInstalled(ModListEntry entry) => _installed.Contains(entry.DescriptorRel);

    /// <summary>The mods currently enabled in dlc_load.json, as a list. Uses names from the last <see cref="RefreshLibrary"/>, so call that first.</summary>
    /// <exception cref="InvalidDataException">dlc_load.json is corrupt (this is not an <see cref="IOException"/>).</exception>
    public ModList ImportCurrent(string name) => ModList.FromDlcLoad(name, DlcLoadFile.Read(Resolve().UserDir), Library);

    /// <summary>Writes the list to dlc_load.json after backing up the old file. Returns the backup path (null if there was none).</summary>
    public string? Apply(ModList list)
    {
        lock (_gate) return DlcLoadFile.Write(Resolve().UserDir, list.ToDlcLoad(), _paths.Backups);
    }

    /// <summary>When set and returning a reason, <see cref="Launch"/> refuses with that reason (e.g. while Workshop downloads run).</summary>
    public Func<string?>? LaunchBlockedReason { get; set; }

    public void Launch()
    {
        if (LaunchBlockedReason?.Invoke() is { } reason) throw new InvalidOperationException(reason);
        lock (_gate)
        {
            var game = Resolve().GameDir
                ?? throw new InvalidOperationException("Stellaris install not found. Set the game folder in Settings.");
            GameLauncher.Launch(game);
        }
    }
}

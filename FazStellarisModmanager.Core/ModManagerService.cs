using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Paths;

namespace FazStellarisModmanager.Core;

public sealed record ResolvedPaths(string UserDir, string? GameDir, string? WorkshopDir);

/// <summary>What Apply did: the dlc_load.json backup, whether the launcher playset was set, and a launcher problem to show (null if none).</summary>
public sealed record ApplyResult(string? BackupPath, bool LauncherPlaysetSet, string? LauncherWarning);

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

    /// <summary>The user folder's "save games" folder, or null when the paths can't be resolved.</summary>
    public string? SaveGamesDir()
    {
        try { return Path.Combine(Resolve().UserDir, "save games"); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException) { return null; }
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

    /// <summary>Checked by <see cref="Apply"/>: while the Paradox launcher runs it would overwrite dlc_load.json from its own playset.</summary>
    public Func<bool> IsLauncherRunning { get; set; } = LauncherPlaysetFile.IsLauncherRunning;

    /// <summary>
    /// Writes the list to dlc_load.json (after backing up the old file), then makes it the Paradox launcher's active playset,
    /// so opening the launcher keeps it. Refuses while the launcher runs. Launcher database problems are returned as a warning.
    /// </summary>
    public ApplyResult Apply(ModList list)
    {
        if (IsLauncherRunning()) throw new InvalidOperationException("Close the Paradox launcher first, or it will overwrite your mod list.");
        lock (_gate)
        {
            var user = Resolve().UserDir;
            var load = list.ToDlcLoad();
            var backup = DlcLoadFile.Write(user, load, _paths.Backups);
            var sync = LauncherPlaysetFile.Write(user, load.EnabledMods, _paths.Backups, DateTimeOffset.UtcNow);
            var warnings = new List<string>();
            if (sync.Warning is { } w) warnings.Add(w);
            if (sync.Skipped.Count > 0) warnings.Add("Not added to the launcher playset (descriptor missing): " + string.Join(", ", sync.Skipped) + ".");
            return new ApplyResult(backup, sync.Written, warnings.Count == 0 ? null : string.Join(" ", warnings));
        }
    }

    /// <summary>Dupe groups (clashing descriptor names) with at least one member in the list. Uses the last <see cref="RefreshLibrary"/>.</summary>
    public IReadOnlyList<DupeGroup> DupesIn(ModList list)
    {
        var rels = RelsOf(list);
        return DupeConflicts.Find(Library).Where(g => g.Mods.Any(m => rels.Contains(m.DescriptorRel))).ToList();
    }

    /// <summary>
    /// Renames the unkept members of each group (descriptor + launcher database), then rescans. Refuses while the launcher runs.
    /// Launcher database problems are returned as <see cref="DupeResolution.LauncherWarning"/>.
    /// </summary>
    public DupeResolution ResolveDupes(IReadOnlyList<DupeChoice> choices)
    {
        if (IsLauncherRunning()) throw new InvalidOperationException("Close the Paradox launcher first, or it will overwrite your mod list.");
        try
        {
            lock (_gate) return DupeResolver.Resolve(Resolve().UserDir, choices, _paths.Backups, DateTimeOffset.UtcNow);
        }
        finally
        {
            RefreshLibrary(); // also after a failure part-way, so the shown names match the files
        }
    }

    /// <summary><see cref="ResolveDupes"/> for every group touching the list, keeping each group's default copy.</summary>
    public DupeResolution ResolveDupesWithDefaults(ModList list)
    {
        var rels = RelsOf(list);
        var choices = DupesIn(list).Select(g => new DupeChoice(g, DupeConflicts.DefaultKeep(g, rels))).ToList();
        return choices.Count == 0 ? new DupeResolution([], null, null) : ResolveDupes(choices);
    }

    static HashSet<string> RelsOf(ModList list) =>
        new(list.Mods.Select(m => m.DescriptorRel.Replace((char)92, '/')), StringComparer.OrdinalIgnoreCase);

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

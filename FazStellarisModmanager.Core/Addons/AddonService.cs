using System.Globalization;
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Addons;

/// <summary>
/// UI facade for the Addons tab: this PC's addon settings (addons.json) and the market and tech catalogs of a mod list, built together with
/// names and icons off the UI thread (one build at a time). The catalogs are cached for the list's mods (descriptors in order), the paths and
/// a version that <see cref="InvalidateCatalog"/> bumps. <see cref="Changed"/> may fire on any thread.
/// </summary>
public sealed class AddonService
{
    readonly ModManagerService _manager;
    readonly IconCache _icons;
    readonly SemaphoreSlim _build = new(1, 1);
    readonly Lock _gate = new();
    volatile AddonSettings _settings;
    volatile MarketCatalog? _catalog;
    volatile TechAddonCatalog? _techCatalog;
    volatile string? _catalogStamp;
    IReadOnlyList<ContentSource>? _sources; // the tech catalog loads its icons from these; disposed when the next build replaces it
    volatile string? _gameVersion;
    volatile string? _progress;
    long _version;
    long _lastProgress;

    public AddonService(ModManagerService manager)
    {
        _manager = manager;
        _icons = new IconCache(manager.Paths.Icons); // shared with the tech tree, which prunes it
        _settings = AddonSettingsStore.Load(manager.Paths.Addons);
    }

    /// <summary>This PC's saved settings; never overwritten by a session's host.</summary>
    public AddonSettings Settings => _settings;

    /// <summary>The last built catalog (with icons), or null before the first build.</summary>
    public MarketCatalog? Catalog => _catalog;

    /// <summary>The tech catalog built with <see cref="Catalog"/> (names, this PC's modifier list, ethic icons, tech icons on request), or null before the first build.</summary>
    public TechAddonCatalog? TechCatalog => _techCatalog;

    public bool IsBuilding => _build.CurrentCount == 0;

    /// <summary>What the build is doing (e.g. "Reading resources…"); null when idle.</summary>
    public string? Progress => _progress;

    public event Action? Changed;

    /// <summary>The settings Apply builds Faz Addons from: the host's while connected to one as a player (null when the host has addons off), else <see cref="Settings"/>.</summary>
    public AddonSettings? EffectiveFor(SessionService session) => session.Role == SessionRole.Client ? session.HostAddons : Settings;

    /// <summary>Saves the settings, first dropping the overrides that equal the game's values in <see cref="Catalog"/> (when built), and raises <see cref="Changed"/>.</summary>
    public void Save(AddonSettings settings)
    {
        lock (_gate)
        {
            var normalized = _catalog is { } catalog ? settings.Normalize(catalog) : settings;
            AddonSettingsStore.Save(_manager.Paths.Addons, normalized);
            _settings = normalized;
        }
        Raise();
    }

    /// <summary>Makes the next <see cref="BuildCatalogAsync"/> read the files again (call it after the mod library was rescanned or mods changed).</summary>
    public void InvalidateCatalog()
    {
        Interlocked.Increment(ref _version);
        Raise();
    }

    /// <summary>
    /// The catalog of the game plus the list's mods in load order, with resource names and icons; the cached one when nothing changed.
    /// <see cref="TechCatalog"/> is built with it. Waits for a running build. On failure (e.g. no game folder:
    /// <see cref="InvalidOperationException"/>) the previous catalogs stay.
    /// </summary>
    public async Task<MarketCatalog> BuildCatalogAsync(ModList list, CancellationToken ct = default)
    {
        // As Apply builds it: Faz Addons itself is never a source.
        var mods = list with { Mods = list.Mods.Where(m => !AddonMod.IsAddon(m.DescriptorRel)).ToList() };
        if (Cached(mods) is { } cached) return cached;
        await _build.WaitAsync(ct);
        try
        {
            if (Cached(mods) is { } built) return built;
            Report("Reading resources…", force: true);
            var stamp = Stamp(mods); // taken first, so an invalidation during the build makes the next call read again
            var (catalog, techs, sources, gameVersion) = await Task.Run(() => BuildCore(mods, ct), ct);
            var old = _sources;
            _sources = sources;
            _gameVersion = gameVersion;
            _techCatalog = techs;
            _catalog = catalog;
            _catalogStamp = stamp;
            if (old is not null)
                foreach (var s in old) s.Dispose(); // the replaced tech catalog's icons not loaded yet come back null
            return catalog;
        }
        finally
        {
            _build.Release();
            Report(null, force: true);
        }
    }

    /// <summary>The files Apply would write with these settings for the list of <see cref="Catalog"/>; null before the first build.</summary>
    public AddonOutput? Preview(AddonSettings settings) => _catalog is { } catalog ? AddonBuilder.Build(catalog, settings, _gameVersion) : null;

    MarketCatalog? Cached(ModList list) => _catalog is { } catalog && _catalogStamp == Stamp(list) ? catalog : null;

    // The invalidation version, the folders read and the list's descriptors in order.
    string Stamp(ModList list)
    {
        var paths = _manager.Resolve();
        var parts = new List<string> { Interlocked.Read(ref _version).ToString(CultureInfo.InvariantCulture), paths.GameDir ?? "", paths.UserDir };
        parts.AddRange(list.Mods.Select(m => m.DescriptorRel.Replace((char)92, '/').ToLowerInvariant()));
        return string.Join("|", parts);
    }

    // The sources stay open for the tech catalog's icons; on failure they are disposed here.
    (MarketCatalog Catalog, TechAddonCatalog Techs, IReadOnlyList<ContentSource> Sources, string? GameVersion) BuildCore(ModList list, CancellationToken ct)
    {
        var paths = _manager.Resolve();
        var gameDir = paths.GameDir ?? throw new InvalidOperationException("Stellaris install not found. Set the game folder in Settings.");
        var warnings = new List<string>();
        var sources = TechSources.Build(gameDir, paths.UserDir, list, warnings);
        try
        {
            ct.ThrowIfCancellationRequested();
            Report("Reading resource names…", force: false);
            var loc = Localisation.Load(sources, warnings);
            var catalog = MarketCatalog.Build(sources, loc, warnings, ct);
            Report("Reading technologies…", force: false);
            var techs = TechAddonCatalog.Build(sources, loc, TechAddonCatalog.ModifiersLogPath(paths.UserDir), warnings, ct);
            Report("Loading icons…", force: false);
            var sprites = SpriteIndex.Build(sources, warnings, ct);
            return (catalog.LoadIcons(sources, sprites, _icons), techs.LoadIcons(sources, _icons), sources, GameVersion.Read(gameDir));
        }
        catch
        {
            foreach (var s in sources) s.Dispose();
            throw;
        }
    }

    void Report(string? text, bool force)
    {
        _progress = text;
        var now = Environment.TickCount64;
        if (!force && now - Interlocked.Read(ref _lastProgress) < 150) return;
        Interlocked.Exchange(ref _lastProgress, now);
        Raise();
    }

    void Raise()
    {
        if (Changed is not { } handlers) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try { handler(); }
            catch (Exception) { /* a faulty subscriber must not break saving or building */ }
        }
    }
}

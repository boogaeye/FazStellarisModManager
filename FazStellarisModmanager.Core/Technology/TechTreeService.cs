namespace FazStellarisModmanager.Core.Technology;

/// <summary>Which mod list to build from: ListName null = the mods enabled in dlc_load.json.</summary>
public sealed record TechTreeChoice(string Label, string? ListName);

/// <summary>A built tree plus the sources it came from (kept open so icons can be read).</summary>
public sealed record TechTree(string Label, TechDatabase Database, IReadOnlyList<ContentSource> Sources);

/// <summary>UI facade: builds trees off the UI thread (one at a time) and serves icons. <see cref="Changed"/> may fire on any thread.</summary>
public sealed class TechTreeService
{
    public const string CurrentGameLabel = "Current game (dlc_load.json)";

    readonly ModManagerService _manager;
    readonly IconCache _icons;
    readonly SemaphoreSlim _build = new(1, 1);
    volatile TechTree? _current;
    volatile string? _progress;
    long _lastProgress;

    public TechTreeService(ModManagerService manager)
    {
        _manager = manager;
        _icons = new IconCache(manager.Paths.Icons);
    }

    public TechTree? Current => _current;
    public bool IsBuilding => _build.CurrentCount == 0;
    public string? Progress => _progress;
    public event Action? Changed;

    public IReadOnlyList<TechTreeChoice> Choices() =>
        [new TechTreeChoice(CurrentGameLabel, null), .. _manager.Lists.LoadAll().Select(l => new TechTreeChoice(l.Name, l.Name))];

    /// <summary>Builds the tree for a choice. On failure the previous tree stays current.</summary>
    public async Task BuildAsync(TechTreeChoice choice, CancellationToken ct = default)
    {
        await _build.WaitAsync(ct);
        try
        {
            Report("Reading mod list…", force: true);
            var tree = await Task.Run(() => BuildCore(choice, ct), ct);
            var old = _current;
            _current = tree;
            if (old is not null)
                foreach (var s in old.Sources) s.Dispose();
        }
        finally
        {
            _build.Release();
            Report(null, force: true);
        }
    }

    async Task<TechTree> BuildCore(TechTreeChoice choice, CancellationToken ct)
    {
        await _manager.RefreshLibraryAsync();
        var paths = _manager.Resolve();
        var gameDir = paths.GameDir ?? throw new InvalidOperationException("Stellaris install not found. Set the game folder in Settings.");
        var list = choice.ListName is null
            ? _manager.ImportCurrent("Current game")
            : _manager.Lists.Load(choice.ListName) ?? throw new InvalidOperationException($"Saved list '{choice.ListName}' not found.");
        var warnings = new List<string>();
        var sources = TechSources.Build(gameDir, paths.UserDir, list, warnings);
        try
        {
            var db = TechDatabase.Build(sources, warnings, new ProgressSink(this), ct);
            return new TechTree(choice.Label, db, sources);
        }
        catch
        {
            foreach (var s in sources) s.Dispose();
            throw;
        }
    }

    /// <summary>PNG data URI for a tech's icon in the current tree, or null.</summary>
    public string? IconUri(Tech tech) => _current is { } tree ? _icons.DataUri(tree.Sources, tech.IconKey) : null;

    sealed class ProgressSink(TechTreeService owner) : IProgress<string>
    {
        public void Report(string value) => owner.Report(value, force: false);
    }

    void Report(string? text, bool force)
    {
        _progress = text;
        var now = Environment.TickCount64;
        if (!force && now - Interlocked.Read(ref _lastProgress) < 150) return;
        Interlocked.Exchange(ref _lastProgress, now);
        if (Changed is not { } handlers) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try { handler(); }
            catch (Exception) { /* a faulty subscriber must not break building */ }
        }
    }
}

using System.IO.Compression;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>One root of game content (the base game or a mod): a folder, or a zip for old-style mods.</summary>
public abstract class ContentSource(string name, bool isBaseGame) : IDisposable
{
    public string Name { get; } = name;
    public bool IsBaseGame { get; } = isBaseGame;

    /// <summary>Relative paths (forward slashes) of files under <paramref name="folder"/>, recursive, ending with <paramref name="extension"/>, ordinal-ignore-case sorted.</summary>
    public abstract IReadOnlyList<string> Files(string folder, string extension);

    public abstract bool Exists(string relativePath);

    public abstract Stream Open(string relativePath);

    /// <summary>"size:utcTicks" of a file, used to key caches.</summary>
    public abstract string Stamp(string relativePath);

    public string ReadText(string relativePath)
    {
        using var stream = Open(relativePath);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public virtual void Dispose() { }

    /// <exception cref="DirectoryNotFoundException">Neither a folder nor a .zip exists at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidDataException">The .zip at <paramref name="path"/> is corrupt or not a valid archive.</exception>
    public static ContentSource FromPath(string name, string path, bool isBaseGame = false)
    {
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) return new DirectorySource(name, path, isBaseGame);
        if (!string.IsNullOrEmpty(path) && File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return new ZipSource(name, path);
        throw new DirectoryNotFoundException($"Content not found for '{name}': {path}");
    }
}

sealed class DirectorySource(string name, string root, bool isBaseGame) : ContentSource(name, isBaseGame)
{
    readonly string _rootFull = Path.GetFullPath(root);

    /// <summary>Full path of a relative path, or null if it is rooted or escapes the root.</summary>
    string? Contain(string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) return null;
        var full = Path.GetFullPath(Path.Combine(_rootFull, relativePath));
        var prefix = _rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    string Require(string relativePath) => Contain(relativePath) ?? throw new UnauthorizedAccessException("Path escapes the content root.");

    public override IReadOnlyList<string> Files(string folder, string extension)
    {
        var dir = Path.Combine(root, folder);
        if (!Directory.Exists(dir)) return [];
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(dir, "*" + extension, options)
            .Where(f => f.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public override bool Exists(string relativePath) => Contain(relativePath) is { } full && File.Exists(full);

    public override Stream Open(string relativePath) => File.OpenRead(Require(relativePath));

    public override string Stamp(string relativePath)
    {
        var info = new FileInfo(Require(relativePath));
        return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
    }
}

sealed class ZipSource : ContentSource
{
    readonly ZipArchive _zip;
    volatile bool _disposed;
    readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public ZipSource(string name, string path) : base(name, isBaseGame: false)
    {
        _zip = ZipFile.OpenRead(path);
        foreach (var e in _zip.Entries)
            if (!e.FullName.EndsWith('/') && Normalise(e.FullName) is { } key) _entries.TryAdd(key, e);
    }

    /// <summary>Forward-slash name, or null if rooted or containing a ".." segment.</summary>
    static string? Normalise(string name)
    {
        var n = name.Replace('\\', '/');
        if (n.StartsWith('/') || Path.IsPathRooted(n) || n.Split('/').Any(p => p == "..")) return null;
        return n;
    }

    bool TryGet(string relativePath, out ZipArchiveEntry entry)
    {
        entry = null!;
        return Normalise(relativePath) is { } key && _entries.TryGetValue(key, out entry!);
    }

    public override IReadOnlyList<string> Files(string folder, string extension)
    {
        var prefix = folder.TrimEnd('/') + "/";
        return _entries.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && k.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public override bool Exists(string relativePath)
    {
        ThrowIfDisposed();
        return TryGet(relativePath, out _);
    }

    // ZipArchive is not thread-safe, so copy the entry out under a lock.
    public override Stream Open(string relativePath)
    {
        ThrowIfDisposed();
        if (!TryGet(relativePath, out var entry)) throw new FileNotFoundException("Not in archive.", relativePath);
        lock (_zip)
        {
            ThrowIfDisposed();
            using var s = entry.Open();
            var copy = new MemoryStream();
            s.CopyTo(copy);
            copy.Position = 0;
            return copy;
        }
    }

    public override string Stamp(string relativePath)
    {
        ThrowIfDisposed();
        if (!TryGet(relativePath, out var entry)) throw new FileNotFoundException("Not in archive.", relativePath);
        return $"{entry.Length}:{entry.LastWriteTime.UtcTicks}";
    }

    public override void Dispose()
    {
        lock (_zip)
        {
            _disposed = true;
            _zip.Dispose();
        }
    }
}

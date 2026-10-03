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
    public static ContentSource FromPath(string name, string path, bool isBaseGame = false)
    {
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) return new DirectorySource(name, path, isBaseGame);
        if (!string.IsNullOrEmpty(path) && File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return new ZipSource(name, path);
        throw new DirectoryNotFoundException($"Content not found for '{name}': {path}");
    }
}

sealed class DirectorySource(string name, string root, bool isBaseGame) : ContentSource(name, isBaseGame)
{
    public override IReadOnlyList<string> Files(string folder, string extension)
    {
        var dir = Path.Combine(root, folder);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*" + extension, SearchOption.AllDirectories)
            .Where(f => f.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public override bool Exists(string relativePath) => File.Exists(Path.Combine(root, relativePath));

    public override Stream Open(string relativePath) => File.OpenRead(Path.Combine(root, relativePath));

    public override string Stamp(string relativePath)
    {
        var info = new FileInfo(Path.Combine(root, relativePath));
        return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
    }
}

sealed class ZipSource : ContentSource
{
    readonly ZipArchive _zip;
    readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public ZipSource(string name, string path) : base(name, isBaseGame: false)
    {
        _zip = ZipFile.OpenRead(path);
        foreach (var e in _zip.Entries)
            if (!e.FullName.EndsWith('/')) _entries.TryAdd(e.FullName.Replace('\\', '/'), e);
    }

    public override IReadOnlyList<string> Files(string folder, string extension)
    {
        var prefix = folder.TrimEnd('/') + "/";
        return _entries.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && k.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public override bool Exists(string relativePath) => _entries.ContainsKey(relativePath.Replace('\\', '/'));

    // ZipArchive is not thread-safe, so copy the entry out under a lock.
    public override Stream Open(string relativePath)
    {
        if (!_entries.TryGetValue(relativePath.Replace('\\', '/'), out var entry)) throw new FileNotFoundException("Not in archive.", relativePath);
        lock (_zip)
        {
            using var s = entry.Open();
            var copy = new MemoryStream();
            s.CopyTo(copy);
            copy.Position = 0;
            return copy;
        }
    }

    public override string Stamp(string relativePath)
    {
        if (!_entries.TryGetValue(relativePath.Replace('\\', '/'), out var entry)) throw new FileNotFoundException("Not in archive.", relativePath);
        return $"{entry.Length}:{entry.LastWriteTime.UtcTicks}";
    }

    public override void Dispose() => _zip.Dispose();
}

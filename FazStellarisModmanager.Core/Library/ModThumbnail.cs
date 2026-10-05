using System.IO.Compression;

namespace FazStellarisModmanager.Core.Library;

/// <summary>A mod picture: a file, or an entry (<see cref="ZipEntry"/>) inside the zip at <see cref="FilePath"/>.</summary>
public sealed record ThumbnailSource(string FilePath, string? ZipEntry)
{
    public string Id => ZipEntry is null ? FilePath : FilePath + "|" + ZipEntry;
}

/// <summary>Finds a mod's picture: the descriptor's picture= (if a safe relative path), else thumbnail.png/.jpg/.jpeg, in the content folder or zip (then beside the zip, for Workshop mods only).</summary>
public static class ModThumbnail
{
    public const long MaxBytes = 20L * 1024 * 1024;
    static readonly string[] DefaultNames = ["thumbnail.png", "thumbnail.jpg", "thumbnail.jpeg"];

    /// <summary>The mod's picture, or null when there is none (or it is empty, too big or unreadable).</summary>
    public static ThumbnailSource? Find(InstalledMod mod)
    {
        try
        {
            var content = mod.ContentPath;
            if (content.Length == 0) return null;
            var names = Names(mod.Picture);
            if (Directory.Exists(content)) return InFolder(content, names);
            if (File.Exists(content) && content.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return InZip(content, names) ?? (mod.Source == ModSource.Workshop ? InFolder(Path.GetDirectoryName(content)!, names) : null);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>"length:lastWriteTicks" of the file (the zip for zip entries); changes when the picture may have changed.</summary>
    public static string Stamp(ThumbnailSource source)
    {
        var info = new FileInfo(source.FilePath);
        return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
    }

    public static byte[] Read(ThumbnailSource source)
    {
        if (source.ZipEntry is null) return File.ReadAllBytes(source.FilePath);
        using var archive = ZipFile.OpenRead(source.FilePath);
        var entry = archive.GetEntry(source.ZipEntry) ?? throw new FileNotFoundException("Picture not found in the archive.", source.ZipEntry);
        if (entry.Length > MaxBytes) throw new InvalidDataException("Picture too large.");
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    static List<string> Names(string? picture)
    {
        var names = new List<string>();
        if (IsSafe(picture)) names.Add(picture!.Replace((char)92, '/'));
        foreach (var n in DefaultNames)
            if (!names.Contains(n, StringComparer.OrdinalIgnoreCase)) names.Add(n);
        return names;
    }

    static bool IsSafe(string? p) =>
        !string.IsNullOrWhiteSpace(p) && !Path.IsPathRooted(p) && !p.Contains(':') && !p.Replace((char)92, '/').Split('/').Contains("..");

    // Windows file names are case-insensitive, so a plain lookup matches Thumbnail.PNG too.
    static ThumbnailSource? InFolder(string folder, List<string> names)
    {
        foreach (var n in names)
        {
            var info = new FileInfo(Path.Combine(folder, n));
            if (info.Exists && info.Length > 0 && info.Length <= MaxBytes) return new ThumbnailSource(info.FullName, null);
        }
        return null;
    }

    static ThumbnailSource? InZip(string zip, List<string> names)
    {
        using var archive = ZipFile.OpenRead(zip);
        foreach (var n in names)
        {
            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace((char)92, '/'), n, StringComparison.OrdinalIgnoreCase));
            if (entry is { Length: > 0 and <= MaxBytes }) return new ThumbnailSource(zip, entry.FullName);
        }
        return null;
    }
}

namespace FazStellarisModmanager.Core.Workshop;

/// <summary>
/// A cheap fingerprint of everything under a folder: how many files, their total size and the newest write time (UTC).
/// Steam writing an update into a mod's folder changes it.
/// </summary>
public sealed record FolderStamp(int Files, long Bytes, DateTime NewestUtc)
{
    /// <summary>The stamp of <paramref name="folder"/> and its subfolders; null when it is missing or can't be read.</summary>
    public static FolderStamp? Of(string? folder)
    {
        if (string.IsNullOrEmpty(folder)) return null;
        try
        {
            var dir = new DirectoryInfo(folder);
            if (!dir.Exists) return null;
            var (files, bytes, newest) = (0, 0L, DateTime.MinValue);
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                files++;
                bytes += file.Length;
                if (file.LastWriteTimeUtc > newest) newest = file.LastWriteTimeUtc;
            }
            return new FolderStamp(files, bytes, newest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

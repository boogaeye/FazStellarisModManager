namespace FazStellarisModmanager.Core.Saves;

public static class SaveFiles
{
    /// <summary>The most recently written .sav under <paramref name="saveGamesDir"/> (any subfolder), or null if there is none.</summary>
    public static FileInfo? Newest(string saveGamesDir)
    {
        if (!Directory.Exists(saveGamesDir)) return null;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return new DirectoryInfo(saveGamesDir).EnumerateFiles("*.sav", options).MaxBy(f => f.LastWriteTimeUtc);
    }
}

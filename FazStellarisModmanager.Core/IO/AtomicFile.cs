namespace FazStellarisModmanager.Core.IO;

public static class AtomicFile
{
    /// <summary>Writes to path + ".tmp" and moves it over path, so a crash never leaves a half-written file.</summary>
    public static void WriteAllText(string path, string contents) => Replace(path, tmp => File.WriteAllText(tmp, contents));

    /// <summary><see cref="WriteAllText"/> for raw bytes (keeps a BOM and line endings exactly as given).</summary>
    public static void WriteAllBytes(string path, byte[] contents) => Replace(path, tmp => File.WriteAllBytes(tmp, contents));

    static void Replace(string path, Action<string> write)
    {
        var tmp = path + ".tmp";
        try
        {
            write(tmp);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}

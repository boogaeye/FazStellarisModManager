namespace FazStellarisModmanager.Core.IO;

public static class AtomicFile
{
    /// <summary>Writes to path + ".tmp" and moves it over path, so a crash never leaves a half-written file.</summary>
    public static void WriteAllText(string path, string contents)
    {
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, contents);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}

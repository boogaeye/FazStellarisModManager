using System.IO.Compression;
using System.Text;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Saves;

/// <summary>Reads a Stellaris .sav (a zip with "meta" and "gamestate").</summary>
public static class SaveReader
{
    /// <exception cref="InvalidDataException">Not a zip, or missing meta/gamestate.</exception>
    /// <exception cref="IOException">The file can't be read (for example still being written).</exception>
    public static GameSnapshot Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var metaEntry = zip.GetEntry("meta") ?? throw new InvalidDataException("Not a Stellaris save (no meta).");
        var stateEntry = zip.GetEntry("gamestate") ?? throw new InvalidDataException("Not a Stellaris save (no gamestate).");
        if (stateEntry.Length > int.MaxValue) throw new InvalidDataException("The gamestate is too large to read.");

        string metaText;
        using (var reader = new StreamReader(metaEntry.Open(), Encoding.UTF8)) metaText = reader.ReadToEnd();
        var meta = ParadoxScriptParser.Parse(metaText);

        var data = new byte[stateEntry.Length];
        using (var stream = stateEntry.Open()) stream.ReadExactly(data);
        var (players, countries, community, megastructures) = GamestateScanner.ScanAll(data);

        return new GameSnapshot(
            meta.GetString("name") ?? Path.GetFileNameWithoutExtension(path),
            meta.GetString("date") ?? "",
            meta.GetString("version") ?? "",
            path,
            File.GetLastWriteTimeUtc(path),
            players,
            countries,
            community,
            megastructures);
    }
}

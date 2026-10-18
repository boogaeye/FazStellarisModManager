namespace FazStellarisModmanager.Core.Addons;

/// <summary>
/// The files of Faz Addons as content-relative paths (forward slashes) and bytes, ordered by path (ordinal), and whether any addon changes
/// the game. Without changes the files are only the descriptor and an empty README.txt, and Apply doesn't load the mod.
/// </summary>
public sealed record AddonOutput(IReadOnlyList<(string Path, byte[] Bytes)> Files, bool HasChanges)
{
    /// <summary>The bytes of the file at <paramref name="path"/> (case-insensitive), or null.</summary>
    public byte[]? Get(string path) => Files.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).Bytes;
}

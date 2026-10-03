namespace FazStellarisModmanager.Core.Hashing;

/// <summary>One "directory { ... }" block from checksum_manifest.txt.</summary>
public sealed record ManifestEntry(string Name, bool SubDirectories, string FileExtension);

/// <summary>Line-based parser for &lt;game&gt;\checksum_manifest.txt (ported from HashCoop).</summary>
public static class Manifest
{
    public static List<ManifestEntry> Parse(string text)
    {
        var entries = new List<ManifestEntry>();
        string? name = null;
        bool sub = false;
        string? ext = null;

        void Flush()
        {
            if (name is not null && ext is not null) entries.Add(new ManifestEntry(name, sub, ext));
            name = null;
            sub = false;
            ext = null;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("directory", StringComparison.OrdinalIgnoreCase)) { Flush(); continue; }

            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim().Trim('"');
            switch (key)
            {
                case "name": name = val.Replace('\\', '/').TrimEnd('/'); break;
                case "sub_directories": sub = val.Equals("yes", StringComparison.OrdinalIgnoreCase); break;
                case "file_extension": ext = val.StartsWith('.') ? val : "." + val; break;
            }
        }
        Flush();
        return entries;
    }
}

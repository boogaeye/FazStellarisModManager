using System.Text.RegularExpressions;

namespace FazStellarisModmanager.Core.Paths;

public static class GameLocator
{
    public const string StellarisAppId = "281990";

    /// <summary>Documents\Paradox Interactive\Stellaris (holds dlc_load.json and mod/*.mod).</summary>
    public static string DefaultUserDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Paradox Interactive", "Stellaris");

    /// <summary>Library roots listed in steamapps/libraryfolders.vdf.</summary>
    public static List<string> ParseLibraryFolders(string vdfText) =>
        Regex.Matches(vdfText, "\"path\"\\s*\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value.Replace("\\\\", "\\"))
            .ToList();

    public static string? SteamPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
    }

    /// <summary>All Steam library roots: the Steam install plus everything in libraryfolders.vdf.</summary>
    public static IReadOnlyList<string> SteamLibraries()
    {
        var steam = SteamPath();
        if (steam is null) return [];
        var libs = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf)) libs.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
        return libs.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsGameDir(string dir) => File.Exists(Path.Combine(dir, "checksum_manifest.txt"));

    /// <summary>The explicit dir if it is a valid install, otherwise the first Steam library containing Stellaris.</summary>
    public static string? FindGameDir(string? explicitDir, IEnumerable<string>? libraries = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir)) return IsGameDir(explicitDir) ? explicitDir : null;
        foreach (var lib in libraries ?? SteamLibraries())
        {
            var candidate = Path.Combine(lib, "steamapps", "common", "Stellaris");
            if (IsGameDir(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>gameDir is &lt;lib&gt;\steamapps\common\Stellaris, so Workshop items live in &lt;lib&gt;\steamapps\workshop\content\281990.</summary>
    public static string WorkshopDirFor(string gameDir) =>
        Path.GetFullPath(Path.Combine(gameDir, "..", "..", "workshop", "content", StellarisAppId));
}

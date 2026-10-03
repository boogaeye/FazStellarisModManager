using System.Diagnostics;

namespace FazStellarisModmanager.Core.Game;

public static class GameLauncher
{
    public static string ExePath(string gameDir) => Path.Combine(gameDir, "stellaris.exe");

    public static bool IsSteamRunning()
    {
        var processes = Process.GetProcessesByName("steam");
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    /// <summary>Starts stellaris.exe directly (skipping the Paradox launcher); the game reads dlc_load.json on startup.</summary>
    public static void Launch(string gameDir)
    {
        var exe = ExePath(gameDir);
        if (!File.Exists(exe)) throw new FileNotFoundException("stellaris.exe not found in the game directory.", exe);
        using var process = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = gameDir, UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start Stellaris.");
    }
}

namespace FazStellarisModmanager.Core;

/// <summary>Where the app keeps its own data. Default: %AppData%\FazStellarisModmanager.</summary>
public sealed class AppPaths(string root)
{
    public static AppPaths Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FazStellarisModmanager"));

    public string Root { get; } = root;
    public string Lists => Path.Combine(Root, "lists");
    public string Backups => Path.Combine(Root, "backups");
    public string HashCache => Path.Combine(Root, "hashcache.json");
    public string Settings => Path.Combine(Root, "settings.json");
    public string Icons => Path.Combine(Root, "icons");
    public string ModIcons => Path.Combine(Root, "mod-icons");
    public string Research => Path.Combine(Root, "research");
}

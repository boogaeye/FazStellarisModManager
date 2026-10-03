using System;
using System.IO;
using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Core.Technology;
using Microsoft.Extensions.DependencyInjection;

namespace FazStellarisModmanager;

public static class AppServices
{
    /// <summary>
    /// Registers app services. "--data-dir &lt;path&gt;" replaces %AppData%\FazStellarisModmanager,
    /// which lets two instances run side by side with different settings (used for session testing).
    /// </summary>
    public static void Register(IServiceCollection services, string[] args)
    {
        var dataDir = ArgValue(args, "--data-dir");
        var paths = dataDir is null ? AppPaths.Default() : new AppPaths(Path.GetFullPath(dataDir));
        services.AddSingleton(paths);
        services.AddSingleton(_ => new ModManagerService(paths));
        services.AddSingleton(sp => new SessionService(sp.GetRequiredService<ModManagerService>()));
        services.AddSingleton(sp => new TechTreeService(sp.GetRequiredService<ModManagerService>()));
    }

    static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

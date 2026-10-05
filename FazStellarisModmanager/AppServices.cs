using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Core.Updates;
using FazStellarisModmanager.Core.Workshop;
using FazStellarisModmanager.Services;
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
        services.AddSingleton(new ModIconCache(paths.ModIcons, ImageShrinker.ToSmallPng));
        // Steam client calls run in this exe started as a helper process: Steam treats whichever process connected as the
        // running game until it exits, so connecting from the app itself would block launching Stellaris until it closes.
        services.AddSingleton<IWorkshopService>(_ => new HelperProcessWorkshopService(
            () => new ProcessStartInfo(Environment.ProcessPath!, WorkshopHelperProtocol.Argument) { WorkingDirectory = AppContext.BaseDirectory }));
        services.AddSingleton(sp =>
        {
            var workshop = sp.GetRequiredService<IWorkshopService>();
            return new ModManagerService(paths)
            {
                LaunchBlockedReason = () => workshop.IsActive ? "Wait for the Workshop downloads to finish before launching Stellaris." : null,
            };
        });
        services.AddSingleton(sp => new SessionService(sp.GetRequiredService<ModManagerService>(), sp.GetRequiredService<IWorkshopService>()));
        services.AddSingleton(sp => new TechTreeService(sp.GetRequiredService<ModManagerService>()));
        services.AddSingleton(sp => new UpdateService(
            sp.GetRequiredService<ModManagerService>(),
            // No overall timeout: the download has its own stall timeout and the checker its own 15 s limit.
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            UpdateService.VersionOf(typeof(AppServices).Assembly),
            AppContext.BaseDirectory,
            StartAndExit));
    }

    // Starts the update script, then closes the app so the script can replace its files.
    static void StartAndExit(ProcessStartInfo script)
    {
        Process.Start(script)?.Dispose();
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => System.Windows.Application.Current.Shutdown());
    }

    static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

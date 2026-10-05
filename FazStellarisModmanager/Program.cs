using FazStellarisModmanager.Core.Workshop;
using FazStellarisModmanager.Steam;

namespace FazStellarisModmanager;

public static class Program
{
    /// <summary>
    /// Normally starts the WPF app. With <see cref="WorkshopHelperProtocol.Argument"/> the exe is instead the short-lived
    /// Workshop helper: no window, the protocol on standard input/output, and exit as soon as the downloads are done so
    /// Steam stops treating it as the running game.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains(WorkshopHelperProtocol.Argument))
            return WorkshopHelperHost.RunAsync(new SteamWorkshopService(), Console.In, Console.Out).GetAwaiter().GetResult();

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}

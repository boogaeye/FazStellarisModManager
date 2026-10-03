using System.ComponentModel;
using System.Windows;
using FazStellarisModmanager.Core.Session;
using Microsoft.Extensions.DependencyInjection;

namespace FazStellarisModmanager;

public partial class MainWindow : Window
{
    readonly ServiceProvider _provider;

    public MainWindow()
    {
        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        AppServices.Register(services, Environment.GetCommandLineArgs());
        _provider = services.BuildServiceProvider();
        Resources.Add("services", _provider);
        InitializeComponent();
        Closing += OnClosing;
    }

    // Tell the host (or clients) we are going away. The session code runs on the thread pool, so blocking briefly here is safe.
    void OnClosing(object? sender, CancelEventArgs e)
    {
        try
        {
            var svc = _provider.GetService<SessionService>();
            if (svc is not null) Task.Run(() => svc.LeaveAsync()).Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception)
        {
            // closing must not fail because of the session
        }
    }
}

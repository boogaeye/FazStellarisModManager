using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace FazStellarisModmanager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        Resources.Add("services", services.BuildServiceProvider());
        InitializeComponent();
    }
}

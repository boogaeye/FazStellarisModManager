using System.Windows;
using System.Windows.Threading;

namespace FazStellarisModmanager;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnUnhandled;
    }

    static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Faz Mod Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FImageStack.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_crash.log");
        File.AppendAllText(logPath, $"[{DateTime.Now}] Dispatcher Exception: {e.Exception}\n");
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_crash.log");
        File.AppendAllText(logPath, $"[{DateTime.Now}] Domain Exception: {e.ExceptionObject}\n");
    }
}

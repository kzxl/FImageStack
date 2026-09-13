using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ZeroUI.Wpf.Theme;

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

    protected override void OnStartup(StartupEventArgs e)
    {
        ZeroThemeEngine.Initialize(this, "obsidian_dark");
        ApplyThemeAliases();
        ZeroWpfTheme.ThemeChanged += ApplyThemeAliases;

        base.OnStartup(e);
    }

    private void ApplyThemeAliases()
    {
        Resources["BgDark"] = ZeroWpfTheme.BgPrimary;
        Resources["BgPanel"] = ZeroWpfTheme.BgCard;
        Resources["BgCard"] = ZeroWpfTheme.BgCard;
        Resources["BgInput"] = ZeroWpfTheme.BgInput;
        Resources["BorderDefault"] = ZeroWpfTheme.BorderDefault;
        Resources["BorderHover"] = ZeroWpfTheme.BorderFocus;
        Resources["PrimaryAccent"] = ZeroWpfTheme.PrimaryAccent;
        Resources["SuccessAccent"] = ZeroWpfTheme.SuccessAccent;
        Resources["WarningAccent"] = ZeroWpfTheme.WarningAccent;
        Resources["DangerAccent"] = ZeroWpfTheme.DangerAccent;
        Resources["TextPrimary"] = ZeroWpfTheme.TextPrimary;
        Resources["TextSecondary"] = ZeroWpfTheme.TextSecondary;
        Resources["TextMuted"] = ZeroWpfTheme.TextMuted;
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

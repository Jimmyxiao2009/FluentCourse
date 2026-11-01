using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace FluentCourse.Designer;

public partial class App : Application
{
    private MainWindow? _window;

    public static readonly string CrashLogPath =
        Path.Combine(Path.GetTempPath(), "fluentcourse-crash.log");

    public App()
    {
        InitializeComponent();
        // Keep the app alive on unhandled exceptions and record them, so a stray bug in an
        // event handler surfaces as a log entry instead of silently closing the window ("闪退").
        UnhandledException += (_, e) =>
        {
            LogException("UnhandledException", e.Exception);
            e.Handled = true;
        };
    }

    public static MainWindow? MainWindowInstance { get; private set; }

    public static void LogException(string context, Exception? ex)
    {
        try
        {
            File.AppendAllText(CrashLogPath,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {context}\r\n{ex}\r\n\r\n");
        }
        catch { /* logging must never throw */ }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        MainWindowInstance = _window;
        _window.Activate();
    }
}

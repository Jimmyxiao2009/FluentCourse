using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCourse.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            var coursePath = LaunchArguments.GetCoursePath(args.Arguments);
            if (coursePath is null && BundleBootstrapper.TryLaunchExtractedPlayer())
            {
                Exit();
                return;
            }

            _window = new MainWindow();
            _window.Activate();

            if (coursePath is not null)
            {
                await _window.OpenAsync(coursePath);
                if (LaunchArguments.HasSwitch(args.Arguments, "presenter"))
                    _window.OpenPresenterView();
            }
        }
        catch (Exception ex)
        {
            LogStartupFailure(ex);

            try
            {
                _window ??= new MainWindow();
                _window.Activate();

                // 窗口内容尚未完成布局时 XamlRoot 可能为 null,此时 ShowAsync 会
                // 二次抛异常并吞掉原始错误 —— 判空后退化为只写日志。
                var xamlRoot = _window.Content?.XamlRoot;
                if (xamlRoot is null) return;

                var dlg = new ContentDialog
                {
                    Title = "启动失败",
                    Content = $"程序启动时出现问题：{ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = xamlRoot
                };
                await dlg.ShowAsync();
            }
            catch (Exception dialogEx)
            {
                System.Diagnostics.Debug.WriteLine($"[App] 启动失败对话框显示失败: {dialogEx}");
            }
        }
    }

    private static void LogStartupFailure(Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[App] 启动失败: {ex}");
        try
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fluentcourse-crash.log");
            System.IO.File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] (App.OnLaunched) {ex}{Environment.NewLine}");
        }
        catch
        {
            // 日志写入失败不再抛出
        }
    }
}

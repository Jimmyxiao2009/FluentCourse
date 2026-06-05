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
                await _window.OpenAsync(coursePath);
        }
        catch (Exception ex)
        {
            _window ??= new MainWindow();
            _window.Activate();

            var dlg = new ContentDialog
            {
                Title = "启动失败",
                Content = $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                CloseButtonText = "确定",
                XamlRoot = _window.Content.XamlRoot
            };
            await dlg.ShowAsync();
        }
    }
}

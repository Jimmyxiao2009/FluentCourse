using Microsoft.UI.Xaml;

namespace FluentCourse.Designer;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    public static MainWindow? MainWindowInstance { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        MainWindowInstance = _window;
        _window.Activate();
    }
}

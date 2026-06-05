using FluentCourse.Designer.Models;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FluentCourse.Designer;

public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _vm;
    private Views.PagesView? _pagesView;
    private Views.CourseSettingsView? _settingsView;
    private Views.AssetsPane? _assetsView;

    public MainWindow()
    {
        InitializeComponent();
        Title = "FluentCourse Designer";

        _vm = new ShellViewModel();
        _vm.DocumentChanged += OnDocumentChanged;
        _vm.SelectionChanged += OnSelectionChanged;
        _vm.RequestRender += OnRequestRender;
        _vm.RequestPreview += OnRequestPreview;
        _vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.WindowTitle)) Title = _vm.WindowTitle;
            if (e.PropertyName == nameof(ShellViewModel.DiagnosticMessage)) ShowDiagnostic(_vm.DiagnosticMessage);
        };

        // Undo/Redo button state
        _vm.UndoRedo.PropertyChanged += (_, _) =>
        {
            UndoButton.IsEnabled = _vm.UndoRedo.CanUndo;
            RedoButton.IsEnabled = _vm.UndoRedo.CanRedo;
        };
        UndoButton.IsEnabled = false;
        RedoButton.IsEnabled = false;

        Title = _vm.WindowTitle;

        StageViewControl.SetViewModel(_vm);
        CodeView.SetViewModel(_vm);
        PropertiesPaneView.SetViewModel(_vm);
        ToolboxPaneView.SetViewModel(_vm);
        PagePropsView.SetViewModel(_vm);
        TriggerEditorView.SetViewModel(_vm);
        AnimationEditorView.SetViewModel(_vm);

        _pagesView = new Views.PagesView(); _pagesView.SetViewModel(_vm);
        _settingsView = new Views.CourseSettingsView(); _settingsView.SetViewModel(_vm);
        _assetsView = new Views.AssetsPane(); _assetsView.SetViewModel(_vm);
        LeftFrame.Content = _pagesView;
        ((NavigationViewItem)LeftNav.MenuItems[0]).IsSelected = true;

        _vm.DocumentChanged += (_, _) =>
        {
            if (_vm.Document is not null)
                _vm.Document.PropertyChanged += (_, e2) =>
                {
                    if (e2.PropertyName == nameof(DesignerDocument.SelectedPage) &&
                        _vm.Document.SelectedPage is { } page)
                        PagePropsView.LoadPage(page);
                };
        };

        // Keyboard shortcuts
        if (Content is FrameworkElement root)
        {
            root.KeyDown += OnGlobalKeyDown;
        }
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        _pagesView?.Refresh();
        _assetsView?.Refresh();
        TriggerEditorView.Refresh();
        AnimationEditorView.Refresh();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_vm.CenterPaneIndex == 0) StageViewControl.UpdateSelectionOverlay();
    }

    private void OnRequestRender(object? sender, EventArgs e) => StageViewControl.RenderCurrentPage();

    private async void OnRequestPreview(object? sender, EventArgs e) => LaunchPreview();

    private async void LaunchPreview()
    {
        if (_vm.Document is null) return;
        var sourceDir = _vm.Document.SourcePath;
        if (sourceDir is null || _vm.Document.IsDirty)
        {
            sourceDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}");
            Services.DocumentService.SaveToDirectory(_vm.Document, sourceDir);
        }
        _vm.Document.IsDirty = false;

        try
        {
            var exePath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "FluentCourse.App.exe");
            if (!System.IO.File.Exists(exePath))
                exePath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..",
                    "..", "FluentCourse.App", "bin", "x64", "Debug",
                    "net8.0-windows10.0.19041.0", "FluentCourse.App.exe");
            if (System.IO.File.Exists(exePath))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = exePath, Arguments = $"\"{sourceDir}\"", UseShellExecute = true });
            else
                ShowDiagnostic("找不到 FluentCourse.App.exe，请确保播放器已编译。");
        }
        catch (Exception ex) { ShowDiagnostic($"预览失败: {ex.Message}"); }
    }

    // ── Button handlers ──
    private void OnNewClicked(object s, RoutedEventArgs e) => _vm.NewDocumentCommand.Execute(null);
    private async void OnOpenClicked(object s, RoutedEventArgs e) => await _vm.OpenCommand.ExecuteAsync(null);
    private async void OnSaveClicked(object s, RoutedEventArgs e) => await _vm.SaveCommand.ExecuteAsync(null);
    private async void OnSaveAsClicked(object s, RoutedEventArgs e) => await _vm.SaveAsCommand.ExecuteAsync(null);
    private async void OnExportClicked(object s, RoutedEventArgs e) => await _vm.ExportCourseCommand.ExecuteAsync(null);
    private void OnUndoClicked(object s, RoutedEventArgs e) => _vm.UndoCommand.Execute(null);
    private void OnRedoClicked(object s, RoutedEventArgs e) => _vm.RedoCommand.Execute(null);
    private void OnPreviewClicked(object s, RoutedEventArgs e) => _vm.PreviewCourseCommand.Execute(null);

    private void OnDesignTabClicked(object s, RoutedEventArgs e)
    {
        _vm.CenterPaneIndex = 0;
        DesignSurfacePanel.Visibility = Visibility.Visible;
        CodeView.Visibility = Visibility.Collapsed;
        StageViewControl.RenderCurrentPage();
    }

    private void OnXamlTabClicked(object s, RoutedEventArgs e)
    {
        _vm.CenterPaneIndex = 1;
        DesignSurfacePanel.Visibility = Visibility.Collapsed;
        CodeView.Visibility = Visibility.Visible;
        CodeView.Refresh();
    }

    private void OnAddPageClicked(object s, RoutedEventArgs e) => _vm.AddPageCommand.Execute(null);

    private void OnLeftNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            LeftFrame.Content = tag switch
            {
                "Pages" => _pagesView, "Settings" => _settingsView, "Assets" => _assetsView, _ => _pagesView
            };
        }
    }

    private void OnGlobalKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrlDown = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Control) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        if ((e.Key == Windows.System.VirtualKey.Z) && ctrlDown)
        {
            _vm.UndoCommand.Execute(null);
            e.Handled = true;
        }
        if ((e.Key == Windows.System.VirtualKey.Y) && ctrlDown)
        {
            _vm.RedoCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ShowDiagnostic(string? message)
    {
        if (string.IsNullOrEmpty(message)) { DiagnosticsBar.Visibility = Visibility.Collapsed; return; }
        DiagnosticsBar.Visibility = Visibility.Visible;
        DiagnosticsText.Text = message;
    }

    private void OnDismissDiagnostics(object sender, RoutedEventArgs e)
    {
        DiagnosticsBar.Visibility = Visibility.Collapsed;
        _vm.ClearDiagnostic();
    }
}

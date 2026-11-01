using FluentCourse.Core.Packaging;
using FluentCourse.Runtime.Player;
using FluentCourse.Runtime.Presenter;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.System;

namespace FluentCourse.App;

public sealed partial class MainWindow : Window
{
    private CoursePackage? _package;
    private PagePlayer? _player;
    private PresenterWindow? _presenter;
    private bool _overviewOpen;

    public MainWindow()
    {
        InitializeComponent();
        Title = "FluentCourse";

        if (Content is FrameworkElement root)
            root.KeyDown += OnGlobalKeyDown;
    }

    private async void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".fcourse");
        picker.FileTypeFilter.Add(".zip");
        picker.FileTypeFilter.Add(".json");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        await OpenAsync(file.Path);
    }

    public async Task OpenAsync(string path)
    {
        try
        {
            // 先释放旧播放器(解绑事件/停定时器/停媒体),再释放旧包
            if (_player is not null)
            {
                _player.PageChanged -= OnPageChanged;
                _player.Dispose();
                _player = null;
            }
            _package?.Dispose();

            _package = CoursePackage.Open(path);
            _player = new PagePlayer(_package, StageHost);
            _player.PageChanged += OnPageChanged;

            EmptyHint.Visibility = Visibility.Collapsed;
            PopulateOverview();

            await _player.StartAsync();
        }
        catch (Exception ex)
        {
            // 详细信息只进 Debug 输出,对用户显示友好文案
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 打开课件失败: {ex}");
            if (Content?.XamlRoot is null) return;
            var dlg = new ContentDialog
            {
                Title = "打开失败",
                Content = $"无法打开该课件：文件可能已损坏或不是有效的 .fcourse 文件。\n\n{ex.Message}",
                CloseButtonText = "确定",
                XamlRoot = Content.XamlRoot
            };
            await dlg.ShowAsync();
        }
    }

    private void PopulateOverview()
    {
        if (_package is null) return;
        PageListView.ItemsSource = _package.Manifest.Pages
            .Select((p, i) => new PageListItem
            {
                Index = $"第 {i + 1} 页",
                Title = p.Title ?? p.Id,
                PageId = p.Id
            })
            .ToList();
    }

    private void OnPageChanged(object? sender, PageChangedEventArgs e)
    {
        // Sync overview selection to current page. Skip the assignment when the list is
        // already on the right item — otherwise we'd re-enter OnPageListSelectionChanged.
        var items = PageListView.ItemsSource as List<PageListItem>;
        if (items is null) return;
        var item = items.FirstOrDefault(x => x.PageId == e.Descriptor.Id);
        if (item is not null && !ReferenceEquals(PageListView.SelectedItem, item))
            PageListView.SelectedItem = item;

        UpdateStepControls();
        UpdateNavButtons();
    }

    private void UpdateNavButtons()
    {
        // 首末页时禁用对应按钮,避免点击无反馈
        PrevButton.IsEnabled = _player?.CanGoPrevious ?? false;
        NextButton.IsEnabled = _player?.CanGoNext ?? false;
    }

    private void UpdateStepControls()
    {
        if (_player is null) { StepControls.Visibility = Visibility.Collapsed; return; }
        // Only show the build control when the page actually has build steps.
        StepControls.Visibility = _player.StepCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        StepLabel.Text = $"{_player.StepIndex}/{_player.StepCount}";
        StepPrevBtn.IsEnabled = _player.StepIndex > 0;
        StepNextBtn.IsEnabled = _player.StepIndex < _player.StepCount;
    }

    private async void OnStepNext(object sender, RoutedEventArgs e)
    {
        if (_player is null) return;
        await _player.StepForwardAsync();
        UpdateStepControls();
    }

    private void OnStepPrev(object sender, RoutedEventArgs e)
    {
        if (_player is null) return;
        _player.StepBackward();
        UpdateStepControls();
    }

    private async void OnPageListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_player is null) return;
        if (PageListView.SelectedItem is PageListItem item)
            await _player.GoToAsync(item.PageId);
    }

    private void OnOverviewClicked(object sender, RoutedEventArgs e)
    {
        _overviewOpen = !_overviewOpen;
        OverviewPanel.Visibility = _overviewOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnNextClicked(object sender, RoutedEventArgs e)
    {
        if (_player is not null) await _player.NextAsync();
    }

    private async void OnPrevClicked(object sender, RoutedEventArgs e)
    {
        if (_player is not null) await _player.PreviousAsync();
    }

    private void OnPresenterClicked(object sender, RoutedEventArgs e)
    {
        OpenPresenterView();
    }

    public void OpenPresenterView()
    {
        if (_player is null) return;
        if (_presenter is not null) { _presenter.Activate(); return; }

        try
        {
            var (audience, presenter) = DualScreenLayout.Pick();
            DualScreenLayout.MoveToFullScreen(this, audience);

            _presenter = new PresenterWindow(_player);
            _presenter.Closed += (_, _) => _presenter = null;
            _presenter.Activate();
            DualScreenLayout.MoveToFullScreen(_presenter, presenter);
        }
        catch (Exception ex)
        {
            if (Content?.XamlRoot is null) return;
            var dlg = new ContentDialog
            {
                Title = "演讲者视图启动失败",
                Content = $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                CloseButtonText = "确定",
                XamlRoot = Content.XamlRoot
            };
            _ = dlg.ShowAsync();
        }
    }

    private void OnFullscreenClicked(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var aw = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
        if (aw.Presenter is Microsoft.UI.Windowing.OverlappedPresenter op)
        {
            if (op.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized) op.Restore();
            else op.Maximize();
        }
    }

    private async void OnGlobalKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (_player is null) return;
        switch (e.Key)
        {
            case VirtualKey.Right:
            case VirtualKey.PageDown:
            case VirtualKey.Space:
                // 先消耗本页未播完的步进动画,步进播完才翻页(PPT 行为)
                if (_player.StepIndex < _player.StepCount)
                {
                    await _player.StepForwardAsync();
                    UpdateStepControls();
                }
                else
                {
                    await _player.NextAsync();
                }
                e.Handled = true;
                break;
            case VirtualKey.Left:
            case VirtualKey.PageUp:
                // 先回退本页已播的步进,回退到 0 才回上一页
                if (_player.StepIndex > 0)
                {
                    _player.StepBackward();
                    UpdateStepControls();
                }
                else
                {
                    await _player.PreviousAsync();
                }
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                if (_overviewOpen) OnOverviewClicked(this, new RoutedEventArgs());
                break;
        }
    }

    private sealed class PageListItem
    {
        public string Index { get; set; } = "";
        public string Title { get; set; } = "";
        public string PageId { get; set; } = "";
    }
}

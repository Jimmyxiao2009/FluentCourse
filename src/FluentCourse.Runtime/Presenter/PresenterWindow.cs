using System.Linq;
using System.Text;
using System.Collections.Generic;
using FluentCourse.Core.Models;
using FluentCourse.Runtime.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentCourse.Runtime.Presenter;

public sealed class PresenterWindow : Window
{
    private readonly PagePlayer _audience;
    private PagePlayer? _mirror;
    private readonly DispatcherTimer _ticker;
    private readonly DateTime _started = DateTime.UtcNow;

    // Template parts — resolved after Content is set
    private TextBlock? _timerBlock;
    private TextBlock? _pageCountBlock;
    private TextBlock? _notesBlock;
    private Image? _nextThumb;
    private ContentPresenter? _mirrorHost;
    private EventHandler<VariableChangedEventArgs>? _varHandler;

    public PresenterWindow(PagePlayer audience)
    {
        _audience = audience;
        Title = $"演讲者视图 — {audience.Manifest.Title}";

        // Build UI entirely in code using only simple property assignments — no object-initializer
        // Children tricks that confuse WinUI's property-set-twice guard.
        Content = BuildUI();

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) =>
        {
            if (_timerBlock is null) return;
            var e = DateTime.UtcNow - _started;
            _timerBlock.Text = e.TotalHours >= 1 ? e.ToString(@"h\:mm\:ss") : e.ToString(@"mm\:ss");
        };

        Activated += OnFirstActivated;
        Closed += OnClosed;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        // 关窗时解除全部订阅,否则窗口关闭后仍跟随翻页、重开后出现双份同步
        _ticker.Stop();
        Activated -= OnFirstActivated;
        _audience.PageChanged -= OnAudiencePageChanged;
        if (_varHandler is not null)
        {
            _audience.VariableChanged -= _varHandler;
        }
        _mirror?.Dispose();
        _mirror = null;
    }

    private UIElement BuildUI()
    {
        var root = new Grid();
        root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(0xFF, 0x16, 0x16, 0x16));
        root.Padding = new Thickness(20);
        root.ColumnSpacing = 20;
        root.RowSpacing = 12;
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Row 0: timer + page count + control panel button
        _timerBlock = new TextBlock { FontSize = 32, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) };
        _pageCountBlock = new TextBlock { FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGray) };
        
        var panelBtn = new Button
        {
            Content = "控制面板",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGray),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DimGray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4)
        };

        var warningDot = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, -4, -4, 0),
            Visibility = Visibility.Collapsed
        };

        if (_audience.Manifest.Diagnostics != null &&
            (_audience.Manifest.Diagnostics.Warnings.Count > 0 || _audience.Manifest.Diagnostics.Errors.Count > 0))
        {
            warningDot.Visibility = Visibility.Visible;
        }

        var btnGrid = new Grid();
        btnGrid.Children.Add(panelBtn);
        btnGrid.Children.Add(warningDot);

        var flyout = new Flyout();
        var pivot = new Pivot { Width = 360, Height = 480 };

        // 1. Pages Tab
        var pagesList = new ListView { SelectionMode = ListViewSelectionMode.Single };
        pagesList.ItemTemplate = GetPageTemplate();
        var pagesPivotItem = new PivotItem { Header = "页面", Content = pagesList };
        pivot.Items.Add(pagesPivotItem);

        // 2. Variables Tab
        var varsList = new ListView { SelectionMode = ListViewSelectionMode.None };
        varsList.ItemTemplate = GetVariableTemplate();
        var varsPivotItem = new PivotItem { Header = "数据", Content = varsList };
        pivot.Items.Add(varsPivotItem);

        // 3. Diagnostics Tab
        var diagList = new ListView { SelectionMode = ListViewSelectionMode.None };
        diagList.ItemTemplate = GetDiagTemplate();
        var diagPivotItem = new PivotItem { Header = "诊断", Content = diagList };
        pivot.Items.Add(diagPivotItem);

        flyout.Content = pivot;
        panelBtn.Flyout = flyout;

        flyout.Opening += (s, e) =>
        {
            var visiblePages = _audience.Manifest.Pages.Where(p => !p.Hidden).ToList();
            pagesList.ItemsSource = visiblePages;
            var currentIdx = visiblePages.FindIndex(p => p.Id == _audience.CurrentPageId);
            if (currentIdx >= 0)
                pagesList.SelectedIndex = currentIdx;

            var vars = _audience.Variables.Select(kvp => new VariableItem 
            { 
                Name = kvp.Key, 
                Value = kvp.Value?.ToString() ?? "null" 
            }).ToList();
            varsList.ItemsSource = vars;

            var allDiags = new List<DiagnosticEntry>();
            if (_audience.Manifest.Diagnostics != null)
            {
                allDiags.AddRange(_audience.Manifest.Diagnostics.Errors);
                allDiags.AddRange(_audience.Manifest.Diagnostics.Warnings);
            }
            diagList.ItemsSource = allDiags;
        };

        pagesList.SelectionChanged += async (s, e) =>
        {
            if (pagesList.SelectedItem is PageDescriptor pd)
            {
                if (pd.Id != _audience.CurrentPageId)
                {
                    flyout.Hide();
                    await _audience.GoToAsync(pd.Id);
                }
            }
        };

        _varHandler = (s, e) =>
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                var vars = _audience.Variables.Select(kvp => new VariableItem 
                { 
                    Name = kvp.Key, 
                    Value = kvp.Value?.ToString() ?? "null" 
                }).ToList();
                varsList.ItemsSource = vars;
            });
        };
        _audience.VariableChanged += _varHandler;

        var topBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        topBar.Children.Add(_timerBlock);
        topBar.Children.Add(_pageCountBlock);
        topBar.Children.Add(btnGrid);
        Grid.SetRow(topBar, 0);
        Grid.SetColumnSpan(topBar, 2);
        root.Children.Add(topBar);

        // Row 1-2 col 0: mirror
        _mirrorHost = new ContentPresenter();
        var mirrorBorder = new Border
        {
            Child = _mirrorHost,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DimGray)
        };
        Grid.SetRow(mirrorBorder, 1);
        Grid.SetRowSpan(mirrorBorder, 2);
        Grid.SetColumn(mirrorBorder, 0);
        root.Children.Add(mirrorBorder);

        // Row 1 col 1: next thumb
        _nextThumb = new Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
        var thumbBorder = new Border
        {
            Child = _nextThumb,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DimGray),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0xFF, 0x22, 0x22, 0x22))
        };
        Grid.SetRow(thumbBorder, 1);
        Grid.SetColumn(thumbBorder, 1);
        root.Children.Add(thumbBorder);

        // Row 2 col 1: notes
        _notesBlock = new TextBlock
        {
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            Padding = new Thickness(12)
        };
        var notesScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        notesScroll.Content = _notesBlock;
        var notesBorder = new Border
        {
            Child = notesScroll,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DimGray),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E))
        };
        Grid.SetRow(notesBorder, 2);
        Grid.SetColumn(notesBorder, 1);
        root.Children.Add(notesBorder);

        return root;
    }

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        _mirror = new PagePlayer(_audience.Package, _mirrorHost!);
        _ticker.Start();
        // 先清理可能残留的旧订阅,保证只挂一份
        _audience.PageChanged -= OnAudiencePageChanged;
        _audience.PageChanged += OnAudiencePageChanged;

        if (_audience.CurrentPageId is { } id)
        {
            var desc = _audience.Manifest.Pages.FirstOrDefault(p => p.Id == id);
            if (desc is not null) await SyncToPageAsync(desc);
        }
    }

    private void OnAudiencePageChanged(object? sender, PageChangedEventArgs e)
        => _ = SyncToPageAsync(e.Descriptor);

    private async Task SyncToPageAsync(PageDescriptor desc)
    {
        if (_mirror is null) return;
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (_notesBlock is not null) _notesBlock.Text = desc.Notes ?? "";
            if (_pageCountBlock is not null)
            {
                // 页码统计跳过 hidden 页,与播放器实际可翻页数保持一致
                var visiblePages = _audience.Manifest.Pages.Where(p => !p.Hidden).ToList();
                var idx = visiblePages.FindIndex(p => p.Id == desc.Id);
                _pageCountBlock.Text = $"{idx + 1} / {visiblePages.Count}";
            }
        });

        try { await _mirror.GoToAsync(desc.Id); } catch { }

        // 下一页缩略图同样跳过 hidden 页
        var i = _audience.Manifest.Pages.FindIndex(p => p.Id == desc.Id);
        var next = _audience.NextVisibleIndex(i);
        if (next >= 0)
            await UpdateThumbAsync(desc, _audience.Manifest.Pages[next]);
        else
            DispatcherQueue?.TryEnqueue(() => { if (_nextThumb is not null) _nextThumb.Source = null; });
    }

    private async Task UpdateThumbAsync(PageDescriptor current, PageDescriptor next)
    {
        if (_mirror is null) return;
        try
        {
            // The mirror always tracks the audience's current page, but RenderTargetBitmap
            // needs a laid-out visual tree. Briefly drive the mirror to the next page so it
            // owns a real element to render, then restore it. The next SyncToPageAsync will
            // also reset the mirror if the audience moved on.
            await _mirror.GoToAsync(next.Id);
            if (_mirror.CurrentPage is not { } page) return;
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(page, 480, 270);
            DispatcherQueue?.TryEnqueue(() => { if (_nextThumb is not null) _nextThumb.Source = rtb; });
        }
        catch { }
        finally
        {
            if (_mirror is not null)
            {
                try { await _mirror.GoToAsync(current.Id); } catch { }
            }
        }
    }

    private DataTemplate GetPageTemplate()
    {
        var xaml = "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                   "  <StackPanel Padding=\"4,6\" Spacing=\"2\">" +
                   "    <TextBlock Text=\"{Binding Title}\" FontSize=\"14\" Foreground=\"White\"/>" +
                   "    <TextBlock Text=\"{Binding Id}\" FontSize=\"11\" Foreground=\"Gray\"/>" +
                   "  </StackPanel>" +
                   "</DataTemplate>";
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    private DataTemplate GetVariableTemplate()
    {
        var xaml = "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                   "  <Grid Padding=\"4,6\">" +
                   "    <Grid.ColumnDefinitions>" +
                   "      <ColumnDefinition Width=\"*\"/>" +
                   "      <ColumnDefinition Width=\"Auto\"/>" +
                   "    </Grid.ColumnDefinitions>" +
                   "    <TextBlock Text=\"{Binding Name}\" FontSize=\"14\" Foreground=\"White\" FontWeight=\"SemiBold\"/>" +
                   "    <TextBlock Grid.Column=\"1\" Text=\"{Binding Value}\" FontSize=\"14\" Foreground=\"#4CAF50\"/>" +
                   "  </Grid>" +
                   "</DataTemplate>";
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    private DataTemplate GetDiagTemplate()
    {
        var xaml = "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                   "  <StackPanel Padding=\"4,6\" Spacing=\"2\">" +
                   "    <TextBlock Text=\"{Binding Message}\" FontSize=\"13\" Foreground=\"#FF5252\" TextWrapping=\"Wrap\"/>" +
                   "    <TextBlock Text=\"{Binding Location}\" FontSize=\"11\" Foreground=\"Gray\"/>" +
                   "  </StackPanel>" +
                   "</DataTemplate>";
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }
}

public class VariableItem
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

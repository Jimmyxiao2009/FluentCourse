using FluentCourse.Core.Models;
using FluentCourse.Runtime.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        Closed += (_, _) => _ticker.Stop();
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

        // Row 0: timer + page count
        _timerBlock = new TextBlock { FontSize = 32, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) };
        _pageCountBlock = new TextBlock { FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGray) };
        var topBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        topBar.Children.Add(_timerBlock);
        topBar.Children.Add(_pageCountBlock);
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
                var idx = _audience.Manifest.Pages.FindIndex(p => p.Id == desc.Id);
                _pageCountBlock.Text = $"{idx + 1} / {_audience.Manifest.Pages.Count}";
            }
        });

        try { await _mirror.GoToAsync(desc.Id); } catch { }

        var i = _audience.Manifest.Pages.FindIndex(p => p.Id == desc.Id);
        if (i >= 0 && i < _audience.Manifest.Pages.Count - 1)
            await UpdateThumbAsync(desc, _audience.Manifest.Pages[i + 1]);
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
}

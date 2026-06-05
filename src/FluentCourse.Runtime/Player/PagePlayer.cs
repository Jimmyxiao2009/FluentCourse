using FluentCourse.Core.Models;
using FluentCourse.Core.Packaging;
using FluentCourse.Runtime.Animations;
using FluentCourse.Runtime.Triggers;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace FluentCourse.Runtime.Player;

/// <summary>
/// Owns the runtime state of a course: which page is showing, which transition is running,
/// and the trigger engine bound to the current page. Embed it inside a <see cref="ContentControl"/>
/// (the App's main page or a presenter window) and call <see cref="GoToAsync(string)"/>.
/// </summary>
public sealed class PagePlayer
{
    private readonly CoursePackage _package;
    private readonly PageLoader _loader;
    private readonly AssetBinder _assetBinder;
    private readonly StoryboardFactory _storyboards;
    private readonly TriggerEngine _triggers;
    private readonly ContentPresenter _host;
    private readonly Grid _stage;

    private FrameworkElement? _current;
    private string? _currentPageId;
    private DispatcherTimer? _autoAdvance;
    private bool _navigating;

    public PagePlayer(CoursePackage package, ContentPresenter host)
    {
        _package = package;
        _host = host;
        _loader = new PageLoader(package);
        _assetBinder = new AssetBinder(package);
        _storyboards = new StoryboardFactory(package.Manifest.Animations);
        _triggers = new TriggerEngine(this, _storyboards, package.Manifest.Triggers);

        _stage = new Grid { Background = ParseBrush(package.Manifest.Stage.Background) };
        _host.Content = _stage;
    }

    public CoursePackage Package => _package;
    public CourseManifest Manifest => _package.Manifest;
    public string? CurrentPageId => _currentPageId;
    public int CurrentPageIndex => IndexOf(_currentPageId);
    public int PageCount => Manifest.Pages.Count;
    public FrameworkElement? CurrentPage => _current;
    public StoryboardFactory Storyboards => _storyboards;
    public event EventHandler<PageChangedEventArgs>? PageChanged;

    public Task StartAsync()
    {
        var first = Manifest.Pages.FirstOrDefault()
            ?? throw new InvalidOperationException("Course has no pages.");
        return GoToAsync(first.Id);
    }

    public Task NextAsync()
    {
        var idx = IndexOf(_currentPageId);
        return idx >= 0 && idx < Manifest.Pages.Count - 1
            ? GoToAsync(Manifest.Pages[idx + 1].Id)
            : Task.CompletedTask;
    }

    public Task PreviousAsync()
    {
        var idx = IndexOf(_currentPageId);
        return idx > 0 ? GoToAsync(Manifest.Pages[idx - 1].Id) : Task.CompletedTask;
    }

    public Task GoToIndexAsync(int index)
    {
        if (index < 0 || index >= Manifest.Pages.Count)
            return Task.CompletedTask;

        return GoToAsync(Manifest.Pages[index].Id);
    }

    public Task RestartAsync() => GoToIndexAsync(0);

    public async Task GoToAsync(string pageId)
    {
        if (pageId == _currentPageId) return;
        // Re-entrancy guard: drop overlapping navigation requests. Without this, the auto-advance
        // timer firing at the same time as a manual Next click can race the transition and leave
        // the stage with the wrong page on top.
        if (_navigating) return;
        _navigating = true;

        try
        {
            await GoToInternalAsync(pageId);
        }
        finally
        {
            _navigating = false;
        }
    }

    private async Task GoToInternalAsync(string pageId)
    {
        var descriptor = _loader.FindPage(pageId);
        FrameworkElement next;
        try
        {
            next = _loader.Load(pageId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PagePlayer] Failed to load page '{pageId}': {ex}");
            // Show error page inline rather than crashing.
            next = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = $"页面加载失败\n{ex.Message}",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Padding = new Thickness(40),
                FontSize = 20
            };
        }

        next.HorizontalAlignment = HorizontalAlignment.Stretch;
        next.VerticalAlignment = VerticalAlignment.Stretch;

        ApplyStateNames(next, descriptor);
        await _assetBinder.BindAsync(next);

        var previous = _current;
        await TransitionAsync(previous, next, descriptor.Transition);

        _current = next;
        _currentPageId = pageId;

        _triggers.RebindToPage(pageId, next);
        ScheduleAutoAdvance(descriptor);

        PageChanged?.Invoke(this, new PageChangedEventArgs(descriptor, next));

        _ = next.Dispatcher?.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
            () => _triggers.RaisePageLoaded(pageId));
    }

    private async Task TransitionAsync(FrameworkElement? from, FrameworkElement to, string transition)
    {
        _stage.Children.Add(to);

        var sb = transition switch
        {
            "Fade"      => PageTransitionFactory.Fade(from, to),
            "SlidePush" => PageTransitionFactory.SlidePush(from, to),
            "Zoom"      => PageTransitionFactory.Zoom(from, to),
            _           => PageTransitionFactory.None(from, to)
        };

        if (sb is null)
        {
            if (from is not null) _stage.Children.Remove(from);
            return;
        }

        var tcs = new TaskCompletionSource();
        sb.Completed += (_, _) => tcs.TrySetResult();
        sb.Begin();
        await tcs.Task;

        // Remove old page AFTER transition so it never sits on top blocking hits.
        if (from is not null) _stage.Children.Remove(from);
    }

    private void ScheduleAutoAdvance(PageDescriptor descriptor)
    {
        _autoAdvance?.Stop();
        _autoAdvance = null;
        if (descriptor.Duration is not { } seconds || seconds <= 0) return;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            await NextAsync();
        };
        timer.Start();
        _autoAdvance = timer;
    }

    private static void ApplyStateNames(FrameworkElement page, PageDescriptor descriptor)
    {
        // No-op for now — VisualStateManager groups should already be in the parsed XAML.
        // Hook for future: validate that descriptor.States are all present.
        _ = page; _ = descriptor;
    }

    private int IndexOf(string? id) =>
        id is null ? -1 : Manifest.Pages.FindIndex(p => p.Id == id);

    private static Brush ParseBrush(string color) => new SolidColorBrush(ColorParser.Parse(color));
}

public sealed class PageChangedEventArgs(PageDescriptor descriptor, FrameworkElement element) : EventArgs
{
    public PageDescriptor Descriptor { get; } = descriptor;
    public FrameworkElement Element { get; } = element;
}

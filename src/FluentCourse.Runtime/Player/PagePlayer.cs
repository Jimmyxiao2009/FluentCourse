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
    private bool _skipBackStack;
    private readonly List<string> _backStack = new();
    private readonly Dictionary<string, object> _variables = new();

    // Cached storyboards for Stop/Pause/Resume control. Key = (targetName, animationName).
    private readonly Dictionary<(string target, string animation), Storyboard> _sbCache = new();

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
    public IReadOnlyDictionary<string, object> Variables => _variables;
    public event EventHandler<PageChangedEventArgs>? PageChanged;

    // ── navigation helpers that skip hidden pages ──

    public int NextVisibleIndex(int from)
    {
        for (int i = from + 1; i < Manifest.Pages.Count; i++)
            if (!Manifest.Pages[i].Hidden) return i;
        return -1;
    }

    public int PreviousVisibleIndex(int from)
    {
        for (int i = from - 1; i >= 0; i--)
            if (!Manifest.Pages[i].Hidden) return i;
        return -1;
    }

    public int FirstVisibleIndex()
    {
        for (int i = 0; i < Manifest.Pages.Count; i++)
            if (!Manifest.Pages[i].Hidden) return i;
        return -1;
    }

    // ── public API ──

    public Task StartAsync()
    {
        var first = FirstVisibleIndex();
        if (first < 0)
            throw new InvalidOperationException("Course has no visible pages.");
        return GoToAsync(Manifest.Pages[first].Id);
    }

    public Task NextAsync()
    {
        var idx = IndexOf(_currentPageId);
        var n = NextVisibleIndex(idx);
        return n >= 0 ? GoToAsync(Manifest.Pages[n].Id) : Task.CompletedTask;
    }

    public Task PreviousAsync()
    {
        var idx = IndexOf(_currentPageId);
        var p = PreviousVisibleIndex(idx);
        return p >= 0 ? GoToAsync(Manifest.Pages[p].Id) : Task.CompletedTask;
    }

    public Task GoToIndexAsync(int index)
    {
        if (index < 0 || index >= Manifest.Pages.Count) return Task.CompletedTask;
        if (Manifest.Pages[index].Hidden)
        {
            var n = NextVisibleIndex(index);
            if (n >= 0) return GoToAsync(Manifest.Pages[n].Id);
            return Task.CompletedTask;
        }
        return GoToAsync(Manifest.Pages[index].Id);
    }

    public Task RestartAsync()
    {
        _variables.Clear();
        _sbCache.Clear();
        _backStack.Clear();
        return GoToIndexAsync(FirstVisibleIndex());
    }

    public Task GoBackAsync()
    {
        if (_backStack.Count == 0) return Task.CompletedTask;
        var previous = _backStack[^1];
        _backStack.RemoveAt(_backStack.Count - 1);
        _skipBackStack = true;
        return GoToAsync(previous);
    }

    // ── variables ──

    public void SetVariable(string key, object value) => _variables[key] = value;
    public T? GetVariable<T>(string key)
    {
        if (_variables.TryGetValue(key, out var v) && v is T t) return t;
        return default;
    }

    // ── sb cache ──

    public void CacheStoryboard(string targetName, string animationName, Storyboard sb)
    {
        _sbCache[(targetName, animationName)] = sb;
    }

    public Storyboard? GetCachedStoryboard(string targetName, string animationName)
    {
        _sbCache.TryGetValue((targetName, animationName), out var sb);
        return sb;
    }

    // ── navigation ──

    public async Task GoToAsync(string pageId)
    {
        if (pageId == _currentPageId) return;
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

        if (descriptor.Background is { } bg)
        {
            try
            {
                if (next is Panel p) p.Background = ParseBrush(bg);
            }
            catch { }
        }

        ApplyStateNames(next, descriptor);
        await _assetBinder.BindAsync(next);

        var previous = _current;
        await TransitionAsync(previous, next, descriptor.Transition, descriptor.TransitionDurationMs ?? 320);

        // push current to back stack for GoBack (except when we are already going back)
        if (_currentPageId is not null && !_skipBackStack)
            _backStack.Add(_currentPageId);
        _skipBackStack = false;

        _current = next;
        _currentPageId = pageId;

        _triggers.RebindToPage(pageId, next);
        ScheduleAutoAdvance(descriptor);

        PageChanged?.Invoke(this, new PageChangedEventArgs(descriptor, next));

        _ = next.Dispatcher?.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
            () => _triggers.RaisePageLoaded(pageId));
    }

    private async Task TransitionAsync(FrameworkElement? from, FrameworkElement to, string transition, double customMs)
    {
        _stage.Children.Add(to);

        var sb = transition switch
        {
            "Fade"      => PageTransitionFactory.Fade(from, to, customMs),
            "SlidePush" => PageTransitionFactory.SlidePush(from, to, customMs),
            "SlideUp"   => PageTransitionFactory.SlideUp(from, to, customMs),
            "SlideDown" => PageTransitionFactory.SlideDown(from, to, customMs),
            "SlideLeft" => PageTransitionFactory.SlideLeft(from, to, customMs),
            "SlideRight"=> PageTransitionFactory.SlideRight(from, to, customMs),
            "Zoom"      => PageTransitionFactory.Zoom(from, to, customMs),
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

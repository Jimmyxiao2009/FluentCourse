using System.IO;
using System.Text.RegularExpressions;
using FluentCourse.Core.Models;
using FluentCourse.Core.Packaging;
using FluentCourse.Runtime.Animations;
using FluentCourse.Runtime.Triggers;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace FluentCourse.Runtime.Player;

/// <summary>
/// Owns the runtime state of a course: which page is showing, which transition is running,
/// and the trigger engine bound to the current page. Embed it inside a <see cref="ContentControl"/>
/// (the App's main page or a presenter window) and call <see cref="GoToAsync(string)"/>.
/// </summary>
public sealed class PagePlayer : IDisposable
{
    private readonly CoursePackage _package;
    private readonly PageLoader _loader;
    private readonly AssetBinder _assetBinder;
    private readonly StoryboardFactory _storyboards;
    private readonly TriggerEngine _triggers;
    private readonly ContentPresenter _host;
    private readonly Grid _stage;
    private readonly ScaleTransform _scaleTransform;

    private FrameworkElement? _current;
    private string? _currentPageId;
    private DispatcherTimer? _autoAdvance;
    private bool _navigating;
    private bool _disposed;
    private int _navGeneration;            // 每次内部导航自增；ApplyBackground 据此判定异步加载完成时背景是否已过期
    // 导航期间收到的请求，完成后执行最后一个 —— 连同它自己的回退栈语义（GoBack 请求 skip=true）。
    private (string pageId, bool skipBackStack)? _pendingNavigation;
    private readonly List<string> _backStack = new();
    private readonly Dictionary<string, object> _variables = new();
    private readonly List<TextBinding> _textBindings = new();

    // Cached storyboards for Stop/Pause/Resume control. Key = (targetName, animationName).
    private readonly Dictionary<(string target, string animation), Storyboard> _sbCache = new();

    // PPT-style build steps: entrance ("Step") triggers, hidden initially and revealed one at a
    // time by the player's prev/next overlay (not auto-played on page load).
    private readonly List<TriggerDescriptor> _steps = new();
    private int _stepIndex;

    public PagePlayer(CoursePackage package, ContentPresenter host)
    {
        _package = package;
        _host = host;
        _loader = new PageLoader(package);
        _assetBinder = new AssetBinder(package);
        _storyboards = new StoryboardFactory(package.Manifest.Animations,
            (code, msg, loc) => package.Manifest.AddWarning(code, msg, loc));
        _triggers = new TriggerEngine(this, _storyboards, package.Manifest.Triggers);

        _stage = new Grid
        {
            Width = package.Manifest.Stage.DesignWidth,
            Height = package.Manifest.Stage.DesignHeight,
            Background = ParseBrush(package.Manifest.Stage.Background),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _scaleTransform = new ScaleTransform();
        _stage.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        _stage.RenderTransform = _scaleTransform;

        _host.Content = _stage;
        _host.SizeChanged += OnHostSizeChanged;

        // Apply initial scale; SizeChanged may not fire until after layout pass.
        _host.Loaded += OnHostLoaded;

        // 让页面过渡的滑动距离使用舞台设计尺寸,而不是 1920/1080 硬编码兜底
        PageTransitionFactory.SetStageExtent(
            package.Manifest.Stage.DesignWidth, package.Manifest.Stage.DesignHeight);
    }

    /// <summary>
    /// 释放播放器:解绑宿主事件、停止自动翻页、解绑触发器并释放当前页媒体。
    /// 宿主在丢弃实例(如打开新课件)前必须调用,否则旧实例的定时器/事件继续存活。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _host.SizeChanged -= OnHostSizeChanged;
        _host.Loaded -= OnHostLoaded;

        _autoAdvance?.Stop();
        _autoAdvance = null;

        try { _triggers.Detach(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PagePlayer] Detach triggers failed: {ex.Message}"); }

        if (_current is not null)
        {
            try { _assetBinder.UnbindPage(_current); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PagePlayer] Unbind page failed: {ex.Message}"); }
        }

        _sbCache.Clear();
        _steps.Clear();
        _stage.Children.Clear();
        _current = null;
        if (ReferenceEquals(_host.Content, _stage))
            _host.Content = null;
    }

    private void OnHostLoaded(object sender, RoutedEventArgs e) => ApplyScale();

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e) => ApplyScale();

    private void ApplyScale()
    {
        var w = _host.ActualWidth;
        var h = _host.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            _scaleTransform.ScaleX = 1;
            _scaleTransform.ScaleY = 1;
            return;
        }

        var designW = Manifest.Stage.DesignWidth;
        var designH = Manifest.Stage.DesignHeight;
        var sx = w / designW;
        var sy = h / designH;

        switch (Manifest.Stage.ScaleMode)
        {
            case "UniformToFill":
                {
                    var s = Math.Max(sx, sy);
                    _scaleTransform.ScaleX = s;
                    _scaleTransform.ScaleY = s;
                }
                break;
            case "Stretch":
                _scaleTransform.ScaleX = sx;
                _scaleTransform.ScaleY = sy;
                break;
            case "None":
                _scaleTransform.ScaleX = 1;
                _scaleTransform.ScaleY = 1;
                break;
            default: // "Uniform"
                {
                    var s = Math.Min(sx, sy);
                    _scaleTransform.ScaleX = s;
                    _scaleTransform.ScaleY = s;
                }
                break;
        }
    }

    public CoursePackage Package => _package;
    public CourseManifest Manifest => _package.Manifest;
    public string? CurrentPageId => _currentPageId;
    public int CurrentPageIndex => IndexOf(_currentPageId);
    public int PageCount => Manifest.Pages.Count;
    public FrameworkElement? CurrentPage => _current;
    /// <summary>当前页之后是否还有可见页(供宿主控制「下一页」按钮可用态)。</summary>
    public bool CanGoNext => NextVisibleIndex(CurrentPageIndex) >= 0;
    /// <summary>当前页之前是否还有可见页(供宿主控制「上一页」按钮可用态)。</summary>
    public bool CanGoPrevious => PreviousVisibleIndex(CurrentPageIndex) >= 0;
    public StoryboardFactory Storyboards => _storyboards;
    public IReadOnlyDictionary<string, object> Variables => _variables;
    public event EventHandler<PageChangedEventArgs>? PageChanged;
    public event EventHandler<VariableChangedEventArgs>? VariableChanged;

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
        var first = FirstVisibleIndex();
        if (first < 0) return Task.CompletedTask;
        // 强制重载:即便当前就停在第一页也要重新加载,否则 Restart 无效果
        return GoToAsync(Manifest.Pages[first].Id, force: true);
    }

    public Task GoBackAsync()
    {
        if (_backStack.Count == 0) return Task.CompletedTask;
        var previous = _backStack[^1];
        _backStack.RemoveAt(_backStack.Count - 1);
        // 回退语义随请求传递，避免被在途的前进导航误消费（旧版用共享字段 _skipBackStack 会污染回退栈）。
        return GoToAsync(previous, force: false, skipBackStack: true);
    }

    // ── variables ──

    public void SetVariable(string key, object value)
    {
        _variables[key] = value;
        VariableChanged?.Invoke(this, new VariableChangedEventArgs(key, value));
        UpdateTextBindings();
    }
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

    public Task GoToAsync(string pageId) => GoToAsync(pageId, force: false, skipBackStack: false);

    /// <param name="force">true 时即便目标与当前页相同也强制重新加载(Restart 用)。</param>
    public Task GoToAsync(string pageId, bool force) => GoToAsync(pageId, force, skipBackStack: false);

    /// <param name="skipBackStack">true 时不把当前页压入回退栈（GoBack 专用）。</param>
    public async Task GoToAsync(string pageId, bool force, bool skipBackStack)
    {
        if (_disposed) return;
        if (!force && pageId == _currentPageId) return;
        if (_navigating)
        {
            // 导航期间的请求不再静默丢弃:记录下来(连同其回退栈语义),当前导航完成后执行最后一个
            _pendingNavigation = (pageId, skipBackStack);
            return;
        }
        _navigating = true;

        try
        {
            await GoToInternalAsync(pageId, skipBackStack);

            // 处理导航期间积压的最后一个请求
            while (!_disposed && _pendingNavigation is { } pending)
            {
                _pendingNavigation = null;
                if (pending.pageId == _currentPageId) break;
                await GoToInternalAsync(pending.pageId, pending.skipBackStack);
            }
        }
        finally
        {
            _navigating = false;
        }
    }

    private async Task GoToInternalAsync(string pageId, bool skipBackStack)
    {
        var generation = ++_navGeneration;
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

        if (descriptor.Background is { } bg && next is Panel bgPanel)
            ApplyBackground(bgPanel, bg, generation);

        ApplyStateNames(next, descriptor);

        // Scan and initialize dynamic text bindings on the new page
        ScanTextBindings(next);

        // 步进入场元素必须在加入舞台/转场之前就隐藏,
        // 否则转场期间元素全部可见、转场结束才集体消失。
        InitSteps(pageId, next);

        // 单页资源绑定失败不应让整个播放器崩溃
        try
        {
            await _assetBinder.BindAsync(next);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PagePlayer] Asset binding failed for '{pageId}': {ex}");
        }

        var previous = _current;
        await TransitionAsync(previous, next, descriptor.Transition, descriptor.TransitionDurationMs ?? 320);

        // 旧页移出舞台后释放其媒体资源(停止播放、关闭流)
        if (previous is not null)
        {
            try { _assetBinder.UnbindPage(previous); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PagePlayer] Unbind previous page failed: {ex.Message}"); }
        }

        // push current to back stack for GoBack (except when we are already going back)
        if (_currentPageId is not null && !skipBackStack)
            _backStack.Add(_currentPageId);

        _current = next;
        _currentPageId = pageId;

        _triggers.RebindToPage(pageId, next);
        ScheduleAutoAdvance(descriptor);

        PageChanged?.Invoke(this, new PageChangedEventArgs(descriptor, next));

        // WinUI 3 中 DependencyObject.Dispatcher 恒为 null,必须用 DispatcherQueue,
        // 否则 PageLoaded 触发器永远不会执行。
        var dispatcher = next.DispatcherQueue
            ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher is null || !dispatcher.TryEnqueue(() => _triggers.RaisePageLoaded(pageId)))
            _triggers.RaisePageLoaded(pageId);
    }

    // ── PPT build steps (prev/next overlay) ──────────────────────────────────

    public int StepCount => _steps.Count;
    public int StepIndex => _stepIndex;

    private static string? FirstTarget(TriggerDescriptor t) => t.Do.Count > 0 ? t.Do[0].Target : null;

    /// <summary>Entrance/Exit/Emphasis, inferred from the animation name "{el}-{phase}-{effect}".</summary>
    private static string StepPhase(TriggerDescriptor t)
    {
        var anim = t.Do.Count > 0 ? t.Do[0].GetString("animation") : null;
        if (anim is null) return "";
        if (anim.Contains("-Enter")) return "Enter";
        if (anim.Contains("-Exit")) return "Exit";
        if (anim.Contains("-Emphasis")) return "Emphasis";
        return "";
    }

    private void InitSteps(string pageId, FrameworkElement root)
    {
        _steps.Clear();
        _stepIndex = 0;
        foreach (var t in Manifest.Triggers)
        {
            if (t.On.Type != "Step") continue;
            if (!(t.Page is null || t.Page == pageId)) continue;
            _steps.Add(t);
        }
        _steps.Sort((a, b) => (a.On.At ?? 0).CompareTo(b.On.At ?? 0));

        // Entrance elements start hidden (revealed by "next"). Exit/emphasis targets stay as-is.
        foreach (var t in _steps)
        {
            if (StepPhase(t) != "Enter") continue;
            var target = FirstTarget(t);
            if (!string.IsNullOrEmpty(target) && root.FindName(target) is FrameworkElement fe)
                fe.Opacity = 0;
        }
    }

    /// <summary>Reveal the next build element (plays its entrance animation). Returns false if none left.</summary>
    public async Task<bool> StepForwardAsync()
    {
        if (_current is null || _stepIndex >= _steps.Count) return false;
        var trig = _steps[_stepIndex];
        foreach (var action in trig.Do)
            await ActionExecutor.ExecuteAsync(action, this, _storyboards, _current);
        _stepIndex++;
        return true;
    }

    /// <summary>Undo the most-recent step: entrance → re-hide; exit → re-show; emphasis → leave.</summary>
    public bool StepBackward()
    {
        if (_current is null || _stepIndex <= 0) return false;
        _stepIndex--;
        var step = _steps[_stepIndex];
        var target = FirstTarget(step);
        if (!string.IsNullOrEmpty(target) && _current.FindName(target) is FrameworkElement fe)
        {
            // 入场 Storyboard 默认 HoldEnd 持有 Opacity 的动画值,直接赋本地值会被压住。
            // 先停止该步缓存的 Storyboard(缓存 key 与 PlayAnimation 一致:target+animation),
            // 本地赋值才能生效。
            foreach (var action in step.Do)
            {
                if (action.Target is not { } t) continue;
                if (action.GetString("animation") is not { } animName) continue;
                GetCachedStoryboard(t, animName)?.Stop();
            }

            var phase = StepPhase(step);
            if (phase == "Enter") fe.Opacity = 0;        // was revealed → hide again
            else if (phase == "Exit") fe.Opacity = 1;    // was faded out → bring back
            // Emphasis: transient, nothing to undo
        }
        return true;
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
            // async void 路径兜底:旧实例 Dispose 后(包已释放)不应把异常抛进进程
            try
            {
                if (_disposed) return;
                await NextAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PagePlayer] Auto-advance failed: {ex}");
            }
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

    /// <summary>Apply a page background that is either a color (#AARRGGBB) or an image asset.</summary>
    private async void ApplyBackground(Panel panel, string bg, int generation)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(bg)) return;
            if (bg.TrimStart().StartsWith("#")) { panel.Background = ParseBrush(bg); return; }

            var entry = AssetUri.StripScheme(bg);
            using var stream = _package.OpenSeekable(entry);
            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            await bmp.SetSourceAsync(stream.AsRandomAccessStream());
            // 异步解码期间若又发生了导航，该背景已过期：丢弃，避免把图刷到已离屏的旧页面板上
            if (_disposed || generation != _navGeneration) return;
            panel.Background = new ImageBrush { ImageSource = bmp, Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PagePlayer] background failed: {ex.Message}");
        }
    }

    // ── dynamic text data binding ──

    private void ScanTextBindings(FrameworkElement root)
    {
        _textBindings.Clear();
        FindAndRegisterBindings(root);
        UpdateTextBindings();
    }

    private void FindAndRegisterBindings(DependencyObject obj)
    {
        if (obj is TextBlock tb)
        {
            var text = tb.Text;
            if (!string.IsNullOrEmpty(text))
            {
                var vars = ExtractVariables(text);
                if (vars.Count > 0)
                {
                    _textBindings.Add(new TextBinding(tb, text, vars));
                }
            }
            foreach (var inline in tb.Inlines)
            {
                if (inline is Run run)
                {
                    var runText = run.Text;
                    if (!string.IsNullOrEmpty(runText))
                    {
                        var runVars = ExtractVariables(runText);
                        if (runVars.Count > 0)
                        {
                            _textBindings.Add(new TextBinding(run, runText, runVars));
                        }
                    }
                }
            }
        }
        else
        {
            int count = VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                FindAndRegisterBindings(child);
            }
        }
    }

    private static readonly Regex VarRegex = new(@"\[(\$?[a-zA-Z0-9_]+)\]", RegexOptions.Compiled);

    private static List<string> ExtractVariables(string template)
    {
        var list = new List<string>();
        var matches = VarRegex.Matches(template);
        foreach (Match m in matches)
        {
            var name = m.Groups[1].Value;
            if (name.StartsWith('$'))
                name = name[1..];
            if (!list.Contains(name))
                list.Add(name);
        }
        return list;
    }

    private void UpdateTextBindings()
    {
        foreach (var binding in _textBindings)
        {
            var text = binding.OriginalTemplate;
            foreach (var name in binding.VariableNames)
            {
                object? val = "";
                _variables.TryGetValue(name, out val);
                var strVal = val?.ToString() ?? "";
                text = text.Replace($"[{name}]", strVal);
                text = text.Replace($"[${name}]", strVal);
            }
            if (binding.Target is TextBlock tb)
            {
                tb.Text = text;
            }
            else if (binding.Target is Run run)
            {
                run.Text = text;
            }
        }
    }

    private sealed class TextBinding
    {
        public object Target { get; }
        public string OriginalTemplate { get; }
        public List<string> VariableNames { get; }

        public TextBinding(object target, string template, List<string> variables)
        {
            Target = target;
            OriginalTemplate = template;
            VariableNames = variables;
        }
    }
}

public sealed class VariableChangedEventArgs(string name, object value) : EventArgs
{
    public string Name { get; } = name;
    public object Value { get; } = value;
}

public sealed class PageChangedEventArgs(PageDescriptor descriptor, FrameworkElement element) : EventArgs
{
    public PageDescriptor Descriptor { get; } = descriptor;
    public FrameworkElement Element { get; } = element;
}

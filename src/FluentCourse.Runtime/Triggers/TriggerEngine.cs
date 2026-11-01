using FluentCourse.Core.Models;
using FluentCourse.Runtime.Animations;
using FluentCourse.Runtime.Player;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace FluentCourse.Runtime.Triggers;

/// <summary>
/// Wires manifest-declared triggers to live page elements. v1.1 adds Pointer events,
/// gestures (Hold/Swipe/Drag), Focus/Scroll, once/debounce, modifiers, and tapCount.
/// </summary>
public sealed class TriggerEngine
{
    private readonly PagePlayer _player;
    private readonly StoryboardFactory _storyboards;
    private readonly List<TriggerDescriptor> _allTriggers;
    private readonly List<IDisposable> _activeHandlers = new();
    private readonly Dictionary<string, List<IDisposable>> _bindings = new(); // triggerId → handlers
    private readonly Dictionary<string, DateTime> _debounceTimers = new();
    private readonly Dictionary<string, int> _stepCounters = new();
    private readonly object _stepLock = new();
    private readonly Dictionary<FrameworkElement, ManipulationState> _manipulations = new();

    // 无 id 触发器(空字符串)会共享 debounce/step/once 状态并在 _bindings 里互相覆盖,
    // 因此绑定时为每个描述符分配稳定的内部 id。
    private readonly Dictionary<TriggerDescriptor, string> _triggerIds = new();
    private int _autoTriggerSeq;

    public TriggerEngine(PagePlayer player, StoryboardFactory storyboards, List<TriggerDescriptor> triggers)
    {
        _player = player;
        _storyboards = storyboards;
        _allTriggers = triggers;
    }

    /// <summary>解绑全部触发器(供 PagePlayer.Dispose 调用)。</summary>
    public void Detach() => DetachAll();

    public void RebindToPage(string pageId, FrameworkElement root)
    {
        DetachAll();
        lock (_stepLock) { _stepCounters.Clear(); }
        var pageTriggers = _allTriggers.Where(t => t.Page is null || t.Page == pageId);
        foreach (var trigger in pageTriggers)
        {
            EnsureTriggerId(trigger);
            Bind(trigger, root);
        }
    }

    /// <summary>给无 id 的触发器分配唯一内部 id(同一描述符多次重绑保持稳定)。</summary>
    private void EnsureTriggerId(TriggerDescriptor trigger)
    {
        if (_triggerIds.ContainsKey(trigger)) return;
        _triggerIds[trigger] = string.IsNullOrEmpty(trigger.Id)
            ? $"auto-trigger-{_autoTriggerSeq++}"
            : trigger.Id;
    }

    private string IdOf(TriggerDescriptor trigger) =>
        _triggerIds.TryGetValue(trigger, out var id)
            ? id
            : (_triggerIds[trigger] = string.IsNullOrEmpty(trigger.Id)
                ? $"auto-trigger-{_autoTriggerSeq++}"
                : trigger.Id);

    public void RaisePageLoaded(string pageId)
    {
        if (_player.CurrentPage is not { } root) return;
        foreach (var t in _allTriggers.Where(t =>
                     (t.Page is null || t.Page == pageId) && t.On.Type == "PageLoaded"))
        {
            _ = ExecuteAsync(t, root);
        }
    }

    // ── bind dispatcher ──

    private void Bind(TriggerDescriptor trigger, FrameworkElement root)
    {
        var on = trigger.On;
        _bindings[IdOf(trigger)] = new List<IDisposable>();

        switch (on.Type)
        {
            case "PageLoaded":   break;
            case "Time":
                BindMediaTime(trigger, root);
                break;

            case "Click":
                BindTapped(trigger, root);
                break;
            case "DoubleClick":
                BindDoubleTapped(trigger, root);
                break;
            case "PointerEnter":
                BindPointerEnter(trigger, root);
                break;
            case "PointerExit":
                BindPointerExit(trigger, root);
                break;
            case "PointerPressed":
                BindPointerPressed(trigger, root);
                break;
            case "PointerReleased":
                BindPointerReleased(trigger, root);
                break;
            case "Hold":
                BindHold(trigger, root);
                break;
            case "SwipeLeft":
            case "SwipeRight":
            case "SwipeUp":
            case "SwipeDown":
                BindSwipe(trigger, root);
                break;
            case "Drag":
                BindDrag(trigger, root);
                break;
            case "Focus":
                BindFocus(trigger, root, got: true);
                break;
            case "LostFocus":
                BindFocus(trigger, root, got: false);
                break;
            case "Scroll":
                BindScroll(trigger, root);
                break;
            case "Drop":
                BindDrop(trigger, root);
                break;
            case "MediaEnded":
                BindMediaEnded(trigger, root);
                break;
            case "ValueChanged":
                BindValueChanged(trigger, root);
                break;
            case "Key":
                BindKey(trigger, root);
                break;
            default:
                _player.Manifest.AddWarning("UNKNOWN_TRIGGER_TYPE", $"未知事件类型: {on.Type}", $"Trigger ID: {IdOf(trigger)}");
                System.Diagnostics.Debug.WriteLine($"[TriggerEngine] Unknown trigger type: {on.Type}");
                break;
        }
    }

    // ── each event type ──

    private void BindPointerEnter(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;
        PointerEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
        target.PointerEntered += handler;
        Register(IdOf(trigger), () => target.PointerEntered -= handler);
    }

    private void BindPointerExit(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;
        PointerEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
        target.PointerExited += handler;
        Register(IdOf(trigger), () => target.PointerExited -= handler);
    }

    private void BindPointerPressed(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;
        PointerEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
        target.PointerPressed += handler;
        Register(IdOf(trigger), () => target.PointerPressed -= handler);
    }

    private void BindPointerReleased(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;
        PointerEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
        target.PointerReleased += handler;
        Register(IdOf(trigger), () => target.PointerReleased -= handler);
    }

    private void BindTapped(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;

        // tapCount=2 → 改挂 DoubleTapped(Tapped 事件拿不到点击次数);>2 不支持
        var tapCount = trigger.On.TapCount;
        if (tapCount is > 2)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[TriggerEngine] tapCount={tapCount} 不支持(仅 1/2),触发器 '{IdOf(trigger)}' 未绑定。");
            return;
        }
        if (tapCount == 2)
        {
            DoubleTappedEventHandler dblHandler = (_, _) => _ = FireAsync(trigger, root);
            target.DoubleTapped += dblHandler;
            Register(IdOf(trigger), () => target.DoubleTapped -= dblHandler);
            return;
        }

        TappedEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
        target.Tapped += handler;
        Register(IdOf(trigger), () => target.Tapped -= handler);
    }

    private void BindDoubleTapped(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;
        DoubleTappedEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
        target.DoubleTapped += handler;
        Register(IdOf(trigger), () => target.DoubleTapped -= handler);
    }

    private void BindHold(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;
        HoldingEventHandler handler = (_, e) =>
        {
            if ((int)e.HoldingState == (int)Windows.UI.Input.HoldingState.Started)
                _ = FireAsync(trigger, root);
        };
        target.Holding += handler;
        Register(IdOf(trigger), () => target.Holding -= handler);
    }

    private void BindSwipe(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) ?? root;
        if (target is null) return;

        var threshold = trigger.On.Threshold ?? GestureThresholds.SwipeDisplacement;
        var direction = trigger.On.Type;
        var state = new ManipulationState();

        ManipulationStartingEventHandler onStart = (_, e) =>
        {
            state.Displacement = new Windows.Foundation.Point(0, 0);
            state.Active = true;
            e.Handled = true;
        };
        ManipulationDeltaEventHandler onDelta = (_, e) =>
        {
            if (!state.Active) return;
            state.Displacement = new Windows.Foundation.Point(
                state.Displacement.X + e.Delta.Translation.X,
                state.Displacement.Y + e.Delta.Translation.Y);
        };
        ManipulationCompletedEventHandler onEnd = (_, e) =>
        {
            if (!state.Active) return;
            state.Active = false;
            var dx = state.Displacement.X;
            var dy = state.Displacement.Y;
            bool match = direction switch
            {
                "SwipeLeft"  => dx < -threshold,
                "SwipeRight" => dx >  threshold,
                "SwipeUp"    => dy < -threshold,
                "SwipeDown"  => dy >  threshold,
                _            => false
            };
            if (match) _ = FireAsync(trigger, root);
        };

        target.ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.TranslateX
                                | Microsoft.UI.Xaml.Input.ManipulationModes.TranslateY;
        target.ManipulationStarting += onStart;
        target.ManipulationDelta += onDelta;
        target.ManipulationCompleted += onEnd;
        _manipulations[target] = state;

        Register(IdOf(trigger), () =>
        {
            target.ManipulationStarting -= onStart;
            target.ManipulationDelta -= onDelta;
            target.ManipulationCompleted -= onEnd;
            _manipulations.Remove(target);
        });
    }

    private void BindDrag(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) ?? root;
        if (target is null) return;

        var state = new ManipulationState();
        var threshold = trigger.On.Threshold ?? GestureThresholds.DragStart;
        ManipulationDeltaEventHandler onDelta = (_, e) =>
        {
            if (!state.Active) return;
            state.Displacement = new Windows.Foundation.Point(
                state.Displacement.X + e.Delta.Translation.X,
                state.Displacement.Y + e.Delta.Translation.Y);
            var dx = Math.Abs(state.Displacement.X);
            var dy = Math.Abs(state.Displacement.Y);
            if (dx > threshold || dy > threshold)
            {
                // 每次拖动手势只触发一次,ManipulationStarted/Starting 时再重置
                state.Active = false;
                _ = FireAsync(trigger, root);
            }
        };
        ManipulationStartingEventHandler onStart = (_, e) =>
        {
            state.Displacement = new Windows.Foundation.Point(0, 0);
            state.Active = true;
            e.Handled = true;
        };
        ManipulationCompletedEventHandler onEnd = (_, _) => state.Active = false;

        target.ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.TranslateX
                                | Microsoft.UI.Xaml.Input.ManipulationModes.TranslateY;
        target.ManipulationStarting += onStart;
        target.ManipulationDelta += onDelta;
        target.ManipulationCompleted += onEnd;
        _manipulations[target] = state;

        Register(IdOf(trigger), () =>
        {
            target.ManipulationStarting -= onStart;
            target.ManipulationDelta -= onDelta;
            target.ManipulationCompleted -= onEnd;
            _manipulations.Remove(target);
        });
    }

    private void BindFocus(TriggerDescriptor trigger, FrameworkElement root, bool got)
    {
        var target = FindByName(root, trigger.On.Target) as Control;
        if (target is null) return;

        if (got)
        {
            RoutedEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
            target.GotFocus += handler;
            Register(IdOf(trigger), () => target.GotFocus -= handler);
        }
        else
        {
            RoutedEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
            target.LostFocus += handler;
            Register(IdOf(trigger), () => target.LostFocus -= handler);
        }
    }

    private void BindScroll(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as ScrollViewer;
        if (target is null) return;

        EventHandler<ScrollViewerViewChangedEventArgs> handler = (_, _) =>
            _ = FireAsync(trigger, root);
        target.ViewChanged += handler;
        Register(IdOf(trigger), () => target.ViewChanged -= handler);
    }

    private void BindDrop(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;
        target.AllowDrop = true;
        DragEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
        target.Drop += handler;
        Register(IdOf(trigger), () => target.Drop -= handler);
    }

    private void BindMediaEnded(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as Microsoft.UI.Xaml.Controls.MediaPlayerElement;
        if (target?.MediaPlayer is { } mp)
        {
            global::Windows.Foundation.TypedEventHandler<
                Windows.Media.Playback.MediaPlayer, object> handler = (_, _) => _ = FireAsync(trigger, root);
            mp.MediaEnded += handler;
            Register(IdOf(trigger), () => mp.MediaEnded -= handler);
        }
    }

    // ── existing event types ──

    private void BindValueChanged(TriggerDescriptor trigger, FrameworkElement root)
    {
        var element = FindByName(root, trigger.On.Target);
        switch (element)
        {
            case TextBox tb:
                TextChangedEventHandler h1 = (_, _) => _ = FireAsync(trigger, root);
                tb.TextChanged += h1;
                Register(IdOf(trigger), () => tb.TextChanged -= h1);
                break;
            case ToggleButton btn:
                RoutedEventHandler h2 = (_, _) => _ = FireAsync(trigger, root);
                btn.Checked += h2;
                btn.Unchecked += h2;
                Register(IdOf(trigger), () => { btn.Checked -= h2; btn.Unchecked -= h2; });
                break;
            case Slider slider:
                RangeBaseValueChangedEventHandler h3 = (_, _) => _ = FireAsync(trigger, root);
                slider.ValueChanged += h3;
                Register(IdOf(trigger), () => slider.ValueChanged -= h3);
                break;
        }
    }

    private void BindKey(TriggerDescriptor trigger, FrameworkElement root)
    {
        if (string.IsNullOrEmpty(trigger.On.Key)) return;
        if (!Enum.TryParse<Windows.System.VirtualKey>(trigger.On.Key, true, out var vk)) return;

        var requiredModifiers = trigger.On.Modifiers ?? new();
        bool requireCtrl  = requiredModifiers.Contains("Ctrl", StringComparer.OrdinalIgnoreCase);
        bool requireShift = requiredModifiers.Contains("Shift", StringComparer.OrdinalIgnoreCase);
        bool requireAlt   = requiredModifiers.Contains("Alt", StringComparer.OrdinalIgnoreCase);

        KeyEventHandler handler = (_, e) =>
        {
            if (e.Key != vk) return;
            // CoreVirtualKeyStates 是 flags(Down|Locked 等组合),必须用 HasFlag 判断
            bool ctrl  = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                         .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                         .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            bool alt   = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
                         .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if ((requireCtrl  && !ctrl)  || (requireShift && !shift) || (requireAlt && !alt)) return;
            _ = FireAsync(trigger, root);
        };

        // 页面根(Panel)拿不到焦点时收不到 KeyDown,改挂到 XamlRoot 顶层内容上;
        // RebindToPage 在页面入树之后调用,此时 XamlRoot 已可用。Detach 时解绑。
        var host = root.XamlRoot?.Content as UIElement ?? root;
        host.KeyDown += handler;
        Register(IdOf(trigger), () => host.KeyDown -= handler);
    }

    /// <summary>
    /// Time 触发(docs/fcourse-format.md:媒体时间点):目标媒体播放到 on.at 秒时触发。
    /// 播放位置回退(seek)到时间点之前后允许再次触发。
    /// </summary>
    private void BindMediaTime(TriggerDescriptor trigger, FrameworkElement root)
    {
        if (trigger.On.At is not { } at)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[TriggerEngine] Time 触发器 '{IdOf(trigger)}' 缺少 on.at(秒),未绑定。");
            return;
        }
        if (FindByName(root, trigger.On.Target) is not Microsoft.UI.Xaml.Controls.MediaPlayerElement mpe ||
            mpe.MediaPlayer is not { } mp)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[TriggerEngine] Time 触发器目标 '{trigger.On.Target}' 不是有效的媒体元素,未绑定。");
            return;
        }

        var dispatcher = root.DispatcherQueue;
        bool fired = false;
        var session = mp.PlaybackSession;
        global::Windows.Foundation.TypedEventHandler<
            Windows.Media.Playback.MediaPlaybackSession, object> handler = (s, _) =>
        {
            var pos = s.Position.TotalSeconds;
            if (pos >= at)
            {
                if (fired) return;
                fired = true;
                // PositionChanged 回调在媒体线程,切回 UI 线程执行动作
                dispatcher?.TryEnqueue(() => _ = FireAsync(trigger, root));
            }
            else
            {
                fired = false;
            }
        };
        session.PositionChanged += handler;
        Register(IdOf(trigger), () => session.PositionChanged -= handler);
    }

    // ── fire lifecycle ──

    private async Task FireAsync(TriggerDescriptor trigger, FrameworkElement root)
    {
        var id = IdOf(trigger);

        // debounce
        if (trigger.On.DebounceMs is { } dbMs && dbMs > 0)
        {
            var now = DateTime.UtcNow;
            if (_debounceTimers.TryGetValue(id, out var last) &&
                (now - last).TotalMilliseconds < dbMs)
                return;
            _debounceTimers[id] = now;
        }

        await ExecuteAsync(trigger, root);

        if (trigger.On.Once)
            DetachBinding(id);
    }

    private async Task ExecuteAsync(TriggerDescriptor trigger, FrameworkElement root)
    {
        // condition 不满足时不执行(PageLoaded 与普通事件路径都经过这里)
        if (!ConditionEvaluator.Evaluate(trigger.Condition, root, _player))
            return;

        var id = IdOf(trigger);
        List<ActionDescriptor> actions;

        lock (_stepLock)
        {
            if (trigger.Steps.Count > 0)
            {
                _stepCounters.TryGetValue(id, out var step);
                if (step >= trigger.Steps.Count)
                {
                    if (trigger.StopAfterLastStep) return;
                    step = 0;
                }
                actions = trigger.Steps[step];
                _stepCounters[id] = step + 1;
            }
            else
            {
                actions = trigger.Do;
            }
        }

        foreach (var action in actions)
        {
            try
            {
                await ActionExecutor.ExecuteAsync(action, _player, _storyboards, root);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TriggerEngine] Action '{action.Type}' failed: {ex}");
                if (root.XamlRoot is { } xamlRoot)
                {
                    var tip = new Microsoft.UI.Xaml.Controls.TeachingTip
                    {
                        Title = $"动作执行失败: {action.Type}",
                        Subtitle = ex.Message,
                        IsLightDismissEnabled = true,
                        XamlRoot = xamlRoot
                    };
                    tip.IsOpen = true;
                }
            }
        }
    }

    // ── detach ──

    private void DetachAll()
    {
        // detach global list
        foreach (var d in _activeHandlers) d.Dispose();
        _activeHandlers.Clear();
        // detach per-trigger bindings
        foreach (var list in _bindings.Values)
            foreach (var d in list) d.Dispose();
        _bindings.Clear();
        _manipulations.Clear();
    }

    private void DetachBinding(string triggerId)
    {
        if (_bindings.TryGetValue(triggerId, out var list))
        {
            foreach (var d in list) d.Dispose();
            _bindings.Remove(triggerId);
        }
    }

    private void Register(string triggerId, Action dispose)
    {
        var d = new DisposableAction(dispose);
        _activeHandlers.Add(d);
        if (_bindings.TryGetValue(triggerId, out var list))
            list.Add(d);
    }

    // ── helpers ──

    private static FrameworkElement? FindByName(FrameworkElement root, string? name) =>
        string.IsNullOrEmpty(name) ? null : root.FindName(name) as FrameworkElement;

    private sealed class DisposableAction(Action a) : IDisposable
    {
        public void Dispose() => a();
    }

    private sealed class ManipulationState
    {
        public Windows.Foundation.Point Displacement;
        public bool Active;
    }
}

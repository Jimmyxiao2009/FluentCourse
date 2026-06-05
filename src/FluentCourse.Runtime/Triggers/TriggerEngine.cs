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

    public TriggerEngine(PagePlayer player, StoryboardFactory storyboards, List<TriggerDescriptor> triggers)
    {
        _player = player;
        _storyboards = storyboards;
        _allTriggers = triggers;
    }

    public void RebindToPage(string pageId, FrameworkElement root)
    {
        DetachAll();
        lock (_stepLock) { _stepCounters.Clear(); }
        var pageTriggers = _allTriggers.Where(t => t.Page is null || t.Page == pageId);
        foreach (var trigger in pageTriggers)
            Bind(trigger, root);
    }

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
        _bindings[trigger.Id] = new List<IDisposable>();

        switch (on.Type)
        {
            case "PageLoaded":   break;
            case "Time":         break;

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
        }
    }

    // ── each event type ──

    private void BindPointerEnter(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;
        PointerEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
        target.PointerEntered += handler;
        Register(trigger.Id, () => target.PointerEntered -= handler);
    }

    private void BindPointerExit(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;
        PointerEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
        target.PointerExited += handler;
        Register(trigger.Id, () => target.PointerExited -= handler);
    }

    private void BindPointerPressed(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;
        PointerEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
        target.PointerPressed += handler;
        Register(trigger.Id, () => target.PointerPressed -= handler);
    }

    private void BindPointerReleased(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;
        PointerEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
        target.PointerReleased += handler;
        Register(trigger.Id, () => target.PointerReleased -= handler);
    }

    private void BindTapped(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;

        TappedEventHandler handler = (_, e) =>
        {
            if (!CheckTapCount(trigger)) return;
            _ = FireAsync(trigger, root);
        };
        target.Tapped += handler;
        Register(trigger.Id, () => target.Tapped -= handler);
    }

    private void BindDoubleTapped(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;
        DoubleTappedEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
        target.DoubleTapped += handler;
        Register(trigger.Id, () => target.DoubleTapped -= handler);
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
        Register(trigger.Id, () => target.Holding -= handler);
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

        Register(trigger.Id, () =>
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
        ManipulationDeltaEventHandler onDelta = (_, e) =>
        {
            if (!state.Active) return;
            var dx = Math.Abs(state.Displacement.X + e.Delta.Translation.X);
            var dy = Math.Abs(state.Displacement.Y + e.Delta.Translation.Y);
            if (dx > GestureThresholds.DragStart || dy > GestureThresholds.DragStart)
                _ = FireAsync(trigger, root);
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

        Register(trigger.Id, () =>
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
            Register(trigger.Id, () => target.GotFocus -= handler);
        }
        else
        {
            RoutedEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
            target.LostFocus += handler;
            Register(trigger.Id, () => target.LostFocus -= handler);
        }
    }

    private void BindScroll(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as ScrollViewer;
        if (target is null) return;

        EventHandler<ScrollViewerViewChangedEventArgs> handler = (_, _) =>
            _ = FireAsync(trigger, root);
        target.ViewChanged += handler;
        Register(trigger.Id, () => target.ViewChanged -= handler);
    }

    private void BindDrop(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as UIElement;
        if (target is null) return;
        target.AllowDrop = true;
        DragEventHandler handler = (_, _) => _ = FireAsync(trigger, root);
        target.Drop += handler;
        Register(trigger.Id, () => target.Drop -= handler);
    }

    private void BindMediaEnded(TriggerDescriptor trigger, FrameworkElement root)
    {
        var target = FindByName(root, trigger.On.Target) as Microsoft.UI.Xaml.Controls.MediaPlayerElement;
        if (target?.MediaPlayer is { } mp)
        {
            global::Windows.Foundation.TypedEventHandler<
                Windows.Media.Playback.MediaPlayer, object> handler = (_, _) => _ = FireAsync(trigger, root);
            mp.MediaEnded += handler;
            Register(trigger.Id, () => mp.MediaEnded -= handler);
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
                Register(trigger.Id, () => tb.TextChanged -= h1);
                break;
            case ToggleButton btn:
                RoutedEventHandler h2 = (_, _) => _ = FireAsync(trigger, root);
                btn.Checked += h2;
                btn.Unchecked += h2;
                Register(trigger.Id, () => { btn.Checked -= h2; btn.Unchecked -= h2; });
                break;
            case Slider slider:
                RangeBaseValueChangedEventHandler h3 = (_, _) => _ = FireAsync(trigger, root);
                slider.ValueChanged += h3;
                Register(trigger.Id, () => slider.ValueChanged -= h3);
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
            bool ctrl  = (e.KeyStatus.IsExtendedKey && e.Key == Windows.System.VirtualKey.Control) ||
                         InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control) ==
                         Windows.UI.Core.CoreVirtualKeyStates.Down;
            bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) ==
                         Windows.UI.Core.CoreVirtualKeyStates.Down;
            bool alt   = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu) ==
                         Windows.UI.Core.CoreVirtualKeyStates.Down;

            if ((requireCtrl  && !ctrl)  || (requireShift && !shift) || (requireAlt && !alt)) return;
            _ = FireAsync(trigger, root);
        };
        root.KeyDown += handler;
        Register(trigger.Id, () => root.KeyDown -= handler);
    }

    // ── fire lifecycle ──

    private async Task FireAsync(TriggerDescriptor trigger, FrameworkElement root)
    {
        // debounce
        if (trigger.On.DebounceMs is { } dbMs && dbMs > 0)
        {
            var now = DateTime.UtcNow;
            if (_debounceTimers.TryGetValue(trigger.Id, out var last) &&
                (now - last).TotalMilliseconds < dbMs)
                return;
            _debounceTimers[trigger.Id] = now;
        }

        await ExecuteAsync(trigger, root);

        if (trigger.On.Once)
            DetachBinding(trigger.Id);
    }

    private async Task ExecuteAsync(TriggerDescriptor trigger, FrameworkElement root)
    {
        List<ActionDescriptor> actions;

        lock (_stepLock)
        {
            if (trigger.Steps.Count > 0)
            {
                _stepCounters.TryGetValue(trigger.Id, out var step);
                if (step >= trigger.Steps.Count)
                {
                    if (trigger.StopAfterLastStep) return;
                    step = 0;
                }
                actions = trigger.Steps[step];
                _stepCounters[trigger.Id] = step + 1;
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

    private static bool CheckTapCount(TriggerDescriptor trigger)
    {
        // WinUI TappedRoutedEventArgs lacks TapCount; track via PointerPressed in v2.
        if (trigger.On.TapCount is not { } expected) return true;
        return expected == 1; // only single-tap is reliably detectable via Tapped
    }

    private sealed class DisposableAction(Action a) : IDisposable
    {
        public void Dispose() => a();
    }

    private sealed class ManipulationState
    {
        public Windows.Foundation.Point Start;
        public Windows.Foundation.Point Previous;
        public Windows.Foundation.Point Displacement;
        public bool Active;
    }
}

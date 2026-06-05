using FluentCourse.Core.Models;
using FluentCourse.Runtime.Animations;
using FluentCourse.Runtime.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace FluentCourse.Runtime.Triggers;

/// <summary>
/// Wires manifest-declared triggers to live page elements. The XAML tree carries no event
/// handlers (XamlReader can't bind those), so we walk the tree by <see cref="FrameworkElement.Name"/>
/// after parse and attach handlers programmatically.
/// </summary>
public sealed class TriggerEngine
{
    private readonly PagePlayer _player;
    private readonly StoryboardFactory _storyboards;
    private readonly List<TriggerDescriptor> _allTriggers;
    private readonly List<IDisposable> _activeHandlers = new();
    private readonly Dictionary<string, int> _stepCounters = new(); // triggerId → current step index
    private readonly object _stepLock = new();

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

    private void Bind(TriggerDescriptor trigger, FrameworkElement root)
    {
        switch (trigger.On.Type)
        {
            case "PageLoaded":
                // Handled centrally via RaisePageLoaded.
                break;

            case "Click":
                if (FindByName(root, trigger.On.Target) is UIElement tapTarget)
                {
                    TappedEventHandler handler = (s, e) => { _ = ExecuteAsync(trigger, root); };
                    tapTarget.Tapped += handler;
                    _activeHandlers.Add(new DisposableAction(() => tapTarget.Tapped -= handler));
                }
                break;

            case "ValueChanged":
                BindValueChanged(trigger, root);
                break;

            case "Key":
                BindKey(trigger, root);
                break;

            case "Time":
                // Wired by media controls themselves — they emit a synthetic trigger via PlayerHost.RaiseTime.
                break;
        }
    }

    private void BindValueChanged(TriggerDescriptor trigger, FrameworkElement root)
    {
        var element = FindByName(root, trigger.On.Target);
        switch (element)
        {
            case TextBox tb:
                TextChangedEventHandler h1 = (_, _) => _ = ExecuteAsync(trigger, root);
                tb.TextChanged += h1;
                _activeHandlers.Add(new DisposableAction(() => tb.TextChanged -= h1));
                break;
            case ToggleButton btn:
                RoutedEventHandler h2 = (_, _) => _ = ExecuteAsync(trigger, root);
                btn.Checked += h2;
                btn.Unchecked += h2;
                _activeHandlers.Add(new DisposableAction(() => { btn.Checked -= h2; btn.Unchecked -= h2; }));
                break;
            case Slider slider:
                RangeBaseValueChangedEventHandler h3 = (_, _) => _ = ExecuteAsync(trigger, root);
                slider.ValueChanged += h3;
                _activeHandlers.Add(new DisposableAction(() => slider.ValueChanged -= h3));
                break;
        }
    }

    private void BindKey(TriggerDescriptor trigger, FrameworkElement root)
    {
        if (string.IsNullOrEmpty(trigger.On.Key)) return;
        if (!Enum.TryParse<Windows.System.VirtualKey>(trigger.On.Key, true, out var vk)) return;

        KeyEventHandler handler = (_, e) =>
        {
            if (e.Key == vk) _ = ExecuteAsync(trigger, root);
        };
        root.KeyDown += handler;
        _activeHandlers.Add(new DisposableAction(() => root.KeyDown -= handler));
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

    private void DetachAll()
    {
        foreach (var d in _activeHandlers) d.Dispose();
        _activeHandlers.Clear();
    }

    private static FrameworkElement? FindByName(FrameworkElement root, string? name) =>
        string.IsNullOrEmpty(name) ? null : root.FindName(name) as FrameworkElement;

    private sealed class DisposableAction(Action a) : IDisposable
    {
        public void Dispose() => a();
    }
}

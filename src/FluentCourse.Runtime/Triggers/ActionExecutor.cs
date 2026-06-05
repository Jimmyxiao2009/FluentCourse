using FluentCourse.Core.Models;
using FluentCourse.Runtime.Animations;
using FluentCourse.Runtime.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace FluentCourse.Runtime.Triggers;

/// <summary>
/// Dispatches a single <see cref="ActionDescriptor"/> to the right runtime call. Add new
/// action types here — the type discriminator string is matched verbatim from the manifest.
/// </summary>
public static class ActionExecutor
{
    public static async Task ExecuteAsync(
        ActionDescriptor action,
        PagePlayer player,
        StoryboardFactory storyboards,
        FrameworkElement pageRoot)
    {
        switch (action.Type)
        {
            case "GoToPage":
                if (action.GetString("page") is { } page) await player.GoToAsync(page);
                break;

            case "NextPage":
                await player.NextAsync();
                break;

            case "PreviousPage":
                await player.PreviousAsync();
                break;

            case "PlayAnimation":
                PlayAnimation(action, storyboards, pageRoot);
                break;

            case "GoToState":
                GoToState(action, pageRoot);
                break;

            case "ShowToast":
                ShowToast(action, pageRoot);
                break;

            case "SetProperty":
                SetProperty(action, pageRoot);
                break;

            default:
                System.Diagnostics.Debug.WriteLine($"Unknown action type: {action.Type}");
                break;
        }
    }

    private static void PlayAnimation(ActionDescriptor action, StoryboardFactory storyboards, FrameworkElement root)
    {
        var animationName = action.GetString("animation") ?? "FadeIn";
        var target = action.Target is null ? root : root.FindName(action.Target) as FrameworkElement;
        if (target is null) return;

        var sb = storyboards.Build(animationName, target);
        sb.Begin();
    }

    private static void GoToState(ActionDescriptor action, FrameworkElement root)
    {
        var state = action.GetString("state");
        if (state is null) return;

        // Prefer a named target; fall back to the page root.
        var target = (action.Target is null ? root : root.FindName(action.Target) as FrameworkElement) ?? root;

        // VisualStateManager.GoToState requires Control. Walk up to the nearest Control ancestor
        // when the target itself isn't one (e.g. a plain Grid page root).
        Control? ctrl = target as Control;
        if (ctrl is null)
        {
            var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(target);
            while (parent is not null && parent is not Control)
                parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
            ctrl = parent as Control;
        }

        if (ctrl is not null)
            VisualStateManager.GoToState(ctrl, state, useTransitions: true);
    }

    private static void ShowToast(ActionDescriptor action, FrameworkElement root)
    {
        var message = action.GetString("message") ?? "";
        if (root.XamlRoot is null) return;

        var tip = new TeachingTip
        {
            Title = action.GetString("title") ?? "提示",
            Subtitle = message,
            IsLightDismissEnabled = true,
            PlacementMargin = new Thickness(16),
            XamlRoot = root.XamlRoot
        };
        // Anchor to the page root so it positions inside the stage rather than off-window.
        if (root is FrameworkElement fe) tip.Target = fe;
        tip.IsOpen = true;
    }

    private static void SetProperty(ActionDescriptor action, FrameworkElement root)
    {
        var name = action.GetString("property");
        var target = action.Target is null ? root : root.FindName(action.Target) as DependencyObject;
        if (target is null || name is null) return;

        // Minimum viable: support a few high-frequency properties without reflection (AOT-safe).
        if (target is FrameworkElement fe)
        {
            switch (name)
            {
                case "Visibility":
                    fe.Visibility = action.GetBool("value") == true ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case "Opacity" when action.GetDouble("value") is { } v:
                    fe.Opacity = v;
                    break;
                case "IsEnabled" when fe is Control c:
                    c.IsEnabled = action.GetBool("value") ?? true;
                    break;
            }
        }
    }
}

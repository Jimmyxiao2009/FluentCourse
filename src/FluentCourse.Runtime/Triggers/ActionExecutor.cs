using FluentCourse.Core.Models;
using FluentCourse.Runtime.Animations;
using FluentCourse.Runtime.Controls;
using FluentCourse.Runtime.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace FluentCourse.Runtime.Triggers;

/// <summary>
/// Dispatches a single <see cref="ActionDescriptor"/> to the right runtime call.
/// v1.1 adds media control, StepReveal, quiz control, variables, window, and nav extensions.
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
            // ── navigation ──
            case "GoToPage":
                if (action.GetString("page") is { } page) await player.GoToAsync(page);
                break;
            case "NextPage":
                await player.NextAsync();
                break;
            case "PreviousPage":
                await player.PreviousAsync();
                break;
            case "FirstPage":
                await player.GoToIndexAsync(player.FirstVisibleIndex());
                break;
            case "LastPage":
                {
                    for (int i = player.PageCount - 1; i >= 0; i--)
                        if (!player.Manifest.Pages[i].Hidden) { await player.GoToIndexAsync(i); break; }
                }
                break;
            case "GoBack":
                await player.GoBackAsync();
                break;

            // ── animation ──
            case "PlayAnimation":
                PlayAnimation(action, storyboards, player, pageRoot);
                break;
            case "StopAnimation":
                StopAnimation(action, player);
                break;
            case "PauseAnimation":
                PauseAnimation(action, player);
                break;
            case "ResumeAnimation":
                ResumeAnimation(action, player);
                break;

            // ── state ──
            case "GoToState":
                GoToState(action, pageRoot);
                break;
            case "ToggleState":
                ToggleState(action, pageRoot);
                break;

            // ── media ──
            case "PlayMedia":
                MediaPlay(action, pageRoot);
                break;
            case "PauseMedia":
                MediaPause(action, pageRoot);
                break;
            case "StopMedia":
                MediaStop(action, pageRoot);
                break;
            case "SeekMedia":
                MediaSeek(action, pageRoot);
                break;
            case "SetVolume":
                MediaSetVolume(action, pageRoot);
                break;
            case "Mute":
                MediaMute(action, pageRoot);
                break;

            // ── StepReveal ──
            case "NextStep":
                if (FindByName(pageRoot, action.Target) is StepRevealControl src) src.Next();
                break;
            case "PreviousStep":
                if (FindByName(pageRoot, action.Target) is StepRevealControl src2) src2.Previous();
                break;
            case "ResetSteps":
                if (FindByName(pageRoot, action.Target) is StepRevealControl src3) src3.Reset();
                break;
            case "GoToStep":
                {
                    if (FindByName(pageRoot, action.Target) is StepRevealControl src4 &&
                        action.GetInt("step") is { } s) src4.CurrentStep = s;
                }
                break;

            // ── quiz ──
            case "SubmitQuiz":
                // 直接调用控件公开方法;模板部件(PART_Submit)在控件命名作用域里
                // FindName 找不到,且按钮只挂了 Click 没有 Command。
                if (FindByName(pageRoot, action.Target) is QuizControl qc) qc.Submit();
                break;
            case "ResetQuiz":
                if (FindByName(pageRoot, action.Target) is QuizControl qc2) qc2.Reset();
                break;
            case "ShowFeedback":
                if (FindByName(pageRoot, action.Target) is QuizControl qc3) qc3.ShowFeedback = true;
                break;
            case "HideFeedback":
                if (FindByName(pageRoot, action.Target) is QuizControl qc4) qc4.ShowFeedback = false;
                break;

            // ── variables ──
            case "SetVariable":
                {
                    if (action.GetString("name") is { } n)
                    {
                        if (action.GetDouble("value") is { } dv) player.SetVariable(n, dv);
                        else if (action.GetBool("value") is { } bv) player.SetVariable(n, bv);
                        else if (action.GetString("value") is { } sv) player.SetVariable(n, sv);
                    }
                }
                break;
            case "IncrementVariable":
                {
                    if (action.GetString("name") is { } n2)
                    {
                        var by = action.GetDouble("by") ?? 1;
                        var cur = player.GetVariable<double>(n2);
                        player.SetVariable(n2, cur + by);
                    }
                }
                break;
            case "ToggleVariable":
                {
                    if (action.GetString("name") is { } n3)
                    {
                        var cur = player.GetVariable<bool>(n3);
                        player.SetVariable(n3, !cur);
                    }
                }
                break;

            // ── window ──
            case "OpenHyperlink":
                {
                    if (action.GetString("url") is { } url)
                        _ = Launcher.LaunchUriAsync(new Uri(url));
                }
                break;
            case "OpenFile":
                {
                    if (action.GetString("path") is { } path)
                        _ = Launcher.LaunchFileAsync(
                            await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
                }
                break;
            case "ExitPresentation":
                Application.Current.Exit();
                break;
            case "ToggleFullscreen":
                // Delegated to the host window; the host must set its own handler
                break;

            // ── legacy ──
            case "ShowToast":
                ShowToast(action, pageRoot);
                break;
            case "SetProperty":
                SetProperty(action, pageRoot);
                break;

            default:
                player.Manifest.AddWarning("UNKNOWN_ACTION_TYPE", $"未知动作类型: {action.Type}", $"Action Target: {action.Target}");
                System.Diagnostics.Debug.WriteLine($"[ActionExecutor] Unknown type: {action.Type}");
                break;
        }
    }

    // ── animation control ──

    private static void PlayAnimation(ActionDescriptor action, StoryboardFactory storyboards,
        PagePlayer player, FrameworkElement root)
    {
        var name = action.GetString("animation") ?? "FadeIn";
        var target = action.Target is null ? root : FindByName(root, action.Target);
        if (target is null) return;

        var sb = storyboards.Build(name, target);
        if (action.Target is { } t)
            player.CacheStoryboard(t, name, sb);
        sb.Begin();
    }

    private static void StopAnimation(ActionDescriptor action, PagePlayer player)
    {
        var sb = player.GetCachedStoryboard(action.Target ?? "", action.GetString("animation") ?? "");
        sb?.Stop();
    }

    private static void PauseAnimation(ActionDescriptor action, PagePlayer player)
    {
        var sb = player.GetCachedStoryboard(action.Target ?? "", action.GetString("animation") ?? "");
        sb?.Pause();
    }

    private static void ResumeAnimation(ActionDescriptor action, PagePlayer player)
    {
        var sb = player.GetCachedStoryboard(action.Target ?? "", action.GetString("animation") ?? "");
        sb?.Resume();
    }

    // ── state ──

    private static void GoToState(ActionDescriptor action, FrameworkElement root)
    {
        var state = action.GetString("state");
        if (state is null) return;
        var target = (action.Target is null ? root : FindByName(root, action.Target)) ?? root;
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

    private static void ToggleState(ActionDescriptor action, FrameworkElement root)
    {
        var state = action.GetString("state");
        if (state is null) return;
        var target = (action.Target is null ? root : FindByName(root, action.Target)) ?? root;
        Control? ctrl = target as Control;
        if (ctrl is null) return;
        VisualStateManager.GoToState(ctrl, state, useTransitions: true);
    }

    // ── media ──

    private static void MediaPlay(ActionDescriptor action, FrameworkElement root)
    {
        if (FindByName(root, action.Target) is Microsoft.UI.Xaml.Controls.MediaPlayerElement me &&
            me.MediaPlayer is { } mp)
            mp.Play();
    }

    private static void MediaPause(ActionDescriptor action, FrameworkElement root)
    {
        if (FindByName(root, action.Target) is Microsoft.UI.Xaml.Controls.MediaPlayerElement me &&
            me.MediaPlayer is { } mp)
            mp.Pause();
    }

    private static void MediaStop(ActionDescriptor action, FrameworkElement root)
    {
        if (FindByName(root, action.Target) is Microsoft.UI.Xaml.Controls.MediaPlayerElement me &&
            me.MediaPlayer is { } mp)
        {
            mp.Pause();
            mp.Position = TimeSpan.Zero;
        }
    }

    private static void MediaSeek(ActionDescriptor action, FrameworkElement root)
    {
        if (action.GetDouble("to") is { } sec &&
            FindByName(root, action.Target) is Microsoft.UI.Xaml.Controls.MediaPlayerElement me &&
            me.MediaPlayer is { } mp)
            mp.Position = TimeSpan.FromSeconds(sec);
    }

    private static void MediaSetVolume(ActionDescriptor action, FrameworkElement root)
    {
        if (action.GetDouble("value") is { } vol &&
            FindByName(root, action.Target) is Microsoft.UI.Xaml.Controls.MediaPlayerElement me &&
            me.MediaPlayer is { } mp)
            mp.Volume = Math.Clamp(vol, 0, 1);
    }

    private static void MediaMute(ActionDescriptor action, FrameworkElement root)
    {
        if (FindByName(root, action.Target) is Microsoft.UI.Xaml.Controls.MediaPlayerElement me &&
            me.MediaPlayer is { } mp)
            mp.IsMuted = action.GetBool("value") ?? true;
    }

    // ── toast ──

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
        if (root is FrameworkElement fe) tip.Target = fe;
        tip.IsOpen = true;
    }

    // ── SetProperty ──

    private static void SetProperty(ActionDescriptor action, FrameworkElement root)
    {
        var name = action.GetString("property");
        var target = action.Target is null ? root : FindByName(root, action.Target);
        if (target is null || name is null) return;

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

    // ── helpers ──

    private static FrameworkElement? FindByName(FrameworkElement root, string? name) =>
        string.IsNullOrEmpty(name) ? null : root.FindName(name) as FrameworkElement;
}

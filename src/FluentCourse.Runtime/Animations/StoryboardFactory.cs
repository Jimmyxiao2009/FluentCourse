using FluentCourse.Core.Models;
using FluentCourse.Runtime.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace FluentCourse.Runtime.Animations;

/// <summary>
/// Builds Storyboards from <see cref="AnimationDescriptor"/> at runtime. The factory is reusable —
/// each Build call returns a fresh Storyboard targeting the supplied element.
/// </summary>
public sealed class StoryboardFactory
{
    private readonly Dictionary<string, AnimationDescriptor> _animations;

    public StoryboardFactory(Dictionary<string, AnimationDescriptor> animations)
    {
        _animations = MergePresets(animations);
    }

    public bool Has(string name) => _animations.ContainsKey(name);

    public Storyboard Build(string animationName, FrameworkElement target)
    {
        if (!_animations.TryGetValue(animationName, out var desc))
            throw new KeyNotFoundException($"Animation '{animationName}' not declared in manifest.");
        return Build(desc, target);
    }

    public Storyboard Build(AnimationDescriptor desc, FrameworkElement target)
    {
        EnsureCompositeTransform(target);
        var sb = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(desc.DurationMs);
        var beginTime = TimeSpan.FromMilliseconds(desc.DelayMs);
        var easing = EasingParser.Parse(desc.Easing);
        var repeat = desc.Repeat <= 0
            ? RepeatBehavior.Forever
            : new RepeatBehavior(desc.Repeat);

        foreach (var track in desc.Tracks)
            sb.Children.Add(BuildTrack(track, target, duration, beginTime, easing, repeat, desc.AutoReverse));

        return sb;
    }

    private static Timeline BuildTrack(
        AnimationTrack track,
        FrameworkElement target,
        TimeSpan duration,
        TimeSpan beginTime,
        EasingFunctionBase? easing,
        RepeatBehavior repeat,
        bool autoReverse)
    {
        if (track.Property == "Color")
            return BuildColorTrack(track, target, duration, beginTime, easing, repeat, autoReverse);

        var path = ResolvePropertyPath(track.Property);
        var anim = new DoubleAnimation
        {
            From = track.From,
            To = track.To ?? track.From,
            Duration = duration,
            BeginTime = beginTime,
            EasingFunction = easing,
            RepeatBehavior = repeat,
            AutoReverse = autoReverse
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, path);
        return anim;
    }

    private static Timeline BuildColorTrack(
        AnimationTrack track,
        FrameworkElement target,
        TimeSpan duration,
        TimeSpan beginTime,
        EasingFunctionBase? easing,
        RepeatBehavior repeat,
        bool autoReverse)
    {
        var anim = new ColorAnimation
        {
            From = track.FromColor is { } f ? ColorParser.Parse(f) : null,
            To = track.ToColor is { } t ? ColorParser.Parse(t) : default,
            Duration = duration,
            BeginTime = beginTime,
            EasingFunction = easing,
            RepeatBehavior = repeat,
            AutoReverse = autoReverse
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, "(Control.Foreground).(SolidColorBrush.Color)");
        return anim;
    }

    private static string ResolvePropertyPath(string property) => property switch
    {
        "Opacity"     => "(UIElement.Opacity)",
        "TranslateX"  => "(UIElement.RenderTransform).(CompositeTransform.TranslateX)",
        "TranslateY"  => "(UIElement.RenderTransform).(CompositeTransform.TranslateY)",
        "ScaleX"      => "(UIElement.RenderTransform).(CompositeTransform.ScaleX)",
        "ScaleY"      => "(UIElement.RenderTransform).(CompositeTransform.ScaleY)",
        "Rotation"    => "(UIElement.RenderTransform).(CompositeTransform.Rotation)",
        "Width"       => "(FrameworkElement.Width)",
        "Height"      => "(FrameworkElement.Height)",
        "FontSize"    => "(Control.FontSize)",
        "CornerRadius" => "(Microsoft.UI.Xaml.Controls.Border.CornerRadius)",
        "BackgroundColor" => "(Panel.Background).(SolidColorBrush.Color)",
        "BorderColor"    => "(Border.BorderBrush).(SolidColorBrush.Color)",
        _ => throw new NotSupportedException($"Animation property '{property}' is not supported. " +
            "Supported: Opacity, TranslateX/Y, ScaleX/Y, Rotation, Width, Height, FontSize, " +
            "CornerRadius, BackgroundColor, BorderColor, Color.")
    };

    private static void EnsureCompositeTransform(FrameworkElement el)
    {
        if (el.RenderTransform is not CompositeTransform)
        {
            el.RenderTransform = new CompositeTransform();
            el.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        }
    }

    private static Dictionary<string, AnimationDescriptor> MergePresets(Dictionary<string, AnimationDescriptor> user)
    {
        var merged = new Dictionary<string, AnimationDescriptor>(StringComparer.Ordinal);
        foreach (var (k, v) in PresetAnimations.All) merged[k] = v;
        foreach (var (k, v) in user) merged[k] = v; // user wins
        return merged;
    }
}

internal static class EasingParser
{
    /// <summary>
    /// Returns null for Linear — WinUI Storyboard treats a null EasingFunction as linear,
    /// and <see cref="EasingFunctionBase.EaseInCore"/> is not virtual so we can't subclass.
    /// </summary>
    public static EasingFunctionBase? Parse(string name) => name switch
    {
        "Linear" => null,
        "EaseInQuad" => new QuadraticEase { EasingMode = EasingMode.EaseIn },
        "EaseOutQuad" => new QuadraticEase { EasingMode = EasingMode.EaseOut },
        "EaseInOutQuad" => new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        "EaseInCubic" => new CubicEase { EasingMode = EasingMode.EaseIn },
        "EaseOutCubic" => new CubicEase { EasingMode = EasingMode.EaseOut },
        "EaseInOutCubic" => new CubicEase { EasingMode = EasingMode.EaseInOut },
        "EaseInCirc" => new CircleEase { EasingMode = EasingMode.EaseIn },
        "EaseOutCirc" => new CircleEase { EasingMode = EasingMode.EaseOut },
        "EaseInBack" => new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.6 },
        "EaseOutBack" => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 },
        "EaseInElastic" => new ElasticEase { EasingMode = EasingMode.EaseIn, Oscillations = 2, Springiness = 4 },
        "EaseOutElastic" => new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 4 },
        "EaseInBounce" => new BounceEase { EasingMode = EasingMode.EaseIn, Bounces = 2, Bounciness = 2 },
        "EaseOutBounce" => new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 2, Bounciness = 2 },
        _ => new CubicEase { EasingMode = EasingMode.EaseOut }
    };
}

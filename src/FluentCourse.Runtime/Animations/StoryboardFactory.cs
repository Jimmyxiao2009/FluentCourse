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
    private readonly Action<string, string, string?>? _addWarning;

    public StoryboardFactory(Dictionary<string, AnimationDescriptor> animations, Action<string, string, string?>? addWarning = null)
    {
        _animations = MergePresets(animations);
        _addWarning = addWarning;
    }

    public bool Has(string name) => _animations.ContainsKey(name);

    public Storyboard Build(string animationName, FrameworkElement target)
    {
        if (!_animations.TryGetValue(animationName, out var desc))
            throw new KeyNotFoundException($"Animation '{animationName}' not declared in manifest.");
        return BuildInternal(desc, target, animationName);
    }

    public Storyboard Build(AnimationDescriptor desc, FrameworkElement target)
    {
        return BuildInternal(desc, target, null);
    }

    private Storyboard BuildInternal(AnimationDescriptor desc, FrameworkElement target, string? animationName)
    {
        EnsureCompositeTransform(target);
        var sb = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(desc.DurationMs);
        var beginTime = TimeSpan.FromMilliseconds(desc.DelayMs);
        
        var easing = EasingParser.Parse(desc.Easing, name =>
        {
            _addWarning?.Invoke("UNKNOWN_EASING", $"未知缓动函数: {name}，已回退到 EaseOutCubic", $"Animation: {animationName ?? "Inline"}");
            System.Diagnostics.Debug.WriteLine($"[StoryboardFactory] 未知缓动函数: {name}，已回退到 EaseOutCubic");
        });

        var repeat = desc.Repeat <= 0
            ? RepeatBehavior.Forever
            : new RepeatBehavior(desc.Repeat);

        foreach (var track in desc.Tracks)
        {
            var timeline = BuildTrack(track, target, duration, beginTime, easing, repeat, desc.AutoReverse);
            if (timeline is not null) sb.Children.Add(timeline);
        }

        return sb;
    }

    private static Timeline? BuildTrack(
        AnimationTrack track,
        FrameworkElement target,
        TimeSpan duration,
        TimeSpan beginTime,
        EasingFunctionBase? easing,
        RepeatBehavior repeat,
        bool autoReverse)
    {
        if (track.Property is "Color" or "BackgroundColor" or "BorderColor")
            return BuildColorTrack(track, target, duration, beginTime, easing, repeat, autoReverse);

        var path = ResolvePropertyPath(track.Property);
        if (path is null) return null;
        var anim = new DoubleAnimation
        {
            From = track.From,
            To = track.To ?? track.From,
            Duration = duration,
            BeginTime = beginTime,
            EasingFunction = easing,
            RepeatBehavior = repeat,
            AutoReverse = autoReverse,
            // 布局属性(Width/Height/FontSize 等)是依赖动画,不开启会被系统静默跳过。
            // 对 Opacity/Transform 等合成属性开启也无害,统一打开。
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, path);
        return anim;
    }

    private static Timeline? BuildColorTrack(
        AnimationTrack track,
        FrameworkElement target,
        TimeSpan duration,
        TimeSpan beginTime,
        EasingFunctionBase? easing,
        RepeatBehavior repeat,
        bool autoReverse)
    {
        var path = ResolveColorPath(track.Property, target);
        if (path is null) return null;
        // 解析出属性路径后,确保目标画刷是该元素独占的可变 SolidColorBrush。
        // 否则:画刷为 null 时 ColorAnimation 静默无效;画刷被多元素共享时 .Color 动画会污染所有引用方。
        EnsureMutableSolidColorBrush(track.Property, target);
        var anim = new ColorAnimation
        {
            From = track.FromColor is { } f ? ColorParser.Parse(f) : null,
            To = track.ToColor is { } t ? ColorParser.Parse(t) : default,
            Duration = duration,
            BeginTime = beginTime,
            EasingFunction = easing,
            RepeatBehavior = repeat,
            AutoReverse = autoReverse,
            // ColorAnimation 全部是依赖动画,必须开启否则被系统静默跳过
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, path);
        return anim;
    }

    /// <summary>颜色轨道按目标实际类型选择属性路径,避免对 TextBlock 用 Control 路径解析失败。</summary>
    private static string? ResolveColorPath(string property, FrameworkElement target)
    {
        switch (property)
        {
            case "Color":
                return target switch
                {
                    Microsoft.UI.Xaml.Controls.TextBlock => "(TextBlock.Foreground).(SolidColorBrush.Color)",
                    Microsoft.UI.Xaml.Shapes.Shape => "(Shape.Fill).(SolidColorBrush.Color)",
                    Microsoft.UI.Xaml.Controls.Control => "(Control.Foreground).(SolidColorBrush.Color)",
                    _ => Warn($"Color 轨道不支持目标类型 {target.GetType().Name}")
                };
            case "BackgroundColor":
                return target switch
                {
                    Microsoft.UI.Xaml.Controls.Panel => "(Panel.Background).(SolidColorBrush.Color)",
                    Microsoft.UI.Xaml.Controls.Border => "(Border.Background).(SolidColorBrush.Color)",
                    Microsoft.UI.Xaml.Shapes.Shape => "(Shape.Fill).(SolidColorBrush.Color)",
                    Microsoft.UI.Xaml.Controls.Control => "(Control.Background).(SolidColorBrush.Color)",
                    _ => Warn($"BackgroundColor 轨道不支持目标类型 {target.GetType().Name}")
                };
            case "BorderColor":
                return target switch
                {
                    Microsoft.UI.Xaml.Controls.Border => "(Border.BorderBrush).(SolidColorBrush.Color)",
                    Microsoft.UI.Xaml.Shapes.Shape => "(Shape.Stroke).(SolidColorBrush.Color)",
                    Microsoft.UI.Xaml.Controls.Control => "(Control.BorderBrush).(SolidColorBrush.Color)",
                    _ => Warn($"BorderColor 轨道不支持目标类型 {target.GetType().Name}")
                };
            default:
                return Warn($"未知颜色轨道属性 '{property}'");
        }

        static string? Warn(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[StoryboardFactory] {message},跳过该轨道。");
            return null;
        }
    }

    /// <summary>
    /// 颜色动画版的 EnsureCompositeTransform:在构建 ColorAnimation 前,确保目标画刷属性是该元素
    /// 独占的可变 <see cref="SolidColorBrush"/>。分支与属性集合必须与 <see cref="ResolveColorPath"/> 一一对应。
    /// </summary>
    private static void EnsureMutableSolidColorBrush(string property, FrameworkElement target)
    {
        switch (property)
        {
            case "Color":
                switch (target)
                {
                    case Microsoft.UI.Xaml.Controls.TextBlock tb:
                        tb.Foreground = Mutable(tb.Foreground); break;
                    case Microsoft.UI.Xaml.Shapes.Shape sh:
                        sh.Fill = Mutable(sh.Fill); break;
                    case Microsoft.UI.Xaml.Controls.Control c:
                        c.Foreground = Mutable(c.Foreground); break;
                }
                break;
            case "BackgroundColor":
                switch (target)
                {
                    case Microsoft.UI.Xaml.Controls.Panel p:
                        p.Background = Mutable(p.Background); break;
                    case Microsoft.UI.Xaml.Controls.Border b:
                        b.Background = Mutable(b.Background); break;
                    case Microsoft.UI.Xaml.Shapes.Shape sh:
                        sh.Fill = Mutable(sh.Fill); break;
                    case Microsoft.UI.Xaml.Controls.Control c:
                        c.Background = Mutable(c.Background); break;
                }
                break;
            case "BorderColor":
                switch (target)
                {
                    case Microsoft.UI.Xaml.Controls.Border b:
                        b.BorderBrush = Mutable(b.BorderBrush); break;
                    case Microsoft.UI.Xaml.Shapes.Shape sh:
                        sh.Stroke = Mutable(sh.Stroke); break;
                    case Microsoft.UI.Xaml.Controls.Control c:
                        c.BorderBrush = Mutable(c.BorderBrush); break;
                }
                break;
        }

        // 返回一个该元素独占、可被 ColorAnimation 安全驱动的 SolidColorBrush:
        //  - null:新建透明画刷,使 From/To 都能从一个确定的起点生效(Transparent 不会在动画前闪出实色);
        //  - 已是 SolidColorBrush:克隆一份(保留原色),避免与其它元素共享同一实例时被动画污染;
        //  - 其它画刷类型(渐变等):ColorAnimation 本就无法驱动,原样返回不做破坏。
        static Brush? Mutable(Brush? existing) => existing switch
        {
            null => new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            SolidColorBrush scb => new SolidColorBrush(scb.Color),
            _ => existing
        };
    }

    private static string? ResolvePropertyPath(string property) => property switch
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
        // CornerRadius 是结构体,DoubleAnimation 无法对其插值,WinUI 也没有对应的动画类型。
        // 不支持该属性 —— 跳过并警告(docs/fcourse-format.md 已同步标注)。
        "CornerRadius" => WarnUnsupported("CornerRadius 是结构体属性,DoubleAnimation 无法驱动,已跳过该轨道"),
        _ => throw new NotSupportedException($"Animation property '{property}' is not supported. " +
            "Supported: Opacity, TranslateX/Y, ScaleX/Y, Rotation, Width, Height, FontSize, " +
            "BackgroundColor, BorderColor, Color.")
    };

    private static string? WarnUnsupported(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[StoryboardFactory] {message}。");
        return null;
    }

    private static void EnsureCompositeTransform(FrameworkElement el)
    {
        if (el.RenderTransform is CompositeTransform) return;

        var existing = el.RenderTransform;
        var composite = new CompositeTransform();
        TransformMigration.Migrate(existing, composite);
        el.RenderTransform = composite;
        // 仅在原本没有任何变换时设置居中原点,避免覆盖页面 XAML 自定义的原点
        if (existing is null)
            el.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
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
    private static readonly HashSet<string> ValidEasings = new(StringComparer.OrdinalIgnoreCase)
    {
        "Linear", "EaseInQuad", "EaseOutQuad", "EaseInOutQuad", "EaseInCubic", "EaseOutCubic", "EaseInOutCubic",
        "EaseInCirc", "EaseOutCirc", "EaseInBack", "EaseOutBack", "EaseInElastic", "EaseOutElastic", "EaseInBounce", "EaseOutBounce"
    };

    /// <summary>
    /// Returns null for Linear — WinUI Storyboard treats a null EasingFunction as linear,
    /// and <see cref="EasingFunctionBase.EaseInCore"/> is not virtual so we can't subclass.
    /// </summary>
    public static EasingFunctionBase? Parse(string name, Action<string>? onWarning = null)
    {
        if (!string.IsNullOrEmpty(name) && !ValidEasings.Contains(name))
        {
            onWarning?.Invoke(name);
        }

        return name switch
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
}

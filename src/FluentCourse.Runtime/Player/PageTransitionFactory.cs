using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace FluentCourse.Runtime.Player;

/// <summary>
/// Built-in page transitions. Each method either returns a Storyboard ready to begin,
/// or null if there's nothing to animate (e.g. "None", or first-page entry with no outgoing).
/// </summary>
internal static class PageTransitionFactory
{
    private const double DefaultMs = 320;

    public static Storyboard? None(FrameworkElement? from, FrameworkElement to)
    {
        to.Opacity = 1;
        return null;
    }

    public static Storyboard? Fade(FrameworkElement? from, FrameworkElement to)
    {
        var sb = new Storyboard();
        if (from is not null) sb.Children.Add(BuildOpacity(from, 1, 0, DefaultMs));
        sb.Children.Add(BuildOpacity(to, 0, 1, DefaultMs));
        return sb;
    }

    public static Storyboard? Zoom(FrameworkElement? from, FrameworkElement to)
    {
        EnsureScaleTransform(to);
        var sb = new Storyboard();
        sb.Children.Add(BuildOpacity(to, 0, 1, DefaultMs));
        sb.Children.Add(BuildScale(to, "ScaleX", 1.04, 1.0, DefaultMs));
        sb.Children.Add(BuildScale(to, "ScaleY", 1.04, 1.0, DefaultMs));
        if (from is not null) sb.Children.Add(BuildOpacity(from, 1, 0, DefaultMs));
        return sb;
    }

    public static Storyboard? SlidePush(FrameworkElement? from, FrameworkElement to)
    {
        EnsureTranslateTransform(to);
        var width = to.ActualWidth > 0 ? to.ActualWidth : 1920;

        var sb = new Storyboard();
        sb.Children.Add(BuildTranslate(to, "X", width, 0, DefaultMs));
        if (from is not null)
        {
            EnsureTranslateTransform(from);
            sb.Children.Add(BuildTranslate(from, "X", 0, -width, DefaultMs));
        }
        return sb;
    }

    private static DoubleAnimation BuildOpacity(FrameworkElement el, double from, double to, double ms)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(ms),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, "(UIElement.Opacity)");
        return anim;
    }

    private static DoubleAnimation BuildScale(FrameworkElement el, string axis, double from, double to, double ms)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(ms),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, $"(UIElement.RenderTransform).(CompositeTransform.{axis})");
        return anim;
    }

    private static DoubleAnimation BuildTranslate(FrameworkElement el, string axis, double from, double to, double ms)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(ms),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, $"(UIElement.RenderTransform).(CompositeTransform.Translate{axis})");
        return anim;
    }

    private static void EnsureScaleTransform(FrameworkElement el)
    {
        if (el.RenderTransform is not CompositeTransform)
        {
            el.RenderTransform = new CompositeTransform();
            el.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        }
    }

    private static void EnsureTranslateTransform(FrameworkElement el)
    {
        if (el.RenderTransform is not CompositeTransform) el.RenderTransform = new CompositeTransform();
    }
}

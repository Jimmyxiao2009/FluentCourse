using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace FluentCourse.Runtime.Player;

internal static class PageTransitionFactory
{
    public static Storyboard? None(FrameworkElement? from, FrameworkElement to)
    {
        to.Opacity = 1;
        return null;
    }

    // ── cover / reveal (standard left-right) ──

    public static Storyboard? Fade(FrameworkElement? from, FrameworkElement to, double ms = 320)
    {
        var sb = new Storyboard();
        if (from is not null) sb.Children.Add(BuildOpacity(from, 1, 0, ms));
        sb.Children.Add(BuildOpacity(to, 0, 1, ms));
        return sb;
    }

    public static Storyboard? Zoom(FrameworkElement? from, FrameworkElement to, double ms = 320)
    {
        EnsureCompositeTransform(to);
        var sb = new Storyboard();
        sb.Children.Add(BuildOpacity(to, 0, 1, ms));
        sb.Children.Add(BuildScale(to, "ScaleX", 1.04, 1.0, ms));
        sb.Children.Add(BuildScale(to, "ScaleY", 1.04, 1.0, ms));
        if (from is not null) sb.Children.Add(BuildOpacity(from, 1, 0, ms));
        return sb;
    }

    public static Storyboard? SlidePush(FrameworkElement? from, FrameworkElement to, double ms = 320)
    {
        EnsureTranslateTransform(to);
        var width = to.ActualWidth > 0 ? to.ActualWidth : 1920;
        var sb = new Storyboard();
        sb.Children.Add(BuildTranslate(to, "X", width, 0, ms));
        if (from is not null)
        {
            EnsureTranslateTransform(from);
            sb.Children.Add(BuildTranslate(from, "X", 0, -width, ms));
        }
        return sb;
    }

    // ── directional slides (v1.1) ──

    public static Storyboard? SlideUp(FrameworkElement? from, FrameworkElement to, double ms = 320)
    {
        return SlideDirectional(from, to, "Y", 1, 0, ms);
    }

    public static Storyboard? SlideDown(FrameworkElement? from, FrameworkElement to, double ms = 320)
    {
        return SlideDirectional(from, to, "Y", -1, 0, ms);
    }

    public static Storyboard? SlideLeft(FrameworkElement? from, FrameworkElement to, double ms = 320)
    {
        return SlideDirectional(from, to, "X", 1, 0, ms);
    }

    public static Storyboard? SlideRight(FrameworkElement? from, FrameworkElement to, double ms = 320)
    {
        return SlideDirectional(from, to, "X", -1, 0, ms);
    }

    private static Storyboard? SlideDirectional(
        FrameworkElement? from, FrameworkElement to, string axis,
        double toMultiplier, double toTarget, double ms)
    {
        EnsureTranslateTransform(to);
        var extent = axis == "X"
            ? (to.ActualWidth > 0 ? to.ActualWidth : 1920)
            : (to.ActualHeight > 0 ? to.ActualHeight : 1080);

        var sb = new Storyboard();
        sb.Children.Add(BuildTranslate(to, axis, extent * toMultiplier, toTarget, ms));
        sb.Children.Add(BuildOpacity(to, 0, 1, ms));
        if (from is not null)
        {
            EnsureTranslateTransform(from);
            sb.Children.Add(BuildTranslate(from, axis, 0, -extent * toMultiplier, ms));
        }
        return sb;
    }

    // ── helpers ──

    private static DoubleAnimation BuildOpacity(FrameworkElement el, double from, double to, double ms)
    {
        var anim = new DoubleAnimation
        {
            From = from, To = to,
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
            From = from, To = to,
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
            From = from, To = to,
            Duration = TimeSpan.FromMilliseconds(ms),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, $"(UIElement.RenderTransform).(CompositeTransform.Translate{axis})");
        return anim;
    }

    private static void EnsureCompositeTransform(FrameworkElement el)
    {
        if (el.RenderTransform is not CompositeTransform)
        {
            el.RenderTransform = new CompositeTransform();
            el.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        }
    }

    private static void EnsureTranslateTransform(FrameworkElement el)
    {
        if (el.RenderTransform is not CompositeTransform)
            el.RenderTransform = new CompositeTransform();
    }
}

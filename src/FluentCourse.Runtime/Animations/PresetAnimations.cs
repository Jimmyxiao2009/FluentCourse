using FluentCourse.Core.Models;

namespace FluentCourse.Runtime.Animations;

/// <summary>Built-in Fluent-style animations. User-defined entries override by name.</summary>
internal static class PresetAnimations
{
    public static readonly IReadOnlyDictionary<string, AnimationDescriptor> All = new Dictionary<string, AnimationDescriptor>
    {
        ["FadeIn"] = new()
        {
            DurationMs = 320,
            Easing = "EaseOutCubic",
            Tracks = { new() { Property = "Opacity", From = 0, To = 1 } }
        },
        ["FadeOut"] = new()
        {
            DurationMs = 240,
            Easing = "EaseInCubic",
            Tracks = { new() { Property = "Opacity", From = 1, To = 0 } }
        },
        ["FadeInUp"] = new()
        {
            DurationMs = 480,
            Easing = "EaseOutCubic",
            Tracks =
            {
                new() { Property = "Opacity", From = 0, To = 1 },
                new() { Property = "TranslateY", From = 24, To = 0 }
            }
        },
        ["FadeInDown"] = new()
        {
            DurationMs = 480,
            Easing = "EaseOutCubic",
            Tracks =
            {
                new() { Property = "Opacity", From = 0, To = 1 },
                new() { Property = "TranslateY", From = -24, To = 0 }
            }
        },
        ["ZoomIn"] = new()
        {
            DurationMs = 360,
            Easing = "EaseOutBack",
            Tracks =
            {
                new() { Property = "Opacity", From = 0, To = 1 },
                new() { Property = "ScaleX", From = 0.85, To = 1 },
                new() { Property = "ScaleY", From = 0.85, To = 1 }
            }
        },
        ["Pulse"] = new()
        {
            DurationMs = 600,
            Easing = "EaseInOutCubic",
            Repeat = 1,
            AutoReverse = true,
            Tracks =
            {
                new() { Property = "ScaleX", From = 1, To = 1.06 },
                new() { Property = "ScaleY", From = 1, To = 1.06 }
            }
        },
        ["Shake"] = new()
        {
            DurationMs = 320,
            Easing = "Linear",
            Tracks = { new() { Property = "TranslateX", From = -8, To = 8 } },
            Repeat = 4,
            AutoReverse = true
        }
    };
}

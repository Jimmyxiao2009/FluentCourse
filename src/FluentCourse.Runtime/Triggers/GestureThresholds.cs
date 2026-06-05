namespace FluentCourse.Runtime.Triggers;

internal static class GestureThresholds
{
    /// <summary>Minimum displacement (px) for a swipe gesture to fire.</summary>
    public const double SwipeDisplacement = 80;

    /// <summary>Minimum displacement (px) before a manipulation is considered a drag.</summary>
    public const double DragStart = 10;

    /// <summary>For swipe velocity classification (px/ms, unused in v1.1).</summary>
    public const double SwipeVelocity = 0.5;

    /// <summary>Debounce window (ms) for Scroll events.</summary>
    public const int ScrollDebounceMs = 100;
}

using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace FluentCourse.Runtime.Presenter;

/// <summary>
/// Picks display areas for the audience and presenter windows. Falls back gracefully when
/// only one monitor is connected — both windows go on the primary display, presenter
/// minimized so the user can resize it.
/// </summary>
public static class DualScreenLayout
{
    /// <summary>
    /// Returns (audienceArea, presenterArea). When a second display exists, audience goes there
    /// in fullscreen; the presenter stays on the primary.
    /// </summary>
    public static (DisplayArea audience, DisplayArea presenter) Pick()
    {
        var primary = DisplayArea.Primary;
        var primaryId = primary.DisplayId.Value;

        // Iterate the WinRT-projected IReadOnlyList by index. Calling .ToList() / LINQ here
        // throws InvalidCastException on Windows App SDK 1.6 — the projected enumerator's
        // Make_IEnumerableObjRef casts each element via the wrong IID.
        DisplayArea? secondary = null;
        var all = DisplayArea.FindAll();
        for (int i = 0; i < all.Count; i++)
        {
            var d = all[i];
            if (d.DisplayId.Value != primaryId)
            {
                secondary = d;
                break;
            }
        }

        return secondary is null ? (primary, primary) : (secondary, primary);
    }

    /// <summary>Maximizes <paramref name="window"/> onto the given display area.</summary>
    public static void MoveToFullScreen(Microsoft.UI.Xaml.Window window, DisplayArea area)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var aw = AppWindow.GetFromWindowId(id);
        var bounds = area.WorkArea;
        aw.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height), area);

        if (aw.Presenter is OverlappedPresenter op)
        {
            op.IsResizable = true;
            op.Maximize();
        }
    }
}

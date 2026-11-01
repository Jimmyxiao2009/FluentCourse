namespace FluentCourse.Designer.Services;

/// <summary>
/// Process-wide stash for the toolbox item currently being dragged. Used because
/// <c>DataPackageView.Properties</c> only exposes standard formats reliably across
/// drag/drop in WinUI 3 — for custom objects we hand the reference through this
/// static and clear it in <c>Drop</c> / <c>DragLeave</c>.
/// </summary>
public static class DragState
{
    public static ToolboxItem? Current { get; set; }
}

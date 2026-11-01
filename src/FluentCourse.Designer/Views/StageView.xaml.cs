using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentCourse.Designer.Models;
using FluentCourse.Designer.Services;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Storage;
using Windows.UI;

namespace FluentCourse.Designer.Views;

public sealed partial class StageView : UserControl
{
    private ShellViewModel? _vm;

    // ElementModel → rendered FrameworkElement (for UpdateSelectionOverlay)
    private readonly Dictionary<ElementModel, FrameworkElement> _elementMap = new();
    // FrameworkElement → its ElementModel (for click-to-select)
    private readonly Dictionary<FrameworkElement, ElementModel> _reverseMap = new();
    // FrameworkElement → 文档顺序号（命中测试用：靠后的兄弟渲染在上层，应优先被选中）
    private readonly Dictionary<FrameworkElement, int> _hitOrder = new();
    private int _hitOrderCounter;
    private FrameworkElement? _currentRoot;
    private bool _showGrid = true;

    // 按绝对路径缓存已解码的位图：每次属性改动都会整页 RenderCurrentPage，若每次都重新从磁盘
    // 解码所有图片会明显卡顿。同一路径视为同一图（符合设计器编辑场景），资源被替换时调
    // InvalidateImageCache 清空。
    private readonly Dictionary<string, Microsoft.UI.Xaml.Media.Imaging.BitmapImage> _imageCache = new();

    // ── Drag state ──
    private bool _isDragging;
    private FrameworkElement? _dragFe;
    private ElementModel? _dragEm;
    private Transform? _dragOriginalTransform;   // 元素原有的旋转/缩放变换，拖动结束后恢复
    private TranslateTransform? _dragTranslate;  // 叠加在原变换之上的拖动位移
    private Point _dragStartPointer;      // in StageRoot coordinates
    private double _dragStartEmX;
    private double _dragStartEmY;
    private List<(ElementModel Element, double X, double Y)> _dragGroupStart = new();
    private Point _dragStartBounds;       // initial top-left of element in StageRoot coords
    private double _dragStartEmWidth;     // element width at drag start
    private double _dragStartEmHeight;    // element height at drag start

    // ── Auto-align snapping (drag) ──
    private readonly List<double> _snapXs = new();   // vertical guide x-positions (StageRoot coords)
    private readonly List<double> _snapYs = new();   // horizontal guide y-positions
    private double _lastDx, _lastDy;                 // snapped delta committed on release
    private const double SnapThreshold = 6;

    // ── Resize state ──
    private FrameworkElement? _resizeHandle;   // the grabbed handle (null = not resizing)
    private FrameworkElement? _resizeFe;       // element being resized
    private ElementModel? _resizeEm;
    private Point _resizeStartPointer;         // in StageRoot coords
    private double _resizeStartX, _resizeStartY, _resizeStartW, _resizeStartH;
    private Rect _resizeGroupBounds;
    private List<(ElementModel Element, double X, double Y, double W, double H)> _resizeGroupStart = new();

    // ── Inline text edit (double-click) ──
    private TextBox? _inlineEdit;
    private ElementModel? _inlineEm;
    private string? _inlineKey;

    public StageView()
    {
        try { InitializeComponent(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[StageView] init failed: {ex}"); }
        Loaded += (_, _) =>
        {
            RenderRulers();
            // Intercept pointer events on StageRoot — including ones already marked Handled by
            // interactive controls (Button, TextBox, Slider, etc.) so they can still be dragged.
            // All three must use handledEventsToo: true; a control that captures the pointer also
            // marks Moved/Released as handled, which would starve the drag if we used CLR events.
            StageRoot.AddHandler(UIElement.PointerPressedEvent,
                new PointerEventHandler(OnStagePointerPressed), handledEventsToo: true);
            StageRoot.AddHandler(UIElement.PointerMovedEvent,
                new PointerEventHandler(OnStagePointerMoved), handledEventsToo: true);
            StageRoot.AddHandler(UIElement.PointerReleasedEvent,
                new PointerEventHandler(OnStagePointerReleased), handledEventsToo: true);
            StageRoot.AddHandler(UIElement.PointerCaptureLostEvent,
                new PointerEventHandler(OnStagePointerCaptureLost), handledEventsToo: true);
            StageRoot.AddHandler(UIElement.RightTappedEvent,
                new RightTappedEventHandler(OnStageRightTapped), handledEventsToo: true);
            StageRoot.AddHandler(UIElement.DoubleTappedEvent,
                new DoubleTappedEventHandler(OnStageDoubleTapped), handledEventsToo: true);
        };
        SizeChanged += (_, _) => RenderRulers();
    }

    public FrameworkElement? CurrentRoot => _currentRoot;

    // "订当前 + 跟随 DocumentChanged 重订（并解订旧文档）"：初始文档在 ShellViewModel 构造
    // 函数里就已创建（早于 DocumentChanged 事件），不立即订阅会导致启动后切页画布不刷新。
    private DesignerDocument? _observedDoc;

    public void SetViewModel(ShellViewModel vm)
    {
        _vm = vm;
        vm.DocumentChanged += (_, _) =>
        {
            HookDocument();
            RenderCurrentPage();
        };
        vm.SelectionChanged += (_, _) => UpdateSelectionOverlay();
        HookDocument();
        RenderCurrentPage();
    }

    private void HookDocument()
    {
        if (_observedDoc is not null) _observedDoc.PropertyChanged -= OnDocumentPropertyChanged;
        _observedDoc = _vm?.Document;
        if (_observedDoc is not null) _observedDoc.PropertyChanged += OnDocumentPropertyChanged;
    }

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesignerDocument.SelectedPage))
            RenderCurrentPage();
    }

    /// <summary>舞台设计尺寸（可在“课程设置”里改），取自 manifest；Document/Manifest 为空时回退 1920×1080。</summary>
    private (double width, double height) StageDesignSize()
    {
        var stage = _vm?.Document?.Manifest?.Stage;
        if (stage is null) return (1920, 1080);
        var w = stage.DesignWidth  > 0 ? stage.DesignWidth  : 1920;
        var h = stage.DesignHeight > 0 ? stage.DesignHeight : 1080;
        return (w, h);
    }

    public void ApplyZoom(double zoom)
    {
        var (w, h) = StageDesignSize();
        StageScaler.Width = w * zoom;
        StageScaler.Height = h * zoom;
    }

    // ── Auto-fit: size the page to the available canvas area so it isn't huge by default ──
    private bool _autoFit = true;

    /// <summary>Re-enable auto-fit and immediately fit (call on open / new document).</summary>
    public void EnableAutoFit() { _autoFit = true; FitToViewport(); }

    /// <summary>Stop auto-fitting (call when the user manually zooms, so we respect their choice).</summary>
    public void DisableAutoFit() => _autoFit = false;

    private void OnCanvasScrollSizeChanged(object sender, SizeChangedEventArgs e) => FitToViewport();

    public void FitToViewport()
    {
        if (!_autoFit || _vm is null) return;
        double vw = CanvasScroll.ViewportWidth, vh = CanvasScroll.ViewportHeight;
        if (vw <= 0 || vh <= 0) return;
        double availW = vw - 80, availH = vh - 80;   // room for the 32px container margin + breathing space
        if (availW <= 0 || availH <= 0) return;
        var (sw, sh) = StageDesignSize();
        double fit = Math.Clamp(Math.Min(availW / sw, availH / sh), 0.1, 4.0);
        if (Math.Abs(fit - _vm.Zoom) > 0.005) _vm.Zoom = fit;
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    public void RenderCurrentPage()
    {
        ClearStage();
        if (_vm?.Document?.SelectedPage is null) return;

        var page = _vm.Document.SelectedPage;
        StageRoot.Width = _vm.Document.Manifest.Stage.DesignWidth;
        StageRoot.Height = _vm.Document.Manifest.Stage.DesignHeight;

        var xaml = page.ToXaml();
        var result = XamlRoundTrip.TryLoad(xaml);
        if (!result.Success)
        {
            ShowError(result.Error);
            page.XamlError = result.Error;
            return;
        }

        var root = result.Root!;
        root.HorizontalAlignment = HorizontalAlignment.Stretch;
        root.VerticalAlignment = VerticalAlignment.Stretch;
        BindDesignerAssets(root);   // point Image sources at real local files so they actually show

        StageRoot.Children.Insert(0, root);

        ErrorOverlay.Visibility = Visibility.Collapsed;
        _currentRoot = root;
        page.XamlError = null;

        // Apply the page-level background (same as the player) so 页面背景 edits show on the canvas.
        try
        {
            var bg = _vm.Document.SelectedPage?.Descriptor.Background;
            if (!string.IsNullOrWhiteSpace(bg) && root is Panel bgPanel)
                ApplyDesignerBackground(bgPanel, bg!);
        }
        catch { /* invalid background — ignore */ }

        // Build bidirectional ElementModel ↔ FrameworkElement maps via parallel tree walk.
        _elementMap.Clear();
        _reverseMap.Clear();
        _hitOrder.Clear();
        _hitOrderCounter = 0;
        if (page.Elements.Count > 0)
        {
            _elementMap[page.Elements[0]] = root;
            _reverseMap[root] = page.Elements[0];
            _hitOrder[root] = _hitOrderCounter++;
            BuildParallelMap(page.Elements[0].Children, root);
        }

        if (_showGrid) RenderGrid();
        UpdateSelectionOverlay();
        StageScaler.Stretch = Stretch.Uniform;

        // Auto-flatten non-Canvas (e.g. Grid) pages to free layout: once the visual tree has
        // been laid out, read each top-level element's real position/size and convert the page
        // root to a Canvas with absolute coordinates so everything becomes freely draggable.
        if (page.Elements.Count > 0 && page.Elements[0].Type != "Canvas")
        {
            EventHandler<object>? onLayout = null;
            onLayout = (_, _) =>
            {
                root.LayoutUpdated -= onLayout;
                FlattenRootToCanvas(page, root);
            };
            root.LayoutUpdated += onLayout;
        }
    }

    /// <summary>
    /// Convert a Grid/StackPanel-rooted page into a Canvas-rooted free-layout page by pinning
    /// each top-level child to its laid-out absolute position and size. Inner content of each
    /// child keeps its own layout. Runs once per page (skipped once the root is already a Canvas).
    /// </summary>
    private void FlattenRootToCanvas(PageModel page, FrameworkElement rootVisual)
    {
        try
        {
            if (page.Elements.Count == 0) return;
            var rootEm = page.Elements[0];
            // Only flatten real containers. Converting a leaf root (e.g. a TextBlock) to a Canvas
            // would keep props like Text and produce invalid XAML (<Canvas Text="...">).
            if (rootEm.Type is not ("Grid" or "StackPanel" or "Border" or "ScrollViewer")) return;

            foreach (var childEm in rootEm.Children)
            {
                if (_elementMap.TryGetValue(childEm, out var fe))
                {
                    try
                    {
                        var p = fe.TransformToVisual(rootVisual).TransformPoint(new Point(0, 0));
                        childEm.X = Math.Round(p.X);
                        childEm.Y = Math.Round(p.Y);
                        if (fe.ActualWidth  > 0) childEm.Width  = Math.Round(fe.ActualWidth);
                        if (fe.ActualHeight > 0) childEm.Height = Math.Round(fe.ActualHeight);
                    }
                    catch { /* element may not be arranged; leave its model values */ }
                }
                // Drop layout props that only made sense inside the old container.
                foreach (var k in new[] { "Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan",
                                          "Margin", "HorizontalAlignment", "VerticalAlignment" })
                    childEm.Props.Remove(k);
            }

            rootEm.Type = "Canvas";
            rootEm.Props.Remove("RowDefinitions");
            rootEm.Props.Remove("ColumnDefinitions");

            // 真正发生了转换才标脏，并告知用户（复用顶部诊断条，避免静默改文档）。
            _vm?.MarkDirty();
            _vm?.ReportDiagnostic("该页已转换为自由布局（元素可直接拖拽），保存后生效。");
            _vm?.RaiseRenderRequest();   // re-render as a Canvas page
        }
        catch (Exception ex)
        {
            App.LogException("FlattenRootToCanvas", ex);
        }
    }

    /// <summary>Page background = a color (#AARRGGBB) or an image asset loaded from the course folder.</summary>
    private void ApplyDesignerBackground(Panel panel, string bg)
    {
        if (bg.TrimStart().StartsWith("#"))
        {
            panel.Background = CanvasRenderer.ParseBrush(bg);
            return;
        }
        try
        {
            var entry = FluentCourse.Runtime.Player.AssetUri.StripScheme(bg);
            var dir = _vm?.Document?.WorkingDirectory;   // 资产永远在工作目录（SourcePath 可能是 .fcourse 文件）
            if (string.IsNullOrEmpty(dir)) return;
            var path = System.IO.Path.Combine(dir, entry.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(path)) return;
            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path));
            panel.Background = new ImageBrush { ImageSource = bmp, Stretch = Stretch.UniformToFill };
        }
        catch { /* missing/invalid image — leave default */ }
    }

    /// <summary>
    /// The player binds ms-fcourse:// asset streams at runtime; the designer has no such binder, so an
    /// Image would try to load from the unresolvable https://fcourse.local/ placeholder and stay blank.
    /// Walk the freshly-loaded tree and repoint every Image at its real file under the course folder.
    /// </summary>
    private void BindDesignerAssets(DependencyObject node)
    {
        switch (node)
        {
            case Image img: FixImageSource(img); break;
            case MediaPlayerElement mpe: FixMediaSource(mpe); break;
            case Panel panel: foreach (var c in panel.Children) BindDesignerAssets(c); break;
            case Border border when border.Child is { } bc: BindDesignerAssets(bc); break;
            case ContentControl cc when cc.Content is DependencyObject cd: BindDesignerAssets(cd); break;
        }
    }

    /// <summary>The MediaPlayerElement source is carried in Tag (Source can't be set from XAML); bind
    /// it to the real local file so video/audio show a poster + transport controls in the designer.</summary>
    private void FixMediaSource(MediaPlayerElement mpe)
    {
        if (mpe.Tag is not string tag || string.IsNullOrEmpty(tag)) return;
        if (!tag.Contains("fcourse.local") && !tag.Contains("ms-fcourse")) return;
        var rel = FluentCourse.Runtime.Player.AssetUri.StripScheme(tag);
        var dir = _vm?.Document?.WorkingDirectory;
        if (string.IsNullOrEmpty(dir)) return;
        var path = System.IO.Path.Combine(dir, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(path)) return;
        try
        {
            mpe.AutoPlay = false;                    // don't blast audio/video while editing
            mpe.AreTransportControlsEnabled = true;
            mpe.Source = MediaSource.CreateFromUri(new Uri(path));
        }
        catch { /* unsupported codec etc. — leave the empty frame */ }
    }

    private void FixImageSource(Image img)
    {
        if (img.Source is not Microsoft.UI.Xaml.Media.Imaging.BitmapImage bmp || bmp.UriSource is not { } u) return;
        var s = u.ToString();
        if (!s.Contains("fcourse.local") && !s.Contains("ms-fcourse")) return;
        var rel = FluentCourse.Runtime.Player.AssetUri.StripScheme(s);
        var dir = _vm?.Document?.WorkingDirectory;
        if (string.IsNullOrEmpty(dir)) return;
        var path = System.IO.Path.Combine(dir, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(path)) return;
        try
        {
            // 命中缓存就复用已解码的位图，避免每次渲染都从磁盘重新解码。
            if (!_imageCache.TryGetValue(path, out var bmp2))
            {
                bmp2 = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path));
                _imageCache[path] = bmp2;
            }
            img.Source = bmp2;
        }
        catch { }
    }

    /// <summary>清空图片缓存（资源被替换/更新后调用，使下次渲染重新解码）。</summary>
    public void InvalidateImageCache() => _imageCache.Clear();

    /// <summary>
    /// Walk ElementModel children and the matching visual children in parallel
    /// (same depth-first order as XamlSerializer produces).
    /// </summary>
    private void BuildParallelMap(IList<ElementModel> modelChildren, FrameworkElement parent)
    {
        IList<UIElement>? visualChildren = null;

        if (parent is Panel panel)
            visualChildren = panel.Children;
        else if (parent is Border border && border.Child is UIElement bc)
            visualChildren = new UIElement[] { bc };
        else if (parent is ContentControl cc && cc.Content is UIElement ccc)
            visualChildren = new UIElement[] { ccc };

        if (visualChildren is null || modelChildren.Count == 0) return;

        var count = Math.Min(modelChildren.Count, visualChildren.Count);
        for (int i = 0; i < count; i++)
        {
            if (visualChildren[i] is not FrameworkElement childFe) continue;
            var em = modelChildren[i];
            _elementMap[em] = childFe;
            _reverseMap[childFe] = em;
            _hitOrder[childFe] = _hitOrderCounter++;   // 深度优先 = 文档顺序
            // Don't recurse into:
            //  • runtime controls — their model "children" are choices/blanks in collections, not
            //    addressable visual children;
            //  • a nested Grid (a 表格) — its cells are laid out by the Grid, so we treat the whole
            //    table as one selectable/draggable unit instead of letting clicks land on a cell
            //    (whose Canvas.Left would be ignored, making the table feel un-draggable).
            if (em.Type is "QuizControl" or "StepRevealControl" or "Grid" ||
                em.Props.TryGetValue("Tag", out var tag) && tag == "FluentCourse.Group")
                continue;
            BuildParallelMap(em.Children, childFe);
        }
    }

    // ── Selection overlay ─────────────────────────────────────────────────────

    public void UpdateSelectionOverlay()
    {
        var sel = _vm?.SelectedElement;
        if (_vm?.SelectedElements.Count > 1)
        {
            if (TryGetSelectionBounds(_vm.SelectedElements, out var multiBounds))
            {
                PlaceAdorner(multiBounds);
            }
            else
            {
                SelectionBorder.Visibility = Visibility.Collapsed;
                HideHandles();
            }
            HideAlignLines();
            return;
        }

        if (sel is null || _currentRoot is null)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            HideHandles();
            HideAlignLines();
            return;
        }

        FrameworkElement? targetFe = null;
        _elementMap.TryGetValue(sel, out targetFe);
        if (targetFe is null)
            targetFe = FindByXName(_currentRoot, sel.Name);

        if (targetFe is null)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            HideHandles();
            HideAlignLines();
            return;
        }

        try
        {
            var transform = targetFe.TransformToVisual(StageRoot);
            // Prefer ActualWidth/Height (post-layout); fall back to EM design size so
            // the adorner is correct immediately after insertion before layout runs.
            var w = targetFe.ActualWidth  > 0 ? targetFe.ActualWidth  : (sel.Width  > 0 ? sel.Width  : 100);
            var h = targetFe.ActualHeight > 0 ? targetFe.ActualHeight : (sel.Height > 0 ? sel.Height : 40);
            var bounds = transform.TransformBounds(new Rect(0, 0, w, h));
            PlaceAdorner(bounds);

            // Auto-height elements (e.g. a question, whose model Height is 0) haven't been laid
            // out yet at selection time, so ActualHeight is 0 and the box collapses to the
            // fallback. Re-place it once the element has its real size.
            if (targetFe.ActualWidth <= 0 || targetFe.ActualHeight <= 0)
            {
                EventHandler<object>? once = null;
                once = (_, _) =>
                {
                    // Selection moved away → stop listening.
                    if (!ReferenceEquals(_vm?.SelectedElement, sel)) { targetFe.LayoutUpdated -= once; return; }
                    // Not measured yet → keep waiting for a later layout pass.
                    if (targetFe.ActualWidth <= 0 || targetFe.ActualHeight <= 0) return;
                    targetFe.LayoutUpdated -= once;
                    UpdateSelectionOverlay();
                };
                targetFe.LayoutUpdated += once;
            }
        }
        catch
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            HideHandles();
        }
    }

    private bool TryGetSelectionBounds(IEnumerable<ElementModel> elements, out Rect bounds)
    {
        bounds = default;
        Rect? union = null;
        foreach (var em in elements)
        {
            if (!_elementMap.TryGetValue(em, out var fe)) continue;
            try
            {
                var transform = fe.TransformToVisual(StageRoot);
                var w = fe.ActualWidth > 0 ? fe.ActualWidth : (em.Width > 0 ? em.Width : 100);
                var h = fe.ActualHeight > 0 ? fe.ActualHeight : (em.Height > 0 ? em.Height : 40);
                var b = transform.TransformBounds(new Rect(0, 0, w, h));
                union = union.HasValue ? Union(union.Value, b) : b;
            }
            catch { }
        }

        if (!union.HasValue) return false;
        bounds = union.Value;
        return true;
    }

    private static Rect Union(Rect a, Rect b)
    {
        var left = Math.Min(a.Left, b.Left);
        var top = Math.Min(a.Top, b.Top);
        var right = Math.Max(a.Right, b.Right);
        var bottom = Math.Max(a.Bottom, b.Bottom);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private void PlaceAdorner(Rect bounds)
    {
        double x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height;
        const double HandleSize = 11;

        PlaceSelectionBorder(bounds);

        PositionHandle(HandleTL, x - HandleSize / 2, y - HandleSize / 2);
        PositionHandle(HandleTR, x + w - HandleSize / 2, y - HandleSize / 2);
        PositionHandle(HandleBL, x - HandleSize / 2, y + h - HandleSize / 2);
        PositionHandle(HandleBR, x + w - HandleSize / 2, y + h - HandleSize / 2);
        PositionHandle(HandleML, x + w / 2 - HandleSize / 2, y - HandleSize / 2);
        PositionHandle(HandleMR, x + w / 2 - HandleSize / 2, y + h - HandleSize / 2);
        PositionHandle(HandleTM, x - HandleSize / 2, y + h / 2 - HandleSize / 2);
        PositionHandle(HandleBM, x + w - HandleSize / 2, y + h / 2 - HandleSize / 2);
    }

    private void PlaceSelectionBorder(Rect bounds)
    {
        SelectionBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionBorder, bounds.X - 1);
        Canvas.SetTop(SelectionBorder, bounds.Y - 1);
        SelectionBorder.Width = bounds.Width + 2;
        SelectionBorder.Height = bounds.Height + 2;
    }

    // ── Pointer / drag handlers ───────────────────────────────────────────────

    // Walk up the visual tree from `source` to find the first element registered in _reverseMap.
    // Stops at StageRoot so we never escape the page content area.
    private FrameworkElement? FindMappedAncestor(DependencyObject? source)
    {
        var current = source;
        while (current is not null && !ReferenceEquals(current, StageRoot))
        {
            if (current is FrameworkElement fe && _reverseMap.ContainsKey(fe))
                return fe;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // Bounds-based hit test over every mapped element. Unlike visual hit-testing, this also
    // selects elements that render no pixels (e.g. an Image whose source can't be resolved,
    // an empty Border, a MediaPlayerElement) as long as they occupy a design rect. Returns the
    // deepest (most nested) element under the point so children win over their containers.
    private FrameworkElement? HitTestMapped(Point pt)
    {
        // 取胜规则：更深的（子元素）优先；同深度时 ZIndex 高的优先；再相同时取文档顺序
        // 靠后的（后画的在上层）—— 否则重叠区域永远选中最底下那个。
        FrameworkElement? best = null;
        int bestDepth = -1, bestZ = int.MinValue, bestOrder = -1;
        foreach (var kv in _reverseMap)
        {
            var fe = kv.Key;
            Rect b;
            try
            {
                var t = fe.TransformToVisual(StageRoot);
                var w = fe.ActualWidth  > 0 ? fe.ActualWidth  : kv.Value.Width;
                var h = fe.ActualHeight > 0 ? fe.ActualHeight : kv.Value.Height;
                if (w <= 0 || h <= 0) continue;
                b = t.TransformBounds(new Rect(0, 0, w, h));
            }
            catch { continue; }

            if (!b.Contains(pt)) continue;
            var depth = DepthInStage(fe);
            var z = Canvas.GetZIndex(fe);
            var order = _hitOrder.TryGetValue(fe, out var o) ? o : -1;
            bool wins = depth > bestDepth
                || (depth == bestDepth && z > bestZ)
                || (depth == bestDepth && z == bestZ && order > bestOrder);
            if (wins) { bestDepth = depth; bestZ = z; bestOrder = order; best = fe; }
        }
        return best;
    }

    private int DepthInStage(DependencyObject fe)
    {
        int d = 0;
        var cur = VisualTreeHelper.GetParent(fe);
        while (cur is not null && !ReferenceEquals(cur, StageRoot)) { d++; cur = VisualTreeHelper.GetParent(cur); }
        return d;
    }

    private void OnStagePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (_vm is null) return;

            // While an inline text editor is open: clicks inside it are its own; a click elsewhere
            // commits the edit and then proceeds as a normal selection/drag.
            if (IsWithinInlineEdit(e.OriginalSource)) return;
            if (_inlineEdit is not null) CommitInlineEdit();

            // Right-click is for the context menu (handled by OnStageRightTapped) — never start a
            // drag or capture the pointer on it, or the menu's gesture would be swallowed.
            if (e.GetCurrentPoint(StageRoot).Properties.IsRightButtonPressed) return;

            // Grabbing a selection handle starts a resize instead of a move.
            if (e.OriginalSource is FrameworkElement src && GetHandleEdges(src) is not null)
            {
                StartResize(src, e);
                return;
            }

            // Prefer bounds-based hit testing (selects transparent/empty elements like Images),
            // then fall back to walking up from OriginalSource. handledEventsToo: true means this
            // still fires even when an interactive control already marked the event Handled.
            var pressPt = e.GetCurrentPoint(StageRoot).Position;
            var fe = HitTestMapped(pressPt) ?? FindMappedAncestor(e.OriginalSource as DependencyObject);
            if (fe is null) return;

            _reverseMap.TryGetValue(fe, out var em);
            if (em is null) return;

            // Clicking the page root canvas (background) deselects.
            var pageRoot = _vm.Document?.SelectedPage?.Elements.Count > 0
                ? _vm.Document.SelectedPage.Elements[0] : null;
            if (pageRoot is not null && ReferenceEquals(em, pageRoot))
            {
                _vm.SetSelection(null);
                _vm.ShowPageProperties();   // clicking blank canvas → edit page background
                e.Handled = true;
                return;
            }

            var multiSelect = IsMultiSelectModifierDown();
            if (multiSelect)
            {
                _vm.ToggleElementSelection(em);
                e.Handled = true;
                return;
            }

            if (!_vm.IsSelected(em))
                _vm.SetSelection(em);

            _dragFe = fe;
            _dragEm = em;
            _dragStartEmX = em.X;
            _dragStartEmY = em.Y;
            _dragGroupStart = _vm.SelectedElements.Count > 1 && _vm.IsSelected(em)
                ? _vm.SelectedElements.Select(x => (x, x.X, x.Y)).ToList()
                : new List<(ElementModel Element, double X, double Y)> { (em, em.X, em.Y) };
            _isDragging = false;

            if (_dragGroupStart.Count > 1 && TryGetSelectionBounds(_vm.SelectedElements, out var groupBounds))
            {
                _dragStartBounds = new Point(groupBounds.X, groupBounds.Y);
                _dragStartEmWidth = groupBounds.Width;
                _dragStartEmHeight = groupBounds.Height;
            }
            else
            {
                try
                {
                    var t = fe.TransformToVisual(StageRoot);
                    _dragStartBounds = t.TransformPoint(new Point(0, 0));
                }
                catch { _dragStartBounds = new Point(em.X, em.Y); }

                _dragStartEmWidth  = fe.ActualWidth  > 0 ? fe.ActualWidth  : em.Width;
                _dragStartEmHeight = fe.ActualHeight > 0 ? fe.ActualHeight : em.Height;
            }

            BuildSnapGuides(fe);
            _lastDx = 0; _lastDy = 0;

            // Capture on StageRoot so Moved/Released route here even when the interactive
            // control has already captured internally.
            StageRoot.CapturePointer(e.Pointer);
            _dragStartPointer = e.GetCurrentPoint(StageRoot).Position;

            e.Handled = true;
        }
        catch (Exception ex)
        {
            _vm?.ReportDiagnostic($"点击处理异常: {ex.Message}");
            ResetDrag();
        }
    }

    private static bool IsMultiSelectModifierDown()
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        return ctrl || shift;
    }

    private void OnStagePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (_resizeHandle is not null) { DoResize(e); e.Handled = true; return; }
            if (_dragFe is null || _dragEm is null) return;

            var pos = e.GetCurrentPoint(StageRoot).Position;
            var dx  = pos.X - _dragStartPointer.X;
            var dy  = pos.Y - _dragStartPointer.Y;

            if (!_isDragging)
            {
                if (Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;
                _isDragging = true;
                // 拖动位移叠加在元素原有变换（旋转/缩放）之上，而不是整体替换 ——
                // 否则旋转过的元素一拖就被"摆正"。
                _dragOriginalTransform = _dragFe.RenderTransform;
                _dragTranslate = new TranslateTransform();
                var group = new TransformGroup();
                if (_dragOriginalTransform is not null) group.Children.Add(_dragOriginalTransform);
                group.Children.Add(_dragTranslate);
                _dragFe.RenderTransform = group;
            }

            var w  = _dragStartEmWidth;
            var h  = _dragStartEmHeight;

            // Snap the element's left/center/right (and top/center/bottom) to nearby guides.
            var sx = BestSnap(new[] { _dragStartBounds.X + dx, _dragStartBounds.X + dx + w / 2, _dragStartBounds.X + dx + w }, _snapXs);
            if (sx.hit) { dx += sx.adjust; ShowAlignV(sx.guide); } else HideAlignV();
            var sy = BestSnap(new[] { _dragStartBounds.Y + dy, _dragStartBounds.Y + dy + h / 2, _dragStartBounds.Y + dy + h }, _snapYs);
            if (sy.hit) { dy += sy.adjust; ShowAlignH(sy.guide); } else HideAlignH();
            _lastDx = dx; _lastDy = dy;

            if (_dragTranslate is not null) { _dragTranslate.X = dx; _dragTranslate.Y = dy; }

            var ax = _dragStartBounds.X + dx;
            var ay = _dragStartBounds.Y + dy;

            Canvas.SetLeft(SelectionBorder, ax - 1);
            Canvas.SetTop(SelectionBorder,  ay - 1);
            SelectionBorder.Width  = w + 2;
            SelectionBorder.Height = h + 2;

            const double HS = 11;
            MoveHandle(HandleTL, ax - HS / 2,          ay - HS / 2);
            MoveHandle(HandleTR, ax + w - HS / 2,      ay - HS / 2);
            MoveHandle(HandleBL, ax - HS / 2,          ay + h - HS / 2);
            MoveHandle(HandleBR, ax + w - HS / 2,      ay + h - HS / 2);
            MoveHandle(HandleTM, ax + w / 2 - HS / 2,  ay - HS / 2);
            MoveHandle(HandleBM, ax + w / 2 - HS / 2,  ay + h - HS / 2);
            MoveHandle(HandleML, ax - HS / 2,           ay + h / 2 - HS / 2);
            MoveHandle(HandleMR, ax + w - HS / 2,       ay + h / 2 - HS / 2);

            e.Handled = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StageView] PointerMoved error: {ex.Message}");
        }
    }

    private void OnStagePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (_resizeHandle is not null)
            {
                CommitResize();
                try { StageRoot.ReleasePointerCaptures(); } catch { }
                e.Handled = true;
                return;
            }

            if (_dragFe is null || _dragEm is null) { ResetDrag(); return; }

            if (_isDragging)
            {
                // Commit the snapped delta (matches what was shown during the drag).
                if (_dragGroupStart.Count > 1)
                {
                    var moves = _dragGroupStart
                        .Select(m => (m.Element, OldX: m.X, OldY: m.Y,
                            NewX: Math.Round(m.X + _lastDx, 2), NewY: Math.Round(m.Y + _lastDy, 2)))
                        .ToList();

                    void Apply()
                    {
                        foreach (var m in moves)
                        {
                            m.Element.X = m.NewX;
                            m.Element.Y = m.NewY;
                        }
                    }

                    void Revert()
                    {
                        foreach (var m in moves)
                        {
                            m.Element.X = m.OldX;
                            m.Element.Y = m.OldY;
                        }
                    }

                    Apply();
                    _vm?.PushUndo(new DelegateUndoCommand(
                        () => { Revert(); _vm?.RaiseRenderRequest(); },
                        () => { Apply(); _vm?.RaiseRenderRequest(); },
                        "移动多个对象"));
                    _vm?.MarkDirty();
                }
                else
                {
                    var newX = _dragStartEmX + _lastDx;
                    var newY = _dragStartEmY + _lastDy;

                    _dragEm.X = newX;
                    _dragEm.Y = newY;
                    _vm?.MoveElement(_dragEm, _dragStartEmX, _dragStartEmY, newX, newY);
                    _vm?.MarkDirty();
                }
                _vm?.RaiseRenderRequest();  // ClearStage → ResetDrag may null _dragFe here
            }

            // Release on StageRoot (not _dragFe) — safe even if RaiseRenderRequest nulled _dragFe.
            try { StageRoot.ReleasePointerCaptures(); } catch { }
            ResetDrag();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            _vm?.ReportDiagnostic($"拖动提交异常: {ex.Message}");
            ResetDrag();
        }
    }

    private void OnStagePointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // Only cancel the drag when StageRoot itself lost capture. A child control losing its
        // internal capture (e.g. when we steal it via StageRoot.CapturePointer on press) also
        // bubbles a CaptureLost here — ignore those or we'd kill the drag the moment it starts.
        if (!ReferenceEquals(e.OriginalSource, StageRoot)) return;
        // 调整大小进行到一半丢失捕获：DoResize 已实时改过模型，必须按起始值正常提交
        // （压入撤销命令），否则这次改动既不能撤销也没有标脏记录。
        if (_resizeHandle is not null) { CommitResize(); return; }
        ResetDrag();
    }

    private void ResetDrag()
    {
        // 只有真的替换过 RenderTransform（开始过拖动）才恢复，单纯点击不能动元素的变换。
        if (_dragFe is not null && _dragTranslate is not null)
        {
            // 恢复元素原有的变换对象（拖动时叠加的 TransformGroup 只是临时的）。
            try { _dragFe.RenderTransform = _dragOriginalTransform; } catch { /* element may be detached */ }
        }
        _isDragging = false;
        _dragFe = null;
        _dragEm = null;
        _dragOriginalTransform = null;
        _dragTranslate = null;
        _resizeHandle = null;
        _resizeFe = null;
        _resizeEm = null;
        _resizeGroupBounds = default;
        _resizeGroupStart.Clear();
        _dragGroupStart.Clear();
        _snapXs.Clear();
        _snapYs.Clear();
        HideAlignLines();
    }

    // ── Right-click context menu ────────────────────────────────────────────────

    // ── Double-click to edit text in place ──────────────────────────────────────

    /// <summary>Which prop holds a type's editable caption (null = not text-editable on canvas).</summary>
    private static string? InlineEditKey(ElementModel em) => em.Type switch
    {
        "TextBlock" or "TextBox" or "AutoSuggestBox" => "Text",
        "Button" or "HyperlinkButton" or "ToggleButton" or "CheckBox" or "RadioButton" => "Content",
        _ => null
    };

    private void OnStageDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_vm is null) return;
        var fe = HitTestMapped(e.GetPosition(StageRoot));
        if (fe is null || !_reverseMap.TryGetValue(fe, out var em)) return;
        var key = InlineEditKey(em);
        if (key is null) return;
        _vm.SetSelection(em);
        BeginInlineEdit(em, fe, key);
        e.Handled = true;
    }

    private void BeginInlineEdit(ElementModel em, FrameworkElement fe, string key)
    {
        CommitInlineEdit();

        double w = fe.ActualWidth  > 0 ? fe.ActualWidth  : (em.Width  > 0 ? em.Width  : 200);
        double h = fe.ActualHeight > 0 ? fe.ActualHeight : (em.Height > 0 ? em.Height : 40);
        Rect bounds;
        try { bounds = fe.TransformToVisual(StageRoot).TransformBounds(new Rect(0, 0, w, h)); }
        catch { bounds = new Rect(em.X, em.Y, w, h); }

        bool multiline = em.Type == "TextBlock";
        double fontSize = em.Props.TryGetValue("FontSize", out var fsv) && double.TryParse(fsv, out var fs)
            ? fs : (multiline ? 24 : 15);

        var tb = new TextBox
        {
            Text = em.Props.TryGetValue(key, out var cur) ? cur : "",
            FontSize = fontSize,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Width = Math.Max(60, bounds.Width),
            MinHeight = Math.Max(28, bounds.Height),
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x12, 0x12, 0x16)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0xC2, 0xFF)),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(4, 2, 4, 2)
        };
        Canvas.SetLeft(tb, bounds.X);
        Canvas.SetTop(tb, bounds.Y);
        Canvas.SetZIndex(tb, 5000);
        AdornerLayer.Children.Add(tb);
        _inlineEdit = tb; _inlineEm = em; _inlineKey = key;

        tb.Loaded += (_, _) => { try { tb.Focus(FocusState.Programmatic); tb.SelectAll(); } catch { } };
        tb.KeyDown += (_, ke) =>
        {
            if (ke.Key == Windows.System.VirtualKey.Escape) { CancelInlineEdit(); ke.Handled = true; }
            else if (ke.Key == Windows.System.VirtualKey.Enter && !multiline) { CommitInlineEdit(); ke.Handled = true; }
        };
        tb.LostFocus += (_, _) => CommitInlineEdit();
    }

    private void CommitInlineEdit()
    {
        if (_inlineEdit is null || _inlineEm is null || _inlineKey is null) return;
        var tb = _inlineEdit; var em = _inlineEm; var key = _inlineKey;
        _inlineEdit = null; _inlineEm = null; _inlineKey = null;   // clear first so LostFocus re-entry is safe
        try { AdornerLayer.Children.Remove(tb); } catch { }

        var old = em.Props.TryGetValue(key, out var o) ? o : "";
        var neu = tb.Text ?? "";
        if (neu != old)
        {
            em.Props[key] = neu;
            _vm?.ChangeProperty(em, key, old, neu);
            _vm?.RaiseRenderRequest();
        }
    }

    private void CancelInlineEdit()
    {
        if (_inlineEdit is null) return;
        var tb = _inlineEdit;
        _inlineEdit = null; _inlineEm = null; _inlineKey = null;
        try { AdornerLayer.Children.Remove(tb); } catch { }
    }

    private bool IsWithinInlineEdit(object? src)
    {
        if (_inlineEdit is null) return false;
        var d = src as DependencyObject;
        while (d is not null)
        {
            if (ReferenceEquals(d, _inlineEdit)) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void OnStageRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        try
        {
            if (_vm is null) return;
            var pos = e.GetPosition(StageRoot);
            var fe = HitTestMapped(pos) ?? FindMappedAncestor(e.OriginalSource as DependencyObject);
            ElementModel? em = null;
            if (fe is not null) _reverseMap.TryGetValue(fe, out em);

            var pageRoot = _vm.Document?.SelectedPage?.Elements.Count > 0
                ? _vm.Document.SelectedPage.Elements[0] : null;
            var isElement = em is not null && !ReferenceEquals(em, pageRoot);

            if (isElement) _vm.SetSelection(em);   // right-click also selects
            var flyout = isElement ? BuildElementMenu(em!) : BuildCanvasMenu(pos);
            flyout.ShowAt(StageRoot, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = pos });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            _vm?.ReportDiagnostic($"右键菜单异常: {ex.Message}");
        }
    }

    private MenuFlyout BuildElementMenu(ElementModel em)
    {
        var mf = new MenuFlyout();

        // Element-specific actions first (表格行列 / 题目选项), then a separator.
        if (_vm!.IsTable(em))
        {
            mf.Items.Add(MenuItem("增加一行", null, () => _vm!.TableAddRow(em)));
            mf.Items.Add(MenuItem("删除一行", null, () => _vm!.TableRemoveRow(em)));
            mf.Items.Add(MenuItem("增加一列", null, () => _vm!.TableAddColumn(em)));
            mf.Items.Add(MenuItem("删除一列", null, () => _vm!.TableRemoveColumn(em)));
            mf.Items.Add(new MenuFlyoutSeparator());
        }
        else
        {
            var mode = _vm!.QuizMode(em);
            if (mode is "Single" or "Multiple")
            {
                mf.Items.Add(MenuItem("增加选项", null, () => _vm!.QuizAddChoice(em)));
                mf.Items.Add(MenuItem("删除最后一个选项", null, () => _vm!.QuizRemoveChoice(em)));
                mf.Items.Add(new MenuFlyoutSeparator());
            }
            else if (mode == "FillInBlank")
            {
                mf.Items.Add(MenuItem("增加一个空", null, () => _vm!.QuizAddBlank(em)));
                mf.Items.Add(MenuItem("删除最后一个空", null, () => _vm!.QuizRemoveBlank(em)));
                mf.Items.Add(new MenuFlyoutSeparator());
            }
        }
        mf.Items.Add(MenuItem("剪切", "cut", () => _vm!.CutElement(em), "Ctrl+X"));
        mf.Items.Add(MenuItem("复制", "copy", () => _vm!.CopyElement(em), "Ctrl+C"));
        var paste = MenuItem("粘贴", "paste", () => _vm!.PasteElement(), "Ctrl+V");
        paste.IsEnabled = _vm!.HasClipboard;
        mf.Items.Add(paste);
        mf.Items.Add(MenuItem("再制", "dup", () => _vm!.DuplicateElement(em), "Ctrl+D"));
        mf.Items.Add(MenuItem("删除", "del", () => _vm!.DeleteElement(em), "Delete"));
        mf.Items.Add(new MenuFlyoutSeparator());

        var layer = new MenuFlyoutSubItem { Text = "层级" };
        layer.Items.Add(MenuItem("置于顶层", "front", () => _vm!.ReorderElement(em, "front")));
        layer.Items.Add(MenuItem("上移一层", null, () => _vm!.ReorderElement(em, "forward")));
        layer.Items.Add(MenuItem("下移一层", null, () => _vm!.ReorderElement(em, "backward")));
        layer.Items.Add(MenuItem("置于底层", "back", () => _vm!.ReorderElement(em, "back")));
        mf.Items.Add(layer);
        return mf;
    }

    private MenuFlyout BuildCanvasMenu(Point pos)
    {
        var mf = new MenuFlyout();
        var paste = MenuItem("粘贴到此处", "paste", () => _vm!.PasteElement(pos.X, pos.Y), "Ctrl+V");
        paste.IsEnabled = _vm!.HasClipboard;
        mf.Items.Add(paste);
        mf.Items.Add(new MenuFlyoutSeparator());
        mf.Items.Add(MenuItem("编辑页面背景", "bg", () => { _vm!.SetSelection(null); _vm.ShowPageProperties(); }));
        return mf;
    }

    private static MenuFlyoutItem MenuItem(string text, string? glyph, Action onClick, string? accel = null)
    {
        var item = new MenuFlyoutItem { Text = text };
        var icon = DrawMenuIcon(glyph);
        if (icon is not null) item.Icon = icon;
        if (accel is not null) item.KeyboardAcceleratorTextOverride = accel;
        item.Click += (_, _) => { try { onClick(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[StageView] menu action failed: {ex.Message}"); } };
        return item;
    }

    /// <summary>Map a short ASCII key to a Segoe MDL2 glyph icon (null = no icon).</summary>
    private static IconElement? DrawMenuIcon(string? key)
    {
        var g = key switch
        {
            "cut"   => "",
            "copy"  => "",
            "paste" => "",
            "dup"   => "",
            "del"   => "",
            "front" => "",
            "back"  => "",
            "bg"    => "",
            _ => null
        };
        return g is null ? null : new FontIcon { Glyph = g, FontSize = 14 };
    }

    // ── Auto-align snapping ─────────────────────────────────────────────────────

    /// <summary>Collect alignment guides from every other element's edges/centers + page guides.</summary>
    private void BuildSnapGuides(FrameworkElement dragged)
    {
        _snapXs.Clear();
        _snapYs.Clear();
        foreach (var kv in _reverseMap)
        {
            var fe = kv.Key;
            if (ReferenceEquals(fe, dragged) || ReferenceEquals(fe, _currentRoot)) continue;
            try
            {
                var t = fe.TransformToVisual(StageRoot);
                var w = fe.ActualWidth  > 0 ? fe.ActualWidth  : kv.Value.Width;
                var h = fe.ActualHeight > 0 ? fe.ActualHeight : kv.Value.Height;
                if (w <= 0 || h <= 0) continue;
                var b = t.TransformBounds(new Rect(0, 0, w, h));
                _snapXs.Add(b.Left); _snapXs.Add(b.Left + b.Width / 2); _snapXs.Add(b.Right);
                _snapYs.Add(b.Top);  _snapYs.Add(b.Top + b.Height / 2); _snapYs.Add(b.Bottom);
            }
            catch { /* not arranged */ }
        }
        double pw = StageRoot.Width, ph = StageRoot.Height;
        _snapXs.Add(0); _snapXs.Add(pw / 2); _snapXs.Add(pw);
        _snapYs.Add(0); _snapYs.Add(ph / 2); _snapYs.Add(ph);
    }

    /// <summary>Find the nearest guide to any of the element's edges, within the snap threshold.</summary>
    private (bool hit, double adjust, double guide) BestSnap(double[] edges, List<double> guides)
    {
        bool hit = false;
        double best = SnapThreshold, adjust = 0, guide = 0;
        foreach (var ed in edges)
            foreach (var g in guides)
            {
                var d = Math.Abs(ed - g);
                if (d <= best) { best = d; adjust = g - ed; guide = g; hit = true; }
            }
        return (hit, adjust, guide);
    }

    private void ShowAlignV(double x)
    {
        AlignLineV.X1 = x; AlignLineV.X2 = x;
        AlignLineV.Y1 = 0; AlignLineV.Y2 = StageRoot.Height;
        AlignLineV.Visibility = Visibility.Visible;
    }
    private void HideAlignV() => AlignLineV.Visibility = Visibility.Collapsed;

    private void ShowAlignH(double y)
    {
        AlignLineH.Y1 = y; AlignLineH.Y2 = y;
        AlignLineH.X1 = 0; AlignLineH.X2 = StageRoot.Width;
        AlignLineH.Visibility = Visibility.Visible;
    }
    private void HideAlignH() => AlignLineH.Visibility = Visibility.Collapsed;

    // ── Resize ────────────────────────────────────────────────────────────────

    /// <summary>Which edges a handle drives, keyed by its actual on-screen position (the x:Names
    /// are historically scrambled, so map by role, matching PlaceAdorner's placement).</summary>
    private (bool left, bool top, bool right, bool bottom)? GetHandleEdges(FrameworkElement h)
    {
        if (ReferenceEquals(h, HandleTL)) return (true, true, false, false);
        if (ReferenceEquals(h, HandleTR)) return (false, true, true, false);
        if (ReferenceEquals(h, HandleBL)) return (true, false, false, true);
        if (ReferenceEquals(h, HandleBR)) return (false, false, true, true);
        if (ReferenceEquals(h, HandleML)) return (false, true, false, false);   // top-center
        if (ReferenceEquals(h, HandleMR)) return (false, false, false, true);   // bottom-center
        if (ReferenceEquals(h, HandleTM)) return (true, false, false, false);   // left-center
        if (ReferenceEquals(h, HandleBM)) return (false, false, true, false);   // right-center
        return null;
    }

    private void StartResize(FrameworkElement handle, PointerRoutedEventArgs e)
    {
        _resizeHandle = handle;
        _resizeStartPointer = e.GetCurrentPoint(StageRoot).Position;

        if (_vm?.SelectedElements.Count > 1 && TryGetSelectionBounds(_vm.SelectedElements, out var groupBounds))
        {
            _resizeGroupBounds = groupBounds;
            _resizeGroupStart = _vm.SelectedElements
                .Select(em =>
                {
                    var (w, h) = ElementVisualSize(em);
                    return (em, em.X, em.Y, w, h);
                })
                .ToList();
            StageRoot.CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }

        var em = _vm?.SelectedElement;
        if (em is null || !_elementMap.TryGetValue(em, out var fe)) return;

        _resizeFe = fe;
        _resizeEm = em;
        _resizeStartX = em.X;
        _resizeStartY = em.Y;
        _resizeStartW = fe.ActualWidth  > 0 ? fe.ActualWidth  : (em.Width  > 0 ? em.Width  : 100);
        _resizeStartH = fe.ActualHeight > 0 ? fe.ActualHeight : (em.Height > 0 ? em.Height : 40);

        StageRoot.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DoResize(PointerRoutedEventArgs e)
    {
        if (_resizeHandle is null) return;
        var edges = GetHandleEdges(_resizeHandle);
        if (edges is null) return;

        if (_resizeGroupStart.Count > 1)
        {
            DoGroupResize(e, edges.Value);
            return;
        }

        if (_resizeFe is null || _resizeEm is null) return;
        var pos = e.GetCurrentPoint(StageRoot).Position;
        var dx = pos.X - _resizeStartPointer.X;
        var dy = pos.Y - _resizeStartPointer.Y;

        double newX = _resizeStartX, newY = _resizeStartY, newW = _resizeStartW, newH = _resizeStartH;
        if (edges.Value.left)   { newX = _resizeStartX + dx; newW = _resizeStartW - dx; }
        if (edges.Value.right)  { newW = _resizeStartW + dx; }
        if (edges.Value.top)    { newY = _resizeStartY + dy; newH = _resizeStartH - dy; }
        if (edges.Value.bottom) { newH = _resizeStartH + dy; }

        // Shift on a corner handle → keep the original aspect ratio (drive height from width).
        var corner = (edges.Value.left || edges.Value.right) && (edges.Value.top || edges.Value.bottom);
        var shift = (e.KeyModifiers & Windows.System.VirtualKeyModifiers.Shift) != 0;
        if (shift && corner && _resizeStartW > 0 && _resizeStartH > 0)
        {
            var adjH = newW * (_resizeStartH / _resizeStartW);
            if (edges.Value.top) newY = _resizeStartY + (_resizeStartH - adjH);
            newH = adjH;
        }

        const double MIN = 20;
        if (newW < MIN) { if (edges.Value.left) newX = _resizeStartX + _resizeStartW - MIN; newW = MIN; }
        if (newH < MIN) { if (edges.Value.top)  newY = _resizeStartY + _resizeStartH - MIN; newH = MIN; }

        newX = Math.Round(newX); newY = Math.Round(newY);
        newW = Math.Round(newW); newH = Math.Round(newH);

        // Apply live to the model and the visual, then move the adorner to match.
        _resizeEm.X = newX; _resizeEm.Y = newY; _resizeEm.Width = newW; _resizeEm.Height = newH;
        _resizeFe.Width = newW; _resizeFe.Height = newH;
        Canvas.SetLeft(_resizeFe, newX);
        Canvas.SetTop(_resizeFe, newY);
        PlaceAdorner(new Rect(newX, newY, newW, newH));
    }

    private void CommitResize()
    {
        if (_resizeGroupStart.Count > 1)
        {
            CommitGroupResize();
            return;
        }

        if (_resizeEm is null) { ResetDrag(); return; }
        var em = _resizeEm;
        _vm?.ResizeElement(em, _resizeStartX, _resizeStartY, _resizeStartW, _resizeStartH,
                           em.X, em.Y, em.Width, em.Height);
        _vm?.MarkDirty();
        ResetDrag();
        _vm?.RaiseRenderRequest();
    }

    private void DoGroupResize(PointerRoutedEventArgs e, (bool left, bool top, bool right, bool bottom) edges)
    {
        var pos = e.GetCurrentPoint(StageRoot).Position;
        var dx = pos.X - _resizeStartPointer.X;
        var dy = pos.Y - _resizeStartPointer.Y;

        var old = _resizeGroupBounds;
        double newX = old.X, newY = old.Y, newW = old.Width, newH = old.Height;
        if (edges.left)   { newX = old.X + dx; newW = old.Width - dx; }
        if (edges.right)  { newW = old.Width + dx; }
        if (edges.top)    { newY = old.Y + dy; newH = old.Height - dy; }
        if (edges.bottom) { newH = old.Height + dy; }

        var corner = (edges.left || edges.right) && (edges.top || edges.bottom);
        var shift = (e.KeyModifiers & Windows.System.VirtualKeyModifiers.Shift) != 0;
        if (shift && corner && old.Width > 0 && old.Height > 0)
        {
            var adjustedH = newW * (old.Height / old.Width);
            if (edges.top) newY = old.Y + (old.Height - adjustedH);
            newH = adjustedH;
        }

        const double MinGroupSize = 24;
        if (newW < MinGroupSize) { if (edges.left) newX = old.Right - MinGroupSize; newW = MinGroupSize; }
        if (newH < MinGroupSize) { if (edges.top)  newY = old.Bottom - MinGroupSize; newH = MinGroupSize; }

        var sx = old.Width <= 0 ? 1 : newW / old.Width;
        var sy = old.Height <= 0 ? 1 : newH / old.Height;
        foreach (var item in _resizeGroupStart)
        {
            var relativeX = item.X - old.X;
            var relativeY = item.Y - old.Y;
            var nextX = Math.Round(newX + relativeX * sx, 2);
            var nextY = Math.Round(newY + relativeY * sy, 2);
            var nextW = Math.Max(1, Math.Round(item.W * sx, 2));
            var nextH = Math.Max(1, Math.Round(item.H * sy, 2));

            item.Element.X = nextX;
            item.Element.Y = nextY;
            item.Element.Width = nextW;
            item.Element.Height = nextH;

            if (_elementMap.TryGetValue(item.Element, out var fe))
            {
                fe.Width = nextW;
                fe.Height = nextH;
                Canvas.SetLeft(fe, nextX);
                Canvas.SetTop(fe, nextY);
            }
        }

        PlaceAdorner(new Rect(Math.Round(newX, 2), Math.Round(newY, 2), Math.Round(newW, 2), Math.Round(newH, 2)));
    }

    private void CommitGroupResize()
    {
        var changes = _resizeGroupStart
            .Select(s => (s.Element, OldX: s.X, OldY: s.Y, OldW: s.W, OldH: s.H,
                NewX: s.Element.X, NewY: s.Element.Y, NewW: s.Element.Width, NewH: s.Element.Height))
            .Where(c => Math.Abs(c.NewX - c.OldX) >= 0.001 ||
                        Math.Abs(c.NewY - c.OldY) >= 0.001 ||
                        Math.Abs(c.NewW - c.OldW) >= 0.001 ||
                        Math.Abs(c.NewH - c.OldH) >= 0.001)
            .ToList();

        if (changes.Count == 0)
        {
            ResetDrag();
            _vm?.RaiseRenderRequest();
            return;
        }

        void Apply()
        {
            foreach (var c in changes)
            {
                c.Element.X = c.NewX;
                c.Element.Y = c.NewY;
                c.Element.Width = c.NewW;
                c.Element.Height = c.NewH;
            }
        }

        void Revert()
        {
            foreach (var c in changes)
            {
                c.Element.X = c.OldX;
                c.Element.Y = c.OldY;
                c.Element.Width = c.OldW;
                c.Element.Height = c.OldH;
            }
        }

        Apply();
        _vm?.PushUndo(new DelegateUndoCommand(
            () => { Revert(); _vm?.RaiseRenderRequest(); },
            () => { Apply(); _vm?.RaiseRenderRequest(); },
            "缩放多个对象"));
        _vm?.MarkDirty();
        ResetDrag();
        _vm?.RaiseRenderRequest();
    }

    private (double Width, double Height) ElementVisualSize(ElementModel em)
    {
        if (_elementMap.TryGetValue(em, out var fe))
        {
            var w = fe.ActualWidth > 0 ? fe.ActualWidth : em.Width;
            var h = fe.ActualHeight > 0 ? fe.ActualHeight : em.Height;
            return (w > 0 ? w : 100, h > 0 ? h : 40);
        }

        return (em.Width > 0 ? em.Width : 100, em.Height > 0 ? em.Height : 40);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FrameworkElement? FindByXName(DependencyObject root, string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (root is FrameworkElement fe && fe.Name == name) return fe;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement cfe && cfe.Name == name) return cfe;
            var found = FindByXName(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static ElementModel? FindElementModelByXName(
        IEnumerable<ElementModel>? elements, string? name)
    {
        if (elements is null || string.IsNullOrEmpty(name)) return null;
        foreach (var em in elements)
        {
            if (em.Name == name) return em;
            var found = FindElementModelByXName(em.Children, name);
            if (found != null) return found;
        }
        return null;
    }

    public void SetShowRulers(bool show)
    {
        TopRuler.Visibility  = show ? Visibility.Visible : Visibility.Collapsed;
        LeftRuler.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetShowGrid(bool show)
    {
        _showGrid = show;
        if (show) RenderGrid();
        else ClearGrid();
    }

    private void ClearStage()
    {
        ResetDrag();
        _elementMap.Clear();
        _reverseMap.Clear();
        _hitOrder.Clear();

        for (int i = 0; i < StageRoot.Children.Count; i++)
        {
            var child = StageRoot.Children[i];
            if (child == GridLayer || child == AdornerLayer) continue;
            StageRoot.Children.RemoveAt(i);
            break;
        }
        _currentRoot = null;
        SelectionBorder.Visibility = Visibility.Collapsed;
        HideHandles();
    }

    private void ClearGrid() => GridLayer.Children.Clear();

    private void RenderGrid()
    {
        ClearGrid();
        var w = StageRoot.Width;
        var h = StageRoot.Height;
        var faintBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));

        for (double x = 0; x <= w; x += 8)
        {
            GridLayer.Children.Add(new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = h,
                Stroke = x % 64 == 0
                    ? new SolidColorBrush(Color.FromArgb(50, 255, 255, 255))
                    : faintBrush,
                StrokeThickness = 1
            });
        }
        for (double y = 0; y <= h; y += 8)
        {
            GridLayer.Children.Add(new Line
            {
                X1 = 0, Y1 = y, X2 = w, Y2 = y,
                Stroke = y % 64 == 0
                    ? new SolidColorBrush(Color.FromArgb(50, 255, 255, 255))
                    : faintBrush,
                StrokeThickness = 1
            });
        }
    }

    private void ShowError(string? message)
    {
        ErrorOverlay.Visibility = Visibility.Visible;
        ErrorText.Text = $"解析错误:\n{message}";
    }

    private static void PositionHandle(Rectangle handle, double x, double y)
    {
        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
        handle.Visibility = Visibility.Visible;
    }

    private static void MoveHandle(Rectangle handle, double x, double y)
    {
        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
    }

    private void HideHandles()
    {
        HandleTL.Visibility = HandleTR.Visibility = HandleBL.Visibility = HandleBR.Visibility = Visibility.Collapsed;
        HandleML.Visibility = HandleMR.Visibility = HandleTM.Visibility = HandleBM.Visibility = Visibility.Collapsed;
    }

    private void HideAlignLines()
    {
        AlignLineH.Visibility = Visibility.Collapsed;
        AlignLineV.Visibility = Visibility.Collapsed;
    }

    // ── Drag and drop (toolbox → canvas) ─────────────────────────────────────

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var item = DragState.Current;
        if (item is null)
        {
            // External (OS) drag: files / image / text dragged in from outside the app.
            if (e.DataView.Contains(StandardDataFormats.StorageItems) ||
                e.DataView.Contains(StandardDataFormats.Bitmap))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "插入图片 / 视频 / 音频";
            }
            else if (e.DataView.Contains(StandardDataFormats.Text))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "插入文字";
            }
            else { e.AcceptedOperation = DataPackageOperation.None; return; }

            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = item.Label;
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = false;
        e.DragUIOverride.IsGlyphVisible = false;

        var pos = e.GetPosition(StageRoot);
        if (pos.X < 0 || pos.Y < 0) { DropIndicator.Visibility = Visibility.Collapsed; return; }
        DropIndicator.Visibility = Visibility.Visible;
        DropIndicator.Width  = item.DefaultWidth;
        DropIndicator.Height = item.DefaultHeight;
        Canvas.SetLeft(DropIndicator, pos.X - item.DefaultWidth  / 2);
        Canvas.SetTop(DropIndicator,  pos.Y - item.DefaultHeight / 2);
    }

    private void OnDragLeave(object sender, DragEventArgs e) =>
        DropIndicator.Visibility = Visibility.Collapsed;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropIndicator.Visibility = Visibility.Collapsed;
        var item = DragState.Current;
        DragState.Current = null;
        if (_vm is null) return;

        var pos = e.GetPosition(StageRoot);
        pos.X = Math.Max(0, Math.Min(pos.X, StageRoot.ActualWidth));
        pos.Y = Math.Max(0, Math.Min(pos.Y, StageRoot.ActualHeight));

        // Internal toolbox drag.
        if (item is not null)
        {
            try { _vm.InsertToolboxItemAtPoint(item, pos.X, pos.Y); }
            catch (Exception ex) { _vm.ReportDiagnostic($"拖放插入失败: {ex.Message}"); }
            return;
        }

        // External (OS) drop: files / image / text. Keep the operation alive with a deferral.
        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                double offset = 0;
                foreach (var it in items)
                    if (it is StorageFile sf && ShellViewModel.IsSupportedMediaFile(sf.Path))
                    { _vm.InsertFile(sf.Path, pos.X + offset, pos.Y + offset); offset += 28; }
            }
            else if (e.DataView.Contains(StandardDataFormats.Bitmap))
            {
                var path = await SaveDataViewBitmapAsync(e.DataView);
                if (path is not null) _vm.InsertFile(path, pos.X, pos.Y);
            }
            else if (e.DataView.Contains(StandardDataFormats.Text))
            {
                var txt = await e.DataView.GetTextAsync();
                _vm.InsertText(txt, pos.X, pos.Y);
            }
        }
        catch (Exception ex) { _vm.ReportDiagnostic($"拖入失败: {ex.Message}"); }
        finally { deferral.Complete(); }
    }

    /// <summary>Save a bitmap from a DataPackageView (drop or clipboard) to a temp file; null on failure.</summary>
    internal static async Task<string?> SaveDataViewBitmapAsync(DataPackageView dv)
    {
        try
        {
            var bref = await dv.GetBitmapAsync();
            using var stream = await bref.OpenReadAsync();
            var ct = stream.ContentType ?? "";
            var ext = ct.Contains("bmp") ? ".bmp" : ct.Contains("jpeg") || ct.Contains("jpg") ? ".jpg" : ".png";
            var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fc_paste_{Guid.NewGuid():N}{ext}");
            using (var fs = File.Create(tmp))
                await stream.AsStreamForRead().CopyToAsync(fs);
            return tmp;
        }
        catch { return null; }
    }

    // ── Rulers ───────────────────────────────────────────────────────────────

    private void RenderRulers()
    {
        TopRulerCanvas.Children.Clear();
        LeftRulerCanvas.Children.Clear();
        if (_vm?.Document?.Manifest.Stage is not { } stage) return;

        for (int x = 0; x <= stage.DesignWidth; x += 100)
        {
            var tick = new Rectangle
            {
                Width = 1, Height = x % 200 == 0 ? 12 : 6,
                Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray)
            };
            Canvas.SetLeft(tick, x);
            TopRulerCanvas.Children.Add(tick);

            if (x % 200 == 0)
            {
                var label = new TextBlock
                {
                    Text = x.ToString(), FontSize = 9,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
                };
                Canvas.SetLeft(label, x + 2);
                TopRulerCanvas.Children.Add(label);
            }
        }
        for (int y = 0; y <= stage.DesignHeight; y += 100)
        {
            var tick = new Rectangle
            {
                Width = y % 200 == 0 ? 12 : 6, Height = 1,
                Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray)
            };
            Canvas.SetTop(tick, y);
            LeftRulerCanvas.Children.Add(tick);

            if (y % 200 == 0)
            {
                var label = new TextBlock
                {
                    Text = y.ToString(), FontSize = 9,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
                };
                Canvas.SetLeft(label, 2);
                Canvas.SetTop(label, y + 2);
                LeftRulerCanvas.Children.Add(label);
            }
        }
    }
}

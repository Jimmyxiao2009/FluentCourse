using System;
using System.IO;
using System.Threading.Tasks;
using FluentCourse.Designer.Models;
using FluentCourse.Designer.Services;
using FluentCourse.Designer.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace FluentCourse.Designer;

public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _vm;
    private Views.CourseSettingsView? _settingsView;
    private Views.AssetsPane? _assetsView;

    private string _rightPanelTag = "Props";
    private bool _xamlMode;
    private bool _syncingZoom;   // true while pushing Zoom→slider, so the slider's change isn't read as a manual zoom
    private DispatcherTimer? _autosaveTimer;
    private bool _recoveryChecked;

    public MainWindow()
    {
        try { InitializeComponent(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] InitializeComponent failed: {ex}");
            throw;
        }
        Title = "FluentCourse Designer";

        try { SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base }; }
        catch { /* Mica requires Windows 11; Win10 falls back to solid color */ }

        _vm = new ShellViewModel();
        _vm.DocumentChanged += OnDocumentChanged;
        _vm.SelectionChanged += OnSelectionChanged;
        _vm.RequestRender += OnRequestRender;
        _vm.RequestPreview += (_, _) => LaunchPreview();
        _vm.RequestShowPageProperties += (_, _) => { if (!_xamlMode) ShowRightPanel("Props"); };
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.WindowTitle))
            {
                Title = _vm.WindowTitle;
                DocNameText.Text = _vm.Document?.DisplayName ?? "";
            }
            if (e.PropertyName == nameof(ShellViewModel.DiagnosticMessage))
                ShowDiagnostic(_vm.DiagnosticMessage);
            if (e.PropertyName == nameof(ShellViewModel.StatusMessage))
                StatusMessageText.Text = _vm.StatusMessage;
            if (e.PropertyName == nameof(ShellViewModel.Zoom))
            {
                ZoomText.Text = $"{_vm.Zoom * 100:0}%";
                _syncingZoom = true;
                if (Math.Abs(ZoomSlider.Value - _vm.Zoom * 100) > 0.5)
                    ZoomSlider.Value = _vm.Zoom * 100;
                _syncingZoom = false;
                StageViewControl.ApplyZoom(_vm.Zoom);
            }
        };

        StageViewControl.SetViewModel(_vm);
        CodeView.SetViewModel(_vm);
        PropertiesPaneView.SetViewModel(_vm);
        PagePropsView.SetViewModel(_vm);
        TriggerEditorView.SetViewModel(_vm);
        AnimationEditorView.SetViewModel(_vm);
        AnimationPanelView.SetViewModel(_vm);
        ElementTreeViewControl.SetViewModel(_vm);
        PagesViewControl.SetViewModel(_vm);

        _settingsView = new Views.CourseSettingsView(); _settingsView.SetViewModel(_vm);
        _assetsView = new Views.AssetsPane(); _assetsView.SetViewModel(_vm);

        // 订阅 SelectedPage 变化：订当前文档 + 跟随 DocumentChanged 重订（解订旧文档）。
        // 初始文档在 VM 构造函数里创建，早于这里的事件订阅，必须立即 Hook 一次。
        HookDocumentEvents();
        _vm.DocumentChanged += (_, _) =>
        {
            HookDocumentEvents();
            DocNameText.Text = _vm.Document?.DisplayName ?? "";
            StageViewControl.EnableAutoFit();   // re-fit the canvas whenever a course is opened/created
        };

        // F1：替换脏文档前确认（新建/打开），以及窗口关闭前确认（见 OnAppWindowClosing）。
        _vm.ConfirmDiscardChangesAsync = ConfirmDiscardChangesAsync;
        AppWindow.Closing += OnAppWindowClosing;

        if (Content is FrameworkElement root)
        {
            root.KeyDown += OnGlobalKeyDown;
            root.Loaded += async (_, _) => await CheckRecoveryAsync();
        }

        // Autosave a dirty document to the recovery folder so a crash doesn't lose work.
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        _autosaveTimer.Tick += (_, _) => { try { _vm.AutosaveIfDirty(); } catch { } };
        _autosaveTimer.Start();

        try { BuildInsertStrip(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MainWindow] BuildInsertStrip failed: {ex}"); }

        // 初始化视图状态
        try
        {
            if (_vm.Document?.SelectedPage is { } initialPage)
                PagePropsView.LoadPage(initialPage);

            ShowRightPanel("Props");
            UpdateTabButtonStates();

            StageViewControl.RenderCurrentPage();
            ElementTreeViewControl.Refresh();
            TriggerEditorView.Refresh();
            AnimationEditorView.Refresh();
            UpdatePageInfo();
            UpdateSlideCount();
            ZoomText.Text = "100%";
            ZoomSlider.Value = 100;
        }
        catch (Exception ex) { ShowDiagnostic($"初始化失败: {ex.Message}"); }
    }

    // ── 文档事件订阅（订当前 + 跟随 DocumentChanged 重订） ──────────────────

    private DesignerDocument? _observedDoc;

    private void HookDocumentEvents()
    {
        if (_observedDoc is not null) _observedDoc.PropertyChanged -= OnObservedDocPropertyChanged;
        _observedDoc = _vm.Document;
        if (_observedDoc is not null) _observedDoc.PropertyChanged += OnObservedDocPropertyChanged;
    }

    private void OnObservedDocPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesignerDocument.SelectedPage))
        {
            if (_vm.Document?.SelectedPage is { } page) PagePropsView.LoadPage(page);
            UpdatePageInfo();
            UpdateSlideCount();
        }
    }

    // ── 未保存提示 ───────────────────────────────────────────────────────────

    private bool _forceClose;

    /// <summary>替换/丢弃脏文档前询问。true = 可以继续，false = 用户取消。</summary>
    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (_vm.Document is null || !_vm.Document.IsDirty) return true;
        var dlg = new ContentDialog
        {
            Title = "未保存的更改",
            Content = "当前课件有未保存的更改，是否先保存？",
            PrimaryButtonText = "保存",
            SecondaryButtonText = "不保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        var r = await dlg.ShowAsync();
        if (r == ContentDialogResult.Primary)
        {
            await _vm.SaveCommand.ExecuteAsync(null);
            return _vm.Document is null || !_vm.Document.IsDirty;   // 另存为被取消则仍是脏的 → 中止
        }
        return r == ContentDialogResult.Secondary;
    }

    private async void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_forceClose || _vm.Document is null || !_vm.Document.IsDirty) return;
        args.Cancel = true;   // 必须在 await 之前同步取消，再异步询问
        try
        {
            if (await ConfirmDiscardChangesAsync())
            {
                _forceClose = true;
                Close();
            }
        }
        catch (Exception ex) { App.LogException("CloseConfirm", ex); }
    }

    // ── 插入条 ──────────────────────────────────────────────────────────────

    private void BuildInsertStrip()
    {
        InsertStrip.Children.Clear();
        // Keep the catalog's declared order (GroupBy is stable: groups appear in first-seen order).
        var groups = ToolboxCatalog.All.GroupBy(i => i.Category);
        bool firstGroup = true;
        foreach (var group in groups)
        {
            if (!firstGroup)
            {
                InsertStrip.Children.Add(new Border
                {
                    Width = 1,
                    Margin = new Thickness(4, 8, 4, 8),
                    Background = (Brush)Application.Current.Resources["StrokeSubtleBrush"]
                });
            }
            firstGroup = false;

            foreach (var item in group)
            {
                var btn = new Button
                {
                    Style = (Style)Application.Current.Resources["InsertButton"],
                    Tag = item
                };
                ToolTipService.SetToolTip(btn, $"{item.DisplayLabel} — {item.Description}");

                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var icon = BuildToolboxIcon(item);
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.Margin = new Thickness(0, 0, 0, 3);
                var iconHost = new Grid { Height = 18, HorizontalAlignment = HorizontalAlignment.Center };
                iconHost.Children.Add(icon);
                stack.Children.Add(iconHost);
                stack.Children.Add(new TextBlock
                {
                    Text = item.DisplayLabel,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 72
                });
                btn.Content = stack;
                btn.Click += OnInsertStripItemClicked;
                InsertStrip.Children.Add(btn);
            }
        }
    }

    /// <summary>Build a recognisable icon per toolbox item — real mini-shapes for shapes, glyphs
    /// for media, and simple drawn marks for the rest (avoids font-glyph "tofu").</summary>
    private static FrameworkElement BuildToolboxIcon(ToolboxItem item)
    {
        var fg = Application.Current.Resources["TextPrimaryBrush"] as Brush
                 ?? new SolidColorBrush(Microsoft.UI.Colors.White);

        FrameworkElement Font(string g) => new FontIcon
        {
            Glyph = g, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, Foreground = fg
        };

        switch (item.Label)
        {
            case "Heading":       return new TextBlock { Text = "A", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = fg };
            case "Body":          return new TextBlock { Text = "A", FontSize = 12, Foreground = fg };
            case "Image":         return Font("");   // Picture
            case "Video":         return Font("");   // Video
            case "QuizSingle":    return RadioIcon(fg);
            case "QuizMultiple":  return CheckboxIcon(fg);
            case "QuizFill":      return BlankIcon(fg);
            case "QuizTrueFalse": return TrueFalseIcon(fg);
            case "Button":        return new Border { Width = 22, Height = 13, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1.4), BorderBrush = fg };
            case "Rectangle":     return new Rectangle { Width = 17, Height = 12, RadiusX = 2, RadiusY = 2, Fill = fg };
            case "Ellipse":       return new Ellipse { Width = 15, Height = 15, Fill = fg };
            case "Line":          return new Rectangle { Width = 17, Height = 2, Fill = fg };
            case "Triangle":      return new Polygon { Points = Pts("8,1 16,16 0,16"), Fill = fg };
            case "Parallelogram": return new Polygon { Points = Pts("5,2 17,2 13,15 1,15"), Fill = fg };
            case "Arrow":         return new Polygon { Points = Pts("0,5 9,5 9,1 16,8 9,15 9,11 0,11"), Fill = fg };
            case "Audio":         return new TextBlock { Text = "♪", FontSize = 16, Foreground = fg };
            case "HyperlinkButton": return Font("");
            case "Table":         return new TextBlock { Text = "▦", FontSize = 15, Foreground = fg };
            case "BarChart":      return Font("");
            case "FlowSmart":     return Font("");
            case "Countdown":     return Font("");
            default:              return Font("");
        }
    }

    private static Microsoft.UI.Xaml.Media.PointCollection Pts(string s)
    {
        var pc = new Microsoft.UI.Xaml.Media.PointCollection();
        foreach (var pair in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var xy = pair.Split(',');
            pc.Add(new Windows.Foundation.Point(double.Parse(xy[0]), double.Parse(xy[1])));
        }
        return pc;
    }

    // ── Quiz toolbox icons (drawn, so they render consistently — the old glyph/unicode ones
    //    showed inconsistently or as tofu) ───────────────────────────────────────────────
    private static FrameworkElement RadioIcon(Brush fg)   // 单选：圆 + 圆心
    {
        var g = new Grid { Width = 16, Height = 16 };
        g.Children.Add(new Ellipse { Width = 15, Height = 15, Stroke = fg, StrokeThickness = 1.4 });
        g.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = fg,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return g;
    }
    private static FrameworkElement CheckboxIcon(Brush fg) // 多选：方框 + 勾
    {
        var g = new Grid { Width = 16, Height = 16 };
        g.Children.Add(new Border { Width = 15, Height = 15, CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1.4), BorderBrush = fg });
        g.Children.Add(new Polyline { Points = Pts("4,8 7,11 12,5"), Stroke = fg, StrokeThickness = 1.6 });
        return g;
    }
    private static FrameworkElement BlankIcon(Brush fg)    // 填空：A + 下划线
    {
        var g = new Grid { Width = 18, Height = 16 };
        g.Children.Add(new TextBlock { Text = "A", FontSize = 12, Foreground = fg,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top });
        g.Children.Add(new Rectangle { Width = 16, Height = 2, Fill = fg, VerticalAlignment = VerticalAlignment.Bottom });
        return g;
    }
    private static FrameworkElement TrueFalseIcon(Brush fg) // 判断：勾 + 叉
    {
        var g = new Grid { Width = 22, Height = 14 };
        g.Children.Add(new Polyline { Points = Pts("0,8 3,12 8,3"), Stroke = fg, StrokeThickness = 1.6 });
        g.Children.Add(new Line { X1 = 13, Y1 = 3, X2 = 21, Y2 = 12, Stroke = fg, StrokeThickness = 1.6 });
        g.Children.Add(new Line { X1 = 21, Y1 = 3, X2 = 13, Y2 = 12, Stroke = fg, StrokeThickness = 1.6 });
        return g;
    }

    private void OnInsertStripItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ToolboxItem item) return;
        try
        {
            _vm.InsertToolboxItem(item);
            // 插入后切换到属性面板，方便立即编辑
            if (_rightPanelTag != "Props") ShowRightPanel("Props");
        }
        catch (Exception ex) { ShowDiagnostic($"插入 {item.DisplayLabel} 失败: {ex.Message}"); }
    }

    // ── 文档 / 选择事件 ──────────────────────────────────────────────────────

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        TriggerEditorView.Refresh();
        AnimationEditorView.Refresh();
        ElementTreeViewControl.Refresh();
        UpdatePageInfo();
        UpdateSlideCount();
        DocNameText.Text = _vm.Document?.DisplayName ?? "";
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (!_xamlMode) StageViewControl.UpdateSelectionOverlay();
        UpdateElementInfo();

        // Props 模式下根据选中状态自动切换 PropertiesPane ↔ PagePropertiesView
        if (_rightPanelTag == "Props")
            ShowRightPanel("Props");
        else
            UpdatePropsHeader(); // 其他模式只刷新标题
    }

    private void OnRequestRender(object? sender, EventArgs e)
    {
        try { if (!_xamlMode) StageViewControl.RenderCurrentPage(); }
        catch (Exception ex) { ShowDiagnostic($"渲染失败: {ex.Message}"); }
    }

    // ── 右面板切换 ───────────────────────────────────────────────────────────

    private void OnRightTabProps(object sender, RoutedEventArgs e) => ShowRightPanel("Props");
    private void OnRightTabTree(object sender, RoutedEventArgs e) => ShowRightPanel("Tree");
    private void OnRightTabAnim(object sender, RoutedEventArgs e) => ShowRightPanel("Anim");
    private void OnRightTabTrig(object sender, RoutedEventArgs e) => ShowRightPanel("Trig");

    private void ShowRightPanel(string tag)
    {
        _rightPanelTag = tag;
        bool hasSel = _vm.SelectedElement is not null;

        PropertiesPaneView.Visibility    = tag == "Props" && hasSel  ? Visibility.Visible : Visibility.Collapsed;
        PagePropsView.Visibility         = tag == "Props" && !hasSel ? Visibility.Visible : Visibility.Collapsed;
        ElementTreeViewControl.Visibility = tag == "Tree"             ? Visibility.Visible : Visibility.Collapsed;
        AnimationPanelView.Visibility    = tag == "Anim"             ? Visibility.Visible : Visibility.Collapsed;
        TriggerEditorView.Visibility     = tag == "Trig"             ? Visibility.Visible : Visibility.Collapsed;
        AnimationEditorView.Visibility   = Visibility.Collapsed;

        UpdatePropsHeader();
        UpdateTabButtonStates();
    }

    private void UpdatePropsHeader()
    {
        var sel = _vm.SelectedElement;
        (string icon, string title, string sub) = _rightPanelTag switch
        {
            "Tree" => ("", "元素树", "页面结构层级"),
            "Anim" => ("", "动画", "入场 · 退场效果"),
            "Trig" => ("", "触发器", "交互与跳转"),
            _ when sel is not null => ("", sel.Type,
                string.IsNullOrEmpty(sel.Name) ? "未命名" : $"\"{sel.Name}\""),
            _ => ("", "幻灯片属性", "背景 · 过渡 · 备注"),
        };

        RightPanelIcon.Glyph = icon;
        RightPanelTitle.Text = title;
        RightPanelSub.Text = sub;
    }

    private void UpdateTabButtonStates()
    {
        // 活跃标签全透明度，其余半透明
        TabBtnProps.Opacity = _rightPanelTag == "Props" && !_xamlMode ? 1.0 : 0.4;
        TabBtnTree.Opacity  = _rightPanelTag == "Tree"  && !_xamlMode ? 1.0 : 0.4;
        TabBtnAnim.Opacity  = _rightPanelTag == "Anim"  && !_xamlMode ? 1.0 : 0.4;
        TabBtnXaml.Opacity  = _xamlMode ? 1.0 : 0.4;
    }

    // ── XAML 视图切换 ────────────────────────────────────────────────────────

    private void OnXamlToggleClicked(object sender, RoutedEventArgs e)
    {
        _xamlMode = !_xamlMode;
        DesignSurfacePanel.Visibility = _xamlMode ? Visibility.Collapsed : Visibility.Visible;
        CodeView.Visibility           = _xamlMode ? Visibility.Visible   : Visibility.Collapsed;

        if (_xamlMode)
            CodeView.Refresh();
        else
            StageViewControl.RenderCurrentPage();

        UpdateTabButtonStates();
    }

    // ── 按钮事件 ─────────────────────────────────────────────────────────────

    private void OnNewClicked(object s, RoutedEventArgs e)    => _vm.NewDocumentCommand.Execute(null);
    private async void OnOpenClicked(object s, RoutedEventArgs e)   => await _vm.OpenCommand.ExecuteAsync(null);
    private async void OnSaveClicked(object s, RoutedEventArgs e)   => await _vm.SaveCommand.ExecuteAsync(null);
    private async void OnSaveAsClicked(object s, RoutedEventArgs e) => await _vm.SaveAsCommand.ExecuteAsync(null);
    private async void OnCourseSettingsClicked(object s, RoutedEventArgs e)
    {
        var view = new Views.CourseSettingsView();
        view.SetViewModel(_vm);
        var dlg = new ContentDialog
        {
            Title = "课程与主题设置",
            Content = view,
            CloseButtonText = "完成",
            XamlRoot = Content.XamlRoot
        };
        await dlg.ShowAsync();
        StageViewControl.RenderCurrentPage();
        UpdatePageInfo();
    }

    private async void OnExportClicked(object s, RoutedEventArgs e) => await _vm.ExportCourseCommand.ExecuteAsync(null);
    private async void OnExportStandaloneClicked(object s, RoutedEventArgs e) => await _vm.ExportStandaloneCommand.ExecuteAsync(null);
    private void OnUndoClicked(object s, RoutedEventArgs e)   => _vm.UndoCommand.Execute(null);
    private void OnRedoClicked(object s, RoutedEventArgs e)   => _vm.RedoCommand.Execute(null);
    private void OnAlignLeftClicked(object s, RoutedEventArgs e) => _vm.AlignSelected("left");
    private void OnAlignHorizontalCenterClicked(object s, RoutedEventArgs e) => _vm.AlignSelected("hcenter");
    private void OnAlignRightClicked(object s, RoutedEventArgs e) => _vm.AlignSelected("right");
    private void OnAlignTopClicked(object s, RoutedEventArgs e) => _vm.AlignSelected("top");
    private void OnAlignVerticalCenterClicked(object s, RoutedEventArgs e) => _vm.AlignSelected("vcenter");
    private void OnAlignBottomClicked(object s, RoutedEventArgs e) => _vm.AlignSelected("bottom");
    private void OnDistributeHorizontalClicked(object s, RoutedEventArgs e) => _vm.DistributeSelected("horizontal");
    private void OnDistributeVerticalClicked(object s, RoutedEventArgs e) => _vm.DistributeSelected("vertical");
    private void OnApplyBlankLayoutClicked(object s, RoutedEventArgs e) => _vm.ApplyLayoutToCurrentPage("blank");
    private void OnApplyTitleLayoutClicked(object s, RoutedEventArgs e) => _vm.ApplyLayoutToCurrentPage("title");
    private void OnApplyTitleContentLayoutClicked(object s, RoutedEventArgs e) => _vm.ApplyLayoutToCurrentPage("title-content");
    private void OnApplyTwoColumnLayoutClicked(object s, RoutedEventArgs e) => _vm.ApplyLayoutToCurrentPage("two-column");
    private void OnApplySectionLayoutClicked(object s, RoutedEventArgs e) => _vm.ApplyLayoutToCurrentPage("section");
    private void OnCutElementClicked(object s, RoutedEventArgs e)
    {
        if (!TryGetSelectedElement(out var em)) return;
        _vm.CutElement(em);
        _vm.StatusMessage = "已剪切对象";
    }

    private void OnCopyElementClicked(object s, RoutedEventArgs e)
    {
        if (!TryGetSelectedElement(out var em)) return;
        _vm.CopyElement(em);
        _vm.StatusMessage = "已复制对象";
    }

    private void OnPasteElementClicked(object s, RoutedEventArgs e) => _ = HandlePasteAsync();

    private void OnDuplicateElementClicked(object s, RoutedEventArgs e)
    {
        if (!TryGetSelectedElement(out var em)) return;
        _vm.DuplicateElement(em);
        _vm.StatusMessage = "已再制对象";
    }

    private void OnDeleteElementClicked(object s, RoutedEventArgs e)
    {
        _vm.DeleteSelected();
    }

    private void OnGroupSelectedClicked(object s, RoutedEventArgs e) => _vm.GroupSelected();
    private void OnUngroupSelectedClicked(object s, RoutedEventArgs e) => _vm.UngroupSelected();

    private void OnBringToFrontClicked(object s, RoutedEventArgs e) => ReorderSelected("front", "已置于顶层");
    private void OnBringForwardClicked(object s, RoutedEventArgs e) => ReorderSelected("forward", "已上移一层");
    private void OnSendBackwardClicked(object s, RoutedEventArgs e) => ReorderSelected("backward", "已下移一层");
    private void OnSendToBackClicked(object s, RoutedEventArgs e) => ReorderSelected("back", "已置于底层");
    private void OnPreviewClicked(object s, RoutedEventArgs e) => _vm.PreviewCourseCommand.Execute(null);
    private void OnPreviewPresenterClicked(object s, RoutedEventArgs e) => LaunchPreview(openPresenter: true);
    private void OnAddPageClicked(object s, RoutedEventArgs e) => _vm.AddPageCommand.Execute(null);
    private void OnAddBlankPageClicked(object s, RoutedEventArgs e) => _vm.AddPageWithLayout("blank");
    private void OnAddTitlePageClicked(object s, RoutedEventArgs e) => _vm.AddPageWithLayout("title");
    private void OnAddTitleContentPageClicked(object s, RoutedEventArgs e) => _vm.AddPageWithLayout("title-content");
    private void OnAddTwoColumnPageClicked(object s, RoutedEventArgs e) => _vm.AddPageWithLayout("two-column");
    private void OnAddSectionPageClicked(object s, RoutedEventArgs e) => _vm.AddPageWithLayout("section");

    private bool TryGetSelectedElement(out ElementModel em)
    {
        if (_vm.SelectedElement is { } selected)
        {
            em = selected;
            return true;
        }

        em = null!;
        _vm.StatusMessage = "请选择一个对象";
        return false;
    }

    private void ReorderSelected(string op, string status)
    {
        if (!TryGetSelectedElement(out var em)) return;
        _vm.ReorderElement(em, op);
        _vm.StatusMessage = status;
    }

    private void LaunchPreview(bool openPresenter = false)
    {
        if (_vm.Document is null) return;
        // 预览永远使用临时快照：完整保存（页面+清单）并镜像 assets 等支撑文件，
        // 否则预览里图片/视频全缺。注意预览 ≠ 保存，绝不能动 IsDirty。
        var sourceDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}");
        try
        {
            DocumentService.SaveToDirectory(_vm.Document, sourceDir);
            if (!string.IsNullOrEmpty(_vm.Document.WorkingDirectory) &&
                System.IO.Directory.Exists(_vm.Document.WorkingDirectory))
                DocumentService.MirrorSupportFiles(_vm.Document.WorkingDirectory!, sourceDir);
        }
        catch (Exception ex) { ShowDiagnostic($"预览失败: {ex.Message}"); return; }
        try
        {
            var exePath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "FluentCourse.App.exe");
            if (!System.IO.File.Exists(exePath))
                exePath = System.IO.Path.Combine(
                    System.AppContext.BaseDirectory, "..", "..", "..", "..",
                    "FluentCourse.App", "bin", "x64", "Debug",
                    "net8.0-windows10.0.19041.0", "FluentCourse.App.exe");
            if (System.IO.File.Exists(exePath))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    { FileName = exePath, Arguments = openPresenter ? $"--presenter \"{sourceDir}\"" : $"\"{sourceDir}\"", UseShellExecute = true });
            else
                ShowDiagnostic("找不到 FluentCourse.App.exe，请确保播放器已编译。");
        }
        catch (Exception ex) { ShowDiagnostic($"预览失败: {ex.Message}"); }
    }

    // ── 缩放 ─────────────────────────────────────────────────────────────────

    private void OnZoomInClicked(object s, RoutedEventArgs e)
    {
        StageViewControl.DisableAutoFit();
        _vm.Zoom = Math.Min(3.0, _vm.Zoom + 0.1);
    }

    private void OnZoomOutClicked(object s, RoutedEventArgs e)
    {
        StageViewControl.DisableAutoFit();
        _vm.Zoom = Math.Max(0.25, _vm.Zoom - 0.1);
    }

    private void OnZoomSliderChanged(object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_vm is null || _syncingZoom) return;   // ignore programmatic (auto-fit) slider updates
        StageViewControl.DisableAutoFit();         // a real drag means the user wants manual zoom
        var newZoom = e.NewValue / 100.0;
        if (Math.Abs(newZoom - _vm.Zoom) > 0.005)
            _vm.Zoom = newZoom;
    }

    // ── 键盘 ─────────────────────────────────────────────────────────────────

    private void OnGlobalKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var nudge = shift ? 10.0 : 1.0;
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Z when ctrl:
                _vm.UndoCommand.Execute(null); e.Handled = true; break;
            case Windows.System.VirtualKey.Y when ctrl:
                _vm.RedoCommand.Execute(null); e.Handled = true; break;
            case Windows.System.VirtualKey.S when ctrl:
                _ = _vm.SaveCommand.ExecuteAsync(null); e.Handled = true; break;
            case Windows.System.VirtualKey.F5:
                _vm.PreviewCourseCommand.Execute(null); e.Handled = true; break;
            case Windows.System.VirtualKey.Delete when _vm.SelectedElement is not null && !IsTextInputFocused():
                _vm.DeleteSelected(); e.Handled = true; break;
            case Windows.System.VirtualKey.C when ctrl && !IsTextInputFocused() && _vm.SelectedElement is not null:
                _vm.CopyElement(_vm.SelectedElement); e.Handled = true; break;
            case Windows.System.VirtualKey.X when ctrl && !IsTextInputFocused() && _vm.SelectedElement is not null:
                _vm.CutElement(_vm.SelectedElement); e.Handled = true; break;
            case Windows.System.VirtualKey.V when ctrl && !IsTextInputFocused():
                _ = HandlePasteAsync(); e.Handled = true; break;
            case Windows.System.VirtualKey.D when ctrl && !IsTextInputFocused() && _vm.SelectedElement is not null:
                _vm.DuplicateElement(_vm.SelectedElement); e.Handled = true; break;
            case Windows.System.VirtualKey.Left when _vm.SelectedElement is not null && !IsTextInputFocused():
                _vm.NudgeSelected(-nudge, 0); e.Handled = true; break;
            case Windows.System.VirtualKey.Right when _vm.SelectedElement is not null && !IsTextInputFocused():
                _vm.NudgeSelected(nudge, 0); e.Handled = true; break;
            case Windows.System.VirtualKey.Up when _vm.SelectedElement is not null && !IsTextInputFocused():
                _vm.NudgeSelected(0, -nudge); e.Handled = true; break;
            case Windows.System.VirtualKey.Down when _vm.SelectedElement is not null && !IsTextInputFocused():
                _vm.NudgeSelected(0, nudge); e.Handled = true; break;
        }
    }

    /// <summary>On startup, if the recovery folder holds an unsaved snapshot, offer to restore it.</summary>
    private async Task CheckRecoveryAsync()
    {
        if (_recoveryChecked) return;
        _recoveryChecked = true;
        var info = ShellViewModel.GetRecoveryInfo();
        if (!info.exists) return;
        try
        {
            var dlg = new ContentDialog
            {
                Title = "恢复未保存的更改",
                Content = $"上次有未保存的课件：{(string.IsNullOrEmpty(info.title) ? "未命名" : info.title)}\n时间：{info.savedAt}\n\n是否恢复？",
                PrimaryButtonText = "恢复",
                CloseButtonText = "丢弃",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            var r = await dlg.ShowAsync();
            if (r == ContentDialogResult.Primary) _vm.OpenRecovery(info.original);
            else ShellViewModel.ClearRecovery();
        }
        catch (Exception ex) { App.LogException("Recovery", ex); }
    }

    /// <summary>Ctrl+V: prefer real clipboard media (image / files), else the in-app element clipboard.</summary>
    private async Task HandlePasteAsync()
    {
        try
        {
            var dv = Clipboard.GetContent();
            if (dv.Contains(StandardDataFormats.Bitmap))
            {
                var path = await Views.StageView.SaveDataViewBitmapAsync(dv);
                if (path is not null) { _vm.InsertFile(path, 960, 540); return; }
            }
            else if (dv.Contains(StandardDataFormats.StorageItems))
            {
                var items = await dv.GetStorageItemsAsync();
                bool any = false; double off = 0;
                foreach (var it in items)
                    if (it is StorageFile sf && ShellViewModel.IsSupportedMediaFile(sf.Path))
                    { _vm.InsertFile(sf.Path, 960 + off, 540 + off); off += 28; any = true; }
                if (any) return;
            }
        }
        catch (Exception ex) { App.LogException("HandlePaste", ex); }
        _vm.PasteElement();   // fall back to the in-app element clipboard
    }

    /// <summary>True when a text-editing control has focus, so element shortcuts (Ctrl+C/X/V/D)
    /// don't hijack normal text editing in the properties pane.</summary>
    private bool IsTextInputFocused()
    {
        try
        {
            var f = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Content?.XamlRoot);
            return f is TextBox or RichEditBox or AutoSuggestBox or PasswordBox;
        }
        catch { return false; }
    }

    // ── 诊断 ─────────────────────────────────────────────────────────────────

    private void ShowDiagnostic(string? message)
    {
        if (string.IsNullOrEmpty(message))
        { DiagnosticsBar.Visibility = Visibility.Collapsed; return; }
        DiagnosticsBar.Visibility = Visibility.Visible;
        DiagnosticsText.Text = message;
    }

    private void OnDismissDiagnostics(object sender, RoutedEventArgs e)
    {
        DiagnosticsBar.Visibility = Visibility.Collapsed;
        _vm.ClearDiagnostic();
    }

    // ── 状态栏 ───────────────────────────────────────────────────────────────

    private void UpdatePageInfo()
    {
        var page = _vm.Document?.SelectedPage;
        if (page is null) { PageInfoText.Text = ""; return; }
        var total = _vm.Document?.Pages.Count ?? 0;
        var idx = (_vm.Document?.Pages.IndexOf(page) ?? -1) + 1;
        PageInfoText.Text = $"第 {idx}/{total} 页";
    }

    private void UpdateSlideCount()
    {
        var count = _vm.Document?.Pages.Count ?? 0;
        SlideCountText.Text = $"{count} 页";
    }

    private void UpdateElementInfo()
    {
        var sel = _vm.SelectedElement;
        ElementInfoText.Text = sel is null
            ? ""
            : $"{sel.Type}{(string.IsNullOrEmpty(sel.Name) ? "" : $" · \"{sel.Name}\"")}";
    }
}

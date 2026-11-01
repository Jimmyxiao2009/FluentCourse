using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentCourse.Designer.Models;
using FluentCourse.Designer.Services;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentCourse.Designer.Views;

public sealed partial class PropertiesPane : UserControl
{
    private ShellViewModel? _vm;
    private bool _building;

    public PropertiesPane()
    {
        try { InitializeComponent(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PropertiesPane] init failed: {ex}"); }
    }

    public void SetViewModel(ShellViewModel vm)
    {
        _vm = vm;
        vm.SelectionChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        PropertyRoot.Children.Clear();
        var em = _vm?.SelectedElement;

        if (em is null)
        {
            EmptyHint.Visibility = Visibility.Visible;
            ElementLabel.Text = "";
            ElementPath.Text = "";
            return;
        }

        EmptyHint.Visibility = Visibility.Collapsed;
        ElementLabel.Text = $"元素: {em.Type}";
        ElementPath.Text = _vm?.SelectedElementPath ?? string.Empty;

        _building = true;
        try
        {
            BuildCommonGroup(em);
            BuildTypeSpecificGroup(em);
            if (em.Type == "Button") BuildButtonActionGroup(em);
        }
        catch (Exception ex)
        {
            // Never let a property-row builder crash the whole app; surface it instead.
            App.LogException("PropertiesPane.Refresh", ex);
            _vm?.ReportDiagnostic($"属性面板构建失败: {ex.Message}");
        }
        finally
        {
            _building = false;
        }
    }

    private void BuildCommonGroup(ElementModel em)
    {
        var expander = CreateExpander("布局与位置", true);
        var panel = new StackPanel { Spacing = 8 };

        panel.Children.Add(CreateNumberRow("X 坐标", em.X, v =>
        {
            if (_building || !TryParseInv(v, out var d) || d == em.X) return;
            var oldX = em.X;
            em.X = d;
            _vm?.MoveElement(em, oldX, em.Y, d, em.Y);   // 压撤销 + 标脏
            _vm?.RaiseRenderRequest();
        }));
        panel.Children.Add(CreateNumberRow("Y 坐标", em.Y, v =>
        {
            if (_building || !TryParseInv(v, out var d) || d == em.Y) return;
            var oldY = em.Y;
            em.Y = d;
            _vm?.MoveElement(em, em.X, oldY, em.X, d);
            _vm?.RaiseRenderRequest();
        }));
        panel.Children.Add(CreateNumberRow("宽度", em.Width, v =>
        {
            if (_building || !TryParseInv(v, out var d)) return;
            var nw = Math.Max(20, d);
            if (nw == em.Width) return;
            var oldW = em.Width;
            em.Width = nw;
            _vm?.ResizeElement(em, em.X, em.Y, oldW, em.Height, em.X, em.Y, nw, em.Height);
            _vm?.RaiseRenderRequest();
        }));
        panel.Children.Add(CreateNumberRow("高度", em.Height, v =>
        {
            if (_building || !TryParseInv(v, out var d)) return;
            var nh = Math.Max(20, d);
            if (nh == em.Height) return;
            var oldH = em.Height;
            em.Height = nh;
            _vm?.ResizeElement(em, em.X, em.Y, em.Width, oldH, em.X, em.Y, em.Width, nh);
            _vm?.RaiseRenderRequest();
        }));
        panel.Children.Add(CreateSliderRow("旋转", em.Rotation, -180, 180,
            onLive: v => { em.Rotation = v; EmChanged(em); },
            onCommitted: (start, end) => _vm?.RotateElement(em, start, end)));   // 拖动结束记一次撤销
        // 缩放: enlarge/shrink the whole control. Text uses 字号 instead, so skip it there.
        if (em.Type != "TextBlock")
        {
            panel.Children.Add(CreateNumberRow("缩放(%)", (em.Scale <= 0 ? 1 : em.Scale) * 100, v =>
            {
                if (_building || !TryParseInv(v, out var d) || d <= 0) return;
                var oldScale = em.Scale <= 0 ? 1 : em.Scale;
                var newScale = d / 100.0;
                if (Math.Abs(newScale - oldScale) < 0.0001) return;
                em.Scale = newScale;
                _vm?.PushUndo(new DelegateUndoCommand(
                    () => { em.Scale = oldScale; _vm?.RaiseRenderRequest(); },
                    () => { em.Scale = newScale; _vm?.RaiseRenderRequest(); },
                    "缩放"));
                EmChanged(em);
            }));
        }
        panel.Children.Add(CreateSliderRow("透明度", GetOpacity(em) * 100, 0, 100,
            onLive: v => { em.Props["Opacity"] = (v / 100.0).ToString("F2", CultureInfo.InvariantCulture); EmChanged(em); },
            onCommitted: (start, end) =>
            {
                // 起始值在拖动中已被覆盖，按起止比例还原旧字符串记撤销。
                var oldStr = (start / 100.0).ToString("F2", CultureInfo.InvariantCulture);
                var newStr = (end / 100.0).ToString("F2", CultureInfo.InvariantCulture);
                _vm?.ChangeProperty(em, "Opacity", oldStr, newStr);
            }));
        panel.Children.Add(CreateTextBoxRow("元素名称", em.Name ?? "", v =>
        {
            if (_building) return;
            var oldName = em.Name;
            var newName = string.IsNullOrWhiteSpace(v) ? null : v;
            if (oldName == newName) return;
            em.Name = newName;
            _vm?.PushUndo(new TextChangeCommand(
                () => em.Name ?? "",
                s => em.Name = string.IsNullOrWhiteSpace(s) ? null : s,
                oldName ?? "", newName ?? "",
                () => _vm?.RaiseRenderRequest(), "修改元素名称"));
            EmChanged(em);
        }));

        // Z-order buttons
        var zRow = new Grid { ColumnSpacing = 4 };
        zRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        zRow.ColumnDefinitions.Add(new ColumnDefinition());
        zRow.Children.Add(new TextBlock { Text = "层级", Style = Application.Current.Resources["PropertyRowLabel"] as Style, VerticalAlignment = VerticalAlignment.Center });
        var zBtnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        zBtnPanel.Children.Add(CreateZButton("↑", () => _vm?.ReorderElement(em, "forward")));
        zBtnPanel.Children.Add(CreateZButton("↓", () => _vm?.ReorderElement(em, "backward")));
        zBtnPanel.Children.Add(CreateZButton("顶", () => _vm?.ReorderElement(em, "front")));
        zBtnPanel.Children.Add(CreateZButton("底", () => _vm?.ReorderElement(em, "back")));
        Grid.SetColumn(zBtnPanel, 1);
        zRow.Children.Add(zBtnPanel);
        panel.Children.Add(zRow);

        expander.Content = panel;
        PropertyRoot.Children.Add(expander);
    }

    private void BuildTypeSpecificGroup(ElementModel em)
    {
        var (title, rows) = BuildTypeRows(em);
        if (rows.Count == 0) return;

        var expander = CreateExpander(title, true);
        var panel = new StackPanel { Spacing = 8 };
        foreach (var row in rows) panel.Children.Add(row);
        expander.Content = panel;
        PropertyRoot.Children.Add(expander);
    }

    // ── Button click-action editor ──────────────────────────────────────────────

    private static readonly (string key, string label)[] ButtonActionKinds =
    {
        ("none", "无（不做事）"),
        ("show", "显示一个元素"),
        ("hide", "隐藏一个元素"),
        ("toggle", "点击切换显示/隐藏"),
        ("anim", "播放动画"),
        ("prevpage", "上一页"),
        ("nextpage", "下一页"),
    };

    private static readonly (string key, string label)[] ButtonAnimations =
    {
        ("FadeIn", "淡入"),
        ("FadeOut", "淡出"),
        ("FadeInUp", "上滑淡入"),
        ("FadeInDown", "下滑淡入"),
        ("FadeInLeft", "左滑淡入"),
        ("FadeInRight", "右滑淡入"),
    };

    private void BuildButtonActionGroup(ElementModel em)
    {
        if (_vm is null) return;
        var info = _vm.GetButtonAction(em);

        var expander = CreateExpander("点击动作", true);
        var panel = new StackPanel { Spacing = 8 };

        // “点击时” — what kind of action
        var kindCombo = new ComboBox { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var k in ButtonActionKinds) kindCombo.Items.Add(k.label);
        var kindIdx = Array.FindIndex(ButtonActionKinds, k => k.key == info.Kind);
        kindCombo.SelectedIndex = kindIdx < 0 ? 0 : kindIdx;
        panel.Children.Add(CreateControlRow("点击时", kindCombo));

        // “目标” — which element the action acts on
        var targets = _vm.TargetableElements(em);
        var targetCombo = new ComboBox { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var t in targets)
            targetCombo.Items.Add(new ComboBoxItem { Content = FriendlyElementName(t), Tag = t });
        if (info.Target is not null)
            targetCombo.SelectedIndex = targets.FindIndex(t => ReferenceEquals(t, info.Target));
        var targetRow = CreateControlRow("目标", targetCombo);
        panel.Children.Add(targetRow);

        // “动画” — only for the anim kind
        var animCombo = new ComboBox { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var a in ButtonAnimations) animCombo.Items.Add(a.label);
        var animIdx = Array.FindIndex(ButtonAnimations, a => a.key == info.Animation);
        animCombo.SelectedIndex = animIdx < 0 ? 0 : animIdx;
        var animRow = CreateControlRow("动画", animCombo);
        panel.Children.Add(animRow);

        var hint = new TextBlock
        {
            Text = "提示：目标如果要“一开始隐藏、点击才出现”，把它的透明度设为 0 或先隐藏。预览按 F5 可测试。",
            FontSize = 10, TextWrapping = TextWrapping.Wrap,
            Foreground = Application.Current.Resources["TextTertiaryBrush"] as Brush,
            Margin = new Thickness(0, 2, 0, 0)
        };
        panel.Children.Add(hint);

        void UpdateVisibility()
        {
            var kind = ButtonActionKinds[kindCombo.SelectedIndex].key;
            bool needTarget = kind is "show" or "hide" or "toggle" or "anim";
            targetRow.Visibility = needTarget ? Visibility.Visible : Visibility.Collapsed;
            animRow.Visibility = kind == "anim" ? Visibility.Visible : Visibility.Collapsed;
        }

        void Apply()
        {
            if (_building) return;
            var kind = ButtonActionKinds[kindCombo.SelectedIndex].key;
            var target = (targetCombo.SelectedItem as ComboBoxItem)?.Tag as ElementModel;
            var anim = ButtonAnimations[animCombo.SelectedIndex < 0 ? 0 : animCombo.SelectedIndex].key;
            _vm!.SetButtonAction(em, kind, target, anim);
        }

        UpdateVisibility();
        kindCombo.SelectionChanged += (_, _) => { UpdateVisibility(); Apply(); };
        targetCombo.SelectionChanged += (_, _) => Apply();
        animCombo.SelectionChanged += (_, _) => Apply();

        expander.Content = panel;
        PropertyRoot.Children.Add(expander);
    }

    private static string FriendlyElementName(ElementModel e)
    {
        string type = e.Type switch
        {
            "Button" => "按钮", "TextBlock" => "文本", "Image" => "图片",
            "MediaPlayerElement" => "视频", "Rectangle" => "矩形", "Ellipse" => "圆形",
            "Polygon" => "形状", "Line" => "线条", "QuizControl" => "题目",
            "StackPanel" or "Grid" or "Canvas" or "Border" or "ScrollViewer" => "容器",
            _ => e.Type
        };
        if (e.Type == "MediaPlayerElement" && e.Props.TryGetValue("fc:Media.Kind", out var mediaKind) && mediaKind == "Audio")
            type = "音频";
        var text = e.Props.TryGetValue("Text", out var t) ? t
                 : e.Props.TryGetValue("Content", out var c) ? c
                 : e.Props.TryGetValue("Question", out var q) ? q
                 : e.Name ?? "";
        if (string.IsNullOrWhiteSpace(text)) return type;
        if (text.Length > 12) text = text.Substring(0, 12) + "…";
        return $"{type}：{text}";
    }

    private async System.Threading.Tasks.Task PickMediaFile(ElementModel em)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker { ViewMode = Windows.Storage.Pickers.PickerViewMode.List };
            foreach (var e in new[] { ".mp4", ".mov", ".webm", ".m4v", ".avi", ".mp3", ".m4a", ".wav", ".ogg", ".aac" })
                picker.FileTypeFilter.Add(e);
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var rel = _vm?.ImportAsset(file.Path);
            if (rel is null) return;
            CommitProp(em, "Source", $"ms-fcourse:///{rel}");
            Refresh();
        }
        catch (Exception ex)
        {
            _vm?.ReportDiagnostic($"导入媒体失败: {ex.Message}");
        }
    }

    // Common Windows-installed fonts (label → actual family name).
    private static readonly (string label, string fam)[] Fonts =
    {
        ("微软雅黑", "Microsoft YaHei UI"),
        ("宋体", "SimSun"),
        ("黑体", "SimHei"),
        ("楷体", "KaiTi"),
        ("仿宋", "FangSong"),
        ("等线", "DengXian"),
        ("Segoe UI", "Segoe UI"),
        ("Arial", "Arial"),
        ("Times New Roman", "Times New Roman"),
        ("Consolas", "Consolas"),
    };

    private FrameworkElement CreateFontRow(ElementModel em)
    {
        var combo = new ComboBox { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (label, fam) in Fonts) combo.Items.Add(new ComboBoxItem { Content = label, Tag = fam });
        var cur = GetProp(em, "FontFamily", "Microsoft YaHei UI");
        var idx = Array.FindIndex(Fonts, f => f.fam == cur);
        combo.SelectedIndex = idx < 0 ? 0 : idx;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem ci && ci.Tag is string fam)
                CommitProp(em, "FontFamily", fam);
        };
        return CreateControlRow("字体", combo);
    }

    private FrameworkElement CreateControlRow(string label, FrameworkElement control)
    {
        var grid = new Grid { Padding = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = Application.Current.Resources["PropertyRowLabel"] as Style,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private (string title, List<FrameworkElement> rows) BuildTypeRows(ElementModel em)
    {
        var rows = new List<FrameworkElement>();

        switch (em.Type)
        {
            case "TextBlock":
                rows.Add(CreateMultilineTextRow("文本内容", GetProp(em, "Text", ""),
                    v => CommitProp(em, "Text", v)));
                rows.Add(CreateFontRow(em));
                rows.Add(CreateNumberRow("字号", GetDoubleProp(em, "FontSize", 24),
                    v => CommitNumProp(em, "FontSize", v)));
                rows.Add(CreateComboRow("字重", GetProp(em, "FontWeight", "Normal"),
                    new[] { "Thin", "ExtraLight", "Light", "Normal", "Medium", "SemiBold", "Bold", "ExtraBold", "Black" },
                    v => CommitProp(em, "FontWeight", v)));
                rows.Add(CreateColorRow("文字颜色", GetProp(em, "Foreground", "#FFFFFFFF"),
                    v => CommitProp(em, "Foreground", v)));
                rows.Add(CreateComboRow("对齐方式", GetProp(em, "TextAlignment", "Left"),
                    new[] { "Left", "Center", "Right", "Justify" },
                    v => CommitProp(em, "TextAlignment", v)));
                rows.Add(CreateToggleRow("自动换行", GetProp(em, "TextWrapping", "NoWrap") == "Wrap",
                    v => CommitProp(em, "TextWrapping", v ? "Wrap" : "NoWrap")));
                return ("文本", rows);

            case "Image":
                rows.Add(CreateTextBoxRow("图片源", GetProp(em, "Source", ""),
                    v => CommitProp(em, "Source", v)));
                rows.Add(new Grid
                {
                    ColumnSpacing = 4
                }.Then(g =>
                {
                    var btn = new Button { Content = "选择图片", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
                    btn.Click += async (_, _) =>
                    {
                        var picker = new Windows.Storage.Pickers.FileOpenPicker { ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail };
                        picker.FileTypeFilter.Add(".png"); picker.FileTypeFilter.Add(".jpg");
                        picker.FileTypeFilter.Add(".jpeg"); picker.FileTypeFilter.Add(".gif");
                        picker.FileTypeFilter.Add(".bmp"); picker.FileTypeFilter.Add(".webp");
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
                        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                        var file = await picker.PickSingleFileAsync();
                        if (file is not null)
                        {
                            // 复制进课件 assets 再引用（否则保存后路径失效）。
                            var rel = _vm?.ImportAsset(file.Path);
                            if (rel is null) return;
                            CommitProp(em, "Source", $"ms-fcourse:///{rel}");
                            Refresh();
                        }
                    };
                    g.Children.Add(btn);   // 之前漏加，按钮根本不显示
                    return g;
                }));
                rows.Add(CreateComboRow("填充方式", GetProp(em, "Stretch", "Uniform"),
                    new[] { "None", "Fill", "Uniform", "UniformToFill" },
                    v => CommitProp(em, "Stretch", v)));
                return ("图片", rows);

            case "Rectangle":
                rows.Add(CreateColorRow("填充色", GetProp(em, "Fill", "#FF0078D4"),
                    v => CommitProp(em, "Fill", v)));
                rows.Add(CreateColorRow("描边色", GetProp(em, "Stroke", "#FF000000"),
                    v => CommitProp(em, "Stroke", v)));
                rows.Add(CreateNumberRow("描边宽度", GetDoubleProp(em, "StrokeThickness", 0),
                    v => CommitNumProp(em, "StrokeThickness", v)));
                rows.Add(CreateNumberRow("圆角", GetDoubleProp(em, "RadiusX", 0), v =>
                {
                    if (!TryParseInv(v, out var d)) return;
                    // RadiusX/Y 必须一起改，合成一条撤销。
                    var s = Inv(d);
                    var oldX = em.Props.TryGetValue("RadiusX", out var ox) ? ox : null;
                    var oldY = em.Props.TryGetValue("RadiusY", out var oy) ? oy : null;
                    if (oldX == s && oldY == s) return;
                    void Set(string? x, string? y)
                    {
                        if (x is null) em.Props.Remove("RadiusX"); else em.Props["RadiusX"] = x;
                        if (y is null) em.Props.Remove("RadiusY"); else em.Props["RadiusY"] = y;
                    }
                    Set(s, s);
                    _vm?.PushUndo(new DelegateUndoCommand(
                        () => { Set(oldX, oldY); _vm?.RaiseRenderRequest(); },
                        () => { Set(s, s); _vm?.RaiseRenderRequest(); },
                        "修改圆角"));
                    EmChanged(em);
                }));
                return ("形状", rows);

            case "Ellipse":
            case "Polygon":
            case "Line":
                rows.Add(CreateColorRow("填充色", GetProp(em, "Fill", "#FF0078D4"),
                    v => CommitProp(em, "Fill", v)));
                rows.Add(CreateColorRow("描边色", GetProp(em, "Stroke", "#FFFFFFFF"),
                    v => CommitProp(em, "Stroke", v)));
                rows.Add(CreateNumberRow("描边宽度", GetDoubleProp(em, "StrokeThickness", 2),
                    v => CommitNumProp(em, "StrokeThickness", v)));
                return ("形状", rows);

            case "Button":
                rows.Add(CreateTextBoxRow("按钮文字", GetProp(em, "Content", "Button"),
                    v => CommitProp(em, "Content", v)));
                rows.Add(CreateColorRow("背景色", GetProp(em, "Background", "#FF0078D4"),
                    v => CommitProp(em, "Background", v)));
                rows.Add(CreateColorRow("文字颜色", GetProp(em, "Foreground", "#FFFFFFFF"),
                    v => CommitProp(em, "Foreground", v)));
                rows.Add(CreateNumberRow("圆角", GetDoubleProp(em, "CornerRadius", 0),
                    v => CommitNumProp(em, "CornerRadius", v)));
                rows.Add(CreateNumberRow("字号", GetDoubleProp(em, "FontSize", 14),
                    v => CommitNumProp(em, "FontSize", v)));
                return ("按钮", rows);

            case "HyperlinkButton":
            case "ToggleButton":
            case "CheckBox":
            case "RadioButton":
                rows.Add(CreateTextBoxRow("文字", GetProp(em, "Content", ""),
                    v => CommitProp(em, "Content", v)));
                rows.Add(CreateNumberRow("字号", GetDoubleProp(em, "FontSize", 14),
                    v => CommitNumProp(em, "FontSize", v)));
                if (em.Type == "RadioButton")
                    rows.Add(CreateTextBoxRow("分组名", GetProp(em, "GroupName", "g1"),
                        v => CommitProp(em, "GroupName", v)));
                if (em.Type == "HyperlinkButton")
                    rows.Add(CreateTextBoxRow("链接地址", GetProp(em, "NavigateUri", ""),
                        v => CommitProp(em, "NavigateUri", v)));
                return ("控件", rows);

            case "CountdownControl":
                rows.Add(CreateNumberRow("秒数", GetDoubleProp(em, "Seconds", 60), v =>
                {
                    if (TryParseInv(v, out var d)) CommitProp(em, "Seconds", ((int)d).ToString(CultureInfo.InvariantCulture));
                }));
                rows.Add(CreateToggleRow("自动开始", GetProp(em, "AutoStart", "True") == "True",
                    v => CommitProp(em, "AutoStart", v ? "True" : "False")));
                return ("倒计时", rows);

            case "FormulaControl":
                rows.Add(BuildFormulaEditor(em));
                return ("公式", rows);

            case "MediaPlayerElement":
            {
                var srcRow = CreateTextBoxRow("媒体地址", GetProp(em, "Source", ""),
                    v => CommitProp(em, "Source", v));
                rows.Add(srcRow);
                var pick = new Button { Content = "选择媒体文件…", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
                pick.Click += async (_, _) => await PickMediaFile(em);
                rows.Add(pick);
                rows.Add(new TextBlock
                {
                    Text = "支持 mp4/mov/webm 视频、mp3/m4a/wav 音频。预览(F5)时播放。",
                    FontSize = 10, TextWrapping = TextWrapping.Wrap,
                    Foreground = Application.Current.Resources["TextTertiaryBrush"] as Brush
                });
                return (GetProp(em, "fc:Media.Kind", "") == "Audio" ? "音频" : "视频", rows);
            }

            case "TextBox":
            case "AutoSuggestBox":
                rows.Add(CreateTextBoxRow("文本", GetProp(em, "Text", ""),
                    v => CommitProp(em, "Text", v)));
                rows.Add(CreateTextBoxRow("占位文字", GetProp(em, "PlaceholderText", ""),
                    v => CommitProp(em, "PlaceholderText", v)));
                rows.Add(CreateNumberRow("字号", GetDoubleProp(em, "FontSize", 14),
                    v => CommitNumProp(em, "FontSize", v)));
                return ("输入框", rows);

            case "Slider":
                rows.Add(CreateNumberRow("最小值", GetDoubleProp(em, "Minimum", 0),
                    v => CommitNumProp(em, "Minimum", v)));
                rows.Add(CreateNumberRow("最大值", GetDoubleProp(em, "Maximum", 100),
                    v => CommitNumProp(em, "Maximum", v)));
                rows.Add(CreateNumberRow("当前值", GetDoubleProp(em, "Value", 50),
                    v => CommitNumProp(em, "Value", v)));
                return ("滑块", rows);

            case "Border":
            case "Grid":
            case "StackPanel":
            case "Canvas":
            case "ScrollViewer":
                rows.Add(CreateColorRow("背景色", GetProp(em, "Background", "#22FFFFFF"),
                    v => CommitProp(em, "Background", v)));
                if (em.Type == "Border")
                {
                    rows.Add(CreateColorRow("边框色", GetProp(em, "BorderBrush", "#FF808080"),
                        v => CommitProp(em, "BorderBrush", v)));
                    rows.Add(CreateNumberRow("边框宽度", GetDoubleProp(em, "BorderThickness", 2),
                        v => CommitNumProp(em, "BorderThickness", v)));
                    rows.Add(CreateNumberRow("圆角", GetDoubleProp(em, "CornerRadius", 0),
                        v => CommitNumProp(em, "CornerRadius", v)));
                }
                if (em.Type == "StackPanel")
                    rows.Add(CreateComboRow("方向", GetProp(em, "Orientation", "Vertical"),
                        new[] { "Vertical", "Horizontal" },
                        v => CommitProp(em, "Orientation", v)));
                return ("容器", rows);

            case "QuizControl":
            {
                var mode = GetProp(em, "Mode", "Single");
                string modeName = mode switch
                {
                    "Multiple"    => "多选题",
                    "FillInBlank" => "填空题",
                    "TrueFalse"   => "判断题",
                    _             => "单选题"
                };
                rows.Add(CreateReadonlyRow("题型", modeName));
                rows.Add(CreateMultilineTextRow("题干", GetProp(em, "Question", ""),
                    v => CommitProp(em, "Question", v)));

                if (mode == "FillInBlank")
                {
                    int n = 1;
                    foreach (var b in em.Children.Where(c => c.Type == "QuizBlank").ToList())
                        rows.Add(CreateQuizBlankRow(em, b, n++));
                    rows.Add(CreateAddButton("＋ 添加一个空", () =>
                    {
                        var b = new ElementModel { Type = "QuizBlank" };
                        b.Props["Pattern"] = "答案";
                        b.Props["CaseSensitive"] = "false";
                        var idx = em.Children.Count;
                        em.Children.Add(b);
                        _vm?.PushUndo(new InsertElementCommand(em.Children, b, idx,
                            () => _vm?.RaiseRenderRequest(), "增加填空"));
                        EmChanged(em); Refresh();
                    }));
                }
                else
                {
                    bool single = mode is "Single" or "TrueFalse";
                    foreach (var c in em.Children.Where(c => c.Type == "QuizChoice").ToList())
                        rows.Add(CreateQuizChoiceRow(em, c, single));
                    if (mode != "TrueFalse")
                        rows.Add(CreateAddButton("＋ 添加选项", () =>
                        {
                            var c = new ElementModel { Type = "QuizChoice" };
                            c.Props["Text"] = "新选项";
                            var idx = em.Children.Count;
                            em.Children.Add(c);
                            _vm?.PushUndo(new InsertElementCommand(em.Children, c, idx,
                                () => _vm?.RaiseRenderRequest(), "增加选项"));
                            EmChanged(em); Refresh();
                        }));
                }
                return ("题目", rows);
            }

            default:
                return (em.Type, rows);
        }
    }

    // ── Quiz editor rows ──

    private FrameworkElement CreateReadonlyRow(string label, string value)
    {
        var grid = new Grid { Padding = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = Application.Current.Resources["PropertyRowLabel"] as Style,
            VerticalAlignment = VerticalAlignment.Center
        });
        var v = new TextBlock { Text = value, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        return grid;
    }

    private FrameworkElement CreateQuizChoiceRow(ElementModel quiz, ElementModel choice, bool single)
    {
        var block = new StackPanel { Spacing = 2, Margin = new Thickness(0, 2, 0, 4) };

        // 行1：☑答案 + 选项文字 + ✕删除
        var grid = new Grid { ColumnSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var chk = new CheckBox
        {
            IsChecked = GetProp(choice, "IsAnswer", "") == "True",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(chk, "勾选 = 正确答案");
        chk.Checked += (_, _) =>
        {
            if (_building) return;
            if (single)
            {
                // 单选：清掉其它选项的答案标记，整组变化合成一条撤销。
                var olds = quiz.Children.Where(c => c.Type == "QuizChoice")
                    .Select(c => (c, c.Props.TryGetValue("IsAnswer", out var a) ? a : null)).ToList();
                void Apply()
                {
                    foreach (var (c, _) in olds) c.Props.Remove("IsAnswer");
                    choice.Props["IsAnswer"] = "True";
                }
                void Revert()
                {
                    foreach (var (c, a) in olds)
                        if (a is null) c.Props.Remove("IsAnswer"); else c.Props["IsAnswer"] = a;
                }
                Apply();
                _vm?.PushUndo(new DelegateUndoCommand(
                    () => { Revert(); _vm?.RaiseRenderRequest(); },
                    () => { Apply(); _vm?.RaiseRenderRequest(); },
                    "设置正确答案"));
                EmChanged(quiz);
                Refresh();
            }
            else { CommitProp(choice, "IsAnswer", "True"); }
        };
        chk.Unchecked += (_, _) => { if (_building) return; CommitProp(choice, "IsAnswer", null); };
        Grid.SetColumn(chk, 0);
        grid.Children.Add(chk);

        var tb = new TextBox { Text = GetProp(choice, "Text", ""), FontSize = 12, Padding = new Thickness(6, 4, 6, 4) };
        AttachCommitOnLostFocus(tb, v => CommitProp(choice, "Text", v), commitOnEnter: true);
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);

        var del = new Button { Content = "✕", FontSize = 11, Width = 30, Height = 30, Padding = new Thickness(0) };
        ToolTipService.SetToolTip(del, "删除此选项");
        del.Click += (_, _) =>
        {
            var idx = quiz.Children.IndexOf(choice);
            if (idx < 0) return;
            quiz.Children.RemoveAt(idx);
            _vm?.PushUndo(new DeleteElementCommand(quiz.Children, choice, idx,
                () => _vm?.RaiseRenderRequest(), "删除选项"));
            EmChanged(quiz); Refresh();
        };
        Grid.SetColumn(del, 2);
        grid.Children.Add(del);
        block.Children.Add(grid);

        // 行2：解析（选填，答题后显示）
        block.Children.Add(CreateExplanationBox(quiz, choice, "解析（选填，答题后显示）"));
        return block;
    }

    private FrameworkElement CreateQuizBlankRow(ElementModel quiz, ElementModel blank, int index)
    {
        var block = new StackPanel { Spacing = 2, Margin = new Thickness(0, 2, 0, 4) };

        var grid = new Grid { ColumnSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = $"空{index}",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Application.Current.Resources["TextTertiaryBrush"] as Brush
        });

        var tb = new TextBox { Text = GetProp(blank, "Pattern", ""), FontSize = 12, Padding = new Thickness(6, 4, 6, 4) };
        ToolTipService.SetToolTip(tb, "正确答案（多个写法用 | 分隔，如 氮|N）");
        AttachCommitOnLostFocus(tb, v => CommitProp(blank, "Pattern", v), commitOnEnter: true);
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);

        var del = new Button { Content = "✕", FontSize = 11, Width = 30, Height = 30, Padding = new Thickness(0) };
        ToolTipService.SetToolTip(del, "删除此空");
        del.Click += (_, _) =>
        {
            var idx = quiz.Children.IndexOf(blank);
            if (idx < 0) return;
            quiz.Children.RemoveAt(idx);
            _vm?.PushUndo(new DeleteElementCommand(quiz.Children, blank, idx,
                () => _vm?.RaiseRenderRequest(), "删除填空"));
            EmChanged(quiz); Refresh();
        };
        Grid.SetColumn(del, 2);
        grid.Children.Add(del);
        block.Children.Add(grid);

        block.Children.Add(CreateExplanationBox(quiz, blank, "解析（选填，答题后显示）"));
        return block;
    }

    /// <summary>A subtle, indented Explanation editor for one choice/blank. Empty clears the prop.</summary>
    private FrameworkElement CreateExplanationBox(ElementModel quiz, ElementModel item, string placeholder)
    {
        var exp = new TextBox
        {
            Text = GetProp(item, "Explanation", ""),
            PlaceholderText = placeholder,
            FontSize = 11,
            Padding = new Thickness(6, 3, 6, 3),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Margin = new Thickness(24, 0, 30, 0)
        };
        AttachCommitOnLostFocus(exp,
            v => CommitProp(item, "Explanation", string.IsNullOrEmpty(v) ? null : v),
            commitOnEnter: false);
        return exp;
    }

    private FrameworkElement CreateAddButton(string text, Action onAdd)
    {
        var btn = new Button
        {
            Content = text,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 4, 0, 0)
        };
        btn.Click += (_, _) => onAdd();
        return btn;
    }

    // ── Change helpers ──

    /// <summary>仅刷新（不记撤销）—— 留给少数实时预览场景。</summary>
    private void EmChanged(ElementModel em)
    {
        if (_building) return;
        _vm?.MarkDirty();
        _vm?.RaiseRenderRequest();
    }

    /// <summary>
    /// 提交一次属性修改：写入 Props、压入撤销命令（旧值→新值）、标脏并重渲染。
    /// newValue 为 null 表示删除该属性。值未变化时不做任何事。
    /// </summary>
    private void CommitProp(ElementModel em, string key, string? newValue)
    {
        if (_building) return;
        var old = em.Props.TryGetValue(key, out var o) ? o : null;
        if (old == newValue) return;
        if (newValue is null) em.Props.Remove(key);
        else em.Props[key] = newValue;
        _vm?.ChangeProperty(em, key, old, newValue);   // 压撤销 + MarkDirty（不重复应用）
        _vm?.RaiseRenderRequest();
    }

    /// <summary>数字属性提交（按 InvariantCulture 写出，逗号小数点地区不产坏 XAML）。</summary>
    private void CommitNumProp(ElementModel em, string key, string raw)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            CommitProp(em, key, d.ToString(CultureInfo.InvariantCulture));
    }

    private static string Inv(double d) => d.ToString(CultureInfo.InvariantCulture);

    private static bool TryParseInv(string s, out double d) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);

    private static string GetProp(ElementModel em, string key, string def) =>
        em.Props.TryGetValue(key, out var v) ? v : def;

    private static double GetDoubleProp(ElementModel em, string key, double def) =>
        em.Props.TryGetValue(key, out var s) &&
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;

    private static double GetOpacity(ElementModel em) =>
        em.Props.TryGetValue("Opacity", out var s) &&
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var o) ? o : 1.0;

    // ── Row builders ──

    private static FrameworkElement CreateNumberRow(string label, double value, Action<string> onChanged)
    {
        var grid = new Grid { Padding = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = Application.Current.Resources["PropertyRowLabel"] as Style,
            VerticalAlignment = VerticalAlignment.Center
        });
        var nb = new NumberBox
        {
            Value = value,
            FontSize = 12,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        // NumberBox 本身在回车/失焦/点按钮时才提交 —— 这里只需把值按 Invariant 传出去。
        nb.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue)) return;
            onChanged(e.NewValue.ToString(CultureInfo.InvariantCulture));
        };
        Grid.SetColumn(nb, 1);
        grid.Children.Add(nb);
        return grid;
    }

    /// <summary>
    /// 滑块行：拖动中 onLive 实时预览；拖动结束（捕获丢失/失焦）调用 onCommitted(起始值, 结束值)
    /// 记一次撤销 —— 避免拖一下产生几十条撤销记录。
    /// </summary>
    private static FrameworkElement CreateSliderRow(string label, double value, double min, double max,
        Action<double> onLive, Action<double, double>? onCommitted = null)
    {
        var grid = new Grid { Padding = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = Application.Current.Resources["PropertyRowLabel"] as Style,
            VerticalAlignment = VerticalAlignment.Center
        });

        var slider = new Slider { Minimum = min, Maximum = max, Value = value, FontSize = 11 };
        double? dragStart = null;
        slider.ValueChanged += (_, e) =>
        {
            dragStart ??= e.OldValue;
            onLive(e.NewValue);
        };
        void Commit()
        {
            if (dragStart is null) return;
            var start = dragStart.Value;
            dragStart = null;
            if (Math.Abs(start - slider.Value) > 0.0001)
                onCommitted?.Invoke(start, slider.Value);
        }
        slider.AddHandler(UIElement.PointerCaptureLostEvent,
            new PointerEventHandler((_, _) => Commit()), handledEventsToo: true);
        slider.LostFocus += (_, _) => Commit();
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);

        var valText = new TextBlock { Text = $"{value:F0}", FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Application.Current.Resources["TextTertiaryBrush"] as Brush, Margin = new Thickness(4, 0, 0, 0) };
        slider.ValueChanged += (_, e) => valText.Text = $"{e.NewValue:F0}";
        Grid.SetColumn(valText, 2);
        grid.Children.Add(valText);

        return grid;
    }

    /// <summary>给 TextBox 装"失焦/回车才提交"的行为（解决每键全页重建的卡顿，也让撤销只记一次）。</summary>
    private static void AttachCommitOnLostFocus(TextBox tb, Action<string> onCommit, bool commitOnEnter)
    {
        var lastCommitted = tb.Text ?? "";
        void Commit()
        {
            var cur = tb.Text ?? "";
            if (cur == lastCommitted) return;
            lastCommitted = cur;
            onCommit(cur);
        }
        tb.LostFocus += (_, _) => Commit();
        if (commitOnEnter)
            tb.KeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Enter) { Commit(); e.Handled = true; }
            };
    }

    private static FrameworkElement CreateTextBoxRow(string label, string value, Action<string> onChanged)
    {
        var grid = new Grid { Padding = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = Application.Current.Resources["PropertyRowLabel"] as Style,
            VerticalAlignment = VerticalAlignment.Center
        });
        var tb = new TextBox { Text = value ?? "", FontSize = 12, Padding = new Thickness(6, 4, 6, 4) };
        AttachCommitOnLostFocus(tb, onChanged, commitOnEnter: true);
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);
        return grid;
    }

    private static FrameworkElement CreateMultilineTextRow(string label, string value, Action<string> onChanged)
    {
        var grid = new Grid { Padding = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = Application.Current.Resources["PropertyRowLabel"] as Style,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 0, 0)
        });
        var tb = new TextBox { Text = value ?? "", FontSize = 12, Padding = new Thickness(6, 4, 6, 4),
            MinHeight = 60, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        AttachCommitOnLostFocus(tb, onChanged, commitOnEnter: false);   // 多行框回车是换行
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);
        return grid;
    }

    private static FrameworkElement CreateComboRow(string label, string selected, string[] options, Action<string> onChanged)
    {
        var grid = new Grid { Padding = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = Application.Current.Resources["PropertyRowLabel"] as Style,
            VerticalAlignment = VerticalAlignment.Center
        });
        var cb = new ComboBox { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var o in options) cb.Items.Add(o);
        cb.SelectedItem = selected;
        cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is string s) onChanged(s); };
        Grid.SetColumn(cb, 1);
        grid.Children.Add(cb);
        return grid;
    }

    private static FrameworkElement CreateColorRow(string label, string hex, Action<string> onChanged)
    {
        var grid = new Grid { Padding = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = Application.Current.Resources["PropertyRowLabel"] as Style,
            VerticalAlignment = VerticalAlignment.Center
        });

        var tb = new TextBox { Text = hex, FontSize = 12, Padding = new Thickness(6, 4, 6, 4) };
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);

        // Swatch is a button that opens a visual ColorPicker — teachers don't know hex codes.
        var fill = new Border
        {
            Width = 26, Height = 18, CornerRadius = new CornerRadius(3),
            Background = CanvasRenderer.ParseBrush(hex)
        };
        var picker = new ColorPicker { IsAlphaEnabled = true, IsHexInputVisible = true, Width = 288, Height = 300 };
        var flyout = new Flyout { Content = picker };
        flyout.Opening += (_, _) => picker.Color = HexToColor(tb.Text);
        var pickBtn = new Button
        {
            Content = fill,
            Padding = new Thickness(3),
            MinWidth = 0,
            Margin = new Thickness(4, 0, 0, 0),
            BorderBrush = Application.Current.Resources["StrokeDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            Flyout = flyout
        };
        Grid.SetColumn(pickBtn, 2);
        grid.Children.Add(pickBtn);

        // 色板实时预览；真正提交（写模型 + 撤销）在失焦/回车/取色器关闭时发生一次。
        tb.TextChanged += (_, _) => fill.Background = CanvasRenderer.ParseBrush(tb.Text);
        AttachCommitOnLostFocus(tb, onChanged, commitOnEnter: true);
        flyout.Closed += (_, _) =>
        {
            var picked = ColorToHex(picker.Color);
            if (picked == tb.Text) return;
            tb.Text = picked;        // 更新色板
            onChanged(picked);       // 立即提交一次
        };
        return grid;
    }

    // 取色器初始色：复用 CanvasRenderer.ParseBrush 做 6/8 位 hex 解析（与色板预览同一套逻辑），
    // 但保留本控件原有的可见回退——非法/空串时取色器以不透明白打开（ParseBrush 对非法串返回透明，
    // 此处仅在该回退场景把它替换为白，避免改变老师可见行为）。
    private static Windows.UI.Color HexToColor(string? hex)
    {
        if (hex is null)
            return Windows.UI.Color.FromArgb(255, 255, 255, 255);
        var h = hex.TrimStart('#');
        // 仅 6/8 位且全为合法 hex 字符才解析；其余（含空串、非法字符）走白色回退，与旧实现一致。
        bool valid = (h.Length == 6 || h.Length == 8) && h.All(Uri.IsHexDigit);
        if (!valid)
            return Windows.UI.Color.FromArgb(255, 255, 255, 255);
        return CanvasRenderer.ParseBrush(hex).Color;
    }
    private static string ColorToHex(Windows.UI.Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static FrameworkElement CreateToggleRow(string label, bool isOn, Action<bool> onChanged)
    {
        var grid = new Grid { Padding = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = Application.Current.Resources["PropertyRowLabel"] as Style,
            VerticalAlignment = VerticalAlignment.Center
        });
        var tog = new ToggleSwitch { IsOn = isOn, MinWidth = 0, OnContent = "", OffContent = "" };
        tog.Toggled += (_, _) => onChanged(tog.IsOn);
        Grid.SetColumn(tog, 1);
        grid.Children.Add(tog);
        return grid;
    }

    // ── 公式可视化编辑器（底层仍是 LaTeX，老师点按钮即可，不必手写反斜杠） ──────

    // (按钮上显示的字, 插入的 LaTeX, 插入后光标相对位移；-1 = 落在末尾)
    private static readonly (string disp, string tex, int caret)[] FormulaTemplates =
    {
        ("a/b", "\\frac{}{}", 6),
        ("√",   "\\sqrt{}", 6),
        ("ⁿ√",  "\\sqrt[]{}", 6),
        ("x²",  "^{}", 2),
        ("xₙ",  "_{}", 2),
        ("上下", "_{}^{}", 2),
        ("∑",   "\\sum_{}^{}", 6),
        ("∏",   "\\prod_{}^{}", 7),
        ("∫",   "\\int_{}^{}", 6),
        ("lim", "\\lim_{x \\to }", -1),
        ("( )", "\\left( \\right)", 7),
        ("[ ]", "\\left[ \\right]", 7),
        ("{ }", "\\left\\{ \\right\\}", 8),
    };

    private static readonly (string disp, string tex)[] FormulaOperators =
    {
        ("×","\\times "),("÷","\\div "),("±","\\pm "),("·","\\cdot "),
        ("≤","\\leq "),("≥","\\geq "),("≠","\\neq "),("≈","\\approx "),
        ("→","\\to "),("∞","\\infty "),("°","^{\\circ} "),("∝","\\propto "),
    };

    private static readonly (string disp, string tex)[] FormulaGreek =
    {
        ("α","\\alpha "),("β","\\beta "),("γ","\\gamma "),("δ","\\delta "),
        ("θ","\\theta "),("λ","\\lambda "),("μ","\\mu "),("π","\\pi "),
        ("σ","\\sigma "),("φ","\\phi "),("ω","\\omega "),("Δ","\\Delta "),("Ω","\\Omega "),
    };

    // 物理 / 化学常用符号：矢量、反应箭头、电荷、温度、文字标注
    private static readonly (string disp, string tex, int caret)[] FormulaSci =
    {
        ("vec", "\\vec{}", 5),
        ("⇌",   "\\rightleftharpoons ", -1),
        ("↑",   "\\uparrow ", -1),
        ("↓",   "\\downarrow ", -1),
        ("⁺",   "^{+} ", -1),
        ("⁻",   "^{-} ", -1),
        ("²⁻",  "^{2-} ", -1),
        ("℃",   "^{\\circ}\\text{C} ", -1),
        ("字",  "\\text{}", 6),
    };

    private static readonly (string name, string tex)[] FormulaPresetsMath =
    {
        ("求根公式", "x = \\frac{-b \\pm \\sqrt{b^2 - 4ac}}{2a}"),
        ("一元二次", "ax^2 + bx + c = 0"),
        ("勾股定理", "a^2 + b^2 = c^2"),
        ("分数相加", "\\frac{a}{b} + \\frac{c}{d} = \\frac{ad + bc}{bd}"),
        ("等差求和", "S_n = \\frac{n(a_1 + a_n)}{2}"),
        ("平方和",   "\\sum_{i=1}^{n} i^2 = \\frac{n(n+1)(2n+1)}{6}"),
        ("重要极限", "\\lim_{x \\to 0} \\frac{\\sin x}{x} = 1"),
        ("定积分",   "\\int_{a}^{b} f(x)\\,dx"),
    };

    private static readonly (string name, string tex)[] FormulaPresetsPhysics =
    {
        ("牛顿第二", "F = ma"),
        ("动能",     "E_k = \\frac{1}{2}mv^2"),
        ("万有引力", "F = G\\frac{m_1 m_2}{r^2}"),
        ("欧姆定律", "U = IR"),
        ("质能方程", "E = mc^2"),
        ("自由落体", "h = \\frac{1}{2}gt^2"),
    };

    private static readonly (string name, string tex)[] FormulaPresetsChem =
    {
        ("水",       "H_2O"),
        ("二氧化碳", "CO_2"),
        ("硫酸根",   "SO_4^{2-}"),
        ("氢气燃烧", "2H_2 + O_2 \\to 2H_2O"),
        ("合成氨",   "N_2 + 3H_2 \\rightleftharpoons 2NH_3"),
        ("酸碱中和", "HCl + NaOH \\to NaCl + H_2O"),
    };

    private FrameworkElement BuildFormulaEditor(ElementModel em)
    {
        var root = new StackPanel { Spacing = 8 };

        // 实时预览（中性灰底，深浅公式都看得清）
        var previewImg = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        root.Children.Add(new Border
        {
            Height = 96,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x6B, 0x72, 0x80)),
            Padding = new Thickness(8),
            Child = previewImg
        });

        // LaTeX 代码框（既是手动编辑入口，也是按钮插入的目标）
        var latexBox = new TextBox
        {
            Text = GetProp(em, "Latex", ""),
            FontSize = 13,
            FontFamily = new FontFamily("Consolas"),
            Padding = new Thickness(6, 4, 6, 4),
            MinHeight = 56,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };
        // 键入只刷新右侧预览（轻量）；写模型 + 撤销 + 画布重渲染放在失焦提交时做一次。
        latexBox.TextChanged += (_, _) =>
            RenderFormulaPreview(previewImg, latexBox.Text,
                GetDoubleProp(em, "FormulaFontSize", 28), GetProp(em, "Color", "#FFFFFFFF"));
        AttachCommitOnLostFocus(latexBox, v => CommitProp(em, "Latex", v), commitOnEnter: false);

        void Insert(string tex, int caret) => InsertFormulaSnippet(latexBox, tex, caret);
        void Replace(string tex)
        {
            latexBox.Text = tex;          // 触发 TextChanged → 刷新预览
            CommitProp(em, "Latex", tex); // 整条套用立即提交（含撤销）
            latexBox.SelectionStart = tex.Length;
            latexBox.Focus(FocusState.Programmatic);
        }

        // 常用公式（整条替换），按学科分组
        root.Children.Add(MakePaletteLabel("常用公式（点一下直接套用）"));
        root.Children.Add(MakeSubLabel("数学"));
        root.Children.Add(BuildWrap(
            FormulaPresetsMath.Select(p => CreatePaletteButton(p.name, () => Replace(p.tex), 0)), 3));
        root.Children.Add(MakeSubLabel("物理"));
        root.Children.Add(BuildWrap(
            FormulaPresetsPhysics.Select(p => CreatePaletteButton(p.name, () => Replace(p.tex), 0)), 3));
        root.Children.Add(MakeSubLabel("化学"));
        root.Children.Add(BuildWrap(
            FormulaPresetsChem.Select(p => CreatePaletteButton(p.name, () => Replace(p.tex), 0)), 3));

        // 结构 / 模板
        root.Children.Add(MakePaletteLabel("分式 · 根号 · 上下标 · 求和"));
        root.Children.Add(BuildWrap(
            FormulaTemplates.Select(t => CreatePaletteButton(t.disp, () => Insert(t.tex, t.caret), 40)), 6));

        // 运算符
        root.Children.Add(MakePaletteLabel("运算符"));
        root.Children.Add(BuildWrap(
            FormulaOperators.Select(o => CreatePaletteButton(o.disp, () => Insert(o.tex, -1), 40)), 6));

        // 物理 / 化学符号（矢量、反应箭头、电荷、温度、文字标注）
        root.Children.Add(MakePaletteLabel("物理 · 化学符号"));
        root.Children.Add(BuildWrap(
            FormulaSci.Select(s => CreatePaletteButton(s.disp, () => Insert(s.tex, s.caret), 40)), 6));

        // 希腊字母
        root.Children.Add(MakePaletteLabel("希腊字母"));
        root.Children.Add(BuildWrap(
            FormulaGreek.Select(g => CreatePaletteButton(g.disp, () => Insert(g.tex, -1), 40)), 7));

        // LaTeX 源码 + 字号 + 颜色
        root.Children.Add(MakePaletteLabel("LaTeX 代码（会写的可以直接改）"));
        root.Children.Add(latexBox);
        root.Children.Add(CreateNumberRow("字号", GetDoubleProp(em, "FormulaFontSize", 28), v =>
        {
            if (!TryParseInv(v, out var d)) return;
            CommitProp(em, "FormulaFontSize", Inv(d));
            RenderFormulaPreview(previewImg, em);
        }));
        root.Children.Add(CreateColorRow("颜色", GetProp(em, "Color", "#FFFFFFFF"), v =>
        {
            CommitProp(em, "Color", v);
            RenderFormulaPreview(previewImg, em);
        }));

        RenderFormulaPreview(previewImg, em);   // 打开面板先渲染一次
        return root;
    }

    private static void RenderFormulaPreview(Image target, ElementModel em) =>
        RenderFormulaPreview(target,
            GetProp(em, "Latex", ""), GetDoubleProp(em, "FormulaFontSize", 28), GetProp(em, "Color", "#FFFFFFFF"));

    private static async void RenderFormulaPreview(Image target, string latex, double fontSize, string color)
    {
        target.Source = await FluentCourse.Runtime.Controls.FormulaControl.RenderToImageAsync(latex, fontSize, color);
    }

    private static void InsertFormulaSnippet(TextBox box, string snippet, int caret)
    {
        var text = box.Text ?? "";
        var start = Math.Min(Math.Max(0, box.SelectionStart), text.Length);
        var selLen = Math.Min(box.SelectionLength, text.Length - start);
        box.Text = text.Substring(0, start) + snippet + text.Substring(start + selLen);
        var pos = start + (caret >= 0 ? caret : snippet.Length);
        box.SelectionStart = Math.Min(pos, box.Text.Length);
        box.SelectionLength = 0;
        box.Focus(FocusState.Programmatic);
    }

    private static Button CreatePaletteButton(string text, Action onClick, double minWidth)
    {
        var b = new Button
        {
            Content = text,
            FontSize = 13,
            MinWidth = minWidth,
            Height = 32,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 4, 4)
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static FrameworkElement BuildWrap(IEnumerable<Button> buttons, int perRow)
    {
        var col = new StackPanel { Spacing = 0 };
        StackPanel? row = null;
        var i = 0;
        foreach (var b in buttons)
        {
            if (i % perRow == 0)
            {
                row = new StackPanel { Orientation = Orientation.Horizontal };
                col.Children.Add(row);
            }
            row!.Children.Add(b);
            i++;
        }
        return col;
    }

    private static TextBlock MakePaletteLabel(string t) => new()
    {
        Text = t,
        FontSize = 11,
        Margin = new Thickness(0, 4, 0, 2),
        Foreground = Application.Current.Resources["TextTertiaryBrush"] as Brush
    };

    private static TextBlock MakeSubLabel(string t) => new()
    {
        Text = t,
        FontSize = 11,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(2, 2, 0, 1),
        Foreground = Application.Current.Resources["TextTertiaryBrush"] as Brush
    };

    private static Expander CreateExpander(string header, bool isExpanded)
    {
        return new Expander
        {
            Header = header,
            IsExpanded = isExpanded,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 4, 0, 0)
        };
    }

    private static Button CreateZButton(string label, Action onClick)
    {
        var btn = new Button
        {
            Content = label,
            Width = 28, Height = 24,
            FontSize = 10, Padding = new Thickness(0),
            Background = Application.Current.Resources["SubtleFillColorTransparent"] as Brush ?? new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(1)
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}

internal static class GridExtensions
{
    public static Grid Then(this Grid grid, Func<Grid, Grid> fn) => fn(grid);
}

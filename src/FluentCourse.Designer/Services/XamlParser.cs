using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using FluentCourse.Designer.Models;
using FluentCourse.Runtime.Controls;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace FluentCourse.Designer.Services;

public static class XamlParser
{
    // 数字一律按 InvariantCulture 写入 Props，否则逗号小数点地区会产出非法 XAML。
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static ObservableCollection<ElementModel> Parse(string xaml) => Parse(xaml, out _);

    /// <summary>
    /// 解析页面 XAML 为元素模型树。<paramref name="success"/> 报告解析是否成功：
    /// 空 XAML 视为成功（空页），仅当 XamlReader 加载失败时为 false。
    /// 调用方据此决定保存时是否回退到原始 XAML（避免不完整往返丢失内容）。
    /// </summary>
    public static ObservableCollection<ElementModel> Parse(string xaml, out bool success)
    {
        var elements = new ObservableCollection<ElementModel>();
        if (string.IsNullOrWhiteSpace(xaml)) { success = true; return elements; }

        var result = XamlRoundTrip.TryLoad(xaml);
        if (!result.Success || result.Root is null) { success = false; return elements; }

        WalkTreeRecursive(result.Root, elements);
        success = true;
        return elements;
    }

    private static void WalkTreeRecursive(DependencyObject node, ObservableCollection<ElementModel> parentList)
    {
        if (node is not FrameworkElement fe) return;

        var em = CreateElementModel(fe, parentList);
        if (em is null) return;

        // Recurse into children if this is a container.
        // QuizControl 的"子结构"是 Choices/Blanks 集合（在 ExtractQuizProps 里读取），不能走视觉树。
        if (fe is StepRevealControl step)
        {
            // StepRevealControl 是 ItemsControl：步骤子元素在 Items 里（序列化器把模型子元素
            // 直接写成元素内容，对应 ContentProperty=Items），这里按 Items 读回。
            foreach (var item in step.Items)
                if (item is FrameworkElement stepChild)
                    WalkTreeRecursive(stepChild, em.Children);
        }
        else if (fe is Panel panel)
        {
            foreach (var child in panel.Children)
                WalkTreeRecursive(child, em.Children);
        }
        else if (fe is Border border && border.Child is FrameworkElement borderChild)
        {
            WalkTreeRecursive(borderChild, em.Children);
        }
        else if (fe is QuizControl)
        {
            // 已在 ExtractQuizProps 中处理（QuizControl 也是 Control/ContentControl 之外的类型，
            // 这里显式短路以防未来基类变化）。
        }
        else if (fe is ContentControl cc && cc.Content is FrameworkElement ccChild)
        {
            WalkTreeRecursive(ccChild, em.Children);
        }
    }

    private static ElementModel? CreateElementModel(FrameworkElement fe, ObservableCollection<ElementModel> parentList)
    {
        var typeName = fe.GetType().Name;

        // Skip transform elements
        if (typeName == "RotateTransform" || typeName == "CompositeTransform" || typeName == "TransformGroup")
            return null;

        // Skip RowDefinitions, ColumnDefinitions
        if (typeName == "RowDefinition" || typeName == "ColumnDefinition")
            return null;

        var em = new ElementModel
        {
            Type = typeName,
            Name = fe.Name,
            X = Canvas.GetLeft(fe),
            Y = Canvas.GetTop(fe),
            Width = fe.Width,
            Height = fe.Height,
            ZIndex = Canvas.GetZIndex(fe)
        };

        if (double.IsNaN(em.X)) em.X = 0;
        if (double.IsNaN(em.Y)) em.Y = 0;

        // Capture Grid attached layout so re-serialization doesn't collapse every child into
        // cell (0,0). (Needed for correct rendering and for the Canvas auto-flatten.)
        var gridRow = Grid.GetRow(fe);
        if (gridRow != 0) em.Props["Grid.Row"] = gridRow.ToString(Inv);
        var gridCol = Grid.GetColumn(fe);
        if (gridCol != 0) em.Props["Grid.Column"] = gridCol.ToString(Inv);
        var gridRowSpan = Grid.GetRowSpan(fe);
        if (gridRowSpan != 1) em.Props["Grid.RowSpan"] = gridRowSpan.ToString(Inv);
        var gridColSpan = Grid.GetColumnSpan(fe);
        if (gridColSpan != 1) em.Props["Grid.ColumnSpan"] = gridColSpan.ToString(Inv);

        switch (fe.RenderTransform)
        {
            case RotateTransform rt:
                em.Rotation = rt.Angle;
                break;
            case ScaleTransform st:
                if (st.ScaleX > 0) em.Scale = st.ScaleX;
                break;
            case CompositeTransform ct:
                em.Rotation = ct.Rotation;
                if (ct.ScaleX > 0) em.Scale = ct.ScaleX;
                break;
        }

        ExtractCommonProps(fe, em);
        ExtractTypeSpecificProps(fe, em);

        parentList.Add(em);
        return em;
    }

    private static void ExtractCommonProps(FrameworkElement fe, ElementModel em)
    {
        if (fe.Opacity != 1.0)
            em.Props["Opacity"] = fe.Opacity.ToString("F2", Inv);

        if (fe.Visibility == Visibility.Collapsed)
            em.Props["Visibility"] = "Collapsed";

        // Tag：MediaPlayerElement 例外 —— 它的 Tag 承载媒体源，由类型分支转回 Source。
        if (fe is not MediaPlayerElement && fe.Tag is string tagStr && tagStr.Length > 0)
            em.Props["Tag"] = tagStr.Replace("https://fcourse.local/", "ms-fcourse:///");

        // HorizontalAlignment
        var ha = fe.HorizontalAlignment;
        if (ha != HorizontalAlignment.Stretch)
            em.Props["HorizontalAlignment"] = ha.ToString();

        // VerticalAlignment
        var va = fe.VerticalAlignment;
        if (va != VerticalAlignment.Stretch)
            em.Props["VerticalAlignment"] = va.ToString();

        // Margin
        if (fe.Margin.Left != 0 || fe.Margin.Top != 0 || fe.Margin.Right != 0 || fe.Margin.Bottom != 0)
            em.Props["Margin"] = FormattableString.Invariant(
                $"{fe.Margin.Left},{fe.Margin.Top},{fe.Margin.Right},{fe.Margin.Bottom}");

        // Padding
        if (fe is Control ctrl && (ctrl.Padding.Left != 0 || ctrl.Padding.Top != 0 ||
            ctrl.Padding.Right != 0 || ctrl.Padding.Bottom != 0))
            em.Props["Padding"] = FormattableString.Invariant(
                $"{ctrl.Padding.Left},{ctrl.Padding.Top},{ctrl.Padding.Right},{ctrl.Padding.Bottom}");

        // Background
        if (fe is Panel pnl && pnl.Background is SolidColorBrush scb)
            em.Props["Background"] = ColorToHex(scb.Color);
    }

    private static void ExtractTypeSpecificProps(FrameworkElement fe, ElementModel em)
    {
        switch (em.Type)
        {
            case "TextBlock":
                ExtractTextBlockProps((TextBlock)fe, em);
                break;
            case "Image":
                ExtractImageProps((Image)fe, em);
                break;
            case "Rectangle":
                ExtractRectangleProps((Rectangle)fe, em);
                break;
            case "Ellipse":
                ExtractEllipseProps((Ellipse)fe, em);
                break;
            case "Polygon":
                ExtractPolygonProps((Polygon)fe, em);
                break;
            case "Line":
                ExtractLineProps((Line)fe, em);
                break;
            case "Button":
            case "HyperlinkButton":
            case "ToggleButton":
            case "CheckBox":
            case "RadioButton":
                ExtractButtonBaseProps((Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)fe, em);
                break;
            case "StackPanel":
                ExtractStackPanelProps((StackPanel)fe, em);
                break;
            case "Border":
                ExtractBorderProps((Border)fe, em);
                break;
            case "Grid":
                ExtractGridProps((Grid)fe, em);
                break;
            case "TextBox":
                var tbx = (TextBox)fe;
                if (!string.IsNullOrEmpty(tbx.Text)) em.Props["Text"] = tbx.Text;
                if (!string.IsNullOrEmpty(tbx.PlaceholderText)) em.Props["PlaceholderText"] = tbx.PlaceholderText;
                if (tbx.FontSize != 14) em.Props["FontSize"] = tbx.FontSize.ToString(Inv);
                break;
            case "AutoSuggestBox":
                var asb = (AutoSuggestBox)fe;
                if (!string.IsNullOrEmpty(asb.Text)) em.Props["Text"] = asb.Text;
                if (!string.IsNullOrEmpty(asb.PlaceholderText)) em.Props["PlaceholderText"] = asb.PlaceholderText;
                if (asb.FontSize != 14) em.Props["FontSize"] = asb.FontSize.ToString(Inv);
                break;
            case "Slider":
                var sld = (Slider)fe;
                if (sld.Minimum != 0) em.Props["Minimum"] = sld.Minimum.ToString(Inv);
                if (sld.Maximum != 100) em.Props["Maximum"] = sld.Maximum.ToString(Inv);
                if (sld.Value != 0) em.Props["Value"] = sld.Value.ToString(Inv);
                break;
            case "MediaPlayerElement":
                var mpe = (MediaPlayerElement)fe;
                // The media source is carried in Tag (see XamlSerializer); restore it to Source.
                if (mpe.Tag is string mediaTag && mediaTag.Length > 0)
                    em.Props["Source"] = mediaTag.Replace("https://fcourse.local/", "ms-fcourse:///");
                if (mpe.AreTransportControlsEnabled)
                    em.Props["AreTransportControlsEnabled"] = "True";
                // 媒体类型标记（音/视频），由 fc:Media.Kind 附加属性往返保留
                // 用全限定名：本文件 using 了 Microsoft.UI.Xaml(.Media)，简单名 Media 会与该命名空间歧义
                if (FluentCourse.Runtime.Controls.Media.GetKind(mpe) is { Length: > 0 } mediaKind)
                    em.Props["fc:Media.Kind"] = mediaKind;
                break;
            case "QuizControl":
                ExtractQuizProps((QuizControl)fe, em);
                break;
            case "StepRevealControl":
                var step = (StepRevealControl)fe;
                if (step.CurrentStep != 0)
                    em.Props["CurrentStep"] = step.CurrentStep.ToString(Inv);
                break;
            case "CountdownControl":
                var cd = (CountdownControl)fe;
                em.Props["Seconds"] = cd.Seconds.ToString(Inv);
                em.Props["AutoStart"] = cd.AutoStart ? "True" : "False";
                break;
            case "FormulaControl":
                var formula = (FormulaControl)fe;
                if (!string.IsNullOrEmpty(formula.Latex)) em.Props["Latex"] = formula.Latex;
                em.Props["FormulaFontSize"] = formula.FormulaFontSize.ToString(Inv);
                if (!string.IsNullOrEmpty(formula.Color)) em.Props["Color"] = formula.Color;
                break;
        }
    }

    // ── 题目：Choices/Blanks 集合读回为 QuizChoice/QuizBlank 子模型（与 WriteQuizCollection 对应） ──

    private static void ExtractQuizProps(QuizControl quiz, ElementModel em)
    {
        // Mode 始终写出：编辑器靠它区分题型（单选/多选/判断/填空）。
        em.Props["Mode"] = quiz.Mode.ToString();
        if (!string.IsNullOrEmpty(quiz.Question)) em.Props["Question"] = quiz.Question;
        if (quiz.ScoreStrategy != ScoreStrategy.AllOrNothing)
            em.Props["ScoreStrategy"] = quiz.ScoreStrategy.ToString();
        if (!quiz.ShowFeedback) em.Props["ShowFeedback"] = "False";
        if (!quiz.AllowRetry) em.Props["AllowRetry"] = "False";

        if (quiz.Choices is not null)
        {
            foreach (var c in quiz.Choices)
            {
                var cm = new ElementModel { Type = "QuizChoice" };
                cm.Props["Text"] = c.Text ?? "";
                if (c.IsAnswer) cm.Props["IsAnswer"] = "True";
                if (!string.IsNullOrEmpty(c.Feedback)) cm.Props["Feedback"] = c.Feedback;
                if (!string.IsNullOrEmpty(c.Explanation)) cm.Props["Explanation"] = c.Explanation;
                em.Children.Add(cm);
            }
        }
        if (quiz.Blanks is not null)
        {
            foreach (var b in quiz.Blanks)
            {
                var bm = new ElementModel { Type = "QuizBlank" };
                bm.Props["Pattern"] = b.Pattern ?? "";
                if (b.CaseSensitive) bm.Props["CaseSensitive"] = "True";
                if (!string.IsNullOrEmpty(b.Explanation)) bm.Props["Explanation"] = b.Explanation;
                em.Children.Add(bm);
            }
        }
    }

    // ── 文本：内联 Run/LineBreak 读回（LineBreak → \n；带格式 Run → 子模型） ──

    private static void ExtractTextBlockProps(TextBlock tb, ElementModel em)
    {
        ExtractTextBlockInlines(tb, em);
        if (tb.FontSize != 15)
            em.Props["FontSize"] = tb.FontSize.ToString(Inv);
        if (tb.FontWeight.Weight != 400)
        {
            var w = tb.FontWeight.Weight;
            em.Props["FontWeight"] = WeightName(w);
        }
        if (IsLocalSet(tb, TextBlock.FontFamilyProperty) && tb.FontFamily?.Source is { Length: > 0 } fam)
            em.Props["FontFamily"] = fam;
        if (tb.FontStyle != Windows.UI.Text.FontStyle.Normal)
            em.Props["FontStyle"] = tb.FontStyle.ToString();
        if (tb.Foreground is SolidColorBrush fb)
            em.Props["Foreground"] = ColorToHex(fb.Color);
        if (tb.TextAlignment != TextAlignment.Left)
            em.Props["TextAlignment"] = tb.TextAlignment.ToString();
        if (tb.TextWrapping != TextWrapping.NoWrap)
            em.Props["TextWrapping"] = tb.TextWrapping.ToString();
        if (tb.CharacterSpacing != 0)
            em.Props["CharacterSpacing"] = tb.CharacterSpacing.ToString(Inv);
        if (tb.LineHeight != 0)
            em.Props["LineHeight"] = tb.LineHeight.ToString(Inv);
        if (tb.TextDecorations != Windows.UI.Text.TextDecorations.None)
            em.Props["TextDecorations"] = tb.TextDecorations.ToString();
        if (tb.TextTrimming != TextTrimming.None)
            em.Props["TextTrimming"] = tb.TextTrimming.ToString();
        if (tb.MaxLines != 0)
            em.Props["MaxLines"] = tb.MaxLines.ToString(Inv);
    }

    private static void ExtractTextBlockInlines(TextBlock tb, ElementModel em)
    {
        // 全部是无格式 Run / LineBreak → 折叠成 Text（换行用 \n，序列化时再展开为 <LineBreak/>）。
        // 只要有带本地格式的 Run，就把所有内联按顺序保留为子模型，保证颜色/字重不丢。
        bool hasFormatted = tb.Inlines.OfType<Run>().Any(HasLocalFormatting);
        bool hasComplex = tb.Inlines.Any(i => i is not Run && i is not LineBreak);

        if (!hasFormatted || hasComplex)
        {
            if (hasComplex)
            {
                // Span/Hyperlink 等复杂内联超出编辑模型表达能力，退回纯文本（保底不崩）。
                if (!string.IsNullOrEmpty(tb.Text)) em.Props["Text"] = tb.Text;
                return;
            }
            var sb = new StringBuilder();
            foreach (var inline in tb.Inlines)
            {
                if (inline is Run r) sb.Append(r.Text);
                else if (inline is LineBreak) sb.Append('\n');
            }
            if (sb.Length > 0) em.Props["Text"] = sb.ToString();
            return;
        }

        foreach (var inline in tb.Inlines)
        {
            switch (inline)
            {
                case Run r:
                    var rm = new ElementModel { Type = "Run" };
                    rm.Props["Text"] = r.Text ?? "";
                    if (IsLocalSet(r, TextElement.ForegroundProperty) && r.Foreground is SolidColorBrush rfb)
                        rm.Props["Foreground"] = ColorToHex(rfb.Color);
                    if (IsLocalSet(r, TextElement.FontWeightProperty))
                        rm.Props["FontWeight"] = WeightName(r.FontWeight.Weight);
                    if (IsLocalSet(r, TextElement.FontSizeProperty))
                        rm.Props["FontSize"] = r.FontSize.ToString(Inv);
                    if (IsLocalSet(r, TextElement.FontStyleProperty))
                        rm.Props["FontStyle"] = r.FontStyle.ToString();
                    if (IsLocalSet(r, TextElement.FontFamilyProperty) && r.FontFamily?.Source is { Length: > 0 } rf)
                        rm.Props["FontFamily"] = rf;
                    em.Children.Add(rm);
                    break;
                case LineBreak:
                    em.Children.Add(new ElementModel { Type = "LineBreak" });
                    break;
            }
        }
    }

    private static bool IsLocalSet(DependencyObject o, DependencyProperty dp) =>
        o.ReadLocalValue(dp) != DependencyProperty.UnsetValue;

    private static bool HasLocalFormatting(Run r) =>
        IsLocalSet(r, TextElement.ForegroundProperty) ||
        IsLocalSet(r, TextElement.FontWeightProperty) ||
        IsLocalSet(r, TextElement.FontSizeProperty) ||
        IsLocalSet(r, TextElement.FontStyleProperty) ||
        IsLocalSet(r, TextElement.FontFamilyProperty);

    private static void ExtractImageProps(Image img, ElementModel em)
    {
        if (img.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bmp && bmp.UriSource != null)
            em.Props["Source"] = bmp.UriSource.ToString().Replace("https://fcourse.local/", "ms-fcourse:///");
        if (img.Stretch != Stretch.Uniform)
            em.Props["Stretch"] = img.Stretch.ToString();
    }

    private static void ExtractRectangleProps(Rectangle rect, ElementModel em)
    {
        if (rect.Fill is SolidColorBrush fill)
            em.Props["Fill"] = ColorToHex(fill.Color);
        if (rect.Stroke is SolidColorBrush stroke)
            em.Props["Stroke"] = ColorToHex(stroke.Color);
        if (rect.StrokeThickness != 0)
            em.Props["StrokeThickness"] = rect.StrokeThickness.ToString(Inv);
        if (rect.RadiusX != 0)
            em.Props["RadiusX"] = rect.RadiusX.ToString(Inv);
        if (rect.RadiusY != 0)
            em.Props["RadiusY"] = rect.RadiusY.ToString(Inv);
    }

    private static void ExtractEllipseProps(Ellipse ellipse, ElementModel em)
    {
        if (ellipse.Fill is SolidColorBrush fill)
            em.Props["Fill"] = ColorToHex(fill.Color);
        if (ellipse.Stroke is SolidColorBrush stroke)
            em.Props["Stroke"] = ColorToHex(stroke.Color);
        if (ellipse.StrokeThickness != 0)
            em.Props["StrokeThickness"] = ellipse.StrokeThickness.ToString(Inv);
    }

    private static void ExtractPolygonProps(Polygon poly, ElementModel em)
    {
        if (poly.Points is { Count: > 0 } pts)
            em.Props["Points"] = string.Join(" ",
                pts.Select(p => FormattableString.Invariant($"{p.X},{p.Y}")));
        if (poly.Fill is SolidColorBrush fill)
            em.Props["Fill"] = ColorToHex(fill.Color);
        if (poly.Stroke is SolidColorBrush stroke)
            em.Props["Stroke"] = ColorToHex(stroke.Color);
        if (poly.StrokeThickness != 0)
            em.Props["StrokeThickness"] = poly.StrokeThickness.ToString(Inv);
        // 工具箱形状默认 Stretch=Fill，使 Points 几何随宽高缩放，必须保留。
        if (poly.Stretch != Stretch.None)
            em.Props["Stretch"] = poly.Stretch.ToString();
    }

    private static void ExtractLineProps(Line line, ElementModel em)
    {
        if (line.X1 != 0) em.Props["X1"] = line.X1.ToString(Inv);
        if (line.Y1 != 0) em.Props["Y1"] = line.Y1.ToString(Inv);
        if (line.X2 != 0) em.Props["X2"] = line.X2.ToString(Inv);
        if (line.Y2 != 0) em.Props["Y2"] = line.Y2.ToString(Inv);
        if (line.Stroke is SolidColorBrush stroke)
            em.Props["Stroke"] = ColorToHex(stroke.Color);
        if (line.StrokeThickness != 0)
            em.Props["StrokeThickness"] = line.StrokeThickness.ToString(Inv);
        if (line.Stretch != Stretch.None)
            em.Props["Stretch"] = line.Stretch.ToString();
    }

    private static void ExtractButtonBaseProps(Microsoft.UI.Xaml.Controls.Primitives.ButtonBase btn, ElementModel em)
    {
        if (btn.Content is string content && !string.IsNullOrEmpty(content))
            em.Props["Content"] = content;
        if (btn.Background is SolidColorBrush bg)
            em.Props["Background"] = ColorToHex(bg.Color);
        if (btn.Foreground is SolidColorBrush fg)
            em.Props["Foreground"] = ColorToHex(fg.Color);
        if (btn.FontSize != 14)
            em.Props["FontSize"] = btn.FontSize.ToString(Inv);
        if (btn.FontWeight.Weight != 400)
            em.Props["FontWeight"] = WeightName(btn.FontWeight.Weight);
        if (IsLocalSet(btn, Control.FontFamilyProperty) && btn.FontFamily?.Source is { Length: > 0 } fam)
            em.Props["FontFamily"] = fam;
        if (btn.CornerRadius.TopLeft > 0)
            em.Props["CornerRadius"] = btn.CornerRadius.TopLeft.ToString(Inv);

        switch (btn)
        {
            case HyperlinkButton hb when hb.NavigateUri is not null:
                em.Props["NavigateUri"] = hb.NavigateUri.ToString();
                break;
            case RadioButton rb when !string.IsNullOrEmpty(rb.GroupName):
                em.Props["GroupName"] = rb.GroupName;
                break;
        }
    }

    private static void ExtractStackPanelProps(StackPanel sp, ElementModel em)
    {
        em.Props["Orientation"] = sp.Orientation == Orientation.Vertical ? "Vertical" : "Horizontal";
        if (sp.Spacing != 0)
            em.Props["Spacing"] = sp.Spacing.ToString(Inv);
    }

    private static void ExtractBorderProps(Border border, ElementModel em)
    {
        if (border.BorderBrush is SolidColorBrush bb)
            em.Props["BorderBrush"] = ColorToHex(bb.Color);
        if (border.BorderThickness.Left != 0)
            em.Props["BorderThickness"] = FormattableString.Invariant(
                $"{border.BorderThickness.Left},{border.BorderThickness.Top},{border.BorderThickness.Right},{border.BorderThickness.Bottom}");
        if (border.CornerRadius.TopLeft > 0)
            em.Props["CornerRadius"] = border.CornerRadius.TopLeft.ToString(Inv);
        if (border.Background is SolidColorBrush bgb)
            em.Props["Background"] = ColorToHex(bgb.Color);
        if (border.Padding.Left != 0 || border.Padding.Top != 0 ||
            border.Padding.Right != 0 || border.Padding.Bottom != 0)
            em.Props["Padding"] = FormattableString.Invariant(
                $"{border.Padding.Left},{border.Padding.Top},{border.Padding.Right},{border.Padding.Bottom}");
    }

    private static void ExtractGridProps(Grid grid, ElementModel em)
    {
        if (grid.RowDefinitions.Count > 0)
            em.Props["RowDefinitions"] = string.Join(";", grid.RowDefinitions.Select(r => r.Height.ToString()));
        if (grid.ColumnDefinitions.Count > 0)
            em.Props["ColumnDefinitions"] = string.Join(";", grid.ColumnDefinitions.Select(c => c.Width.ToString()));
        if (grid.CornerRadius.TopLeft > 0)
            em.Props["CornerRadius"] = grid.CornerRadius.TopLeft.ToString(Inv);
    }

    private static string ColorToHex(Windows.UI.Color c) =>
        $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string WeightName(ushort w) => w switch
    {
        100 => "Thin", 200 => "ExtraLight", 300 => "Light", 400 => "Normal",
        500 => "Medium", 600 => "SemiBold", 700 => "Bold", 800 => "ExtraBold", 900 => "Black",
        _ => w.ToString(Inv)
    };
}

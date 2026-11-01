using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FluentCourse.Designer.Models;

namespace FluentCourse.Designer.Services;

public static class XamlSerializer
{
    // 所有数字必须按 InvariantCulture 写出 —— 逗号小数点地区（如 de-DE）直接 Append(double)
    // 会产出 Width="12,5" 这类非法 XAML。
    private static string Num(double d) => d.ToString(CultureInfo.InvariantCulture);
    private static string Num(int i) => i.ToString(CultureInfo.InvariantCulture);

    private static readonly HashSet<string> SelfClosingTypes = new(StringComparer.Ordinal)
    {
        "TextBlock", "Image", "Rectangle", "Ellipse", "Button", "MediaPlayerElement",
        "HyperlinkButton", "ToggleButton", "CheckBox", "RadioButton", "Slider",
        "ToggleSwitch", "Line", "Polygon", "TextBox", "AutoSuggestBox", "ProgressRing", "PersonPicture"
    };

    private static readonly HashSet<string> RuntimeControlTypes = new(StringComparer.Ordinal)
    {
        "QuizControl", "StepRevealControl", "CountdownControl", "FormulaControl"
    };

    private static bool HasRuntimeControls(ElementModel root)
    {
        if (RuntimeControlTypes.Contains(root.Type)) return true;
        foreach (var child in root.Children)
            if (HasRuntimeControls(child)) return true;
        return false;
    }

    // 任意元素带 fc: 前缀属性（如 fc:Media.Kind）时也需要声明 xmlns:fc，否则根标签缺前缀声明导致 XAML 非法。
    private static bool HasFcAttribute(ElementModel root)
    {
        foreach (var k in root.Props.Keys)
            if (k.StartsWith("fc:", StringComparison.Ordinal)) return true;
        foreach (var child in root.Children)
            if (HasFcAttribute(child)) return true;
        return false;
    }

    public static string Serialize(PageModel page)
    {
        if (page.Elements.Count == 0)
            return NewBlankPage();

        var root = page.Elements[0];
        var rootType = root.Type == "Canvas" ? "Canvas" : root.Type;
        var needsFcNs = HasRuntimeControls(root) || HasFcAttribute(root);

        var sb = new StringBuilder();
        sb.Append('<').Append(RuntimeControlTypes.Contains(rootType) ? "fc:" : "").Append(rootType);
        sb.Append(" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
        sb.Append(" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"");
        if (needsFcNs)
            sb.Append(" xmlns:fc=\"using:FluentCourse.Runtime.Controls\"");

        WriteElementAttributes(sb, root);

        if ((SelfClosingTypes.Contains(rootType) || RuntimeControlTypes.Contains(rootType)) && root.Children.Count == 0)
        {
            sb.Append("/>\r\n");
        }
        else
        {
            sb.AppendLine(">");
            WriteContainerChildren(sb, root, 1);
            sb.Append("</").Append(RuntimeControlTypes.Contains(rootType) ? "fc:" : "").Append(rootType).Append(">\r\n");
        }

        return sb.ToString();
    }

    private static void WriteElement(StringBuilder sb, ElementModel em, int depth)
    {
        var indent = new string(' ', depth * 4);
        var tag = RuntimeControlTypes.Contains(em.Type) ? $"fc:{em.Type}" : em.Type;
        sb.Append(indent).Append('<').Append(tag);

        WriteElementAttributes(sb, em);

        // QuizControl: its choices/blanks are property-element collections, not plain children.
        if (em.Type == "QuizControl")
        {
            sb.Append(">\r\n");
            WriteRenderTransform(sb, em, depth + 1);
            WriteQuizCollection(sb, em, "QuizChoice", "Choices", depth + 1);
            WriteQuizCollection(sb, em, "QuizBlank", "Blanks", depth + 1);
            sb.Append(indent).Append("</").Append(tag).Append(">\r\n");
            return;
        }

        // TextBlock 多行/带格式文本 → 内联 <Run/>/<LineBreak/>。属性值里不能放换行符
        // （会被 XML 规范化成空格），页面规则也禁止 &#x0a; 实体，只能用内联元素。
        // 内联元素之间不写任何空白，避免 XAML 空白折叠把多余空格混进文本。
        if (em.Type == "TextBlock" && NeedsInlineText(em))
        {
            sb.Append('>');
            if (HasTransform(em))
            {
                sb.Append("\r\n");
                WriteRenderTransform(sb, em, depth + 1);
                sb.Append(indent).Append("    ");
            }
            WriteTextBlockInlines(sb, em);
            if (HasTransform(em)) sb.Append("\r\n").Append(indent);
            sb.Append("</").Append(tag).Append(">\r\n");
            return;
        }

        var hasTransform = HasTransform(em);
        var isLeaf = SelfClosingTypes.Contains(em.Type) || RuntimeControlTypes.Contains(em.Type);

        if (isLeaf && em.Children.Count == 0 && !hasTransform)
        {
            sb.Append("/>\r\n");
            return;
        }

        sb.Append(">\r\n");
        WriteRenderTransform(sb, em, depth + 1);
        WriteContainerChildren(sb, em, depth + 1);
        sb.Append(indent).Append("</").Append(tag).Append(">\r\n");
    }

    private static bool HasTransform(ElementModel em) => em.Rotation != 0 || (em.Scale > 0 && em.Scale != 1);

    /// <summary>Emit a CompositeTransform (uniform scale + rotation) around the element's center.</summary>
    private static void WriteRenderTransform(StringBuilder sb, ElementModel em, int depth)
    {
        if (!HasTransform(em)) return;
        var indent = new string(' ', depth * 4);
        var scale = em.Scale > 0 ? em.Scale : 1;
        sb.Append(indent).Append("<FrameworkElement.RenderTransform>\r\n");
        sb.Append(indent).Append("    <CompositeTransform");
        if (scale != 1) sb.Append(" ScaleX=\"").Append(Num(scale)).Append("\" ScaleY=\"").Append(Num(scale)).Append('"');
        if (em.Rotation != 0) sb.Append(" Rotation=\"").Append(Num(em.Rotation)).Append('"');
        if (em.Width > 0 && em.Height > 0)
            sb.Append(" CenterX=\"").Append(Num(em.Width / 2)).Append("\" CenterY=\"").Append(Num(em.Height / 2)).Append('"');
        sb.Append("/>\r\n");
        sb.Append(indent).Append("</FrameworkElement.RenderTransform>\r\n");
    }

    // ── TextBlock 内联文本 ──

    /// <summary>含 Run/LineBreak 子模型、或 Text 含换行的 TextBlock，需要展开成内联元素。</summary>
    private static bool NeedsInlineText(ElementModel em)
    {
        if (em.Children.Any(c => c.Type is "Run" or "LineBreak")) return true;
        return em.Props.TryGetValue("Text", out var t) && (t.Contains('\n') || t.Contains('\r'));
    }

    /// <summary>写出内联内容：优先 Run/LineBreak 子模型（带格式），否则按 Text 的换行拆成
    /// <c>&lt;Run/&gt;&lt;LineBreak/&gt;</c> 序列。元素间零空白。</summary>
    private static void WriteTextBlockInlines(StringBuilder sb, ElementModel em)
    {
        var inlines = em.Children.Where(c => c.Type is "Run" or "LineBreak").ToList();
        if (inlines.Count > 0)
        {
            foreach (var c in inlines)
            {
                if (c.Type == "LineBreak") { sb.Append("<LineBreak/>"); continue; }
                sb.Append("<Run");
                foreach (var kv in c.Props)
                    sb.Append(' ').Append(kv.Key).Append("=\"").Append(XmlEscape(kv.Value)).Append('"');
                sb.Append("/>");
            }
            return;
        }

        var text = em.Props.TryGetValue("Text", out var t) ? t : "";
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append("<LineBreak/>");
            if (lines[i].Length > 0)
                sb.Append("<Run Text=\"").Append(XmlEscape(lines[i])).Append("\"/>");
        }
    }

    /// <summary>Write a QuizControl choices/blanks property-element with fc:-prefixed leaf items.</summary>
    private static void WriteQuizCollection(StringBuilder sb, ElementModel quiz, string childType, string propName, int depth)
    {
        var items = quiz.Children.Where(c => c.Type == childType).ToList();
        if (items.Count == 0) return;

        var indent = new string(' ', depth * 4);
        var itemIndent = new string(' ', (depth + 1) * 4);
        sb.Append(indent).Append("<fc:QuizControl.").Append(propName).Append(">\r\n");
        foreach (var it in items)
        {
            sb.Append(itemIndent).Append("<fc:").Append(childType);
            foreach (var kv in it.Props)
                sb.Append(' ').Append(kv.Key).Append("=\"").Append(XmlEscape(kv.Value)).Append('"');
            sb.Append("/>\r\n");
        }
        sb.Append(indent).Append("</fc:QuizControl.").Append(propName).Append(">\r\n");
    }

    private static void WriteElementAttributes(StringBuilder sb, ElementModel em)
    {
        // x:Name
        if (!string.IsNullOrEmpty(em.Name))
            sb.Append(" x:Name=\"").Append(XmlEscape(em.Name)).Append('"');

        // Canvas positioning (only for Canvas children)
        if (em.X != 0)
            sb.Append(" Canvas.Left=\"").Append(Num(em.X)).Append('"');
        if (em.Y != 0)
            sb.Append(" Canvas.Top=\"").Append(Num(em.Y)).Append('"');
        if (em.ZIndex != 0)
            sb.Append(" Canvas.ZIndex=\"").Append(Num(em.ZIndex)).Append('"');

        // Size
        if (!double.IsNaN(em.Width) && em.Width > 0)
            sb.Append(" Width=\"").Append(Num(em.Width)).Append('"');
        if (!double.IsNaN(em.Height) && em.Height > 0)
            sb.Append(" Height=\"").Append(Num(em.Height)).Append('"');

        // Type-specific props
        foreach (var kv in em.Props)
        {
            if (kv.Key == "RowDefinitions" || kv.Key == "ColumnDefinitions" || kv.Key == "Children")
                continue;
            // Containers have no Text — emitting one (e.g. after a root retype) yields invalid
            // XAML like <Canvas Text="...">. Skip it defensively.
            if (kv.Key == "Text" && em.Type is "Grid" or "Canvas" or "StackPanel" or "Border" or "ScrollViewer")
                continue;
            // 多行/带格式文本由 WriteTextBlockInlines 以内联元素写出，不能再写 Text 属性
            // （属性里的换行会被 XML 规范化吃掉，且会与内联内容重复）。
            if (kv.Key == "Text" && em.Type == "TextBlock" && NeedsInlineText(em))
                continue;
            // IMediaPlaybackSource can't be set from a XAML string, so carry the media source in
            // Tag — the player's AssetBinder turns it into a real MediaSource at load time.
            if (em.Type == "MediaPlayerElement" && kv.Key == "Source")
            {
                if (!string.IsNullOrEmpty(kv.Value))
                    sb.Append(" Tag=\"").Append(XmlEscape(kv.Value)).Append('"');
                continue;
            }

            sb.Append(' ').Append(kv.Key).Append("=\"").Append(XmlEscape(kv.Value)).Append('"');
        }
    }

    private static void WriteContainerChildren(StringBuilder sb, ElementModel container, int depth)
    {
        var indent = new string(' ', depth * 4);

        // Grid definitions
        if (container.Type == "Grid")
        {
            if (container.Props.TryGetValue("RowDefinitions", out var rows))
            {
                sb.Append(indent).Append("<Grid.RowDefinitions>\r\n");
                foreach (var r in rows.Split(';'))
                {
                    sb.Append(indent).Append("    <RowDefinition");
                    if (!string.IsNullOrEmpty(r))
                        sb.Append(" Height=\"").Append(XmlEscape(r)).Append('"');
                    sb.Append("/>\r\n");
                }
                sb.Append(indent).Append("</Grid.RowDefinitions>\r\n");
            }
            if (container.Props.TryGetValue("ColumnDefinitions", out var cols))
            {
                sb.Append(indent).Append("<Grid.ColumnDefinitions>\r\n");
                foreach (var c in cols.Split(';'))
                {
                    sb.Append(indent).Append("    <ColumnDefinition");
                    if (!string.IsNullOrEmpty(c))
                        sb.Append(" Width=\"").Append(XmlEscape(c)).Append('"');
                    sb.Append("/>\r\n");
                }
                sb.Append(indent).Append("</Grid.ColumnDefinitions>\r\n");
            }
        }

        // Write children
        foreach (var child in container.Children)
            WriteElement(sb, child, depth);
    }

    private static string XmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string NewBlankPage() =>
        "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\r\n" +
        "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\r\n" +
        "        Width=\"1920\" Height=\"1080\">\r\n" +
        "</Canvas>\r\n";
}

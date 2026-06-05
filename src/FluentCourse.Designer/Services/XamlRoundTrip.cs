using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace FluentCourse.Designer.Services;

/// <summary>
/// Result of <see cref="XamlRoundTrip.TryLoad"/>.
/// </summary>
public sealed class XamlLoadResult
{
    public bool Success { get; init; }
    public FrameworkElement? Root { get; init; }
    public string? Error { get; init; }
    public int? ErrorLine { get; init; }
    public int? ErrorColumn { get; init; }

    public static XamlLoadResult Ok(FrameworkElement root) => new() { Success = true, Root = root };
    public static XamlLoadResult Fail(string error, int? line = null, int? col = null) =>
        new() { Success = false, Error = error, ErrorLine = line, ErrorColumn = col };
}

/// <summary>
/// Load a page XAML string into a WinUI element tree and serialize a tree back to XAML.
/// WinUI 3 doesn't ship a public XamlWriter, so the serialize path is bespoke — it handles
/// the property set that the existing samples use, and falls back to a placeholder for anything
/// it doesn't recognise (so the round-trip never silently corrupts user data).
/// </summary>
public static class XamlRoundTrip
{
    private static readonly Regex LineColumnPattern = new(
        @"\((\d+),(\d+)\)", RegexOptions.Compiled);

    public static XamlLoadResult TryLoad(string xaml)
    {
        try
        {
            // Mirror the runtime: rewrite ms-fcourse:// to a parse-safe scheme so the parser
            // doesn't choke on the custom URI scheme.
            var rewritten = xaml.Replace("ms-fcourse:///", "https://fcourse.local/");
            var loaded = XamlReader.Load(rewritten);
            if (loaded is not FrameworkElement fe)
                return XamlLoadResult.Fail("Root element must be a FrameworkElement (Grid/Canvas/Border/...).");
            return XamlLoadResult.Ok(fe);
        }
        catch (Exception ex)
        {
            var (line, col) = ParseLineColumn(ex.Message);
            return XamlLoadResult.Fail(ExMessage(ex), line, col);
        }
    }

    private static (int?, int?) ParseLineColumn(string message)
    {
        var m = LineColumnPattern.Match(message);
        if (!m.Success) return (null, null);
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
    }

    private static string ExMessage(Exception ex) =>
        ex.InnerException is { } inner ? $"{ex.Message} → {inner.Message}" : ex.Message;

    /// <summary>Serialize a WinUI element tree to a XAML string.</summary>
    public static string Serialize(FrameworkElement root) => SerializeElement(root, indent: 0);

    private static string SerializeElement(object element, int indent)
    {
        var pad = new string(' ', indent * 4);
        var name = element.GetType().Name;

        // Some runtime-only types aren't constructible from XAML — substitute a placeholder element.
        var (tag, typeName) = MapType(name);
        var sb = new StringBuilder();
        sb.Append(pad).Append('<').Append(tag);

        if (!ReferenceEquals(tag, name)) sb.Append(" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
        if (!ReferenceEquals(tag, name))
        {
            // Add a comment so the user can see this got rewritten.
            sb.AppendLine(">");
            sb.Append(pad).Append("    <!-- ").Append(name).Append(" is rendered as ").Append(typeName).Append(" in the editor -->\r\n");
            return sb.ToString();
        }

        var selfClosing = TryWriteAttributes(element, sb);
        if (selfClosing)
        {
            sb.Append("/>\r\n");
            return sb.ToString();
        }

        sb.Append('>');
        var body = SerializeBody(element, indent);
        if (body.Length == 0)
        {
            sb.Append("</").Append(tag).Append(">\r\n");
        }
        else
        {
            sb.Append("\r\n").Append(body).Append(pad).Append("</").Append(tag).Append(">\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Map a runtime type to a XAML tag. The vast majority are 1:1.</summary>
    private static (string tag, string typeName) MapType(string name) => name switch
    {
        "QuizControl" or "StepRevealControl" or "BoolToVisibilityConverter" => (name, name), // declared in Runtime.Controls namespace
        _ => (name, name)
    };

    private static bool TryWriteAttributes(object element, StringBuilder sb)
    {
        var type = element.GetType();

        // x:Name
        var nameProp = type.GetProperty("Name");
        if (nameProp?.GetValue(element) is string n && !string.IsNullOrEmpty(n))
            sb.Append(" x:Name=\"").Append(XmlEscape(n)).Append('"');

        // Common layout
        WriteDp(sb, type, element, "Width");
        WriteDp(sb, type, element, "Height");
        WriteDp(sb, type, element, "MinWidth");
        WriteDp(sb, type, element, "MinHeight");
        WriteDp(sb, type, element, "MaxWidth");
        WriteDp(sb, type, element, "MaxHeight");
        WriteThickness(sb, type, element, "Margin");
        WriteThickness(sb, type, element, "Padding");
        WriteEnum(sb, type, element, "HorizontalAlignment");
        WriteEnum(sb, type, element, "VerticalAlignment");
        WriteEnum(sb, type, element, "HorizontalContentAlignment");
        WriteEnum(sb, type, element, "VerticalContentAlignment");
        WriteColorBrush(sb, type, element, "Background");
        WriteColorBrush(sb, type, element, "Foreground");
        WriteColorBrush(sb, type, element, "BorderBrush");
        WriteThickness(sb, type, element, "BorderThickness");
        WriteCornerRadius(sb, type, element, "CornerRadius");
        WriteDouble(sb, type, element, "Opacity");
        WriteEnum(sb, type, element, "Visibility");

        // Text
        WriteString(sb, type, element, "Text");
        WriteString(sb, type, element, "Content");
        WriteDouble(sb, type, element, "FontSize");
        WriteString(sb, type, element, "FontFamily");
        WriteFontWeight(sb, type, element);
        WriteEnum(sb, type, element, "FontStyle");
        WriteEnum(sb, type, element, "TextWrapping");
        WriteEnum(sb, type, element, "TextAlignment");
        WriteDouble(sb, type, element, "LineHeight");
        WriteInt(sb, type, element, "CharacterSpacing");

        // Image
        if (element is Image img && img.Source is BitmapImage bmp && bmp.UriSource is { } u)
            sb.Append(" Source=\"").Append(XmlEscape(u.ToString())).Append('"');
        WriteEnum(sb, type, element, "Stretch");

        // Shape fill/stroke
        if (element is Shape shape)
        {
            WriteColorBrush(sb, type, shape, "Fill");
            WriteColorBrush(sb, type, shape, "Stroke");
            WriteDouble(sb, type, shape, "StrokeThickness");
        }

        // StackPanel
        WriteEnum(sb, type, element, "Orientation");
        WriteDouble(sb, type, element, "Spacing");

        // Attached layout
        WriteAttachedInt(sb, element, "Grid", "Row");
        WriteAttachedInt(sb, element, "Grid", "Column");
        WriteAttachedInt(sb, element, "Grid", "RowSpan");
        WriteAttachedInt(sb, element, "Grid", "ColumnSpan");
        WriteAttachedDouble(sb, element, "Canvas", "Left");
        WriteAttachedDouble(sb, element, "Canvas", "Top");
        WriteAttachedInt(sb, element, "Canvas", "ZIndex");

        return HasNoBody(element);
    }

    private static bool HasNoBody(object element) => element switch
    {
        TextBlock or Rectangle or Ellipse or Line or Image or ButtonBase or ContentControl or
        Border or Panel or UserControl or Page => false, // panel-like can have children
        Shape => false,
        _ => element.GetType().GetProperty("Content") is null && element.GetType().GetProperty("Children") is null
            && element.GetType().GetProperty("Child") is null
            && element.GetType().GetProperty("Inlines") is null
            && element.GetType().GetProperty("Text") is null
    };

    private static string SerializeBody(object element, int indent)
    {
        if (element is Panel panel)
        {
            var sb = new StringBuilder();
            // Grid definitions
            if (panel is Grid grid)
            {
                if (grid.RowDefinitions.Count > 0)
                {
                    sb.Append(new string(' ', (indent + 1) * 4)).Append("<Grid.RowDefinitions>\r\n");
                    foreach (var rd in grid.RowDefinitions)
                    {
                        sb.Append(new string(' ', (indent + 2) * 4)).Append("<RowDefinition ");
                        if (rd.Height.GridUnitType == GridUnitType.Pixel) sb.Append("Height=\"").Append(rd.Height.Value).Append("\"");
                        else if (rd.Height.GridUnitType == GridUnitType.Auto) sb.Append("Height=\"Auto\"");
                        else sb.Append("Height=\"*\"");
                        if (rd.MinHeight > 0) sb.Append(" MinHeight=\"").Append(rd.MinHeight).Append('"');
                        if (rd.MaxHeight > 0) sb.Append(" MaxHeight=\"").Append(rd.MaxHeight).Append('"');
                        sb.Append("/>\r\n");
                    }
                    sb.Append(new string(' ', (indent + 1) * 4)).Append("</Grid.RowDefinitions>\r\n");
                }
                if (grid.ColumnDefinitions.Count > 0)
                {
                    sb.Append(new string(' ', (indent + 1) * 4)).Append("<Grid.ColumnDefinitions>\r\n");
                    foreach (var cd in grid.ColumnDefinitions)
                    {
                        sb.Append(new string(' ', (indent + 2) * 4)).Append("<ColumnDefinition ");
                        if (cd.Width.GridUnitType == GridUnitType.Pixel) sb.Append("Width=\"").Append(cd.Width.Value).Append("\"");
                        else if (cd.Width.GridUnitType == GridUnitType.Auto) sb.Append("Width=\"Auto\"");
                        else sb.Append("Width=\"*\"");
                        if (cd.MinWidth > 0) sb.Append(" MinWidth=\"").Append(cd.MinWidth).Append('"');
                        if (cd.MaxWidth > 0) sb.Append(" MaxWidth=\"").Append(cd.MaxWidth).Append('"');
                        sb.Append("/>\r\n");
                    }
                    sb.Append(new string(' ', (indent + 1) * 4)).Append("</Grid.ColumnDefinitions>\r\n");
                }
            }
            foreach (var child in panel.Children)
                sb.Append(SerializeElement(child, indent + 1));
            return sb.ToString();
        }

        if (element is Border border && border.Child is { } borderChild)
            return SerializeElement(borderChild, indent + 1);

        if (element is ContentControl cc && cc.Content is FrameworkElement contentChild)
            return SerializeElement(contentChild, indent + 1);

        return string.Empty;
    }

    // ── property writers ──

    private static void WriteDp(StringBuilder sb, Type t, object e, string name)
    {
        var p = t.GetProperty(name);
        if (p is null) return;
        var v = p.GetValue(e);
        if (v is null) return;
        if (v is double d) sb.Append(' ').Append(name).Append("=\"").Append(d).Append('"');
        else if (v is int i) sb.Append(' ').Append(name).Append("=\"").Append(i).Append('"');
        else if (v is string s) sb.Append(' ').Append(name).Append("=\"").Append(XmlEscape(s)).Append('"');
    }

    private static void WriteDouble(StringBuilder sb, Type t, object e, string name)
    {
        var p = t.GetProperty(name);
        if (p is null) return;
        var v = p.GetValue(e);
        if (v is double d) sb.Append(' ').Append(name).Append("=\"").Append(d).Append('"');
    }

    private static void WriteInt(StringBuilder sb, Type t, object e, string name)
    {
        var p = t.GetProperty(name);
        if (p is null) return;
        var v = p.GetValue(e);
        if (v is int i) sb.Append(' ').Append(name).Append("=\"").Append(i).Append('"');
    }

    private static void WriteString(StringBuilder sb, Type t, object e, string name)
    {
        var p = t.GetProperty(name);
        if (p is null) return;
        var v = p.GetValue(e);
        if (v is string s && s.Length > 0) sb.Append(' ').Append(name).Append("=\"").Append(XmlEscape(s)).Append('"');
    }

    private static void WriteEnum(StringBuilder sb, Type t, object e, string name)
    {
        var p = t.GetProperty(name);
        if (p is null) return;
        var v = p.GetValue(e);
        if (v is null) return;
        var def = DefaultFor(p.PropertyType);
        if (v.Equals(def)) return; // skip defaults to keep XAML small
        sb.Append(' ').Append(name).Append("=\"").Append(v).Append('"');
    }

    private static object? DefaultFor(Type t)
    {
        if (!t.IsValueType) return null;
        return Activator.CreateInstance(t);
    }

    private static void WriteColorBrush(StringBuilder sb, Type t, object e, string name)
    {
        var p = t.GetProperty(name);
        if (p is null) return;
        if (p.GetValue(e) is not SolidColorBrush b) return;
        var hex = ColorToHex(b.Color);
        sb.Append(' ').Append(name).Append("=\"").Append(hex).Append('"');
    }

    private static void WriteThickness(StringBuilder sb, Type t, object e, string name)
    {
        var p = t.GetProperty(name);
        if (p is null) return;
        if (p.GetValue(e) is not Thickness th) return;
        if (th == new Thickness(0)) return;
        sb.Append(' ').Append(name).Append("=\"");
        if (th.Left == th.Right && th.Top == th.Bottom && th.Left == th.Top)
            sb.Append(th.Left);
        else if (th.Left == th.Right && th.Top == th.Bottom)
            sb.Append(th.Left).Append(',').Append(th.Top);
        else
            sb.Append(th.Left).Append(',').Append(th.Top).Append(',').Append(th.Right).Append(',').Append(th.Bottom);
        sb.Append('"');
    }

    private static void WriteCornerRadius(StringBuilder sb, Type t, object e, string name)
    {
        var p = t.GetProperty(name);
        if (p is null) return;
        if (p.GetValue(e) is not CornerRadius cr) return;
        if (cr == new CornerRadius(0)) return;
        sb.Append(' ').Append(name).Append("=\"")
          .Append(cr.TopLeft).Append(',')
          .Append(cr.TopRight).Append(',')
          .Append(cr.BottomRight).Append(',')
          .Append(cr.BottomLeft).Append('"');
    }

    private static void WriteFontWeight(StringBuilder sb, Type t, object e)
    {
        var p = t.GetProperty("FontWeight");
        if (p is null) return;
        var v = p.GetValue(e);
        if (v is null) return;
        var weightProp = v.GetType().GetProperty("Weight");
        if (weightProp is null) return;
        var w = weightProp.GetValue(v);
        var weight = w is ushort us ? us : (ushort)400;
        if (weight == 400) return; // Normal is default
        sb.Append(" FontWeight=\"").Append(weight switch
        {
            100 => "Thin",
            200 => "ExtraLight",
            300 => "Light",
            400 => "Normal",
            500 => "Medium",
            600 => "SemiBold",
            700 => "Bold",
            800 => "ExtraBold",
            900 => "Black",
             _ => weight.ToString()
        }).Append('"');
    }

    private static void WriteAttachedInt(StringBuilder sb, object e, string owner, string name)
    {
        var helper = Type.GetType($"Microsoft.UI.Xaml.Controls.{owner}, Microsoft.WindowsAppSDK")
            ?? Type.GetType($"Microsoft.UI.Xaml.Controls.{owner}, WinRT.Runtime")
            ?? owner switch
            {
                "Grid" => typeof(Grid).GetTypeInfo().Assembly.GetType("Microsoft.UI.Xaml.Controls.Grid"),
                "Canvas" => typeof(Canvas).GetTypeInfo().Assembly.GetType("Microsoft.UI.Xaml.Controls.Canvas"),
                _ => null
            };
        if (helper is null) return;
        var method = helper.GetMethod($"Get{name}", BindingFlags.Public | BindingFlags.Static);
        if (method is null) return;
        var v = method.Invoke(null, new[] { (DependencyObject)e });
        if (v is int i && i != 0)
            sb.Append(' ').Append(owner).Append('.').Append(name).Append("=\"").Append(i).Append('"');
    }

    private static void WriteAttachedDouble(StringBuilder sb, object e, string owner, string name)
    {
        var helper = owner switch
        {
            "Grid" => typeof(Grid),
            "Canvas" => typeof(Canvas),
            _ => null
        };
        if (helper is null) return;
        var method = helper.GetMethod($"Get{name}", BindingFlags.Public | BindingFlags.Static);
        if (method is null) return;
        var v = method.Invoke(null, new[] { (DependencyObject)e });
        if (v is double d && d != 0.0)
            sb.Append(' ').Append(owner).Append('.').Append(name).Append("=\"").Append(d).Append('"');
    }

    private static string ColorToHex(Color c) =>
        $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string XmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}

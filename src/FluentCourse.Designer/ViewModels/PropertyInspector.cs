using System.Reflection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace FluentCourse.Designer.ViewModels;

/// <summary>One editable property in the properties pane.</summary>
public sealed class PropertyDescriptor
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public PropertyKind Kind { get; init; }
    public object? Value { get; set; }
    public string? StringValue { get; set; }
    public string? EnumValues { get; init; }   // pipe-separated enum values
    public string Category { get; init; } = "杂项";
    public Action<object>? Apply { get; init; }
    public Type ElementType { get; init; } = typeof(object);
}

public enum PropertyKind { String, Number, Boolean, Color, Enum, Thickness, CornerRadius, FontWeight, Margin, ReadOnly }

/// <summary>
/// Walks a FrameworkElement's most-edited DependencyProperties and returns descriptors
/// for the properties pane. Names are kept small on purpose — full introspection is
/// overwhelming, so we curate the common ones.
/// </summary>
public static class PropertyInspector
{
    private static readonly (string Category, string Name, string Display)[] Common = new[]
    {
        ("布局", "Width",          "宽度"),
        ("布局", "Height",         "高度"),
        ("布局", "Margin",         "外边距"),
        ("布局", "Padding",        "内边距"),
        ("布局", "HorizontalAlignment", "水平对齐"),
        ("布局", "VerticalAlignment",   "垂直对齐"),

        ("外观", "Background",     "背景"),
        ("外观", "Foreground",     "前景"),
        ("外观", "BorderBrush",    "边框色"),
        ("外观", "BorderThickness","边框粗细"),
        ("外观", "CornerRadius",   "圆角"),
        ("外观", "Opacity",        "不透明度"),

        ("文本", "Text",           "文本"),
        ("文本", "Content",        "内容"),
        ("文本", "FontSize",       "字号"),
        ("文本", "FontWeight",     "字重"),
        ("文本", "FontFamily",     "字体"),
        ("文本", "TextWrapping",   "换行"),
        ("文本", "TextAlignment",  "对齐"),
    };

    public static IEnumerable<PropertyDescriptor> Inspect(FrameworkElement element)
    {
        var t = element.GetType();
        var dpCache = new Dictionary<string, DependencyProperty>();
        foreach (var (cat, name, display) in Common)
        {
            var p = t.GetProperty(name);
            if (p is null || !p.CanRead || !p.CanWrite) continue;
            var descriptor = BuildFor(element, p, name, display, cat, dpCache);
            if (descriptor is not null) yield return descriptor;
        }

        // Always show x:Name
        yield return new PropertyDescriptor
        {
            Name = "Name",
            DisplayName = "x:Name",
            Kind = PropertyKind.String,
            StringValue = element.Name,
            Category = "标识",
            Apply = v => { if (v is string s) { element.Name = s; } }
        };
    }

    private static PropertyDescriptor? BuildFor(
        FrameworkElement element, PropertyInfo p, string name, string display, string category,
        Dictionary<string, DependencyProperty> dpCache)
    {
        try
        {
            var value = p.GetValue(element);
            var propType = p.PropertyType;

            // String
            if (propType == typeof(string))
            {
                return new PropertyDescriptor
                {
                    Name = name, DisplayName = display, Category = category,
                    Kind = PropertyKind.String, StringValue = value as string,
                    Apply = v => { p.SetValue(element, v); },
                    ElementType = propType
                };
            }

            // Double / int
            if (propType == typeof(double) || propType == typeof(int))
            {
                return new PropertyDescriptor
                {
                    Name = name, DisplayName = display, Category = category,
                    Kind = PropertyKind.Number,
                    StringValue = (value?.ToString()) ?? "0",
                    Apply = v =>
                    {
                        if (v is string s && double.TryParse(s, out var d))
                        {
                            if (propType == typeof(int)) p.SetValue(element, (int)d);
                            else p.SetValue(element, d);
                        }
                    },
                    ElementType = propType
                };
            }

            // Color (SolidColorBrush properties)
            if (propType == typeof(Brush) && value is SolidColorBrush b)
            {
                return new PropertyDescriptor
                {
                    Name = name, DisplayName = display, Category = category,
                    Kind = PropertyKind.Color, StringValue = ColorToHex(b.Color),
                    Apply = v =>
                    {
                        if (v is string s && TryParseColor(s, out var c))
                            p.SetValue(element, new SolidColorBrush(c));
                    },
                    ElementType = propType
                };
            }

            // Thickness
            if (propType == typeof(Thickness))
            {
                var t = (Thickness)(value ?? new Thickness(0));
                return new PropertyDescriptor
                {
                    Name = name, DisplayName = display, Category = category,
                    Kind = PropertyKind.Thickness, StringValue = $"{t.Left},{t.Top},{t.Right},{t.Bottom}",
                    Apply = v =>
                    {
                        if (v is string s) p.SetValue(element, ParseThickness(s));
                    }
                };
            }

            // CornerRadius
            if (propType == typeof(CornerRadius))
            {
                var cr = (CornerRadius)(value ?? new CornerRadius(0));
                return new PropertyDescriptor
                {
                    Name = name, DisplayName = display, Category = category,
                    Kind = PropertyKind.CornerRadius, StringValue = $"{cr.TopLeft},{cr.TopRight},{cr.BottomRight},{cr.BottomLeft}",
                    Apply = v =>
                    {
                        if (v is string s)
                        {
                            var parts = s.Split(',');
                            if (parts.Length == 4 &&
                                double.TryParse(parts[0], out var tl) &&
                                double.TryParse(parts[1], out var tr) &&
                                double.TryParse(parts[2], out var br) &&
                                double.TryParse(parts[3], out var bl))
                                p.SetValue(element, new CornerRadius(tl, tr, br, bl));
                        }
                    }
                };
            }

            // FontWeight — avoid type-level lookup (WinRT projection quirk)
            if (name == "FontWeight")
            {
                // Read as struct via reflection
                var fwValue = value;
                var weightProp = propType.GetProperty("Weight");
                var weight = weightProp?.GetValue(fwValue) is ushort w ? w : (ushort)400;
                return new PropertyDescriptor
                {
                    Name = name, DisplayName = display, Category = category,
                    Kind = PropertyKind.FontWeight, StringValue = WeightName(weight),
                    EnumValues = "Thin|ExtraLight|Light|Normal|Medium|SemiBold|Bold|ExtraBold|Black",
                    Apply = v =>
                    {
                        if (v is string s) p.SetValue(element, ParseWeight(s, propType));
                    }
                };
            }

            // Enum
            if (propType.IsEnum)
            {
                var names = string.Join("|", Enum.GetNames(propType));
                return new PropertyDescriptor
                {
                    Name = name, DisplayName = display, Category = category,
                    Kind = PropertyKind.Enum, StringValue = value?.ToString(),
                    EnumValues = names,
                    Apply = v =>
                    {
                        if (v is string s)
                        {
                            try { p.SetValue(element, Enum.Parse(propType, s)); } catch { }
                        }
                    }
                };
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static string ColorToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool TryParseColor(string s, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.StartsWith('#'))
        {
            s = s.Substring(1);
            if (s.Length == 6) s = "FF" + s;
            if (s.Length != 8) return false;
            if (!byte.TryParse(s.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var a)) return false;
            if (!byte.TryParse(s.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)) return false;
            if (!byte.TryParse(s.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)) return false;
            if (!byte.TryParse(s.AsSpan(6, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)) return false;
            color = Color.FromArgb(a, r, g, b);
            return true;
        }
        return false;
    }

    public static Thickness ParseThickness(string s)
    {
        var parts = s.Split(',');
        double L = 0, T = 0, R = 0, B = 0;
        if (parts.Length == 1) { double.TryParse(parts[0], out L); T = R = B = L; }
        else if (parts.Length == 2) { double.TryParse(parts[0], out L); double.TryParse(parts[1], out T); R = L; B = T; }
        else if (parts.Length == 4)
        {
            double.TryParse(parts[0], out L);
            double.TryParse(parts[1], out T);
            double.TryParse(parts[2], out R);
            double.TryParse(parts[3], out B);
        }
        return new Thickness(L, T, R, B);
    }

    private static string WeightName(ushort w) => w switch
    {
        100 => "Thin", 200 => "ExtraLight", 300 => "Light", 400 => "Normal",
        500 => "Medium", 600 => "SemiBold", 700 => "Bold", 800 => "ExtraBold", 900 => "Black",
        _ => w.ToString()
    };

    private static object? ParseWeight(string s, Type fontWeightType)
    {
        var w = s switch
        {
            "Thin" => (ushort)100, "ExtraLight" => (ushort)200, "Light" => (ushort)300,
            "Normal" => (ushort)400, "Medium" => (ushort)500, "SemiBold" => (ushort)600,
            "Bold" => (ushort)700, "ExtraBold" => (ushort)800, "Black" => (ushort)900,
            _ => (ushort)400
        };
        try { return Activator.CreateInstance(fontWeightType, w); }
        catch { return null; }
    }
}

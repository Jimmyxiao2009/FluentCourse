using System;
using FluentCourse.Designer.Models;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace FluentCourse.Designer.Services;

public static class CanvasRenderer
{
    public static FrameworkElement Render(ElementModel em)
    {
        FrameworkElement el = em.Type switch
        {
            "TextBlock" => RenderTextBlock(em),
            "Image" => RenderImage(em),
            "Rectangle" => RenderRectangle(em),
            "Ellipse" => RenderEllipse(em),
            "Button" => RenderButton(em),
            "MediaPlayerElement" => RenderMedia(em),
            _ => RenderFallback(em),
        };

        ApplyCommonProps(el, em);
        return el;
    }

    public static void Update(FrameworkElement el, ElementModel em)
    {
        ApplyCommonProps(el, em);

        switch (em.Type)
        {
            case "TextBlock":
                UpdateTextBlock((TextBlock)el, em);
                break;
            case "Image":
                UpdateImage((Image)el, em);
                break;
            case "Rectangle":
                UpdateRectangle((Rectangle)el, em);
                break;
            case "Ellipse":
                UpdateEllipse((Ellipse)el, em);
                break;
            case "Button":
                UpdateButton((Button)el, em);
                break;
        }
    }

    private static void ApplyCommonProps(FrameworkElement el, ElementModel em)
    {
        Canvas.SetLeft(el, em.X);
        Canvas.SetTop(el, em.Y);
        Canvas.SetZIndex(el, em.ZIndex);

        if (!double.IsNaN(em.Width) && em.Width > 0)
            el.Width = em.Width;
        if (!double.IsNaN(em.Height) && em.Height > 0)
            el.Height = em.Height;

        if (em.Rotation != 0)
        {
            el.RenderTransform = new RotateTransform
            {
                Angle = em.Rotation,
                CenterX = (em.Width > 0 ? em.Width : el.Width) / 2,
                CenterY = (em.Height > 0 ? em.Height : el.Height) / 2
            };
        }
        else
        {
            el.RenderTransform = null;
        }

        if (em.Props.TryGetValue("Opacity", out var opStr) && double.TryParse(opStr, out var op))
            el.Opacity = op;
        else
            el.Opacity = 1.0;
    }

    private static FrameworkElement RenderTextBlock(ElementModel em)
    {
        var tb = new TextBlock
        {
            Text = GetProp(em, "Text", "Text"),
            FontSize = GetDouble(em, "FontSize", 24),
            TextWrapping = TextWrapping.Wrap
        };

        if (em.Props.TryGetValue("Foreground", out var fgHex))
            tb.Foreground = ParseBrush(fgHex);
        else
            tb.Foreground = new SolidColorBrush(Colors.White);

        if (em.Props.TryGetValue("FontWeight", out var fw))
            ApplyFontWeight(tb, fw);

        if (em.Props.TryGetValue("TextAlignment", out var ta) && Enum.TryParse<TextAlignment>(ta, out var taVal))
            tb.TextAlignment = taVal;

        return tb;
    }

    private static void UpdateTextBlock(TextBlock tb, ElementModel em)
    {
        tb.Text = GetProp(em, "Text", "");
        tb.FontSize = GetDouble(em, "FontSize", 24);
        if (em.Props.TryGetValue("Foreground", out var fgHex))
            tb.Foreground = ParseBrush(fgHex);
        if (em.Props.TryGetValue("FontWeight", out var fw))
            ApplyFontWeight(tb, fw);
        if (em.Props.TryGetValue("TextAlignment", out var ta) && Enum.TryParse<TextAlignment>(ta, out var taVal))
            tb.TextAlignment = taVal;
    }

    private static FrameworkElement RenderImage(ElementModel em)
    {
        var img = new Image
        {
            Stretch = Stretch.Uniform
        };

        if (em.Props.TryGetValue("Source", out var src))
        {
            try
            {
                var uri = src.Replace("ms-fcourse:///", "https://fcourse.local/");
                img.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(uri));
            }
            catch { }
        }

        if (em.Props.TryGetValue("Stretch", out var stretchStr) && Enum.TryParse<Stretch>(stretchStr, out var stretchVal))
            img.Stretch = stretchVal;

        if (em.Props.TryGetValue("CornerRadius", out var crStr) && double.TryParse(crStr, out var cr))
            img.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, em.Width, em.Height) };

        return img;
    }

    private static void UpdateImage(Image img, ElementModel em)
    {
        if (em.Props.TryGetValue("Source", out var src))
        {
            try
            {
                var uri = src.Replace("ms-fcourse:///", "https://fcourse.local/");
                img.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(uri));
            }
            catch { }
        }
        if (em.Props.TryGetValue("Stretch", out var stretchStr) && Enum.TryParse<Stretch>(stretchStr, out var stretchVal))
            img.Stretch = stretchVal;
    }

    private static FrameworkElement RenderRectangle(ElementModel em)
    {
        var rect = new Rectangle
        {
            Fill = ParseBrush(GetProp(em, "Fill", "#FF0078D4")),
            StrokeThickness = GetDouble(em, "StrokeThickness", 0),
            RadiusX = GetDouble(em, "RadiusX", 0),
            RadiusY = GetDouble(em, "RadiusY", 0)
        };

        if (em.Props.TryGetValue("Stroke", out var strokeHex))
            rect.Stroke = ParseBrush(strokeHex);

        return rect;
    }

    private static void UpdateRectangle(Rectangle rect, ElementModel em)
    {
        if (em.Props.TryGetValue("Fill", out var fillHex))
            rect.Fill = ParseBrush(fillHex);
        if (em.Props.TryGetValue("Stroke", out var strokeHex))
            rect.Stroke = ParseBrush(strokeHex);
        rect.StrokeThickness = GetDouble(em, "StrokeThickness", 0);
        rect.RadiusX = GetDouble(em, "RadiusX", 0);
        rect.RadiusY = GetDouble(em, "RadiusY", 0);
    }

    private static FrameworkElement RenderEllipse(ElementModel em)
    {
        var ellipse = new Ellipse
        {
            Fill = ParseBrush(GetProp(em, "Fill", "#FF0078D4")),
            StrokeThickness = GetDouble(em, "StrokeThickness", 0)
        };

        if (em.Props.TryGetValue("Stroke", out var strokeHex))
            ellipse.Stroke = ParseBrush(strokeHex);

        return ellipse;
    }

    private static void UpdateEllipse(Ellipse ellipse, ElementModel em)
    {
        if (em.Props.TryGetValue("Fill", out var fillHex))
            ellipse.Fill = ParseBrush(fillHex);
        if (em.Props.TryGetValue("Stroke", out var strokeHex))
            ellipse.Stroke = ParseBrush(strokeHex);
        ellipse.StrokeThickness = GetDouble(em, "StrokeThickness", 0);
    }

    private static FrameworkElement RenderButton(ElementModel em)
    {
        var btn = new Button
        {
            Content = GetProp(em, "Content", "Button"),
            Background = ParseBrush(GetProp(em, "Background", "#FF0078D4")),
            Foreground = ParseBrush(GetProp(em, "Foreground", "#FFFFFFFF")),
            FontSize = GetDouble(em, "FontSize", 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        if (em.Props.TryGetValue("CornerRadius", out var crStr) && double.TryParse(crStr, out var crVal))
            btn.CornerRadius = new CornerRadius(crVal);

        return btn;
    }

    private static void UpdateButton(Button btn, ElementModel em)
    {
        btn.Content = GetProp(em, "Content", "Button");
        if (em.Props.TryGetValue("Background", out var bgHex))
            btn.Background = ParseBrush(bgHex);
        if (em.Props.TryGetValue("Foreground", out var fgHex))
            btn.Foreground = ParseBrush(fgHex);
        btn.FontSize = GetDouble(em, "FontSize", 14);
        if (em.Props.TryGetValue("CornerRadius", out var crStr) && double.TryParse(crStr, out var crVal))
            btn.CornerRadius = new CornerRadius(crVal);
    }

    private static FrameworkElement RenderMedia(ElementModel em)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
            Child = new TextBlock
            {
                Text = $"[Media: {GetProp(em, "Source", "")}]",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static FrameworkElement RenderFallback(ElementModel em)
    {
        return new Border
        {
            Background = new SolidColorBrush(Colors.DarkGray),
            Child = new TextBlock
            {
                Text = $"[{em.Type}]",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static string GetProp(ElementModel em, string key, string def) =>
        em.Props.TryGetValue(key, out var v) ? v : def;

    private static double GetDouble(ElementModel em, string key, double def) =>
        em.Props.TryGetValue(key, out var s) && double.TryParse(s, out var d) ? d : def;

    public static SolidColorBrush ParseBrush(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return new SolidColorBrush(Colors.Transparent);
        hex = hex.TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        if (hex.Length != 8) return new SolidColorBrush(Colors.Transparent);
        try
        {
            return new SolidColorBrush(Color.FromArgb(
                byte.Parse(hex.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber),
                byte.Parse(hex.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber),
                byte.Parse(hex.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber),
                byte.Parse(hex.AsSpan(6, 2), System.Globalization.NumberStyles.HexNumber)));
        }
        catch { return new SolidColorBrush(Colors.Transparent); }
    }

    private static void ApplyFontWeight(TextBlock tb, string w)
    {
        try
        {
            var fontWeightType = typeof(TextBlock).GetProperty("FontWeight")?.PropertyType;
            if (fontWeightType is null) return;
            var fontWeightsType = fontWeightType.Assembly.GetType("Microsoft.UI.Text.FontWeights");
            if (fontWeightsType is null) return;
            var prop = fontWeightsType.GetProperty(w);
            if (prop is null) return;
            var val = prop.GetValue(null);
            if (val is not null)
                typeof(TextBlock).GetProperty("FontWeight")?.SetValue(tb, val);
        }
        catch { }
    }
}

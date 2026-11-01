using System;
using System.IO;
using System.Threading.Tasks;
using CSharpMath.SkiaSharp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;

namespace FluentCourse.Runtime.Controls;

/// <summary>
/// Renders a LaTeX math formula to an image natively (CSharpMath + SkiaSharp, fully offline).
/// </summary>
public sealed partial class FormulaControl : Control
{
    public static readonly DependencyProperty LatexProperty =
        DependencyProperty.Register(nameof(Latex), typeof(string), typeof(FormulaControl),
            new PropertyMetadata("x = \\frac{-b \\pm \\sqrt{b^2 - 4ac}}{2a}", OnAnyChanged));

    public static readonly DependencyProperty FormulaFontSizeProperty =
        DependencyProperty.Register(nameof(FormulaFontSize), typeof(double), typeof(FormulaControl),
            new PropertyMetadata(28.0, OnAnyChanged));

    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(nameof(Color), typeof(string), typeof(FormulaControl),
            new PropertyMetadata("#FFFFFFFF", OnAnyChanged));

    public string Latex { get => (string)GetValue(LatexProperty); set => SetValue(LatexProperty, value); }
    public double FormulaFontSize { get => (double)GetValue(FormulaFontSizeProperty); set => SetValue(FormulaFontSizeProperty, value); }
    public string Color { get => (string)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    // We render the bitmap at FontSize × Supersample so that displaying it at the real FontSize
    // (or scaled by the player's stage transform) downscales a high-res source → stays crisp.
    internal const double Supersample = 4.0;

    private Image? _image;

    // 单调递增的渲染版本号:并发 Render 完成时版本不匹配则丢弃过期结果,
    // 避免 async void 竞态导致旧渲染覆盖新渲染。
    private int _renderVersion;

    public FormulaControl()
    {
        DefaultStyleKey = typeof(FormulaControl);
        Loaded += (_, _) => Render();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _image = GetTemplateChild("PART_Image") as Image;
        Render();
    }

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FormulaControl c) c.Render();
    }

    private async void Render()
    {
        if (_image is null) return;
        var version = ++_renderVersion;
        var src = await RenderToImageAsync(Latex, FormulaFontSize, Color);
        if (version != _renderVersion) return; // 已有更新的渲染请求,丢弃本次结果
        _image.Source = src;

        // Cap the image to its real (1×) size so it is only ever downscaled, never blown up:
        // the formula shows at the chosen FontSize, centered, sharp. A smaller host box still
        // shrinks it via Stretch=Uniform.
        if (src is BitmapImage bmp && bmp.PixelWidth > 0)
        {
            _image.MaxWidth = bmp.PixelWidth / Supersample;
            _image.MaxHeight = bmp.PixelHeight / Supersample;
        }
        else
        {
            _image.MaxWidth = double.PositiveInfinity;
            _image.MaxHeight = double.PositiveInfinity;
        }
    }

    /// <summary>
    /// Render LaTeX to an <see cref="ImageSource"/> using the same offline pipeline as the control.
    /// Shared so the designer can show a live preview without re-implementing the rendering.
    /// Returns null (never throws) on bad LaTeX, so callers can degrade to a blank box.
    /// </summary>
    public static async Task<ImageSource?> RenderToImageAsync(string? latex, double fontSize, string? color)
    {
        try
        {
            var painter = new MathPainter
            {
                LaTeX = latex ?? string.Empty,
                FontSize = (float)(fontSize * Supersample),
                TextColor = ParseColor(color ?? "#FFFFFFFF")
            };

            using var stream = painter.DrawAsStream();
            if (stream is null) return null;
            stream.Position = 0;

            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(stream.AsRandomAccessStream());
            return bmp;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FormulaControl] render failed: {ex.Message}");
            return null;
        }
    }

    private static SKColor ParseColor(string hex)
    {
        try
        {
            hex = (hex ?? "").TrimStart('#');
            if (hex.Length == 8)
                return new SKColor(
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16),
                    Convert.ToByte(hex.Substring(6, 2), 16),
                    Convert.ToByte(hex.Substring(0, 2), 16));
            if (hex.Length == 6)
                return new SKColor(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16));
        }
        catch { }
        return SKColors.White;
    }
}

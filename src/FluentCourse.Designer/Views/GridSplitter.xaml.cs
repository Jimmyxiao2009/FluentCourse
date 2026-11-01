using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;
using ColumnDefinition = Microsoft.UI.Xaml.Controls.ColumnDefinition;
using RowDefinition = Microsoft.UI.Xaml.Controls.RowDefinition;

namespace FluentCourse.Designer.Views;

/// <summary>
/// Lightweight resizable splitter. Place it between two columns (or rows) of a Grid; dragging it
/// resizes the column (or row) on the <c>ResizeTarget</c> side. Supports both horizontal (between
/// columns) and vertical (between rows) orientations.
/// </summary>
public sealed partial class GridSplitter : UserControl
{
    public enum SplitterOrientation { Horizontal, Vertical }

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(SplitterOrientation),
            typeof(GridSplitter), new PropertyMetadata(SplitterOrientation.Horizontal));

    public static readonly DependencyProperty ResizeTargetIndexProperty =
        DependencyProperty.Register(nameof(ResizeTargetIndex), typeof(int),
            typeof(GridSplitter), new PropertyMetadata(0));

    public static readonly DependencyProperty MinTargetSizeProperty =
        DependencyProperty.Register(nameof(MinTargetSize), typeof(double),
            typeof(GridSplitter), new PropertyMetadata(120.0));

    public static readonly DependencyProperty MaxTargetSizeProperty =
        DependencyProperty.Register(nameof(MaxTargetSize), typeof(double),
            typeof(GridSplitter), new PropertyMetadata(2000.0));

    public SplitterOrientation Orientation
    {
        get => (SplitterOrientation)GetValue(OrientationProperty);
        set { SetValue(OrientationProperty, value); ApplyOrientation(); }
    }
    public int ResizeTargetIndex
    {
        get => (int)GetValue(ResizeTargetIndexProperty);
        set => SetValue(ResizeTargetIndexProperty, value);
    }
    public double MinTargetSize
    {
        get => (double)GetValue(MinTargetSizeProperty);
        set => SetValue(MinTargetSizeProperty, value);
    }
    public double MaxTargetSize
    {
        get => (double)GetValue(MaxTargetSizeProperty);
        set => SetValue(MaxTargetSizeProperty, value);
    }

    private bool _isDragging;
    private double _startPos;
    private double _startSize;
    private Grid? _parent;

    public GridSplitter()
    {
        try { InitializeComponent(); }
        catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"[GridSplitter] init failed: {ex}"); }
        Loaded += (_, _) => ApplyOrientation();
    }

    private void ApplyOrientation()
    {
        if (Orientation == SplitterOrientation.Horizontal)
        {
            Width = 6;
            Height = double.NaN;
            GripBorder.BorderThickness = new Thickness(1, 0, 0, 0);
            GripBar.Width = 2;
            GripBar.Height = 40;
        }
        else
        {
            Width = double.NaN;
            Height = 6;
            GripBorder.BorderThickness = new Thickness(0, 1, 0, 0);
            GripBar.Width = 40;
            GripBar.Height = 2;
        }
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        GripBar.Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0xC2, 0xFF));
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
            GripBar.Fill = (Brush)Application.Current.Resources["StrokeDefaultBrush"];
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _parent = FindParentGrid(this);
        if (_parent is null) return;
        if (ResizeTargetIndex < 0 || ResizeTargetIndex >= _parent.ColumnDefinitions.Count
            && Orientation == SplitterOrientation.Horizontal) return;
        if (ResizeTargetIndex < 0 || ResizeTargetIndex >= _parent.RowDefinitions.Count
            && Orientation == SplitterOrientation.Vertical) return;
        _startSize = Orientation == SplitterOrientation.Horizontal
            ? _parent.ColumnDefinitions[ResizeTargetIndex].ActualWidth
            : _parent.RowDefinitions[ResizeTargetIndex].ActualHeight;
        _startPos = Orientation == SplitterOrientation.Horizontal
            ? e.GetCurrentPoint(_parent).Position.X
            : e.GetCurrentPoint(_parent).Position.Y;
        _isDragging = true;
        GripBorder.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || _parent is null) return;
        var pos = Orientation == SplitterOrientation.Horizontal
            ? e.GetCurrentPoint(_parent).Position.X
            : e.GetCurrentPoint(_parent).Position.Y;
        var delta = pos - _startPos;
        var newSize = Math.Clamp(_startSize + delta, MinTargetSize, MaxTargetSize);
        if (Orientation == SplitterOrientation.Horizontal)
            _parent.ColumnDefinitions[ResizeTargetIndex].Width = new GridLength(newSize, GridUnitType.Pixel);
        else
            _parent.RowDefinitions[ResizeTargetIndex].Height = new GridLength(newSize, GridUnitType.Pixel);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
        GripBorder.ReleasePointerCapture(e.Pointer);
    }

    private void OnPointerLost(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
    }

    private static Grid? FindParentGrid(DependencyObject child)
    {
        DependencyObject? current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is Grid g) return g;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}

using FluentCourse.Designer.Services;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace FluentCourse.Designer.Views;

public sealed partial class StageView : UserControl
{
    private ShellViewModel? _vm;
    private FrameworkElement? _currentRoot;
    private readonly List<FrameworkElement> _hitTargets = new();

    public StageView()
    {
        InitializeComponent();
    }

    public void SetViewModel(ShellViewModel vm)
    {
        _vm = vm;
        _vm.DocumentChanged += (_, _) => RenderCurrentPage();
    }

    public void RenderCurrentPage()
    {
        ClearStage();
        if (_vm?.Document?.SelectedPage is null) return;

        var xaml = _vm.Document.SelectedPage.Xaml;
        var result = XamlRoundTrip.TryLoad(xaml);
        if (!result.Success)
        {
            ShowError(result.Error);
            _vm.Document.SelectedPage.XamlError = result.Error;
            return;
        }

        var root = result.Root!;
        root.HorizontalAlignment = HorizontalAlignment.Stretch;
        root.VerticalAlignment = VerticalAlignment.Stretch;

        StageRoot.Children.Insert(0, root);
        StageRoot.Width = _vm.Document.Manifest.Stage.DesignWidth;
        StageRoot.Height = _vm.Document.Manifest.Stage.DesignHeight;

        // Try to set stage background
        try
        {
            var bg = _vm.Document.Manifest.Stage.Background;
            if (!string.IsNullOrEmpty(bg))
            {
                if (bg.StartsWith('#') && bg.Length == 9)
                {
                    var colorBytes = ParseColorBytes(bg);
                    StageRoot.Background = new SolidColorBrush(Color.FromArgb(
                        colorBytes.a, colorBytes.r, colorBytes.g, colorBytes.b));
                }
            }
        }
        catch { }

        ErrorOverlay.Visibility = Visibility.Collapsed;
        _currentRoot = root;
        _vm.Document.SelectedPage.XamlError = null;

        // Walk the tree and collect all FrameworkElements that can be selected
        _hitTargets.Clear();
        WalkTree(root);

        // Scale the viewport
        StageScaler.Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform;
    }

    public void UpdateSelectionOverlay()
    {
        var sel = _vm?.SelectedElement;
        if (sel is null || _currentRoot is null)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            HandleTL.Visibility = HandleTR.Visibility = HandleBL.Visibility = HandleBR.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var transform = sel.TransformToVisual(StageRoot);
            var bounds = transform.TransformBounds(new Windows.Foundation.Rect(
                0, 0, sel.ActualWidth > 0 ? sel.ActualWidth : 100,
                sel.ActualHeight > 0 ? sel.ActualHeight : 40));

            double x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height;
            double handleSize = 8;

            SelectionBorder.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionBorder, x);
            Canvas.SetTop(SelectionBorder, y);
            SelectionBorder.Width = w;
            SelectionBorder.Height = h;

            PositionHandle(HandleTL, x - handleSize / 2, y - handleSize / 2);
            PositionHandle(HandleTR, x + w - handleSize / 2, y - handleSize / 2);
            PositionHandle(HandleBL, x - handleSize / 2, y + h - handleSize / 2);
            PositionHandle(HandleBR, x + w - handleSize / 2, y + h - handleSize / 2);
        }
        catch
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void ClearStage()
    {
        DetachHits();
        _hitTargets.Clear();
        // Remove the page root (index 0) but keep the adorner canvas (index 1)
        if (StageRoot.Children.Count > 1)
            StageRoot.Children.RemoveAt(0);
        _currentRoot = null;
        SelectionBorder.Visibility = Visibility.Collapsed;
    }

    private void WalkTree(DependencyObject node)
    {
        if (node is FrameworkElement fe && fe is not Shape && fe is not Canvas)
        {
            if (!string.IsNullOrEmpty(fe.Name) ||
                fe is TextBlock || fe is Button || fe is Image ||
                fe is Border || fe is Grid || fe is StackPanel ||
                fe is ContentControl || fe is TextBox || fe is CheckBox ||
                fe is Slider || fe is ToggleSwitch || fe is ComboBox ||
                fe is ListView || fe is ScrollViewer)
            {
                _hitTargets.Add(fe);
                fe.PointerPressed += OnElementPointerPressed;
            }
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            WalkTree(VisualTreeHelper.GetChild(node, i));
    }

    private void DetachHits()
    {
        foreach (var fe in _hitTargets)
            fe.PointerPressed -= OnElementPointerPressed;
    }

    private void OnElementPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_vm is null || sender is not FrameworkElement fe) return;
        _vm.SelectedElement = fe;
        e.Handled = true; // stop propagation so we don't accidentally select parent
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

    private static (byte a, byte r, byte g, byte b) ParseColorBytes(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        return (
            byte.Parse(hex.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber),
            byte.Parse(hex.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber),
            byte.Parse(hex.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber),
            byte.Parse(hex.AsSpan(6, 2), System.Globalization.NumberStyles.HexNumber));
    }
}

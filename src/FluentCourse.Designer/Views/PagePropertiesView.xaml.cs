using FluentCourse.Core.Models;
using FluentCourse.Designer.Models;
using FluentCourse.Designer.Services;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FluentCourse.Designer.Views;

public sealed partial class PagePropertiesView : UserControl
{
    private ShellViewModel? _vm;
    private bool _suppress;

    // Common slide-background presets (one-click, no hex typing needed).
    private static readonly string[] BgPresets =
    {
        "#FF0E1424", "#FF1F1F1F", "#FF000000", "#FFFFFFFF",
        "#FFF5F0E8", "#FF15233B", "#FF1A1130", "#FF12322B"
    };

    public PagePropertiesView()
    {
        InitializeComponent();
        BuildBgPresets();
    }

    private void BuildBgPresets()
    {
        foreach (var hex in BgPresets)
        {
            var btn = new Button
            {
                Width = 28, Height = 28, Padding = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Background = CanvasRenderer.ParseBrush(hex),
                BorderThickness = new Thickness(1),
                Tag = hex
            };
            ToolTipService.SetToolTip(btn, hex);
            btn.Click += (s, _) =>
            {
                if (s is Button b && b.Tag is string h) BgBox.Text = h;   // triggers OnChanged
            };
            BgPresetRow.Children.Add(btn);
        }
    }

    private void UpdateSwatch()
    {
        var bg = BgBox.Text;
        if (string.IsNullOrWhiteSpace(bg) || bg.TrimStart().StartsWith("#"))
        {
            try { BgSwatch.Background = CanvasRenderer.ParseBrush(string.IsNullOrWhiteSpace(bg) ? "#00000000" : bg); }
            catch { BgSwatch.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); }
            return;
        }
        // image background → show a thumbnail in the swatch
        try
        {
            var entry = FluentCourse.Runtime.Player.AssetUri.StripScheme(bg);
            var dir = _vm?.Document?.WorkingDirectory;   // 资产在工作目录里
            if (!string.IsNullOrEmpty(dir))
            {
                var path = System.IO.Path.Combine(dir, entry.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(path))
                {
                    BgSwatch.Background = new ImageBrush
                    {
                        ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new System.Uri(path)),
                        Stretch = Stretch.UniformToFill
                    };
                    return;
                }
            }
        }
        catch { }
        BgSwatch.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private async void OnPickBgImage(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker { ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail };
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" })
                picker.FileTypeFilter.Add(ext);
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var rel = _vm.ImportAsset(file.Path);
            if (rel is null) return;
            BgBox.Text = $"ms-fcourse:///{rel}";   // triggers OnChanged → sets background + re-renders
        }
        catch (System.Exception ex) { _vm.ReportDiagnostic($"设置背景图片失败: {ex.Message}"); }
    }

    private void OnClearBgImage(object sender, RoutedEventArgs e)
    {
        BgBox.Text = "#FF1F1F1F";   // back to a solid color
    }

    private DesignerDocument? _observedDoc;

    public void SetViewModel(ShellViewModel vm)
    {
        _vm = vm;
        // 订当前文档 + 跟随 DocumentChanged 重订（解订旧文档）——只订初始文档会在
        // 打开/新建课件后失联。
        HookDocument();
        vm.DocumentChanged += (_, _) => HookDocument();
    }

    private void HookDocument()
    {
        if (_observedDoc is not null) _observedDoc.PropertyChanged -= OnDocumentPropertyChanged;
        _observedDoc = _vm?.Document;
        if (_observedDoc is not null) _observedDoc.PropertyChanged += OnDocumentPropertyChanged;
    }

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 只在选中页变化时整面板重载；IsDirty 等无关属性变化不再触发重载
        // （否则每次编辑都会把面板里未提交的输入冲掉）。
        if (e.PropertyName == nameof(DesignerDocument.SelectedPage) &&
            _observedDoc?.SelectedPage is { } page)
            LoadPage(page);
    }

    public void LoadPage(PageModel page)
    {
        _suppress = true;
        PageIdLabel.Text = $"ID: {page.Descriptor.Id}";
        TitleBox.Text = page.Descriptor.Title ?? "";
        SectionBox.Text = page.Descriptor.Section ?? "";
        TransitionBox.SelectedItem = TransitionBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Content == (page.Descriptor.Transition ?? "Fade"));
        DurationMsBox.Text = page.Descriptor.TransitionDurationMs?.ToString() ?? "";
        BgBox.Text = page.Descriptor.Background ?? "";
        NotesBox.Text = page.Descriptor.Notes ?? "";
        HiddenBox.IsChecked = page.Descriptor.Hidden;
        FileBox.Text = page.Descriptor.File;
        UpdateSwatch();
        _suppress = false;
    }

    private void OnChanged(object sender, object e)
    {
        if (_suppress || _vm?.Document?.SelectedPage is null) return;
        var desc = _vm.Document.SelectedPage.Descriptor;
        desc.Title = TitleBox.Text;
        desc.Section = SectionBox.Text;
        if (TransitionBox.SelectedItem is ComboBoxItem tbi)
            desc.Transition = (string)tbi.Content;
        if (double.TryParse(DurationMsBox.Text, out var dms))
            desc.TransitionDurationMs = (int)dms;
        else
            desc.TransitionDurationMs = null;
        desc.Background = BgBox.Text.NullIfEmpty();
        desc.Notes = NotesBox.Text.NullIfEmpty();
        desc.Hidden = HiddenBox.IsChecked == true;
        // desc.File 不再可编辑（只读显示）：改路径会留下旧文件并可能覆盖其他页。
        UpdateSwatch();
        _vm.MarkDirty();
        _vm.RaiseRenderRequest();
    }
}

internal static class StringEx
{
    public static string? NullIfEmpty(this string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}

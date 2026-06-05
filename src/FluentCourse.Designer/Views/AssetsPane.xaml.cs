using FluentCourse.Core.Models;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace FluentCourse.Designer.Views;

public sealed partial class AssetsPane : UserControl
{
    private ShellViewModel? _vm;

    public AssetsPane()
    {
        InitializeComponent();
    }

    public void SetViewModel(ShellViewModel vm) { _vm = vm; Refresh(); }

    public void Refresh()
    {
        if (_vm?.Document is not null) AssetList.ItemsSource = _vm.Document.Assets;
    }

    private async void OnImportAsset(object sender, RoutedEventArgs e)
    {
        if (_vm?.Document is null) return;
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail };
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var sourceDir = _vm.Document.SourcePath
            ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"FluentCourse_{Guid.NewGuid():N}");
        var assetsDir = System.IO.Path.Combine(sourceDir, "assets");
        System.IO.Directory.CreateDirectory(assetsDir);

        var fileName = System.IO.Path.GetFileName(file.Path);
        var destPath = System.IO.Path.Combine(assetsDir, fileName);
        System.IO.File.Copy(file.Path, destPath, overwrite: true);

        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        var assetType = ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => "image",
            ".mp4" or ".mov" or ".avi" or ".wmv" => "video",
            ".mp3" or ".m4a" or ".wav" or ".ogg" or ".flac" => "audio",
            ".ttf" or ".otf" => "font",
            ".json" => "lottie",
            _ => "binary"
        };

        var key = System.IO.Path.GetFileNameWithoutExtension(fileName);
        _vm.Document.Assets.Add(new AssetDescriptor
        {
            Key = key,
            Path = $"assets/{fileName}",
            Type = assetType
        });
        _vm.MarkDirty();
        Refresh();
    }
}

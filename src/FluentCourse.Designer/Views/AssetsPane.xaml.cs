using System;
using FluentCourse.Core.Models;
using FluentCourse.Designer.Services;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
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
        try
        {
            var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail };
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            // 走 ShellViewModel.ImportAsset 的同一套逻辑（复制进工作目录 + 注册 + 标脏），
            // 不再各自维护一份会把素材丢进孤儿临时目录的副本实现。
            var rel = _vm.ImportAsset(file.Path);
            if (rel is null)
            {
                _vm.ReportDiagnostic("导入素材失败：无法复制文件。");
                return;
            }
            Refresh();
        }
        catch (Exception ex)
        {
            _vm?.ReportDiagnostic($"导入素材失败: {ex.Message}");
        }
    }

    private void OnAssetDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count > 0 && e.Items[0] is AssetDescriptor asset)
        {
            e.Data.SetText(asset.Path);
            e.Data.RequestedOperation = DataPackageOperation.Copy;
        }
    }
}

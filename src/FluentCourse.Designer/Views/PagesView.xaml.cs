using FluentCourse.Designer.Models;
using FluentCourse.Designer.Services;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCourse.Designer.Views;

public sealed partial class PagesView : UserControl
{
    private ShellViewModel? _vm;

    // 拖拽排序：记录被拖页与起始索引，结束时压入移动撤销命令
    private PageModel? _dragPage;
    private int _dragFromIndex = -1;

    public PagesView()
    {
        try { InitializeComponent(); }
        catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"[PagesView] init failed: {ex}"); }
    }

    public void SetViewModel(ShellViewModel vm)
    {
        _vm = vm;
        if (vm.Document is not null) PageList.ItemsSource = vm.Document.Pages;
        vm.DocumentChanged += (_, _) =>
        {
            if (_vm?.Document is not null) PageList.ItemsSource = _vm.Document.Pages;
            SyncSelection();
        };
        vm.SelectionChanged += (_, _) => SyncSelection();
    }

    public void Refresh()
    {
        if (_vm?.Document is not null) PageList.ItemsSource = _vm.Document.Pages;
        SyncSelection();
    }

    private void SyncSelection()
    {
        if (PageList.SelectedItem != _vm?.Document?.SelectedPage)
            PageList.SelectedItem = _vm?.Document?.SelectedPage;
    }

    private void OnPageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null || PageList.SelectedItem is not PageModel page) return;
        if (_vm.Document is null) return;
        if (_vm.Document.SelectedPage != page) _vm.Document.SelectedPage = page;
    }

    private void OnPageItemClick(object sender, ItemClickEventArgs e)
    {
        if (_vm is null || e.ClickedItem is not PageModel page) return;
        _vm.Document!.SelectedPage = page;
    }

    private async void OnDeletePage(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PageModel page }) await ConfirmAndDeleteAsync(page);
    }

    private void OnDuplicatePageClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: PageModel page }) _vm?.DuplicatePageCommand.Execute(page);
    }

    private async void OnDeletePageMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: PageModel page }) await ConfirmAndDeleteAsync(page);
    }

    /// <summary>删除页面前确认（可撤销，但双保险防误点）。</summary>
    private async System.Threading.Tasks.Task ConfirmAndDeleteAsync(PageModel page)
    {
        if (_vm is null) return;
        try
        {
            var title = page.Descriptor.Title ?? page.Descriptor.Id;
            var dlg = new ContentDialog
            {
                Title = "删除页面",
                Content = $"确定删除页面「{title}」吗？（删除后可用 Ctrl+Z 撤销）",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                _vm.DeletePageCommand.Execute(page);
        }
        catch (System.Exception ex) { _vm.ReportDiagnostic($"删除页面失败: {ex.Message}"); }
    }

    private void OnPagesDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragPage = e.Items.Count > 0 ? e.Items[0] as PageModel : null;
        _dragFromIndex = _dragPage is not null && _vm?.Document is not null
            ? _vm.Document.Pages.IndexOf(_dragPage)
            : -1;
    }

    // Drag-reorder modifies the bound Pages collection directly; 这里补一条移动撤销并标脏。
    private void OnPagesReordered(object sender, DragItemsCompletedEventArgs e)
    {
        if (_vm?.Document is null) return;
        if (_dragPage is not null && _dragFromIndex >= 0)
        {
            var to = _vm.Document.Pages.IndexOf(_dragPage);
            if (to >= 0 && to != _dragFromIndex)
            {
                _vm.PushUndo(new CollectionMoveCommand<PageModel>(
                    _vm.Document.Pages, _dragFromIndex, to, null, "调整页面顺序"), affectsPage: false);
                _vm.Document.IsDirty = true;
            }
        }
        _dragPage = null;
        _dragFromIndex = -1;
    }
}

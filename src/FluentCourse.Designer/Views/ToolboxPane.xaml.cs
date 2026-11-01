using System;
using System.Collections.Generic;
using System.Linq;
using FluentCourse.Designer.Services;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace FluentCourse.Designer.Views;

public sealed partial class ToolboxPane : UserControl
{
    private ShellViewModel? _vm;
    private List<ToolboxGroup> _allGroups = new();
    private List<ToolboxGroup> _filteredGroups = new();

    public ToolboxPane()
    {
        try
        {
            InitializeComponent();
            _allGroups = ToolboxCatalog.All
                .GroupBy(i => i.Category)
                .Select(g => new ToolboxGroup { Category = g.Key, Items = g.OrderBy(i => i.Label).ToList() })
                .ToList();
            _filteredGroups = _allGroups;
            ToolboxList.ItemsSource = _filteredGroups;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToolboxPane] ctor failed: {ex}");
            if (App.MainWindowInstance is { } win)
            {
                var dialog = new ContentDialog
                {
                    Title = "工具箱加载失败",
                    Content = ex.Message,
                    CloseButtonText = "确定",
                    XamlRoot = win.Content.XamlRoot
                };
                _ = dialog.ShowAsync();
            }
        }
    }

    public void SetViewModel(ShellViewModel vm) => _vm = vm;

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(q))
        {
            _filteredGroups = _allGroups;
        }
        else
        {
            _filteredGroups = _allGroups
                .Select(g => new ToolboxGroup
                {
                    Category = g.Category,
                    Items = g.Items.Where(i =>
                        i.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        i.DisplayLabel.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        (i.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    ).ToList()
                })
                .Where(g => g.Items.Count > 0)
                .ToList();
        }
        ToolboxList.ItemsSource = _filteredGroups;
    }

    private void OnItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ToolboxItem item || _vm is null) return;
        try { _vm.InsertToolboxItem(item); }
        catch (Exception ex) { _vm.ReportDiagnostic($"插入 {item.DisplayLabel} 失败: {ex.Message}"); }
    }

    private void OnDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is not Button btn || btn.Tag is not ToolboxItem item) return;
        try
        {
            DragState.Current = item;
            args.Data.SetText($"toolbox:{item.Label}");
            args.Data.RequestedOperation = DataPackageOperation.Copy;
            btn.Opacity = 0.4;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToolboxPane] drag start failed: {ex}");
        }
    }

    private void OnDragCompleted(object sender, DropCompletedEventArgs e)
    {
        if (sender is Button btn) btn.Opacity = 1.0;
        DragState.Current = null;
    }

    private void OnItemRightTapped(object sender, RightTappedRoutedEventArgs e) { e.Handled = true; }

    public sealed class ToolboxGroup
    {
        public string Category { get; set; } = "";
        public List<ToolboxItem> Items { get; set; } = new();
    }
}

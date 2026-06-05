using System.Linq;
using FluentCourse.Designer.Services;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCourse.Designer.Views;

public sealed partial class ToolboxPane : UserControl
{
    private ShellViewModel? _vm;

    public ToolboxPane()
    {
        InitializeComponent();
        var groups = ToolboxCatalog.All
            .GroupBy(i => i.Category)
            .Select(g => new ToolboxGroup { Category = g.Key, Items = g.ToList() })
            .ToList();
        ToolboxList.ItemsSource = groups;
    }

    public void SetViewModel(ShellViewModel vm) => _vm = vm;

    private void OnItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ToolboxItem item)
            _vm?.InsertToolboxItemCommand.Execute(item);
    }

    public sealed class ToolboxGroup
    {
        public string Category { get; set; } = "";
        public System.Collections.Generic.List<ToolboxItem> Items { get; set; } = new();
    }
}

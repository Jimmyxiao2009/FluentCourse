using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCourse.Designer.Views;

public sealed partial class PropertiesPane : UserControl
{
    private ShellViewModel? _vm;

    public PropertiesPane()
    {
        InitializeComponent();
    }

    public void SetViewModel(ShellViewModel vm)
    {
        _vm = vm;
        vm.SelectionChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var sel = _vm?.SelectedElement;
        if (sel is null)
        {
            EmptyHint.Visibility = Visibility.Visible;
            PropertyList.ItemsSource = null;
            ElementLabel.Text = "";
            return;
        }

        EmptyHint.Visibility = Visibility.Collapsed;
        PropertyList.ItemsSource = _vm!.SelectedProperties;
        ElementLabel.Text = $"元素: {sel.GetType().Name}" +
                            (string.IsNullOrEmpty(sel.Name) ? "" : $" ({sel.Name})");
    }
}

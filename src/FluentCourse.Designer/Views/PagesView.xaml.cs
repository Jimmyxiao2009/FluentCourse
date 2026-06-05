using FluentCourse.Designer.Models;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCourse.Designer.Views;

public sealed partial class PagesView : UserControl
{
    private ShellViewModel? _vm;

    public PagesView()
    {
        InitializeComponent();
    }

    public void SetViewModel(ShellViewModel vm)
    {
        _vm = vm;
        if (vm.Document is not null) PageList.ItemsSource = vm.Document.Pages;
    }

    public void Refresh()
    {
        if (_vm?.Document is not null) PageList.ItemsSource = _vm.Document.Pages;
    }

    private void OnPageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null || PageList.SelectedItem is not PageModel page) return;
        _vm.Document!.SelectedPage = page;
    }

    private void OnPageItemClick(object sender, ItemClickEventArgs e)
    {
        if (_vm is null || e.ClickedItem is not PageModel page) return;
        _vm.Document!.SelectedPage = page;
    }

    private void OnAddPage(object sender, RoutedEventArgs e)
    {
        _vm?.AddPageCommand.Execute(null);
    }
}

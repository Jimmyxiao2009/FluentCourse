using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCourse.Designer.Views;

public sealed partial class XamlCodeView : UserControl
{
    private ShellViewModel? _vm;
    private bool _suppress;

    public XamlCodeView()
    {
        InitializeComponent();
    }

    public void SetViewModel(ShellViewModel vm) => _vm = vm;

    public void Refresh()
    {
        if (_vm?.Document?.SelectedPage is null) return;
        _suppress = true;
        Editor.Text = _vm.Document.SelectedPage.Xaml;
        PageLabel.Text = $"编辑: {_vm.Document.SelectedPage.Descriptor.File}";
        _suppress = false;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _vm?.Document?.SelectedPage is null) return;
        _vm.Document.SelectedPage.Xaml = Editor.Text;
        _vm.MarkDirty();

        // Validate
        var result = Services.XamlRoundTrip.TryLoad(Editor.Text);
        _vm.Document.SelectedPage.XamlError = result.Success ? null : result.Error;
    }
}

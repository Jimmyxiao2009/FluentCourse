using FluentCourse.Core.Models;
using FluentCourse.Designer.Models;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCourse.Designer.Views;

public sealed partial class PagePropertiesView : UserControl
{
    private ShellViewModel? _vm;
    private bool _suppress;

    public PagePropertiesView()
    {
        InitializeComponent();
    }

    public void SetViewModel(ShellViewModel vm)
    {
        _vm = vm;
        if (vm.Document is not null)
        {
            vm.Document.PropertyChanged += (_, _) =>
            {
                if (vm.Document.SelectedPage is not null) LoadPage(vm.Document.SelectedPage);
            };
        }
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
        desc.File = FileBox.Text;
        _vm.MarkDirty();
        _vm.RaiseRenderRequest();
    }
}

internal static class StringEx
{
    public static string? NullIfEmpty(this string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}

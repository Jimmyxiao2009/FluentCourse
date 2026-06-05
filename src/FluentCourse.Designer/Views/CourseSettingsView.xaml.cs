using FluentCourse.Core.Models;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCourse.Designer.Views;

public sealed partial class CourseSettingsView : UserControl
{
    private ShellViewModel? _vm;
    private bool _suppress;

    public CourseSettingsView()
    {
        InitializeComponent();
    }

    public void SetViewModel(ShellViewModel vm) { _vm = vm; LoadData(); }

    private void LoadData()
    {
        if (_vm?.Document is null) return;
        _suppress = true;
        var m = _vm.Document.Manifest;
        TitleBox.Text = m.Title ?? "";
        AuthorBox.Text = m.Author ?? "";
        foreach (ComboBoxItem item in LanguageBox.Items)
        {
            if (item.Tag as string == m.Language) { item.IsSelected = true; break; }
            item.IsSelected = false;
        }
        DesignWidthBox.Value = m.Stage.DesignWidth;
        DesignHeightBox.Value = m.Stage.DesignHeight;
        foreach (ComboBoxItem item in ScaleModeBox.Items)
        {
            item.IsSelected = item.Tag as string == m.Stage.ScaleMode;
        }
        BgBox.Text = m.Stage.Background;
        foreach (ComboBoxItem item in ThemeBox.Items)
        {
            item.IsSelected = item.Tag as string == m.Stage.Theme;
        }
        _suppress = false;
    }

    private void OnFieldChanged(object sender, object e)
    {
        if (_suppress || _vm?.Document is null) return;
        var m = _vm.Document.Manifest;
        m.Title = TitleBox.Text;
        m.Author = AuthorBox.Text;
        if (LanguageBox.SelectedItem is ComboBoxItem lang) m.Language = lang.Tag as string ?? "zh-CN";
        m.Stage.DesignWidth = DesignWidthBox.Value;
        m.Stage.DesignHeight = DesignHeightBox.Value;
        if (ScaleModeBox.SelectedItem is ComboBoxItem sm) m.Stage.ScaleMode = sm.Tag as string ?? "Uniform";
        m.Stage.Background = BgBox.Text;
        if (ThemeBox.SelectedItem is ComboBoxItem th) m.Stage.Theme = th.Tag as string ?? "System";
        _vm.MarkDirty();
    }
}

using FluentCourse.Designer.Models;
using FluentCourse.Designer.Services;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCourse.Designer.Views;

public sealed partial class XamlCodeView : UserControl
{
    private ShellViewModel? _vm;
    private bool _suppress;
    // 500ms 防抖：每个键击都做 XamlReader.Load + 全树替换太重，还会清空选区。
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public XamlCodeView()
    {
        InitializeComponent();
        _debounce.Tick += (_, _) => { _debounce.Stop(); ApplyXaml(); };
    }

    public void SetViewModel(ShellViewModel vm) => _vm = vm;

    public void Refresh()
    {
        if (_vm?.Document?.SelectedPage is null) return;
        _debounce.Stop();
        _suppress = true;
        Editor.Text = _vm.Document.SelectedPage.ToXaml();
        PageLabel.Text = $"编辑: {_vm.Document.SelectedPage.Descriptor.File}";
        ErrorLabel.Visibility = Visibility.Collapsed;
        _suppress = false;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _vm?.Document?.SelectedPage is null) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private void ApplyXaml()
    {
        if (_vm?.Document?.SelectedPage is null) return;
        var page = _vm.Document.SelectedPage;
        var xaml = Editor.Text;

        var result = XamlRoundTrip.TryLoad(xaml);
        if (result.Success)
        {
            page.XamlError = null;
            ErrorLabel.Visibility = Visibility.Collapsed;
            page.Elements.Clear();
            var parsed = XamlParser.Parse(xaml);
            foreach (var em in parsed)
                page.Elements.Add(em);
            // 用户在代码视图手写的 XAML 即本页权威原文：保存时原样写回，不经 ToXaml() 改写。
            page.AdoptXaml(xaml);
            _vm.MarkDirty();
        }
        else
        {
            // 解析失败：只提示错误，不替换模型（半成品 XAML 不应毁掉页面）。
            page.XamlError = result.Error;
            ErrorLabel.Text = $"解析错误：{result.Error}";
            ErrorLabel.Visibility = Visibility.Visible;
        }
    }
}

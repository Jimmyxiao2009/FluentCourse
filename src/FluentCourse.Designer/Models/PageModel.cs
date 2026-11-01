using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FluentCourse.Core.Models;

namespace FluentCourse.Designer.Models;

public sealed class PageModel : INotifyPropertyChanged
{
    private string? _xamlError;

    public PageModel(PageDescriptor descriptor, ObservableCollection<ElementModel>? elements = null)
    {
        Descriptor = descriptor;
        if (elements != null)
            Elements = elements;
    }

    public PageDescriptor Descriptor { get; }

    public ObservableCollection<ElementModel> Elements { get; } = new();

    /// <summary>加载时（或代码视图编辑后）的页面 XAML 原文。未被用户在画布上编辑过的页面，
    /// 保存时直接写回它，避免不完整的解析/序列化往返丢失内容（如内联 Span、自定义结构）。</summary>
    public string? SourceXaml { get; private set; }

    /// <summary>用户是否在设计器画布上实际编辑过本页内容（增删改元素）。
    /// 仅当为 true 时保存才用 <see cref="ToXaml"/> 重新序列化；否则写回 <see cref="SourceXaml"/>。</summary>
    public bool IsContentDirty { get; set; }

    public string? XamlError
    {
        get => _xamlError;
        set
        {
            if (_xamlError != value)
            {
                _xamlError = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(DisplayLabel));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_xamlError);

    public string DisplayLabel
    {
        get
        {
            var prefix = HasError ? "⚠ " : "";
            var title = Descriptor.Title ?? Descriptor.Id;
            return $"{prefix}{title}";
        }
    }

    public string ToXaml()
    {
        return Services.XamlSerializer.Serialize(this);
    }

    public static PageModel FromXaml(PageDescriptor descriptor, string xaml)
    {
        var elements = Services.XamlParser.Parse(xaml, out var ok);
        var page = new PageModel(descriptor, elements) { SourceXaml = xaml };
        if (!ok)
            // 解析失败：保留原文，画布显示为空但保存时不会覆盖原始 XAML（除非用户编辑本页）。
            page.XamlError = "页面 XAML 无法解析，已保留原始内容；保存不会覆盖它，除非你在本页编辑。";
        return page;
    }

    /// <summary>采纳一段 XAML 为本页的权威原文基线，并清除内容脏标记。
    /// 用于：保存成功后（已落盘的内容即新基线）、或代码视图里用户直接编辑了 XAML 文本。</summary>
    public void AdoptXaml(string xaml)
    {
        SourceXaml = xaml;
        IsContentDirty = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

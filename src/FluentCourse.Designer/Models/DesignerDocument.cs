using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FluentCourse.Core.Models;
using FluentCourse.Core.Packaging;

namespace FluentCourse.Designer.Models;

public sealed class DesignerDocument : INotifyPropertyChanged
{
    private string? _sourcePath;
    private string? _workingDirectory;
    private bool _isDirty;
    private PageModel? _selectedPage;

    public DesignerDocument(CourseManifest manifest, IReadOnlyDictionary<string, string> pageXaml)
    {
        Manifest = manifest;
        Pages = new ObservableCollection<PageModel>(
            manifest.Pages.Select(p => PageModel.FromXaml(p,
                pageXaml.TryGetValue(p.File, out var x) ? x : string.Empty)));
        Animations = new ObservableCollection<AnimationEntry>(
            manifest.Animations.Select(kv => new AnimationEntry(kv.Key, kv.Value)));
        Triggers = new ObservableCollection<TriggerDescriptor>(manifest.Triggers);
        Assets = new ObservableCollection<AssetDescriptor>(manifest.Assets);
    }

    public CourseManifest Manifest { get; }
    public ObservableCollection<PageModel> Pages { get; }
    public ObservableCollection<AnimationEntry> Animations { get; }
    public ObservableCollection<TriggerDescriptor> Triggers { get; }
    public ObservableCollection<AssetDescriptor> Assets { get; }

    /// <summary>
    /// 用户可见的保存位置：目录课件 = 目录本身；单文件课件 = .fcourse 文件路径。
    /// null 表示尚未保存过（Ctrl+S 走"另存为"）。
    /// </summary>
    public string? SourcePath
    {
        get => _sourcePath;
        set { if (_sourcePath != value) { _sourcePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
    }

    /// <summary>
    /// 磁盘上的工作目录（页面/资产实际读写位置）：目录课件 = 自身；.fcourse = 解压出的临时目录；
    /// 新建文档 = 临时目录。所有资产导入与画布资源解析都以它为根。
    /// </summary>
    public string? WorkingDirectory
    {
        get => _workingDirectory;
        set { if (_workingDirectory != value) { _workingDirectory = value; OnPropertyChanged(); } }
    }

    public bool IsDirty
    {
        get => _isDirty;
        set { if (_isDirty != value) { _isDirty = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
    }

    public string DisplayName
    {
        get
        {
            var star = IsDirty ? " *" : "";
            if (SourcePath is null) return $"未命名{star}";
            var name = System.IO.Path.GetFileNameWithoutExtension(SourcePath);
            return $"{name}{star}";
        }
    }

    public PageModel? SelectedPage
    {
        get => _selectedPage;
        set { if (_selectedPage != value) { _selectedPage = value; OnPropertyChanged(); } }
    }

    public void FlushPagesToManifest()
    {
        Manifest.Pages.Clear();
        foreach (var p in Pages) Manifest.Pages.Add(p.Descriptor);
    }

    public void FlushAnimationsToManifest()
    {
        Manifest.Animations.Clear();
        foreach (var a in Animations) Manifest.Animations[a.Name] = a.Descriptor;
    }

    public void FlushTriggersToManifest()
    {
        Manifest.Triggers.Clear();
        foreach (var t in Triggers) Manifest.Triggers.Add(t);
    }

    public void FlushAssetsToManifest()
    {
        Manifest.Assets.Clear();
        foreach (var a in Assets) Manifest.Assets.Add(a);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AnimationEntry : INotifyPropertyChanged
{
    public AnimationEntry(string name, AnimationDescriptor descriptor)
    {
        Name = name;
        Descriptor = descriptor;
    }

    public string Name { get; set; }
    public AnimationDescriptor Descriptor { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

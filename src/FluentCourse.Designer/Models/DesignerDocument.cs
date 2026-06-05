using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FluentCourse.Core.Models;
using FluentCourse.Core.Packaging;

namespace FluentCourse.Designer.Models;

/// <summary>
/// In-memory representation of a .fcourse file being edited. Wraps the JSON manifest with
/// the page XAML strings loaded alongside it, plus dirty tracking. Owns no streams — the
/// <see cref="DocumentService"/> handles I/O.
/// </summary>
public sealed class DesignerDocument : INotifyPropertyChanged
{
    private string? _sourcePath;
    private bool _isDirty;
    private PageModel? _selectedPage;

    public DesignerDocument(CourseManifest manifest, IReadOnlyDictionary<string, string> pageXaml)
    {
        Manifest = manifest;
        Pages = new ObservableCollection<PageModel>(
            manifest.Pages.Select(p => new PageModel(p, pageXaml.TryGetValue(p.File, out var x) ? x : string.Empty)));
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

    /// <summary>Path to the source .fcourse file or directory. Null for new (unsaved) docs.</summary>
    public string? SourcePath
    {
        get => _sourcePath;
        set { if (_sourcePath != value) { _sourcePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
    }

    /// <summary>True if any in-memory change hasn't been written back to disk.</summary>
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

    /// <summary>Currently active page in the design surface / code view.</summary>
    public PageModel? SelectedPage
    {
        get => _selectedPage;
        set { if (_selectedPage != value) { _selectedPage = value; OnPropertyChanged(); } }
    }

    /// <summary>Re-apply the in-memory page list back to the manifest's <see cref="CourseManifest.Pages"/>.</summary>
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

/// <summary>One page in the document. Wraps a <see cref="PageDescriptor"/> with its XAML string.</summary>
public sealed class PageModel : INotifyPropertyChanged
{
    private string _xaml;
    private string? _xamlError;

    public PageModel(PageDescriptor descriptor, string xaml)
    {
        Descriptor = descriptor;
        _xaml = xaml;
    }

    public PageDescriptor Descriptor { get; }

    public string Xaml
    {
        get => _xaml;
        set
        {
            if (_xaml != value)
            {
                _xaml = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayLabel));
            }
        }
    }

    /// <summary>Last parse error from <see cref="FluentCourse.Designer.Services.XamlRoundTrip.TryLoad"/>.</summary>
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One entry in the document's animations dictionary.</summary>
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

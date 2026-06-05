using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentCourse.Core.Models;
using FluentCourse.Designer.Models;
using FluentCourse.Designer.Services;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace FluentCourse.Designer.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel()
    {
        NewDocument();
    }

    [ObservableProperty] private DesignerDocument? _document;
    [ObservableProperty] private FrameworkElement? _selectedElement;
    [ObservableProperty] private string? _selectedElementPath;
    [ObservableProperty] private string? _diagnosticMessage;
    [ObservableProperty] private int _centerPaneIndex;

    public UndoRedoService UndoRedo { get; } = new();
    public ObservableCollection<PropertyDescriptor> SelectedProperties { get; } = new();

    public event EventHandler? DocumentChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler? RequestRender;
    public event EventHandler? RequestPreview;
    public event EventHandler? RequestRefreshTriggers;
    public event EventHandler? RequestRefreshAnimations;

    partial void OnSelectedElementChanged(FrameworkElement? value)
    {
        RebuildPropertyList(value);
        SelectedElementPath = DescribePath(value);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnDocumentChanged(DesignerDocument? value)
    {
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        if (value is not null) value.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DesignerDocument.IsDirty) or nameof(DesignerDocument.DisplayName))
                OnPropertyChanged(nameof(WindowTitle));
        };
    }

    public string WindowTitle => $"{(Document?.DisplayName ?? "FluentCourse Designer")} — FluentCourse Designer";

    // ── File commands ──

    [RelayCommand] private void NewDocument()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}");
        Document = DocumentService.CreateNew(dir);
        SelectedElement = null;
        DiagnosticMessage = null;
        UndoRedo.Clear();
    }

    [RelayCommand] private async Task OpenAsync()
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add(".fcourse");
        picker.FileTypeFilter.Add(".zip");
        picker.FileTypeFilter.Add(".json");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try { Document = DocumentService.Open(file.Path); }
        catch (Exception ex) { DiagnosticMessage = $"打开失败: {ex.Message}"; return; }
        SelectedElement = null;
        DiagnosticMessage = null;
        UndoRedo.Clear();
    }

    [RelayCommand] private async Task SaveAsync()
    {
        if (Document is null) return;
        if (Document.SourcePath is null) { await SaveAsAsync(); return; }
        try
        {
            DocumentService.SaveToDirectory(Document, Document.SourcePath);
            Document.IsDirty = false;
        }
        catch (Exception ex) { DiagnosticMessage = $"保存失败: {ex.Message}"; }
    }

    [RelayCommand] private async Task SaveAsAsync()
    {
        if (Document is null) return;
        var picker = new FileSavePicker
        {
            SuggestedFileName = Document.Manifest.Title ?? "Untitled",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeChoices.Add("FluentCourse 包", new List<string> { ".fcourse" });
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}");
            DocumentService.SaveToDirectory(Document, tempDir);
            DocumentService.PackAsCourse(tempDir, file.Path);
            Document.SourcePath = file.Path;
            Document.IsDirty = false;
        }
        catch (Exception ex) { DiagnosticMessage = $"保存失败: {ex.Message}"; }
    }

    [RelayCommand] private async Task ExportCourseAsync()
    {
        if (Document is null) return;
        var picker = new FileSavePicker
        {
            SuggestedFileName = $"{Document.Manifest.Title}.fcourse",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeChoices.Add("FluentCourse 包", new List<string> { ".fcourse" });
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}");
            DocumentService.SaveToDirectory(Document, tempDir);
            DocumentService.PackAsCourse(tempDir, file.Path);
        }
        catch (Exception ex) { DiagnosticMessage = $"导出失败: {ex.Message}"; }
    }

    // ── Undo/Redo ──

    [RelayCommand] private void Undo() { UndoRedo.Undo(); RequestRender?.Invoke(this, EventArgs.Empty); }
    [RelayCommand] private void Redo() { UndoRedo.Redo(); RequestRender?.Invoke(this, EventArgs.Empty); }

    public void PushUndo(IUndoCommand cmd) => UndoRedo.Push(cmd);

    // ── Page commands ──

    [RelayCommand] private void AddPage()
    {
        if (Document is null) return;
        var i = Document.Pages.Count + 1;
        var id = $"page-{i:00}";
        var file = $"pages/{i:00}-{id}.xaml";
        var page = new PageModel(new PageDescriptor
        {
            Id = id, File = file, Title = id,
            Section = Document.Pages.LastOrDefault()?.Descriptor.Section,
            Transition = "Fade"
        }, NewPageTemplate(id));
        Document.Pages.Add(page);
        PushUndo(new CollectionRemoveCommand<PageModel>(Document.Pages, page, Document.Pages.Count - 1, "删除页面"));
        MarkDirty();
    }

    [RelayCommand] private void DeletePage(PageModel? page)
    {
        if (Document is null || page is null) return;
        var idx = Document.Pages.IndexOf(page);
        if (idx < 0) return;
        Document.Pages.RemoveAt(idx);
        PushUndo(new CollectionInsertCommand<PageModel>(Document.Pages, page, idx, "恢复页面"));
        if (Document.SelectedPage == page) Document.SelectedPage = Document.Pages.FirstOrDefault();
        MarkDirty();
    }

    [RelayCommand] private void MovePageUp(PageModel? page)
    {
        if (Document is null || page is null) return;
        var i = Document.Pages.IndexOf(page);
        if (i > 0) { Document.Pages.Move(i, i - 1); MarkDirty(); }
    }

    [RelayCommand] private void MovePageDown(PageModel? page)
    {
        if (Document is null || page is null) return;
        var i = Document.Pages.IndexOf(page);
        if (i >= 0 && i < Document.Pages.Count - 1) { Document.Pages.Move(i, i + 1); MarkDirty(); }
    }

    // ── Trigger / Animation helpers ──

    public void AddTrigger(TriggerDescriptor td)
    {
        if (Document is null) return;
        Document.Triggers.Add(td);
        PushUndo(new CollectionRemoveCommand<TriggerDescriptor>(Document.Triggers, td,
            Document.Triggers.Count - 1, "删除触发器"));
        MarkDirty();
        RequestRefreshTriggers?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveTrigger(TriggerDescriptor td)
    {
        if (Document is null) return;
        var idx = -1;
        for (int i = 0; i < Document.Triggers.Count; i++)
            if (ReferenceEquals(Document.Triggers[i], td)) { idx = i; break; }
        if (idx < 0) return;
        Document.Triggers.RemoveAt(idx);
        PushUndo(new CollectionInsertCommand<TriggerDescriptor>(Document.Triggers, td, idx, "恢复触发器"));
        MarkDirty();
        RequestRefreshTriggers?.Invoke(this, EventArgs.Empty);
    }

    public void AddAnimation(string name, AnimationDescriptor ad)
    {
        if (Document is null) return;
        var existing = Document.Animations.FirstOrDefault(a => a.Name == name);
        if (existing is not null) { DeleteAnimation(existing); }
        var entry = new AnimationEntry(name, ad);
        Document.Animations.Add(entry);
        PushUndo(new CollectionRemoveCommand<AnimationEntry>(Document.Animations, entry,
            Document.Animations.Count - 1, "删除动画"));
        MarkDirty();
        RequestRefreshAnimations?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteAnimation(AnimationEntry entry)
    {
        if (Document is null) return;
        var idx = Document.Animations.IndexOf(entry);
        if (idx < 0) return;
        Document.Animations.RemoveAt(idx);
        PushUndo(new CollectionInsertCommand<AnimationEntry>(Document.Animations, entry, idx, "恢复动画"));
        MarkDirty();
        RequestRefreshAnimations?.Invoke(this, EventArgs.Empty);
    }

    // ── Toolbox ──

    public void RaiseRenderRequest() => RequestRender?.Invoke(this, EventArgs.Empty);

    [RelayCommand] private void InsertToolboxItem(ToolboxItem? item)
    {
        if (item is null || Document?.SelectedPage is null) return;
        var p = Document.SelectedPage;
        var oldXaml = p.Xaml;
        var closing = p.Xaml.LastIndexOf("</", StringComparison.Ordinal);
        if (closing < 0) return;
        p.Xaml = p.Xaml.Insert(closing, "    " + item.Snippet + "\r\n");
        PushUndo(new TextChangeCommand(() => p.Xaml, v => p.Xaml = v, oldXaml, p.Xaml, () => RequestRender?.Invoke(this, EventArgs.Empty), $"插入 {item.Label}"));
        MarkDirty();
        RequestRender?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand] private void PreviewCourse() { RequestPreview?.Invoke(this, EventArgs.Empty); }

    public void MarkDirty() => Document!.IsDirty = true;
    public void ReportDiagnostic(string message) => DiagnosticMessage = message;
    public void ClearDiagnostic() => DiagnosticMessage = null;

    private void RebuildPropertyList(FrameworkElement? element)
    {
        SelectedProperties.Clear();
        if (element is null) return;
        foreach (var desc in PropertyInspector.Inspect(element))
            SelectedProperties.Add(desc);
    }

    private static string? DescribePath(FrameworkElement? e)
    {
        if (e is null) return null;
        var name = !string.IsNullOrEmpty(e.Name) ? e.Name : e.GetType().Name;
        return $"{e.GetType().Name} \"{name}\"";
    }

    private static string NewPageTemplate(string id) =>
        $"<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\r\n" +
        $"      xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\r\n" +
        $"    <TextBlock Text=\"{id}\" FontSize=\"48\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/>\r\n" +
        $"</Grid>\r\n";
}

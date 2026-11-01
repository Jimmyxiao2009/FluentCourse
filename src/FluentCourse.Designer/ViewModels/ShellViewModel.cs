using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentCourse.Core.Models;
using FluentCourse.Designer.Models;
using FluentCourse.Designer.Services;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace FluentCourse.Designer.ViewModels;

/// <summary>What a button does on click (read/written by the button-action editor).</summary>
public sealed class ButtonActionInfo
{
    public string Kind = "none";       // none | show | hide | toggle | anim | prevpage | nextpage
    public ElementModel? Target;
    public string Animation = "FadeIn";
}

public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel()
    {
        CreateNewDocumentCore();
    }

    /// <summary>
    /// 替换脏文档前的确认回调（由 MainWindow 提供 ContentDialog 实现）。
    /// 返回 true = 可以继续（已保存或用户选择不保存），false = 用户取消。
    /// </summary>
    public Func<System.Threading.Tasks.Task<bool>>? ConfirmDiscardChangesAsync { get; set; }

    private async System.Threading.Tasks.Task<bool> ConfirmDiscardAsync()
    {
        if (Document is null || !Document.IsDirty) return true;
        if (ConfirmDiscardChangesAsync is null) return true;
        return await ConfirmDiscardChangesAsync();
    }

    [ObservableProperty] private DesignerDocument? _document;
    [ObservableProperty] private ElementModel? _selectedElement;
    [ObservableProperty] private string? _selectedElementPath;
    [ObservableProperty] private string? _diagnosticMessage;
    [ObservableProperty] private int _centerPaneIndex;
    [ObservableProperty] private double _zoom = 1.0;
    [ObservableProperty] private bool _showRulers = true;
    [ObservableProperty] private bool _showGrid = true;
    [ObservableProperty] private bool _snapToGrid = true;
    [ObservableProperty] private string _statusMessage = "就绪";

    public UndoRedoService UndoRedo { get; } = new();
    public ObservableCollection<ElementModel> SelectedElements { get; } = new();
    private readonly Dictionary<IUndoCommand, bool> _undoAffectsPage = new();
    private bool _syncingSelection;

    public event EventHandler? DocumentChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler? RequestRender;
    public event EventHandler? RequestPreview;
    public event EventHandler? RequestRefreshTriggers;
    public event EventHandler? RequestRefreshAnimations;
    public event EventHandler? RequestShowPageProperties;

    /// <summary>Ask the shell to show the page-properties panel (e.g. clicking blank canvas).</summary>
    public void ShowPageProperties() => RequestShowPageProperties?.Invoke(this, EventArgs.Empty);

    partial void OnSelectedElementChanged(ElementModel? value)
    {
        if (!_syncingSelection)
        {
            SelectedElements.Clear();
            if (value is not null) SelectedElements.Add(value);
        }

        SelectedElementPath = value is not null
            ? $"{value.Type}{(string.IsNullOrEmpty(value.Name) ? "" : $" \"{value.Name}\"")}"
            : null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsSelected(ElementModel em) => SelectedElements.Contains(em);

    public void SetSelection(ElementModel? em)
    {
        _syncingSelection = true;
        SelectedElements.Clear();
        if (em is not null) SelectedElements.Add(em);
        SelectedElement = em;
        _syncingSelection = false;
        UpdateSelectionText();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleElementSelection(ElementModel em)
    {
        _syncingSelection = true;
        if (SelectedElements.Contains(em))
            SelectedElements.Remove(em);
        else
            SelectedElements.Add(em);
        SelectedElement = SelectedElements.LastOrDefault();
        _syncingSelection = false;
        UpdateSelectionText();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelectionText()
    {
        SelectedElementPath = SelectedElements.Count switch
        {
            0 => null,
            1 => SelectedElement is null ? null :
                $"{SelectedElement.Type}{(string.IsNullOrEmpty(SelectedElement.Name) ? "" : $" \"{SelectedElement.Name}\"")}",
            _ => $"已选择 {SelectedElements.Count} 个对象"
        };
    }

    partial void OnDocumentChanged(DesignerDocument? value)
    {
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        if (value is not null)
            value.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(DesignerDocument.IsDirty) or nameof(DesignerDocument.DisplayName))
                    OnPropertyChanged(nameof(WindowTitle));
            };
    }

    public string WindowTitle =>
        $"{(Document?.DisplayName ?? "FluentCourse Designer")} — FluentCourse Designer";

    // ── File commands ──

    [RelayCommand] private async System.Threading.Tasks.Task NewDocumentAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        CreateNewDocumentCore();
    }

    private void CreateNewDocumentCore()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}");
        Document = DocumentService.CreateNew(dir);
        Document.SelectedPage = Document.Pages.FirstOrDefault();
        SelectedElement = null;
        DiagnosticMessage = null;
        UndoRedo.Clear();
        _undoAffectsPage.Clear();
    }

    [RelayCommand] private async System.Threading.Tasks.Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add(".fcourse");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try { Document = DocumentService.Open(file.Path); }
        catch (Exception ex) { DiagnosticMessage = $"打开失败: {ex.Message}"; return; }
        Document.SelectedPage = Document.Pages.FirstOrDefault();
        SelectedElement = null;
        DiagnosticMessage = null;
        UndoRedo.Clear();
        _undoAffectsPage.Clear();
    }

    [RelayCommand] private async System.Threading.Tasks.Task SaveAsync()
    {
        if (Document is null) return;
        if (Document.SourcePath is null) { await SaveAsAsync(); return; }
        try { DocumentService.Save(Document); Document.IsDirty = false; ClearRecovery(); }
        catch (Exception ex) { DiagnosticMessage = $"保存失败: {ex.Message}"; }
    }

    [RelayCommand] private async System.Threading.Tasks.Task SaveAsAsync()
    {
        if (Document is null) return;
        var picker = new FileSavePicker
        {
            SuggestedFileName = Document.Manifest.Title ?? "Untitled",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeChoices.Add("FluentCourse 包", new System.Collections.Generic.List<string> { ".fcourse" });
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            // 工作目录就是文档内容（含已导入的 assets）的所在地：写入页面/清单后整体打包，
            // 这样另存为不会丢资产；工作目录继续作为后续编辑的工作副本。
            var work = Document.WorkingDirectory;
            if (string.IsNullOrEmpty(work))
            {
                work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}");
                Document.WorkingDirectory = work;
            }
            DocumentService.SaveToDirectory(Document, work!);
            DocumentService.PackAsCourse(work!, file.Path);
            Document.SourcePath = file.Path;
            Document.IsDirty = false;
            ClearRecovery();
        }
        catch (Exception ex) { DiagnosticMessage = $"保存失败: {ex.Message}"; }
    }

    [RelayCommand] private async System.Threading.Tasks.Task ExportCourseAsync()
    {
        if (Document is null) return;
        var picker = new FileSavePicker
        {
            SuggestedFileName = $"{Document.Manifest.Title}.fcourse",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeChoices.Add("FluentCourse 包", new System.Collections.Generic.List<string> { ".fcourse" });
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            // 同另存为：从工作目录打包，保证 assets 一并进包。
            var work = Document.WorkingDirectory;
            if (string.IsNullOrEmpty(work))
            {
                work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}");
                Document.WorkingDirectory = work;
            }
            DocumentService.SaveToDirectory(Document, work!);
            DocumentService.PackAsCourse(work!, file.Path);
        }
        catch (Exception ex) { DiagnosticMessage = $"导出失败: {ex.Message}"; }
    }

    [RelayCommand] private async System.Threading.Tasks.Task ExportStandaloneAsync()
    {
        if (Document is null) return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var picker = new FileSavePicker
        {
            SuggestedFileName = $"{Document.Manifest.Title}.exe",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeChoices.Add("独立播放器", new System.Collections.Generic.List<string> { ".exe" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        string? tempCourseArchive = null;
        try
        {
            var playerDirectory = await ResolvePlayerBundleDirectoryAsync(hwnd);
            if (playerDirectory is null) return;

            var work = Document.WorkingDirectory;
            if (string.IsNullOrEmpty(work))
            {
                work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}");
                Document.WorkingDirectory = work;
            }

            DocumentService.SaveToDirectory(Document, work!);
            tempCourseArchive = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}.fcourse");
            DocumentService.PackAsCourse(work!, tempCourseArchive);
            StandaloneBundleService.Bundle(tempCourseArchive, playerDirectory, file.Path);
            StatusMessage = $"已导出独立播放器: {System.IO.Path.GetFileName(file.Path)}";
        }
        catch (Exception ex)
        {
            DiagnosticMessage = $"导出失败: {ex.Message}";
        }
        finally
        {
            if (tempCourseArchive is not null)
            {
                try { System.IO.File.Delete(tempCourseArchive); }
                catch { }
            }
        }
    }

    private async System.Threading.Tasks.Task<string?> ResolvePlayerBundleDirectoryAsync(IntPtr hwnd)
    {
        var root = FindRepositoryRoot();
        if (root is not null)
        {
            var publishDir = GetDefaultPlayerPublishDirectory(root);
            if (System.IO.File.Exists(System.IO.Path.Combine(publishDir, "FluentCourse.App.exe")))
                return publishDir;

            StatusMessage = "正在发布播放器...";
            await PublishPlayerAsync(root, publishDir);
            if (System.IO.File.Exists(System.IO.Path.Combine(publishDir, "FluentCourse.App.exe")))
                return publishDir;
        }

        var debugDir = FindDebugPlayerDirectory(root);
        if (debugDir is not null)
            return debugDir;

        var folderPicker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Desktop
        };
        folderPicker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
        var playerFolder = await folderPicker.PickSingleFolderAsync();
        if (playerFolder is null) return null;

        if (!System.IO.File.Exists(System.IO.Path.Combine(playerFolder.Path, "FluentCourse.App.exe")))
            throw new System.IO.FileNotFoundException("请选择已发布播放器目录，目录内需要包含 FluentCourse.App.exe。");

        return playerFolder.Path;
    }

    private static string? FindRepositoryRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "FluentCourse.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static string GetDefaultPlayerPublishDirectory(string root) =>
        System.IO.Path.Combine(root, "build", "player-publish", "win-x64");

    private static string? FindDebugPlayerDirectory(string? root)
    {
        if (root is null) return null;
        var dir = System.IO.Path.Combine(
            root, "src", "FluentCourse.App", "bin", "x64", "Debug",
            "net8.0-windows10.0.19041.0", "win-x64");
        return System.IO.File.Exists(System.IO.Path.Combine(dir, "FluentCourse.App.exe")) ? dir : null;
    }

    private static async System.Threading.Tasks.Task PublishPlayerAsync(string root, string publishDir)
    {
        System.IO.Directory.CreateDirectory(publishDir);
        var project = System.IO.Path.Combine(root, "src", "FluentCourse.App", "FluentCourse.App.csproj");
        var output = new System.Text.StringBuilder();
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = root
            }
        };

        var args = process.StartInfo.ArgumentList;
        args.Add("publish");
        args.Add(project);
        args.Add("-c");
        args.Add("Release");
        args.Add("-r");
        args.Add("win-x64");
        args.Add("-p:Platform=x64");
        args.Add("-p:WindowsAppSDKSelfContained=true");
        args.Add("-p:WindowsPackageType=None");
        args.Add("-p:BuildForMsix=false");
        args.Add("-p:AppxGeneratePriEnabled=false");
        args.Add("-p:EnablePriGenTooling=false");
        args.Add("-p:EnableCoreMrtTooling=false");
        args.Add("-p:ErrorOnDuplicatePublishOutputFiles=false");
        args.Add("-o");
        args.Add(publishDir);

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };

        if (!process.Start())
            throw new InvalidOperationException("无法启动 dotnet publish。");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var message = output.ToString();
            if (message.Length > 1200)
                message = message[^1200..];
            throw new InvalidOperationException($"播放器发布失败: {message}");
        }
    }

    // ── Autosave / crash recovery ──────────────────────────────────────────────────────
    // Every tick (driven by MainWindow) we snapshot a dirty document to a fixed recovery folder,
    // mirroring its assets so the snapshot is self-contained. On a clean Save we clear it; if the app
    // dies with unsaved changes the folder survives and startup offers to restore it.

    public static readonly string RecoveryDir =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FluentCourse", "recovery");

    public void AutosaveIfDirty()
    {
        if (Document is null || !Document.IsDirty) return;
        try
        {
            System.IO.Directory.CreateDirectory(RecoveryDir);
            DocumentService.SaveToDirectory(Document, RecoveryDir);
            if (!string.IsNullOrEmpty(Document.WorkingDirectory) && System.IO.Directory.Exists(Document.WorkingDirectory))
                DocumentService.MirrorSupportFiles(Document.WorkingDirectory!, RecoveryDir);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(RecoveryDir, "recovery.txt"),
                $"{Document.SourcePath}\n{Document.Manifest.Title}\n{System.DateTimeOffset.Now:yyyy-MM-dd HH:mm}");
        }
        catch (Exception ex) { App.LogException("Autosave", ex); }
    }

    public static void ClearRecovery()
    {
        try { if (System.IO.Directory.Exists(RecoveryDir)) System.IO.Directory.Delete(RecoveryDir, true); } catch { }
    }

    public static (bool exists, string original, string title, string savedAt) GetRecoveryInfo()
    {
        try
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(RecoveryDir, "manifest.json")) &&
                System.IO.File.Exists(System.IO.Path.Combine(RecoveryDir, "recovery.txt")))
            {
                var lines = System.IO.File.ReadAllLines(System.IO.Path.Combine(RecoveryDir, "recovery.txt"));
                string L(int i) => i < lines.Length ? lines[i] : "";
                return (true, L(0), L(1), L(2));
            }
        }
        catch { }
        return (false, "", "", "");
    }

    public void OpenRecovery(string original)
    {
        try
        {
            // 把恢复快照复制到全新的工作目录再打开 —— 否则 WorkingDirectory 指向 RecoveryDir，
            // 下次干净保存时 ClearRecovery 会把正在使用的资产一起删掉。
            var work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}");
            DocumentService.CopyDirectory(RecoveryDir, work);
            Document = DocumentService.Open(work);
            if (!string.IsNullOrEmpty(original) && original != "" ) Document.SourcePath = original;
            else Document.SourcePath = null;   // 原本未命名 → 保持"另存为"路径
            Document.SelectedPage = Document.Pages.FirstOrDefault();
            SelectedElement = null;
            UndoRedo.Clear();
            _undoAffectsPage.Clear();
            MarkDirty();
            DiagnosticMessage = "已恢复上次未保存的更改，请记得另存或保存。";
        }
        catch (Exception ex) { DiagnosticMessage = $"恢复失败: {ex.Message}"; }
    }

    // ── Undo/Redo ──

    [RelayCommand] private void Undo()
    {
        if (!UndoRedo.CanUndo) return;
        var cmd = UndoRedo.Undo();
        MarkDirtyAfterUndoRedo(cmd);
        RequestRender?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand] private void Redo()
    {
        if (!UndoRedo.CanRedo) return;
        var cmd = UndoRedo.Redo();
        MarkDirtyAfterUndoRedo(cmd);
        RequestRender?.Invoke(this, EventArgs.Empty);
    }
    // affectsPage=true（默认）表示该命令改动了当前页的元素内容，需把该页标记为"已编辑"，
    // 这样保存时才用 ToXaml() 重新序列化（否则写回原始 XAML 以免不完整往返丢内容）。
    // 触发器/动画等非页面内容的命令传 affectsPage:false。
    public void PushUndo(IUndoCommand cmd, bool affectsPage = true)
    {
        UndoRedo.Push(cmd);
        _undoAffectsPage[cmd] = affectsPage;
        if (affectsPage && Document?.SelectedPage is { } pg) pg.IsContentDirty = true;
    }

    // ── Page commands ──

    [RelayCommand] private void AddPage()
    {
        AddPageWithLayout("blank");
    }

    public void AddPageWithLayout(string layout)
    {
        if (Document is null) return;
        // id / 文件名都必须全课件唯一：删页后再建页，单纯用 Pages.Count+1 会与现存页同名，
        // 保存时互相覆盖。
        var id = UniquePageId($"page-{Document.Pages.Count + 1:00}");
        var file = UniquePageFile(id);
        // New pages MUST start with a Canvas root. The serializer only emits Elements[0], so a page
        // with no root container silently swallows every element added to it past the first.
        var rootCanvas = BuildPageLayout(layout, Document.Manifest.Stage.DesignWidth, Document.Manifest.Stage.DesignHeight);
        var page = new PageModel(new PageDescriptor
        {
            Id = id, File = file, Title = LayoutTitle(layout, id),
            Section = Document.Pages.LastOrDefault()?.Descriptor.Section,
            Transition = "Fade"
        }, new ObservableCollection<ElementModel> { rootCanvas });
        Document.Pages.Add(page);
        // 撤销"新建页面" = 把它移除（CollectionInsertCommand.Undo=RemoveAt / Redo=Insert）。
        PushUndo(new CollectionInsertCommand<PageModel>(Document.Pages, page,
            Document.Pages.Count - 1, "新建页面"));
        MarkDirty();
        Document.SelectedPage = page;   // 新建后立即切到该页编辑
        StatusMessage = $"已新建页面: {LayoutTitle(layout, "空白")}";
    }

    public void ApplyLayoutToCurrentPage(string layout)
    {
        if (Document?.SelectedPage is not { } page) return;

        var oldElements = page.Elements.Select(e => e.Clone()).ToList();
        var oldTitle = page.Descriptor.Title;
        var newRoot = BuildPageLayout(layout, Document.Manifest.Stage.DesignWidth, Document.Manifest.Stage.DesignHeight);
        var newTitle = LayoutTitle(layout, oldTitle ?? page.Descriptor.Id);
        var newElements = new List<ElementModel> { newRoot };

        void SetPage(IEnumerable<ElementModel> elements, string? title)
        {
            page.Elements.Clear();
            foreach (var em in elements)
                page.Elements.Add(em.Clone());
            page.Descriptor.Title = title;
        }

        SetPage(newElements, newTitle);
        SetSelection(null);
        PushUndo(new DelegateUndoCommand(
            () => { SetPage(oldElements, oldTitle); SetSelection(null); RaiseRenderRequest(); },
            () => { SetPage(newElements, newTitle); SetSelection(null); RaiseRenderRequest(); },
            $"应用版式: {newTitle}"));
        MarkDirty();
        RaiseRenderRequest();
        StatusMessage = $"已应用版式: {newTitle}";
    }

    private static string LayoutTitle(string layout, string fallback) => layout switch
    {
        "title" => "标题页",
        "title-content" => "标题与内容",
        "two-column" => "双栏内容",
        "section" => "章节页",
        _ => fallback
    };

    private static ElementModel BuildPageLayout(string layout, double stageW, double stageH)
    {
        var root = new ElementModel { Type = "Canvas", Width = stageW, Height = stageH };
        if (layout == "blank") return root;

        ElementModel Text(string text, double x, double y, double w, double h, double size, string weight = "Normal")
        {
            var em = new ElementModel { Type = "TextBlock", X = x, Y = y, Width = w, Height = h };
            em.Props["Text"] = text;
            em.Props["FontSize"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            em.Props["Foreground"] = "#FFFFFFFF";
            em.Props["FontWeight"] = weight;
            em.Props["TextWrapping"] = "Wrap";
            return em;
        }

        var margin = Math.Max(80, stageW * 0.075);
        switch (layout)
        {
            case "title":
                root.Children.Add(Text("单击输入标题", margin, stageH * 0.34, stageW - margin * 2, 110, 64, "SemiBold"));
                root.Children.Add(Text("单击输入副标题", margin, stageH * 0.48, stageW - margin * 2, 70, 30));
                break;
            case "title-content":
                root.Children.Add(Text("单击输入标题", margin, 70, stageW - margin * 2, 80, 48, "SemiBold"));
                root.Children.Add(Text("单击输入内容", margin, 190, stageW - margin * 2, stageH - 260, 28));
                break;
            case "two-column":
                root.Children.Add(Text("单击输入标题", margin, 70, stageW - margin * 2, 80, 48, "SemiBold"));
                var gap = 48.0;
                var colW = (stageW - margin * 2 - gap) / 2;
                root.Children.Add(Text("左侧内容", margin, 190, colW, stageH - 260, 26));
                root.Children.Add(Text("右侧内容", margin + colW + gap, 190, colW, stageH - 260, 26));
                break;
            case "section":
                root.Children.Add(Text("章节标题", margin, stageH * 0.40, stageW - margin * 2, 100, 58, "SemiBold"));
                root.Children.Add(Text("章节说明", margin, stageH * 0.52, stageW - margin * 2, 70, 28));
                break;
        }
        return root;
    }

    [RelayCommand] private void DeletePage(PageModel? page)
    {
        if (Document is null || page is null) return;
        var idx = Document.Pages.IndexOf(page);
        if (idx < 0) return;
        var wasSelected = Document.SelectedPage == page;
        Document.Pages.RemoveAt(idx);
        // 撤销"删除页面" = 在原位置重新插入（CollectionRemoveCommand.Undo=Insert / Redo=RemoveAt）。
        PushUndo(new CollectionRemoveCommand<PageModel>(Document.Pages, page, idx, "删除页面"));
        MarkDirty();
        // 删除当前页后选中相邻页，避免画布留下可编辑的"幽灵页"。
        if (wasSelected)
        {
            SelectedElement = null;
            Document.SelectedPage = Document.Pages.Count > 0
                ? Document.Pages[Math.Min(idx, Document.Pages.Count - 1)]
                : null;
        }
    }

    [RelayCommand]
    private void DuplicatePage(PageModel? page)
    {
        if (Document is null || page is null) return;
        var idx = Document.Pages.IndexOf(page);
        if (idx < 0) return;

        var src = page.Descriptor;
        var newId = UniquePageId((string.IsNullOrEmpty(src.Id) ? "page" : src.Id) + "-copy");
        var clones = new ObservableCollection<ElementModel>(page.Elements.Select(e => e.Clone()));
        var desc = new PageDescriptor
        {
            Id = newId,
            File = UniquePageFile(newId),
            Title = (string.IsNullOrEmpty(src.Title) ? src.Id : src.Title) + " 副本",
            Section = src.Section,
            Transition = src.Transition,
            Background = src.Background
        };
        var copy = new PageModel(desc, clones);
        Document.Pages.Insert(idx + 1, copy);
        // 撤销"再制页面" = 移除副本。
        PushUndo(new CollectionInsertCommand<PageModel>(Document.Pages, copy, idx + 1, "再制页面"));
        MarkDirty();
        Document.SelectedPage = copy;
    }

    private string UniquePageId(string baseId)
    {
        var ids = new HashSet<string>(Document!.Pages.Select(p => p.Descriptor.Id));
        if (!ids.Contains(baseId)) return baseId;
        int n = 2;
        while (ids.Contains($"{baseId}-{n}")) n++;
        return $"{baseId}-{n}";
    }

    /// <summary>生成不与任何现存页冲突的页面文件名（id 唯一仍可能撞上历史命名风格的文件）。</summary>
    private string UniquePageFile(string id)
    {
        var files = new HashSet<string>(Document!.Pages.Select(p => p.Descriptor.File),
            StringComparer.OrdinalIgnoreCase);
        var f = $"pages/{id}.xaml";
        int n = 2;
        while (files.Contains(f)) f = $"pages/{id}-{n++}.xaml";
        return f;
    }

    // ── Element insertion from toolbox ──

    /// <summary>
    /// Return the page's root container, creating a Canvas root when the page has none (or its root
    /// isn't a container). The serializer only emits <c>Elements[0]</c>; without a container root the
    /// 2nd+ element added to a page would become a stray sibling root that never renders.
    /// </summary>
    private ElementModel EnsureContainerRoot(PageModel page)
    {
        var root = page.Elements.FirstOrDefault();
        if (root != null && IsContainer(root.Type)) return root;

        var canvas = new ElementModel
        {
            Type = "Canvas",
            Width = Document?.Manifest.Stage.DesignWidth ?? 1920,
            Height = Document?.Manifest.Stage.DesignHeight ?? 1080
        };
        // Preserve any stray top-level elements by nesting them inside the new canvas.
        foreach (var existing in page.Elements.ToList())
            canvas.Children.Add(existing);
        page.Elements.Clear();
        page.Elements.Add(canvas);
        return canvas;
    }

    public void InsertToolboxItem(ToolboxItem item)
    {
        if (item is null || Document?.SelectedPage is null) return;
        var page = Document.SelectedPage;
        var em = CreateElementFromToolbox(item);

        var root = EnsureContainerRoot(page);
        var idx = root.Children.Count;
        // Place new elements centered on the page (the old behavior dumped everything at 100,100),
        // cascaded a little so successive inserts don't perfectly overlap.
        var stageW = Document.Manifest.Stage.DesignWidth;
        var stageH = Document.Manifest.Stage.DesignHeight;
        var w = em.Width > 0 ? em.Width : item.DefaultWidth;
        var h = em.Height > 0 ? em.Height : item.DefaultHeight;
        var step = (idx % 6) * 28;
        em.X = Math.Round(Math.Max(0, (stageW - w) / 2 - 70 + step));
        em.Y = Math.Round(Math.Max(0, (stageH - h) / 2 - 70 + step));
        root.Children.Add(em);
        PushUndo(new InsertElementCommand(root.Children, em, idx, RaiseRenderRequest, $"插入 {item.Label}"));

        MarkDirty();
        RequestRender?.Invoke(this, EventArgs.Empty);
        SelectedElement = em;  // auto-select so properties panel opens immediately
    }

    public void InsertToolboxItemAtPoint(ToolboxItem item, double pageX, double pageY)
    {
        if (item is null || Document?.SelectedPage is null) return;
        var page = Document.SelectedPage;
        var em = CreateElementFromToolbox(item);
        em.X = Math.Max(0, pageX - item.DefaultWidth / 2);
        em.Y = Math.Max(0, pageY - item.DefaultHeight / 2);

        var root = EnsureContainerRoot(page);
        var idx = root.Children.Count;
        root.Children.Add(em);
        PushUndo(new InsertElementCommand(root.Children, em, idx, RaiseRenderRequest, $"插入 {item.Label}"));

        MarkDirty();
        RequestRender?.Invoke(this, EventArgs.Empty);
        SelectedElement = em;
    }

    /// <summary>
    /// Copy a file into the course's assets folder and register it. Returns the relative path
    /// ("assets/x.mp3") for use as a "ms-fcourse:///{path}" source, or null on failure.
    /// </summary>
    public string? ImportAsset(string sourceFilePath)
    {
        if (Document is null) return null;
        // 素材一律进工作目录（不是 SourcePath —— 那可能是 .fcourse 文件路径）。
        var sourceDir = Document.WorkingDirectory;
        if (string.IsNullOrEmpty(sourceDir))
        {
            sourceDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}");
            Document.WorkingDirectory = sourceDir;
        }

        var assetsDir = System.IO.Path.Combine(sourceDir, "assets");
        System.IO.Directory.CreateDirectory(assetsDir);

        var fileName = System.IO.Path.GetFileName(sourceFilePath);
        var dest = System.IO.Path.Combine(assetsDir, fileName);
        int counter = 1;
        while (System.IO.File.Exists(dest))
        {
            var n = System.IO.Path.GetFileNameWithoutExtension(fileName);
            var e = System.IO.Path.GetExtension(fileName);
            fileName = $"{n}_{counter++}{e}";
            dest = System.IO.Path.Combine(assetsDir, fileName);
        }
        System.IO.File.Copy(sourceFilePath, dest, overwrite: true);

        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        var type = ext switch
        {
            ".mp4" or ".mov" or ".webm" or ".avi" or ".wmv" or ".m4v" => "video",
            ".mp3" or ".m4a" or ".wav" or ".ogg" or ".flac" or ".aac" => "audio",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => "image",
            _ => "binary"
        };
        Document.Assets.Add(new AssetDescriptor
        {
            Key = System.IO.Path.GetFileNameWithoutExtension(fileName),
            Path = $"assets/{fileName}",
            Type = type
        });
        MarkDirty();
        return $"assets/{fileName}";
    }

    public void InsertImageElement(string assetPath, double pageX, double pageY)
    {
        if (Document?.SelectedPage is null) return;
        var page = Document.SelectedPage;
        var em = new ElementModel
        {
            Type = "Image",
            X = Math.Max(0, pageX - 100),
            Y = Math.Max(0, pageY - 70),
            Width = 200,
            Height = 140
        };
        em.Props["Source"] = $"ms-fcourse:///{assetPath}";
        em.Props["Stretch"] = "Uniform";
        var root = EnsureContainerRoot(page);
        var idx = root.Children.Count;
        root.Children.Add(em);
        PushUndo(new InsertElementCommand(root.Children, em, idx, RaiseRenderRequest, "插入图片"));
        MarkDirty();
        RequestRender?.Invoke(this, EventArgs.Empty);
    }

    // ── Drop / paste external content (files, images, text) onto the page ───────────────

    private static bool IsImageExt(string e) => e is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
    private static bool IsVideoExt(string e) => e is ".mp4" or ".mov" or ".webm" or ".avi" or ".wmv" or ".m4v";
    private static bool IsAudioExt(string e) => e is ".mp3" or ".m4a" or ".wav" or ".ogg" or ".flac" or ".aac";

    public static bool IsSupportedMediaFile(string path)
    {
        var e = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return IsImageExt(e) || IsVideoExt(e) || IsAudioExt(e);
    }

    /// <summary>Import a file into assets and drop the right element (image / video / audio) at a point.</summary>
    public void InsertFile(string sourceFilePath, double pageX, double pageY)
    {
        if (Document?.SelectedPage is null) return;
        var ext = System.IO.Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (!IsImageExt(ext) && !IsVideoExt(ext) && !IsAudioExt(ext))
        {
            ReportDiagnostic("不支持的文件类型（仅图片 / 视频 / 音频）");
            return;
        }
        var rel = ImportAsset(sourceFilePath);
        if (rel is null) return;

        ElementModel em;
        if (IsImageExt(ext))
        {
            em = new ElementModel { Type = "Image", Width = 360, Height = 240 };
            em.Props["Source"] = $"ms-fcourse:///{rel}";
            em.Props["Stretch"] = "Uniform";
        }
        else
        {
            var audio = IsAudioExt(ext);
            em = new ElementModel { Type = "MediaPlayerElement", Width = audio ? 420 : 480, Height = audio ? 64 : 270 };
            em.Props["Source"] = $"ms-fcourse:///{rel}";
            em.Props["AreTransportControlsEnabled"] = "True";
            em.Props["fc:Media.Kind"] = audio ? "Audio" : "Video";   // 往返区分音/视频（运行时忽略）
        }
        em.X = Math.Round(Math.Max(0, pageX - em.Width / 2));
        em.Y = Math.Round(Math.Max(0, pageY - em.Height / 2));

        var root = EnsureContainerRoot(Document.SelectedPage);
        var idx = root.Children.Count;
        root.Children.Add(em);
        PushUndo(new InsertElementCommand(root.Children, em, idx, RaiseRenderRequest, "插入媒体"));
        MarkDirty();
        RequestRender?.Invoke(this, EventArgs.Empty);
        SelectedElement = em;
    }

    /// <summary>Drop a block of text onto the page as a TextBlock.</summary>
    public void InsertText(string text, double pageX, double pageY)
    {
        if (Document?.SelectedPage is null || string.IsNullOrWhiteSpace(text)) return;
        var em = new ElementModel { Type = "TextBlock", Width = 480, Height = 0 };
        em.Props["Text"] = text;   // 不截断 —— 长文本由自动换行/滚动自行处理
        em.Props["FontSize"] = "24";
        em.Props["Foreground"] = "#FFFFFFFF";
        em.Props["TextWrapping"] = "Wrap";
        em.X = Math.Round(Math.Max(0, pageX - 240));
        em.Y = Math.Round(Math.Max(0, pageY - 24));

        var root = EnsureContainerRoot(Document.SelectedPage);
        var idx = root.Children.Count;
        root.Children.Add(em);
        PushUndo(new InsertElementCommand(root.Children, em, idx, RaiseRenderRequest, "插入文字"));
        MarkDirty();
        RequestRender?.Invoke(this, EventArgs.Empty);
        SelectedElement = em;
    }

    public void DeleteElement(ElementModel? em)
    {
        if (em is null || Document?.SelectedPage is null) return;
        var page = Document.SelectedPage;

        // Search page.Elements and all children recursively
        var (parentList, idx) = FindInTree(page.Elements, em);
        if (parentList is null || idx < 0) return;

        parentList.RemoveAt(idx);
        PushUndo(new DeleteElementCommand(parentList, em, idx, RaiseRenderRequest, $"删除 {em.Type}"));
        if (SelectedElement == em) SelectedElement = null;
        MarkDirty();
        RequestRender?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteSelected()
    {
        var selection = ActiveSelection();
        if (selection.Count <= 1)
        {
            DeleteElement(selection.FirstOrDefault());
            return;
        }
        if (Document?.SelectedPage is null) return;

        var located = selection
            .Select(e => (Element: e, Found: FindInTree(Document.SelectedPage.Elements, e)))
            .Where(x => x.Found.list is not null && x.Found.idx >= 0)
            .ToList();
        if (located.Count == 0) return;

        void Apply()
        {
            foreach (var group in located.GroupBy(x => x.Found.list!))
            {
                foreach (var item in group.OrderByDescending(x => x.Found.idx))
                    if (group.Key.Contains(item.Element)) group.Key.Remove(item.Element);
            }
            SetSelection(null);
        }

        void Revert()
        {
            foreach (var group in located.GroupBy(x => x.Found.list!))
            {
                foreach (var item in group.OrderBy(x => x.Found.idx))
                    if (!group.Key.Contains(item.Element))
                        group.Key.Insert(Math.Min(item.Found.idx, group.Key.Count), item.Element);
            }
            SetSelection(located.Last().Element);
        }

        Apply();
        PushUndo(new DelegateUndoCommand(
            () => { Revert(); RaiseRenderRequest(); },
            () => { Apply(); RaiseRenderRequest(); },
            "删除多个对象"));
        MarkDirty();
        StatusMessage = $"已删除 {located.Count} 个对象";
        RequestRender?.Invoke(this, EventArgs.Empty);
    }

    private static (ObservableCollection<ElementModel>? list, int idx)
        FindInTree(ObservableCollection<ElementModel> list, ElementModel target)
    {
        var idx = list.IndexOf(target);
        if (idx >= 0) return (list, idx);
        foreach (var em in list)
        {
            var (found, i) = FindInTree(em.Children, target);
            if (found is not null) return (found, i);
        }
        return (null, -1);
    }

    public void MoveElement(ElementModel em, double oldX, double oldY, double newX, double newY)
    {
        PushUndo(new MoveCommand(em, oldX, oldY, newX, newY, RaiseRenderRequest));
        MarkDirty();
    }

    public void AlignSelected(string mode)
    {
        var selection = ActiveSelection();
        if (selection.Count > 1)
        {
            AlignSelection(selection, mode);
            return;
        }

        var em = SelectedElement;
        if (em is null || Document?.SelectedPage is null)
        {
            StatusMessage = "请选择一个对象再对齐";
            return;
        }

        var stageW = Document.Manifest.Stage.DesignWidth;
        var stageH = Document.Manifest.Stage.DesignHeight;
        var w = em.Width > 0 ? em.Width : ElementModel.GetDefaultWidth(em.Type);
        var h = em.Height > 0 ? em.Height : ElementModel.GetDefaultHeight(em.Type);
        var oldX = em.X;
        var oldY = em.Y;
        var newX = oldX;
        var newY = oldY;
        string label;

        switch (mode)
        {
            case "left":
                newX = 0;
                label = "左对齐";
                break;
            case "hcenter":
                newX = Math.Max(0, (stageW - w) / 2);
                label = "水平居中";
                break;
            case "right":
                newX = Math.Max(0, stageW - w);
                label = "右对齐";
                break;
            case "top":
                newY = 0;
                label = "顶端对齐";
                break;
            case "vcenter":
                newY = Math.Max(0, (stageH - h) / 2);
                label = "垂直居中";
                break;
            case "bottom":
                newY = Math.Max(0, stageH - h);
                label = "底端对齐";
                break;
            default:
                return;
        }

        newX = Math.Round(newX, 2);
        newY = Math.Round(newY, 2);
        if (Math.Abs(newX - oldX) < 0.001 && Math.Abs(newY - oldY) < 0.001)
        {
            StatusMessage = $"{label}: 对象已在目标位置";
            return;
        }

        em.X = newX;
        em.Y = newY;
        PushUndo(new MoveCommand(em, oldX, oldY, newX, newY, RaiseRenderRequest, label));
        MarkDirty();
        StatusMessage = $"{label}: {em.Type}";
        RaiseRenderRequest();
    }

    public void DistributeSelected(string axis)
    {
        var selection = ActiveSelection();
        if (selection.Count < 3)
        {
            StatusMessage = "分布至少需要选择 3 个对象";
            return;
        }

        var boxes = selection.Select(e => new
        {
            Element = e,
            X = e.X,
            Y = e.Y,
            Width = ElementWidth(e),
            Height = ElementHeight(e)
        }).ToList();

        List<(ElementModel Element, double OldX, double OldY, double NewX, double NewY)> moves = new();
        string label;
        if (axis == "horizontal")
        {
            var ordered = boxes.OrderBy(b => b.X).ToList();
            var left = ordered.Min(b => b.X);
            var right = ordered.Max(b => b.X + b.Width);
            var total = ordered.Sum(b => b.Width);
            var gap = (right - left - total) / (ordered.Count - 1);
            var cursor = left;
            foreach (var b in ordered)
            {
                moves.Add((b.Element, b.X, b.Y, Math.Round(cursor, 2), b.Y));
                cursor += b.Width + gap;
            }
            label = "水平分布";
        }
        else if (axis == "vertical")
        {
            var ordered = boxes.OrderBy(b => b.Y).ToList();
            var top = ordered.Min(b => b.Y);
            var bottom = ordered.Max(b => b.Y + b.Height);
            var total = ordered.Sum(b => b.Height);
            var gap = (bottom - top - total) / (ordered.Count - 1);
            var cursor = top;
            foreach (var b in ordered)
            {
                moves.Add((b.Element, b.X, b.Y, b.X, Math.Round(cursor, 2)));
                cursor += b.Height + gap;
            }
            label = "垂直分布";
        }
        else return;

        ApplyBatchMove(moves, label);
    }

    public void GroupSelected()
    {
        if (Document?.SelectedPage is null)
        {
            StatusMessage = "没有可组合的页面";
            return;
        }

        var pageRoot = Document.SelectedPage.Elements.FirstOrDefault();
        var selected = ActiveSelection().Where(e => !ReferenceEquals(e, pageRoot)).Distinct().ToList();
        if (selected.Count < 2)
        {
            StatusMessage = "组合至少需要选择 2 个对象";
            return;
        }

        var located = selected
            .Select(e => (Element: e, Found: FindInTree(Document.SelectedPage.Elements, e)))
            .ToList();
        if (located.Any(x => x.Found.list is null || x.Found.idx < 0))
            return;

        var parent = located[0].Found.list!;
        if (located.Any(x => !ReferenceEquals(x.Found.list, parent)))
        {
            StatusMessage = "只能组合同一层级内的对象";
            return;
        }

        var ordered = located
            .Select(x => (x.Element, Index: x.Found.idx, OldX: x.Element.X, OldY: x.Element.Y))
            .OrderBy(x => x.Index)
            .ToList();
        var bounds = SelectionBounds(selected);
        var group = new ElementModel
        {
            Type = "Canvas",
            X = bounds.Left,
            Y = bounds.Top,
            Width = Math.Max(1, bounds.Right - bounds.Left),
            Height = Math.Max(1, bounds.Bottom - bounds.Top)
        };
        group.Props["Tag"] = "FluentCourse.Group";
        var insertAt = ordered.Min(x => x.Index);

        void Apply()
        {
            foreach (var item in ordered.OrderByDescending(x => x.Index))
                if (parent.Contains(item.Element)) parent.Remove(item.Element);
            group.Children.Clear();
            foreach (var item in ordered)
            {
                item.Element.X = Math.Round(item.OldX - bounds.Left, 2);
                item.Element.Y = Math.Round(item.OldY - bounds.Top, 2);
                group.Children.Add(item.Element);
            }
            if (!parent.Contains(group)) parent.Insert(Math.Min(insertAt, parent.Count), group);
            SetSelection(group);
        }

        void Revert()
        {
            parent.Remove(group);
            group.Children.Clear();
            foreach (var item in ordered)
            {
                item.Element.X = item.OldX;
                item.Element.Y = item.OldY;
                parent.Insert(Math.Min(item.Index, parent.Count), item.Element);
            }
            SetSelection(ordered.Last().Element);
        }

        Apply();
        PushUndo(new DelegateUndoCommand(
            () => { Revert(); RaiseRenderRequest(); },
            () => { Apply(); RaiseRenderRequest(); },
            "组合"));
        MarkDirty();
        StatusMessage = $"已组合 {ordered.Count} 个对象";
        RaiseRenderRequest();
    }

    public void UngroupSelected()
    {
        if (Document?.SelectedPage is null || SelectedElement is null)
        {
            StatusMessage = "请选择一个组合";
            return;
        }

        var group = SelectedElement;
        if (group.Children.Count == 0 || ReferenceEquals(group, Document.SelectedPage.Elements.FirstOrDefault()))
        {
            StatusMessage = "当前对象不能取消组合";
            return;
        }

        var (parent, index) = FindInTree(Document.SelectedPage.Elements, group);
        if (parent is null || index < 0) return;

        var children = group.Children.ToList();
        var relative = children.Select(c => (Element: c, X: c.X, Y: c.Y)).ToList();

        void Apply()
        {
            parent.Remove(group);
            group.Children.Clear();
            for (int i = 0; i < relative.Count; i++)
            {
                var item = relative[i];
                item.Element.X = Math.Round(group.X + item.X, 2);
                item.Element.Y = Math.Round(group.Y + item.Y, 2);
                parent.Insert(Math.Min(index + i, parent.Count), item.Element);
            }
            SetSelection(relative.Last().Element);
        }

        void Revert()
        {
            foreach (var item in relative)
                parent.Remove(item.Element);
            group.Children.Clear();
            foreach (var item in relative)
            {
                item.Element.X = item.X;
                item.Element.Y = item.Y;
                group.Children.Add(item.Element);
            }
            parent.Insert(Math.Min(index, parent.Count), group);
            SetSelection(group);
        }

        Apply();
        PushUndo(new DelegateUndoCommand(
            () => { Revert(); RaiseRenderRequest(); },
            () => { Apply(); RaiseRenderRequest(); },
            "取消组合"));
        MarkDirty();
        StatusMessage = $"已取消组合 {children.Count} 个对象";
        RaiseRenderRequest();
    }

    private IReadOnlyList<ElementModel> ActiveSelection()
    {
        if (SelectedElements.Count > 0) return SelectedElements.ToList();
        return SelectedElement is null ? Array.Empty<ElementModel>() : new[] { SelectedElement };
    }

    private static double ElementWidth(ElementModel em) =>
        em.Width > 0 ? em.Width : ElementModel.GetDefaultWidth(em.Type);

    private static double ElementHeight(ElementModel em) =>
        em.Height > 0 ? em.Height : ElementModel.GetDefaultHeight(em.Type);

    private static (double Left, double Top, double Right, double Bottom) SelectionBounds(IEnumerable<ElementModel> elements)
    {
        var items = elements.ToList();
        return (
            items.Min(e => e.X),
            items.Min(e => e.Y),
            items.Max(e => e.X + ElementWidth(e)),
            items.Max(e => e.Y + ElementHeight(e)));
    }

    private void AlignSelection(IReadOnlyList<ElementModel> selection, string mode)
    {
        var bounds = SelectionBounds(selection);
        var moves = new List<(ElementModel Element, double OldX, double OldY, double NewX, double NewY)>();
        var label = mode switch
        {
            "left" => "左对齐",
            "hcenter" => "水平居中",
            "right" => "右对齐",
            "top" => "顶端对齐",
            "vcenter" => "垂直居中",
            "bottom" => "底端对齐",
            _ => null
        };
        if (label is null)
        {
            return;
        }

        foreach (var em in selection)
        {
            var newX = em.X;
            var newY = em.Y;
            var w = ElementWidth(em);
            var h = ElementHeight(em);
            switch (mode)
            {
                case "left":
                    newX = bounds.Left;
                    break;
                case "hcenter":
                    newX = bounds.Left + (bounds.Right - bounds.Left - w) / 2;
                    break;
                case "right":
                    newX = bounds.Right - w;
                    break;
                case "top":
                    newY = bounds.Top;
                    break;
                case "vcenter":
                    newY = bounds.Top + (bounds.Bottom - bounds.Top - h) / 2;
                    break;
                case "bottom":
                    newY = bounds.Bottom - h;
                    break;
                default:
                    return;
            }
            moves.Add((em, em.X, em.Y, Math.Round(newX, 2), Math.Round(newY, 2)));
        }

        ApplyBatchMove(moves, label);
    }

    private void ApplyBatchMove(
        IReadOnlyList<(ElementModel Element, double OldX, double OldY, double NewX, double NewY)> moves,
        string description)
    {
        var changed = moves
            .Where(m => Math.Abs(m.NewX - m.OldX) >= 0.001 || Math.Abs(m.NewY - m.OldY) >= 0.001)
            .ToList();
        if (changed.Count == 0)
        {
            StatusMessage = $"{description}: 对象已在目标位置";
            return;
        }

        void Apply()
        {
            foreach (var m in changed)
            {
                m.Element.X = m.NewX;
                m.Element.Y = m.NewY;
            }
        }

        void Revert()
        {
            foreach (var m in changed)
            {
                m.Element.X = m.OldX;
                m.Element.Y = m.OldY;
            }
        }

        Apply();
        PushUndo(new DelegateUndoCommand(
            () => { Revert(); RaiseRenderRequest(); },
            () => { Apply(); RaiseRenderRequest(); },
            description));
        MarkDirty();
        StatusMessage = $"{description}: {changed.Count} 个对象";
        RaiseRenderRequest();
    }

    /// <summary>Nudge the selected element by (dx,dy) — used by the arrow keys.</summary>
    public void NudgeSelected(double dx, double dy)
    {
        var em = SelectedElement;
        if (em is null) return;
        var oldX = em.X; var oldY = em.Y;
        em.X = Math.Max(0, oldX + dx);
        em.Y = Math.Max(0, oldY + dy);
        if (em.X == oldX && em.Y == oldY) return;
        PushUndo(new MoveCommand(em, oldX, oldY, em.X, em.Y, RaiseRenderRequest));
        MarkDirty();
        RaiseRenderRequest();
    }

    public void ResizeElement(ElementModel em,
        double oldX, double oldY, double oldW, double oldH,
        double newX, double newY, double newW, double newH)
    {
        PushUndo(new ResizeCommand(em, oldX, oldY, oldW, oldH, newX, newY, newW, newH, RaiseRenderRequest));
        MarkDirty();
    }

    public void ChangeProperty(ElementModel em, string propKey, string? oldValue, string? newValue)
    {
        PushUndo(new PropertyChangeCommand(em, propKey, oldValue, newValue, RaiseRenderRequest));
        MarkDirty();
    }

    public void RotateElement(ElementModel em, double oldAngle, double newAngle)
    {
        PushUndo(new RotateCommand(em, oldAngle, newAngle, RaiseRenderRequest));
        MarkDirty();
    }

    /// <summary>
    /// Change stacking order by reordering the element within its sibling list (later siblings
    /// render on top). More reliable than ZIndex, which is 0 for everything by default.
    /// op: "front" | "back" | "forward" | "backward".
    /// </summary>
    public void ReorderElement(ElementModel em, string op)
    {
        if (Document?.SelectedPage is null) return;
        var (list, idx) = FindInTree(Document.SelectedPage.Elements, em);
        if (list is null || idx < 0) return;

        // Stacking is now decided purely by sibling order, so clear any leftover ZIndex values
        // (e.g. an earlier "置顶" that set 9999) that would otherwise override the order.
        foreach (var sib in list) sib.ZIndex = 0;

        int last = list.Count - 1;
        int to = op switch
        {
            "front"    => idx < last ? last : idx,
            "back"     => idx > 0 ? 0 : idx,
            "forward"  => idx < last ? idx + 1 : idx,
            "backward" => idx > 0 ? idx - 1 : idx,
            _ => idx
        };
        if (to == idx) return;
        list.Move(idx, to);
        PushUndo(new CollectionMoveCommand<ElementModel>(list, idx, to, RaiseRenderRequest, "调整层级"));

        MarkDirty();
        RaiseRenderRequest();
    }

    // ── Clipboard: copy / cut / paste / duplicate ──────────────────────────────────────
    // An in-app clipboard holding a detached clone. Names are stripped on paste/duplicate so a
    // copy never collides with the original's x:Name (which triggers/FindName resolve by).

    private ElementModel? _clipboard;
    public bool HasClipboard => _clipboard is not null;

    public void CopyElement(ElementModel? em)
    {
        if (em is not null) _clipboard = em.Clone();
    }

    public void CutElement(ElementModel? em)
    {
        if (em is null) return;
        _clipboard = em.Clone();
        DeleteElement(em);
    }

    /// <summary>Paste the clipboard clone into the page (offset from original, or at a point).</summary>
    public ElementModel? PasteElement(double? atX = null, double? atY = null)
    {
        if (_clipboard is null || Document?.SelectedPage is null) return null;
        var page = Document.SelectedPage;

        var clone = _clipboard.Clone();
        ClearNamesRecursive(clone);
        if (atX.HasValue && atY.HasValue) { clone.X = Math.Max(0, atX.Value); clone.Y = Math.Max(0, atY.Value); }
        else { clone.X += 24; clone.Y += 24; }

        var root = EnsureContainerRoot(page);
        var idx = root.Children.Count;
        root.Children.Add(clone);
        PushUndo(new InsertElementCommand(root.Children, clone, idx, RaiseRenderRequest, "粘贴"));
        MarkDirty();
        RequestRender?.Invoke(this, EventArgs.Empty);
        SelectedElement = clone;
        return clone;
    }

    /// <summary>Duplicate an element in place (clone inserted as the next sibling, slightly offset).</summary>
    public ElementModel? DuplicateElement(ElementModel? em)
    {
        if (em is null || Document?.SelectedPage is null) return null;
        var (list, idx) = FindInTree(Document.SelectedPage.Elements, em);
        if (list is null || idx < 0) return null;

        var clone = em.Clone();
        ClearNamesRecursive(clone);
        clone.X += 24; clone.Y += 24;
        var insertAt = idx + 1;
        list.Insert(insertAt, clone);
        PushUndo(new InsertElementCommand(list, clone, insertAt, RaiseRenderRequest, "再制"));
        MarkDirty();
        RequestRender?.Invoke(this, EventArgs.Empty);
        SelectedElement = clone;
        return clone;
    }

    private static void ClearNamesRecursive(ElementModel em)
    {
        em.Name = null;
        foreach (var c in em.Children) ClearNamesRecursive(c);
    }

    // ── Button click-action binding (teacher-friendly; no trigger panel needed) ─────────
    // Generates/updates a Click TriggerDescriptor for the button. show/hide use SetProperty
    // Visibility; toggle is a 2-step wrapping trigger; anim plays a preset animation.

    /// <summary>触发器是否属于当前页：Page 字段匹配当前页 id；没写 Page 的旧触发器视为全局也算匹配，
    /// 否则编辑器没法管理它们。配合全课件唯一命名，两页同名按钮不会再互相串扰。</summary>
    private bool TriggerOnCurrentPage(TriggerDescriptor t)
    {
        var pageId = Document?.SelectedPage?.Descriptor.Id;
        return string.IsNullOrEmpty(t.Page) || t.Page == pageId;
    }

    public ButtonActionInfo GetButtonAction(ElementModel button)
    {
        var info = new ButtonActionInfo();
        if (Document?.SelectedPage is null || string.IsNullOrEmpty(button.Name)) return info;
        var trig = Document.Triggers.FirstOrDefault(t =>
            t.On.Type == "Click" && t.On.Target == button.Name && TriggerOnCurrentPage(t));
        if (trig is null) return info;

        if (trig.Steps.Count >= 2)
        {
            info.Kind = "toggle";
            info.Target = FindElementByName(trig.Steps[0].FirstOrDefault()?.Target);
            return info;
        }
        var a = trig.Do.FirstOrDefault();
        if (a is null) return info;
        switch (a.Type)
        {
            case "SetProperty":
                info.Kind = (a.GetDouble("value") ?? 1.0) > 0.5 ? "show" : "hide";
                info.Target = FindElementByName(a.Target);
                break;
            case "PlayAnimation":
                info.Kind = "anim";
                info.Target = FindElementByName(a.Target);
                info.Animation = a.GetString("animation") ?? "FadeIn";
                break;
            case "NextPage":     info.Kind = "nextpage"; break;
            case "PreviousPage": info.Kind = "prevpage"; break;
        }
        return info;
    }

    public void SetButtonAction(ElementModel button, string kind, ElementModel? target, string animation)
    {
        if (Document?.SelectedPage is null) return;
        EnsureUniqueName(button);

        // Drop any existing click trigger for this button —— 只删属于当前页的，别误删其他页同名按钮的。
        for (int i = Document.Triggers.Count - 1; i >= 0; i--)
            if (Document.Triggers[i].On.Type == "Click" && Document.Triggers[i].On.Target == button.Name &&
                TriggerOnCurrentPage(Document.Triggers[i]))
                Document.Triggers.RemoveAt(i);

        bool needsTarget = kind is "show" or "hide" or "toggle" or "anim";
        if (needsTarget)
        {
            if (target is null) { MarkDirty(); RequestRefreshTriggers?.Invoke(this, EventArgs.Empty); return; }
            EnsureUniqueName(target);
        }

        if (kind != "none")
        {
            var trig = new TriggerDescriptor
            {
                Id = "t_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Page = Document.SelectedPage.Descriptor.Id,
                On = new TriggerEvent { Type = "Click", Target = button.Name }
            };
            switch (kind)
            {
                case "show": trig.Do.Add(VisAction(target!.Name!, true)); break;
                case "hide": trig.Do.Add(VisAction(target!.Name!, false)); break;
                case "toggle":
                    trig.Steps.Add(new List<ActionDescriptor> { VisAction(target!.Name!, true) });
                    trig.Steps.Add(new List<ActionDescriptor> { VisAction(target!.Name!, false) });
                    trig.StopAfterLastStep = false;
                    break;
                case "anim":
                    var pa = new ActionDescriptor { Type = "PlayAnimation", Target = target!.Name };
                    pa.Parameters["animation"] = JsonSerializer.SerializeToElement(animation);
                    trig.Do.Add(pa);
                    break;
                case "prevpage": trig.Do.Add(new ActionDescriptor { Type = "PreviousPage" }); break;
                case "nextpage": trig.Do.Add(new ActionDescriptor { Type = "NextPage" }); break;
            }
            Document.Triggers.Add(trig);
        }

        MarkDirty();
        RaiseRenderRequest();
        RequestRefreshTriggers?.Invoke(this, EventArgs.Empty);
    }

    // Show/hide via Opacity (not Visibility) so it lines up with the 透明度 slider in the panel:
    // a teacher sets a target's 透明度 to 0 to "start hidden", and the button fades it to 1.
    private static ActionDescriptor VisAction(string target, bool visible)
    {
        var a = new ActionDescriptor { Type = "SetProperty", Target = target };
        a.Parameters["property"] = JsonSerializer.SerializeToElement("Opacity");
        a.Parameters["value"] = JsonSerializer.SerializeToElement(visible ? 1.0 : 0.0);
        return a;
    }

    public ElementModel? FindElementByName(string? name)
    {
        if (string.IsNullOrEmpty(name) || Document?.SelectedPage is null) return null;
        return FindByNameRec(Document.SelectedPage.Elements, name!);
    }

    private static ElementModel? FindByNameRec(IEnumerable<ElementModel> list, string name)
    {
        foreach (var e in list)
        {
            if (e.Name == name) return e;
            var f = FindByNameRec(e.Children, name);
            if (f != null) return f;
        }
        return null;
    }

    /// <summary>Elements a button can target — visible page elements minus the page root and
    /// quiz/text sub-items.</summary>
    public List<ElementModel> TargetableElements(ElementModel? exclude)
    {
        var result = new List<ElementModel>();
        if (Document?.SelectedPage is null) return result;
        var roots = Document.SelectedPage.Elements;

        void Walk(IEnumerable<ElementModel> list)
        {
            foreach (var e in list)
            {
                if (e.Type is "QuizChoice" or "QuizBlank" or "Run" or "Paragraph") continue;
                if (!ReferenceEquals(e, exclude)) result.Add(e);
                Walk(e.Children);
            }
        }
        if (roots.Count > 0) Walk(roots[0].Children);
        return result;
    }

    /// <summary>Public: give an element a unique x:Name if it doesn't have one (for animations/triggers).</summary>
    public void AssignNameIfMissing(ElementModel em) => EnsureUniqueName(em);

    private void EnsureUniqueName(ElementModel em)
    {
        if (!string.IsNullOrEmpty(em.Name)) return;
        // 收集整个课件的名字而不只是当前页：触发器按名字找元素，跨页重名会互相串扰。
        var used = new HashSet<string>();
        if (Document != null)
            foreach (var p in Document.Pages)
                CollectNames(p.Elements, used);
        var b = em.Type switch
        {
            "Button"     => "btn",
            "TextBlock"  => "text",
            "Image"      => "img",
            "QuizControl" => "quiz",
            "Rectangle"  => "rect",
            "Ellipse"    => "oval",
            "Polygon"    => "shape",
            "MediaPlayerElement" => "video",
            _            => "el"
        };
        int i = 1; string name;
        do { name = b + i++; } while (used.Contains(name));
        em.Name = name;
    }

    private static void CollectNames(IEnumerable<ElementModel> list, HashSet<string> used)
    {
        foreach (var e in list)
        {
            if (!string.IsNullOrEmpty(e.Name)) used.Add(e.Name!);
            CollectNames(e.Children, used);
        }
    }

    private static bool IsContainer(string type) =>
        type is "Canvas" or "Grid" or "StackPanel";

    private static ElementModel CreateElementFromToolbox(ToolboxItem item)
    {
        // Questions build a whole subtree (QuizControl + choices/blanks).
        switch (item.Label)
        {
            case "QuizSingle":    return BuildQuiz(item, "Single");
            case "QuizMultiple":  return BuildQuiz(item, "Multiple");
            case "QuizFill":      return BuildQuiz(item, "FillInBlank");
            case "QuizTrueFalse": return BuildQuiz(item, "TrueFalse");
            case "Table":         return BuildTable(item);
            case "BarChart":      return BuildBarChart(item);
            case "FlowSmart":     return BuildFlowSmartArt(item);
        }

        var em = new ElementModel
        {
            X = 100,
            Y = 100,
            Width = item.DefaultWidth,
            Height = item.DefaultHeight,
            Type = "TextBlock"
        };
        var w = item.DefaultWidth;
        var h = item.DefaultHeight;

        switch (item.Label)
        {
            case "Heading":
                em.Type = "TextBlock";
                em.Props["Text"] = "大标题";
                em.Props["FontSize"] = "44";
                em.Props["FontWeight"] = "Bold";
                em.Props["Foreground"] = "#FFFFFFFF";
                break;
            case "Body":
                em.Type = "TextBlock";
                em.Props["Text"] = "正文内容，点这里修改文字。";
                em.Props["FontSize"] = "22";
                em.Props["Foreground"] = "#FFE6E8EE";
                em.Props["TextWrapping"] = "Wrap";
                break;

            case "Image":
                em.Type = "Image";
                em.Props["Stretch"] = "Uniform";
                break;
            case "Video":
                em.Type = "MediaPlayerElement";
                em.Props["AreTransportControlsEnabled"] = "True";
                em.Props["fc:Media.Kind"] = "Video";
                break;
            case "Audio":
                em.Type = "MediaPlayerElement";
                em.Props["AreTransportControlsEnabled"] = "True";
                em.Props["fc:Media.Kind"] = "Audio";
                break;
            case "HyperlinkButton":
                em.Type = "HyperlinkButton";
                em.Props["Content"] = "点击打开链接";
                em.Props["NavigateUri"] = "https://";
                break;
            case "Countdown":
                em.Type = "CountdownControl";
                em.Props["Seconds"] = "60";
                em.Props["AutoStart"] = "True";
                break;
            case "Formula":
                em.Type = "FormulaControl";
                em.Props["Latex"] = "x = \\frac{-b \\pm \\sqrt{b^2 - 4ac}}{2a}";
                em.Props["FormulaFontSize"] = "48";
                em.Props["Color"] = "#FFFFFFFF";
                break;

            case "Rectangle":
                em.Type = "Rectangle";
                em.Props["Fill"] = "#FF0078D4";
                em.Props["Stroke"] = "#FFFFFFFF";
                em.Props["StrokeThickness"] = "2";
                em.Props["RadiusX"] = "8";
                em.Props["RadiusY"] = "8";
                break;
            case "Ellipse":
                em.Type = "Ellipse";
                em.Props["Fill"] = "#FF0078D4";
                em.Props["Stroke"] = "#FFFFFFFF";
                em.Props["StrokeThickness"] = "2";
                break;
            case "Line":
                em.Type = "Line";
                em.Props["X1"] = "0"; em.Props["Y1"] = "0";
                em.Props["X2"] = w.ToString("0"); em.Props["Y2"] = "0";
                em.Props["Stroke"] = "#FFFFFFFF";
                em.Props["StrokeThickness"] = "3";
                break;
            case "Triangle":
                em.Type = "Polygon";
                em.Props["Points"] = $"{w / 2:0},0 {w:0},{h:0} 0,{h:0}";
                em.Props["Fill"] = "#FF0078D4";
                em.Props["Stroke"] = "#FFFFFFFF";
                em.Props["StrokeThickness"] = "2";
                break;
            case "Parallelogram":
                em.Type = "Polygon";
                var sk = w * 0.22;
                em.Props["Points"] = $"{sk:0},0 {w:0},0 {w - sk:0},{h:0} 0,{h:0}";
                em.Props["Fill"] = "#FF0078D4";
                em.Props["Stroke"] = "#FFFFFFFF";
                em.Props["StrokeThickness"] = "2";
                break;
            case "Arrow":
                em.Type = "Polygon";
                double st = h * 0.30, sb = h * 0.70, hx = w * 0.62;
                em.Props["Points"] =
                    $"0,{st:0} {hx:0},{st:0} {hx:0},0 {w:0},{h / 2:0} {hx:0},{h:0} {hx:0},{sb:0} 0,{sb:0}";
                em.Props["Fill"] = "#FF0078D4";
                em.Props["Stroke"] = "#FFFFFFFF";
                em.Props["StrokeThickness"] = "2";
                break;

            case "Button":
                em.Type = "Button";
                em.Props["Content"] = "按钮";
                em.Props["Background"] = "#FF0078D4";
                em.Props["Foreground"] = "#FFFFFFFF";
                em.Props["CornerRadius"] = "6";
                break;

            default:
                em.Props["Text"] = "文本";
                em.Props["FontSize"] = "24";
                em.Props["Foreground"] = "#FFFFFFFF";
                break;
        }

        // Polygons are sized by their Points geometry; Stretch=Fill makes them scale to W/H so
        // resizing the box actually scales the shape.
        if (em.Type == "Polygon") em.Props["Stretch"] = "Fill";

        return em;
    }

    /// <summary>Build a ready-to-use question (QuizControl + choices/blanks) the teacher just edits.</summary>
    private static ElementModel BuildQuiz(ToolboxItem item, string mode)
    {
        var em = new ElementModel
        {
            X = 100,
            Y = 100,
            Width = item.DefaultWidth,
            Height = 0,                 // auto height — the question sizes to its content
            Type = "QuizControl"
        };
        em.Props["Mode"] = mode;
        em.Props["AllowRetry"] = "True";
        em.Props["ShowFeedback"] = "True";

        switch (mode)
        {
            case "FillInBlank":
                em.Props["ScoreStrategy"] = "PartialCredit";
                em.Props["Question"] = "在题干里用括号标出要填的空，例如：水的化学式是 ( ① )，由氢和 ( ② ) 组成。";
                em.Children.Add(MakeBlank("答案一"));
                em.Children.Add(MakeBlank("答案二"));
                break;
            case "TrueFalse":
                em.Props["ScoreStrategy"] = "AllOrNothing";
                em.Props["Question"] = "在这里输入判断题题干。";
                em.Children.Add(MakeChoice("正确", true));
                em.Children.Add(MakeChoice("错误", false));
                break;
            case "Multiple":
                em.Props["ScoreStrategy"] = "PartialCredit";
                em.Props["Question"] = "在这里输入多选题题干（可多选）。";
                em.Children.Add(MakeChoice("A. 选项一", true));
                em.Children.Add(MakeChoice("B. 选项二", true));
                em.Children.Add(MakeChoice("C. 选项三", false));
                em.Children.Add(MakeChoice("D. 选项四", false));
                break;
            default: // Single
                em.Props["ScoreStrategy"] = "AllOrNothing";
                em.Props["Question"] = "在这里输入单选题题干。";
                em.Children.Add(MakeChoice("A. 选项一", true));
                em.Children.Add(MakeChoice("B. 选项二", false));
                em.Children.Add(MakeChoice("C. 选项三", false));
                em.Children.Add(MakeChoice("D. 选项四", false));
                break;
        }
        return em;
    }

    private static ElementModel MakeChoice(string text, bool isAnswer)
    {
        var c = new ElementModel { Type = "QuizChoice" };
        c.Props["Text"] = text;
        if (isAnswer) c.Props["IsAnswer"] = "True";
        return c;
    }

    private static ElementModel MakeBlank(string pattern)
    {
        var b = new ElementModel { Type = "QuizBlank" };
        b.Props["Pattern"] = pattern;
        b.Props["CaseSensitive"] = "false";
        return b;
    }

    private static ElementModel BuildBarChart(ToolboxItem item)
    {
        var canvas = new ElementModel
        {
            X = 100,
            Y = 100,
            Width = item.DefaultWidth,
            Height = item.DefaultHeight,
            Type = "Canvas"
        };
        canvas.Children.Add(MakeText("柱状图标题", 0, 0, item.DefaultWidth, 42, 30, "SemiBold"));
        canvas.Children.Add(MakeLine(70, 285, 600, 285, "#FF9AA4BF", 2));
        canvas.Children.Add(MakeLine(70, 72, 70, 285, "#FF9AA4BF", 2));

        var data = new[] { ("A", 0.56, "#FF4CC2FF"), ("B", 0.82, "#FFFFD166"), ("C", 0.44, "#FF7BE495"), ("D", 0.68, "#FFFF6B8A") };
        for (int i = 0; i < data.Length; i++)
        {
            var x = 118 + i * 112;
            var h = 190 * data[i].Item2;
            canvas.Children.Add(MakeRect(x, 285 - h, 58, h, data[i].Item3));
            canvas.Children.Add(MakeText(data[i].Item1, x - 8, 296, 74, 28, 18, "SemiBold"));
        }
        return canvas;
    }

    private static ElementModel BuildFlowSmartArt(ToolboxItem item)
    {
        var canvas = new ElementModel
        {
            X = 100,
            Y = 100,
            Width = item.DefaultWidth,
            Height = item.DefaultHeight,
            Type = "Canvas"
        };
        var labels = new[] { "步骤一", "步骤二", "步骤三" };
        for (int i = 0; i < labels.Length; i++)
        {
            var x = i * 230.0;
            canvas.Children.Add(MakeRect(x, 42, 170, 92, i == 1 ? "#FFFFD166" : "#FF4CC2FF"));
            canvas.Children.Add(MakeText(labels[i], x + 20, 68, 130, 42, 24, "SemiBold"));
            if (i < labels.Length - 1)
                canvas.Children.Add(MakeArrow(x + 178, 82, 62, 22));
        }
        return canvas;
    }

    private static ElementModel MakeText(string text, double x, double y, double w, double h, double size, string weight)
    {
        var em = new ElementModel { Type = "TextBlock", X = x, Y = y, Width = w, Height = h };
        em.Props["Text"] = text;
        em.Props["FontSize"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        em.Props["FontWeight"] = weight;
        em.Props["Foreground"] = "#FFFFFFFF";
        em.Props["TextAlignment"] = "Center";
        em.Props["TextWrapping"] = "Wrap";
        return em;
    }

    private static ElementModel MakeRect(double x, double y, double w, double h, string fill)
    {
        var em = new ElementModel { Type = "Rectangle", X = x, Y = y, Width = w, Height = h };
        em.Props["Fill"] = fill;
        em.Props["RadiusX"] = "8";
        em.Props["RadiusY"] = "8";
        return em;
    }

    private static ElementModel MakeLine(double x1, double y1, double x2, double y2, string stroke, double thickness)
    {
        var em = new ElementModel { Type = "Line", Width = Math.Abs(x2 - x1), Height = Math.Abs(y2 - y1) };
        em.Props["X1"] = x1.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        em.Props["Y1"] = y1.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        em.Props["X2"] = x2.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        em.Props["Y2"] = y2.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        em.Props["Stroke"] = stroke;
        em.Props["StrokeThickness"] = thickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return em;
    }

    private static ElementModel MakeArrow(double x, double y, double w, double h)
    {
        var em = new ElementModel { Type = "Polygon", X = x, Y = y, Width = w, Height = h };
        em.Props["Points"] = $"0,{h * 0.25:0} {w * 0.62:0},{h * 0.25:0} {w * 0.62:0},0 {w:0},{h / 2:0} {w * 0.62:0},{h:0} {w * 0.62:0},{h * 0.75:0} 0,{h * 0.75:0}";
        em.Props["Fill"] = "#FFFFFFFF";
        em.Props["Stretch"] = "Fill";
        return em;
    }

    /// <summary>A 3×3 table = a Grid of bordered cells, each holding an editable TextBlock.</summary>
    private static ElementModel BuildTable(ToolboxItem item)
    {
        const int rows = 3, cols = 3;
        var grid = new ElementModel
        {
            X = 100, Y = 100,
            Width = item.DefaultWidth,
            Height = item.DefaultHeight,
            Type = "Grid"
        };
        grid.Props["RowDefinitions"] = string.Join(";", Enumerable.Repeat("*", rows));
        grid.Props["ColumnDefinitions"] = string.Join(";", Enumerable.Repeat("*", cols));

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                grid.Children.Add(MakeTableCell(r, c));
        return grid;
    }

    /// <summary>One table cell: a bordered Border holding a centered editable TextBlock. Header row
    /// (r==0) is darker/bold. Grid.Row/Column omitted when 0 (matches what XamlSerializer writes).</summary>
    private static ElementModel MakeTableCell(int r, int c)
    {
        var cell = new ElementModel { Type = "Border" };
        cell.Props["BorderBrush"] = "#FF9AA4BF";
        cell.Props["BorderThickness"] = "1";
        cell.Props["Background"] = r == 0 ? "#33000000" : "#11FFFFFF";
        if (r > 0) cell.Props["Grid.Row"] = r.ToString();
        if (c > 0) cell.Props["Grid.Column"] = c.ToString();

        var tb = new ElementModel { Type = "TextBlock" };
        tb.Props["Text"] = r == 0 ? $"标题{c + 1}" : "文字";
        tb.Props["FontSize"] = "20";
        tb.Props["FontWeight"] = r == 0 ? "SemiBold" : "Normal";
        tb.Props["Foreground"] = "#FFFFFFFF";
        tb.Props["HorizontalAlignment"] = "Center";
        tb.Props["VerticalAlignment"] = "Center";
        cell.Children.Add(tb);
        return cell;
    }

    // ── Table & quiz structure edits (driven by the right-click context menu) ───────────

    public bool IsTable(ElementModel? em) =>
        em is { Type: "Grid" } && em.Props.ContainsKey("RowDefinitions");

    /// <summary>Mode of a 题目 ("Single"/"Multiple"/"TrueFalse"/"FillInBlank"), or "" if not a quiz.</summary>
    public string QuizMode(ElementModel? em) =>
        em is { Type: "QuizControl" } && em.Props.TryGetValue("Mode", out var m) ? m : "";

    private static int StarCount(ElementModel grid, string key) =>
        grid.Props.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v.Split(';').Length : 0;
    private static int CellRow(ElementModel cell) =>
        cell.Props.TryGetValue("Grid.Row", out var v) && int.TryParse(v, out var r) ? r : 0;
    private static int CellCol(ElementModel cell) =>
        cell.Props.TryGetValue("Grid.Column", out var v) && int.TryParse(v, out var c) ? c : 0;

    public void TableAddRow(ElementModel grid)
    {
        if (!IsTable(grid)) return;
        int rows = StarCount(grid, "RowDefinitions"), cols = StarCount(grid, "ColumnDefinitions");
        var oldDefs = grid.Props["RowDefinitions"];
        var newDefs = string.Join(";", Enumerable.Repeat("*", rows + 1));
        var oldH = grid.Height;
        var newH = grid.Height > 0 && rows > 0 ? Math.Round(grid.Height * (rows + 1) / rows) : grid.Height;
        var cells = Enumerable.Range(0, cols).Select(c => MakeTableCell(rows, c)).ToList();

        void Apply() { grid.Props["RowDefinitions"] = newDefs; foreach (var c in cells) grid.Children.Add(c); grid.Height = newH; }
        void Revert() { foreach (var c in cells) grid.Children.Remove(c); grid.Props["RowDefinitions"] = oldDefs; grid.Height = oldH; }
        Apply();
        PushUndo(new DelegateUndoCommand(
            () => { Revert(); RaiseRenderRequest(); },
            () => { Apply(); RaiseRenderRequest(); },
            "表格增加一行"));
        StructureEdited();
    }

    public void TableRemoveRow(ElementModel grid)
    {
        if (!IsTable(grid)) return;
        int rows = StarCount(grid, "RowDefinitions");
        if (rows <= 1) return;
        int last = rows - 1;
        var oldDefs = grid.Props["RowDefinitions"];
        var newDefs = string.Join(";", Enumerable.Repeat("*", rows - 1));
        var oldH = grid.Height;
        var newH = grid.Height > 0 ? Math.Round(grid.Height * (rows - 1) / rows) : grid.Height;
        var cells = grid.Children.Where(ch => CellRow(ch) == last).ToList();

        void Apply() { foreach (var c in cells) grid.Children.Remove(c); grid.Props["RowDefinitions"] = newDefs; grid.Height = newH; }
        void Revert() { grid.Props["RowDefinitions"] = oldDefs; foreach (var c in cells) grid.Children.Add(c); grid.Height = oldH; }
        Apply();
        PushUndo(new DelegateUndoCommand(
            () => { Revert(); RaiseRenderRequest(); },
            () => { Apply(); RaiseRenderRequest(); },
            "表格删除一行"));
        StructureEdited();
    }

    public void TableAddColumn(ElementModel grid)
    {
        if (!IsTable(grid)) return;
        int rows = StarCount(grid, "RowDefinitions"), cols = StarCount(grid, "ColumnDefinitions");
        var oldDefs = grid.Props["ColumnDefinitions"];
        var newDefs = string.Join(";", Enumerable.Repeat("*", cols + 1));
        var oldW = grid.Width;
        var newW = grid.Width > 0 && cols > 0 ? Math.Round(grid.Width * (cols + 1) / cols) : grid.Width;
        var cells = Enumerable.Range(0, rows).Select(r => MakeTableCell(r, cols)).ToList();

        void Apply() { grid.Props["ColumnDefinitions"] = newDefs; foreach (var c in cells) grid.Children.Add(c); grid.Width = newW; }
        void Revert() { foreach (var c in cells) grid.Children.Remove(c); grid.Props["ColumnDefinitions"] = oldDefs; grid.Width = oldW; }
        Apply();
        PushUndo(new DelegateUndoCommand(
            () => { Revert(); RaiseRenderRequest(); },
            () => { Apply(); RaiseRenderRequest(); },
            "表格增加一列"));
        StructureEdited();
    }

    public void TableRemoveColumn(ElementModel grid)
    {
        if (!IsTable(grid)) return;
        int cols = StarCount(grid, "ColumnDefinitions");
        if (cols <= 1) return;
        int last = cols - 1;
        var oldDefs = grid.Props["ColumnDefinitions"];
        var newDefs = string.Join(";", Enumerable.Repeat("*", cols - 1));
        var oldW = grid.Width;
        var newW = grid.Width > 0 ? Math.Round(grid.Width * (cols - 1) / cols) : grid.Width;
        var cells = grid.Children.Where(ch => CellCol(ch) == last).ToList();

        void Apply() { foreach (var c in cells) grid.Children.Remove(c); grid.Props["ColumnDefinitions"] = newDefs; grid.Width = newW; }
        void Revert() { grid.Props["ColumnDefinitions"] = oldDefs; foreach (var c in cells) grid.Children.Add(c); grid.Width = oldW; }
        Apply();
        PushUndo(new DelegateUndoCommand(
            () => { Revert(); RaiseRenderRequest(); },
            () => { Apply(); RaiseRenderRequest(); },
            "表格删除一列"));
        StructureEdited();
    }

    public void QuizAddChoice(ElementModel quiz)
    {
        if (quiz.Type != "QuizControl") return;
        int n = quiz.Children.Count(c => c.Type == "QuizChoice");
        var letter = n < 26 ? ((char)('A' + n)).ToString() : (n + 1).ToString();
        var choice = MakeChoice($"{letter}. 新选项", false);
        var idx = quiz.Children.Count;
        quiz.Children.Add(choice);
        PushUndo(new InsertElementCommand(quiz.Children, choice, idx, RaiseRenderRequest, "增加选项"));
        StructureEdited();
    }

    public void QuizRemoveChoice(ElementModel quiz)
    {
        if (quiz.Type != "QuizControl") return;
        var choices = quiz.Children.Where(c => c.Type == "QuizChoice").ToList();
        if (choices.Count <= 2) return;   // keep at least two options
        var victim = choices[^1];
        var idx = quiz.Children.IndexOf(victim);
        quiz.Children.RemoveAt(idx);
        PushUndo(new DeleteElementCommand(quiz.Children, victim, idx, RaiseRenderRequest, "删除选项"));
        StructureEdited();
    }

    public void QuizAddBlank(ElementModel quiz)
    {
        if (quiz.Type != "QuizControl") return;
        int n = quiz.Children.Count(c => c.Type == "QuizBlank");
        var blank = MakeBlank($"答案{n + 1}");
        var idx = quiz.Children.Count;
        quiz.Children.Add(blank);
        PushUndo(new InsertElementCommand(quiz.Children, blank, idx, RaiseRenderRequest, "增加填空"));
        StructureEdited();
    }

    public void QuizRemoveBlank(ElementModel quiz)
    {
        if (quiz.Type != "QuizControl") return;
        var blanks = quiz.Children.Where(c => c.Type == "QuizBlank").ToList();
        if (blanks.Count <= 1) return;    // keep at least one blank
        var victim = blanks[^1];
        var idx = quiz.Children.IndexOf(victim);
        quiz.Children.RemoveAt(idx);
        PushUndo(new DeleteElementCommand(quiz.Children, victim, idx, RaiseRenderRequest, "删除填空"));
        StructureEdited();
    }

    private void StructureEdited()
    {
        MarkDirty();
        RequestRender?.Invoke(this, EventArgs.Empty);
    }

    // ── Trigger commands (unchanged) ──

    // 撤销方向约定：CollectionInsertCommand 表示"一次插入"（Undo=RemoveAt / Redo=Insert），
    // CollectionRemoveCommand 表示"一次删除"（Undo=Insert / Redo=RemoveAt）。

    public void AddTrigger(TriggerDescriptor td)
    {
        if (Document is null) return;
        Document.Triggers.Add(td);
        PushUndo(new CollectionInsertCommand<TriggerDescriptor>(Document.Triggers, td,
            Document.Triggers.Count - 1, "添加触发器"), affectsPage: false);
        MarkDirty();
        RequestRefreshTriggers?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveTrigger(TriggerDescriptor td)
    {
        if (Document is null) return;
        var idx = Document.Triggers.IndexOf(td);
        if (idx < 0) return;
        Document.Triggers.RemoveAt(idx);
        PushUndo(new CollectionRemoveCommand<TriggerDescriptor>(Document.Triggers, td, idx, "删除触发器"), affectsPage: false);
        MarkDirty();
        RequestRefreshTriggers?.Invoke(this, EventArgs.Empty);
    }

    public void AddAnimation(string name, AnimationDescriptor ad)
    {
        if (Document is null) return;
        var existing = Document.Animations.FirstOrDefault(a => a.Name == name);
        if (existing is not null) DeleteAnimation(existing);
        var entry = new AnimationEntry(name, ad);
        Document.Animations.Add(entry);
        PushUndo(new CollectionInsertCommand<AnimationEntry>(Document.Animations, entry,
            Document.Animations.Count - 1, "添加动画"), affectsPage: false);
        MarkDirty();
        RequestRefreshAnimations?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteAnimation(AnimationEntry entry)
    {
        if (Document is null) return;
        var idx = Document.Animations.IndexOf(entry);
        if (idx < 0) return;
        Document.Animations.RemoveAt(idx);
        PushUndo(new CollectionRemoveCommand<AnimationEntry>(Document.Animations, entry, idx, "删除动画"), affectsPage: false);
        MarkDirty();
        RequestRefreshAnimations?.Invoke(this, EventArgs.Empty);
    }

    // ── Misc ──

    public void RaiseRenderRequest() => RequestRender?.Invoke(this, EventArgs.Empty);

    [RelayCommand] private void PreviewCourse() => RequestPreview?.Invoke(this, EventArgs.Empty);

    public void MarkDirty()
    {
        if (Document is not null) Document.IsDirty = true;
    }

    private void MarkDirtyAfterUndoRedo(IUndoCommand? cmd)
    {
        if (cmd is null) return;
        if ((!_undoAffectsPage.TryGetValue(cmd, out var affectsPage) || affectsPage) &&
            Document?.SelectedPage is { } page)
            page.IsContentDirty = true;
        MarkDirty();
    }

    public void ReportDiagnostic(string message) => DiagnosticMessage = message;
    public void ClearDiagnostic() => DiagnosticMessage = null;
}

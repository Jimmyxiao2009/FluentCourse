using System.IO;
using System.IO.Compression;
using FluentCourse.Core.Models;
using FluentCourse.Core.Packaging;
using FluentCourse.Designer.Models;

namespace FluentCourse.Designer.Services;

/// <summary>
/// File I/O for .fcourse documents. The editor always works against an unpacked directory
/// (either a user-selected folder or a temp directory extracted from a .fcourse ZIP), so
/// save is just a re-zip; the JSON source is edited as-is on disk.
/// </summary>
public sealed class DocumentService
{
    /// <summary>Open a .fcourse file or directory. Extracts to a temp dir if it's a ZIP.
    /// SourcePath 记录用户可见的保存位置；WorkingDirectory 记录磁盘上的实际工作目录。</summary>
    public static DesignerDocument Open(string path)
    {
        if (File.Exists(path) && Path.GetExtension(path).Equals(".fcourse", StringComparison.OrdinalIgnoreCase))
        {
            var extractDir = Path.Combine(Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}");
            ZipFile.ExtractToDirectory(path, extractDir);
            var doc = LoadFromDirectory(extractDir);
            doc.SourcePath = path;
            doc.WorkingDirectory = extractDir;
            return doc;
        }

        if (Directory.Exists(path))
        {
            var doc = LoadFromDirectory(path);
            doc.SourcePath = path;
            doc.WorkingDirectory = path;
            return doc;
        }

        throw new FileNotFoundException("Course path not found.", path);
    }

    /// <summary>Create a blank document in <paramref name="directory"/>.</summary>
    public static DesignerDocument CreateNew(string directory)
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "pages"));

        var manifest = new CourseManifest
        {
            Title = "新课程",
            Id = Guid.NewGuid().ToString(),
            FormatVersion = 1
        };

        var firstPage = new PageDescriptor
        {
            Id = "cover",
            File = "pages/01-cover.xaml",
            Title = "封面",
            Section = "开场",
            Transition = "Fade"
        };
        manifest.Pages.Add(firstPage);

        var firstXaml = NewPageTemplate(firstPage.Id);
        var firstPagePath = Path.Combine(directory, firstPage.File);
        Directory.CreateDirectory(Path.GetDirectoryName(firstPagePath)!);
        File.WriteAllText(firstPagePath, firstXaml);

        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest, FluentCourse.Core.Json.FluentCourseJsonContext.Default.CourseManifest));

        var doc = new DesignerDocument(manifest, new Dictionary<string, string>
        {
            [firstPage.File] = firstXaml
        })
        {
            // 新建文档只有工作目录（临时），SourcePath 保持 null，让 Ctrl+S 正常走"另存为"，
            // 否则课件会被悄悄存进临时目录。
            SourcePath = null,
            WorkingDirectory = directory
        };
        return doc;
    }

    /// <summary>Write all in-memory page XAML and the JSON manifest back to <paramref name="directory"/>.</summary>
    public static void SaveToDirectory(DesignerDocument doc, string directory)
    {
        Directory.CreateDirectory(directory);
        doc.FlushPagesToManifest();
        doc.FlushAnimationsToManifest();
        doc.FlushTriggersToManifest();
        doc.FlushAssetsToManifest();
        doc.Manifest.UpdatedAt = DateTimeOffset.UtcNow;

        // Write all pages (create missing directories).
        // 未被用户编辑过的页面直接写回原始 XAML，避免不完整的解析/序列化往返丢失内容
        // （内联 Span、未建模的容器/自定义结构等）；仅用户在画布编辑过的页面才重新序列化。
        foreach (var p in doc.Pages)
        {
            var full = Path.Combine(directory, p.Descriptor.File.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var xaml = (!p.IsContentDirty && !string.IsNullOrWhiteSpace(p.SourceXaml))
                ? p.SourceXaml!
                : p.ToXaml();
            File.WriteAllText(full, xaml);
            p.AdoptXaml(xaml);   // 已落盘内容成为新的原文基线，并清除脏标记
        }

        // Write manifest with source-generation context (AOT-friendly)
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(doc.Manifest, FluentCourse.Core.Json.FluentCourseJsonContext.Default.CourseManifest));
    }

    /// <summary>
    /// 保存到 SourcePath：目录课件直接写回目录；.fcourse 单文件则先写入工作目录再重新压缩
    /// 覆盖（压缩到临时文件后整体替换，避免写一半损坏原包）。
    /// </summary>
    public static void Save(DesignerDocument doc)
    {
        var target = doc.SourcePath
            ?? throw new InvalidOperationException("文档尚未指定保存位置，请使用另存为。");

        // 目录课件（包括以 .fcourse 命名的解包文件夹）：直接写回目录本身。
        if (Directory.Exists(target))
        {
            SaveToDirectory(doc, target);
            // 工作目录与目标不同（例如崩溃恢复后），把资产等支撑文件一并带回去。
            if (!string.IsNullOrEmpty(doc.WorkingDirectory) &&
                !PathsEqual(doc.WorkingDirectory!, target))
                MirrorSupportFiles(doc.WorkingDirectory!, target);
            return;
        }

        // .fcourse 单文件：工作目录是唯一的解包副本。
        var work = doc.WorkingDirectory;
        if (string.IsNullOrEmpty(work))
        {
            work = Path.Combine(Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}");
            doc.WorkingDirectory = work;
        }
        SaveToDirectory(doc, work!);
        PackAsCourse(work!, target);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                      Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                      StringComparison.OrdinalIgnoreCase);

    /// <summary>Bundle a directory into a .fcourse ZIP. Replaces the file if it exists.
    /// 先压到临时文件再原子替换，写一半失败不会损坏已有文件。</summary>
    public static void PackAsCourse(string sourceDirectory, string outputPath)
    {
        var tmp = outputPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            ZipFile.CreateFromDirectory(
                sourceDirectory, tmp,
                System.IO.Compression.CompressionLevel.Optimal,
                includeBaseDirectory: false);
            File.Move(tmp, outputPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>把课件的支撑文件（assets/theme/animations）从一个目录镜像到另一个目录
    /// （大小相同的文件跳过，避免反复拷贝大视频）。</summary>
    public static void MirrorSupportFiles(string srcDir, string dstDir)
    {
        foreach (var sub in new[] { "assets", "theme", "animations" })
        {
            var src = Path.Combine(srcDir, sub);
            if (!Directory.Exists(src)) continue;
            var dst = Path.Combine(dstDir, sub);
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                var dest = Path.Combine(dst, Path.GetFileName(f));
                try
                {
                    if (!File.Exists(dest) || new FileInfo(dest).Length != new FileInfo(f).Length)
                        File.Copy(f, dest, true);
                }
                catch { }
            }
        }
    }

    /// <summary>递归复制目录（崩溃恢复时把快照搬进新的工作目录用）。</summary>
    public static void CopyDirectory(string srcDir, string dstDir)
    {
        Directory.CreateDirectory(dstDir);
        foreach (var f in Directory.GetFiles(srcDir))
            File.Copy(f, Path.Combine(dstDir, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(srcDir))
            CopyDirectory(d, Path.Combine(dstDir, Path.GetFileName(d)));
    }

    private static DesignerDocument LoadFromDirectory(string directory)
    {
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"manifest.json not found in {directory}");

        using var package = CoursePackage.Open(directory);
        var manifest = package.Manifest;

        var pageXaml = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in manifest.Pages)
        {
            var full = Path.Combine(directory, page.File.Replace('/', Path.DirectorySeparatorChar));
            pageXaml[page.File] = File.Exists(full) ? File.ReadAllText(full) : string.Empty;
        }

        return new DesignerDocument(manifest, pageXaml);
    }

    private static string NewPageTemplate(string pageId) =>
        // Canvas root: elements positioned by Canvas.Left/Top (absolute layout, designer-friendly).
        // Intentionally empty — no default placeholder text.
        "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\r\n" +
        "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\r\n" +
        "        Width=\"1920\" Height=\"1080\">\r\n" +
        "</Canvas>\r\n";
}

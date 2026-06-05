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
    /// <summary>Open a .fcourse file or directory. Extracts to a temp dir if it's a ZIP.</summary>
    public static DesignerDocument Open(string path)
    {
        if (File.Exists(path) && Path.GetExtension(path).Equals(".fcourse", StringComparison.OrdinalIgnoreCase))
        {
            var extractDir = Path.Combine(Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}");
            ZipFile.ExtractToDirectory(path, extractDir);
            var doc = LoadFromDirectory(extractDir);
            doc.SourcePath = path;
            return doc;
        }

        if (Directory.Exists(path))
        {
            var doc = LoadFromDirectory(path);
            doc.SourcePath = path;
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

        var firstPagePath = Path.Combine(directory, firstPage.File);
        Directory.CreateDirectory(Path.GetDirectoryName(firstPagePath)!);
        File.WriteAllText(firstPagePath, NewPageTemplate(firstPage.Id));

        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest, FluentCourse.Core.Json.FluentCourseJsonContext.Default.CourseManifest));

        var doc = new DesignerDocument(manifest, new Dictionary<string, string>
        {
            [firstPage.File] = File.ReadAllText(firstPagePath)
        })
        {
            SourcePath = directory
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

        // Write all pages (create missing directories)
        foreach (var p in doc.Pages)
        {
            var full = Path.Combine(directory, p.Descriptor.File.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, p.Xaml);
        }

        // Write manifest with source-generation context (AOT-friendly)
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(doc.Manifest, FluentCourse.Core.Json.FluentCourseJsonContext.Default.CourseManifest));
    }

    /// <summary>Bundle a directory into a .fcourse ZIP. Replaces the file if it exists.</summary>
    public static void PackAsCourse(string sourceDirectory, string outputPath)
    {
        if (File.Exists(outputPath)) File.Delete(outputPath);
        System.IO.Compression.ZipFile.CreateFromDirectory(
            sourceDirectory, outputPath,
            System.IO.Compression.CompressionLevel.Optimal,
            includeBaseDirectory: false);
    }

    private static DesignerDocument LoadFromDirectory(string directory)
    {
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"manifest.json not found in {directory}");

        var manifest = System.Text.Json.JsonSerializer.Deserialize(
            File.ReadAllText(manifestPath),
            FluentCourse.Core.Json.FluentCourseJsonContext.Default.CourseManifest)
            ?? throw new InvalidDataException("manifest.json could not be deserialized.");

        var pageXaml = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in manifest.Pages)
        {
            var full = Path.Combine(directory, page.File.Replace('/', Path.DirectorySeparatorChar));
            pageXaml[page.File] = File.Exists(full) ? File.ReadAllText(full) : string.Empty;
        }

        return new DesignerDocument(manifest, pageXaml);
    }

    private static string NewPageTemplate(string pageId) =>
        $"<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\r\n" +
        $"      xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\r\n\r\n" +
        $"    <TextBlock Text=\"{pageId}\"\r\n" +
        $"               FontSize=\"48\"\r\n" +
        $"               HorizontalAlignment=\"Center\"\r\n" +
        $"               VerticalAlignment=\"Center\"/>\r\n" +
        $"</Grid>\r\n";
}

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentCourse.Core.Json;
using FluentCourse.Core.Models;

namespace FluentCourse.Core.Packaging;

/// <summary>
/// Read-only handle on an open .fcourse archive OR an unpacked directory.
/// Use <see cref="Open(string)"/> for both — it auto-detects which mode to use.
/// </summary>
public sealed class CoursePackage : IDisposable
{
    private readonly ZipArchive? _archive;
    private readonly string? _directory;   // non-null in directory mode
    private readonly bool _ownsArchive;

    public CourseManifest Manifest { get; }
    public string? SourcePath { get; }

    private CoursePackage(ZipArchive archive, CourseManifest manifest, string? sourcePath)
    {
        _archive = archive;
        _ownsArchive = true;
        Manifest = manifest;
        SourcePath = sourcePath;
    }

    private CoursePackage(string directory, CourseManifest manifest)
    {
        _directory = directory;
        _ownsArchive = false;
        Manifest = manifest;
        SourcePath = directory;
    }

    /// <summary>
    /// Accepts a .fcourse/.zip file, OR a directory that contains manifest.json,
    /// OR a path to manifest.json itself.
    /// </summary>
    public static CoursePackage Open(string path)
    {
        // If the user selected the manifest.json file directly, treat its parent as the directory.
        if (Path.GetFileName(path).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            path = Path.GetDirectoryName(path)!;

        if (Directory.Exists(path))
        {
            var manifestPath = Path.Combine(path, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new InvalidDataException($"manifest.json not found in directory: {path}");
            var manifest = ReadManifestFromFile(manifestPath);
            return new CoursePackage(path, manifest);
        }

        if (File.Exists(path))
        {
            var archive = ZipFile.Open(path, ZipArchiveMode.Read);
            var manifest = ReadManifestFromZip(archive);
            return new CoursePackage(archive, manifest, path);
        }

        throw new FileNotFoundException($"Path not found: {path}");
    }

    public static CoursePackage Open(Stream stream, bool leaveOpen = false)
    {
        var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen);
        var manifest = ReadManifestFromZip(archive);
        return new CoursePackage(archive, manifest, null);
    }

    public byte[] ReadAllBytes(string entryPath)
    {
        if (_directory is not null)
            return File.ReadAllBytes(Path.Combine(_directory, NormalizeToLocal(entryPath)));

        var entry = _archive!.GetEntry(NormalizePath(entryPath))
            ?? throw new FileNotFoundException($"Entry not found in package: {entryPath}");
        using var s = entry.Open();
        using var ms = new MemoryStream(checked((int)entry.Length));
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public Stream OpenRead(string entryPath)
    {
        if (_directory is not null)
            return File.OpenRead(Path.Combine(_directory, NormalizeToLocal(entryPath)));

        var entry = _archive!.GetEntry(NormalizePath(entryPath))
            ?? throw new FileNotFoundException($"Entry not found in package: {entryPath}");
        return entry.Open();
    }

    public Stream OpenSeekable(string entryPath)
    {
        if (_directory is not null)
            return File.OpenRead(Path.Combine(_directory, NormalizeToLocal(entryPath)));

        var bytes = ReadAllBytes(entryPath);
        return new MemoryStream(bytes, writable: false);
    }

    public bool Exists(string entryPath)
    {
        if (_directory is not null)
            return File.Exists(Path.Combine(_directory, NormalizeToLocal(entryPath)));
        return _archive!.GetEntry(NormalizePath(entryPath)) is not null;
    }

    public string ReadText(string entryPath)
    {
        using var s = OpenRead(entryPath);
        using var sr = new StreamReader(s, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    private static CourseManifest ReadManifestFromZip(ZipArchive archive)
    {
        var entry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("manifest.json missing — not a valid .fcourse package.");
        using var s = entry.Open();
        return DeserializeManifest(s);
    }

    private static CourseManifest ReadManifestFromFile(string manifestPath)
    {
        using var s = File.OpenRead(manifestPath);
        return DeserializeManifest(s);
    }

    private static CourseManifest DeserializeManifest(Stream s)
    {
        var manifest = JsonSerializer.Deserialize(s, FluentCourseJsonContext.Default.CourseManifest)
            ?? throw new InvalidDataException("manifest.json could not be deserialized.");
        if (manifest.FormatVersion > 1)
            throw new NotSupportedException(
                $".fcourse formatVersion {manifest.FormatVersion} is newer than this runtime supports (1).");
        return manifest;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');
    private static string NormalizeToLocal(string path) => path.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

    public void Dispose()
    {
        if (_ownsArchive) _archive?.Dispose();
    }
}

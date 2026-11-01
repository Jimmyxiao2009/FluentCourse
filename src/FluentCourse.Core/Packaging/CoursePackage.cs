using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            // A stray .json file (not manifest.json) would otherwise fall through to the ZIP
            // reader and surface an opaque "End of Central Directory" error.
            if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Not a course package: {path}. Select a .fcourse file, or the manifest.json inside an unpacked course.");

            ZipArchive? archive = null;
            try
            {
                archive = ZipFile.Open(path, ZipArchiveMode.Read);
                var manifest = ReadManifestFromZip(archive);
                return new CoursePackage(archive, manifest, path);
            }
            catch (InvalidDataException ex) when (archive is null)
            {
                throw new InvalidDataException(
                    $"Not a valid .fcourse package (could not be read as a ZIP archive): {path}", ex);
            }
            catch
            {
                archive?.Dispose();
                throw;
            }
        }

        throw new FileNotFoundException($"Path not found: {path}");
    }

    public static CoursePackage Open(Stream stream, bool leaveOpen = false)
    {
        var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen);
        try
        {
            var manifest = ReadManifestFromZip(archive);
            return new CoursePackage(archive, manifest, null);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    public byte[] ReadAllBytes(string entryPath)
    {
        if (_directory is not null)
            return File.ReadAllBytes(ResolveLocal(entryPath));

        var entry = _archive!.GetEntry(NormalizePath(entryPath))
            ?? throw new FileNotFoundException($"Entry not found in package: {entryPath}");
        using var s = entry.Open();
        // entry.Length comes from the ZIP central directory and can lie in a corrupt
        // archive — use it only as a capped initial capacity, never trust it outright.
        using var ms = new MemoryStream((int)Math.Clamp(entry.Length, 0, 1 << 20));
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public Stream OpenRead(string entryPath)
    {
        if (_directory is not null)
            return File.OpenRead(ResolveLocal(entryPath));

        var entry = _archive!.GetEntry(NormalizePath(entryPath))
            ?? throw new FileNotFoundException($"Entry not found in package: {entryPath}");
        return entry.Open();
    }

    public Stream OpenSeekable(string entryPath)
    {
        if (_directory is not null)
            return File.OpenRead(ResolveLocal(entryPath));

        var bytes = ReadAllBytes(entryPath);
        return new MemoryStream(bytes, writable: false);
    }

    public bool Exists(string entryPath)
    {
        if (_directory is not null)
        {
            string full;
            try { full = ResolveLocal(entryPath); }
            catch (InvalidDataException) { return false; }
            return File.Exists(full);
        }
        return _archive!.GetEntry(NormalizePath(entryPath)) is not null;
    }

    /// <summary>
    /// Maps an entry path to a file inside the course directory, rejecting any path
    /// (e.g. via "..") that would escape it.
    /// </summary>
    private string ResolveLocal(string entryPath)
    {
        var root = Path.GetFullPath(_directory!);
        var full = Path.GetFullPath(Path.Combine(root, NormalizeToLocal(entryPath)));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Entry path escapes the course directory: {entryPath}");
        return full;
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
        byte[] bytes;
        if (s is MemoryStream ms)
        {
            bytes = ms.ToArray();
        }
        else
        {
            using var tempMs = new MemoryStream();
            s.CopyTo(tempMs);
            bytes = tempMs.ToArray();
        }

        CourseManifest? manifest = null;
        var warnings = new List<DiagnosticEntry>();
        var errors = new List<DiagnosticEntry>();

        try
        {
            manifest = JsonSerializer.Deserialize(bytes, FluentCourseJsonContext.Default.CourseManifest);
        }
        catch (JsonException ex)
        {
            errors.Add(new DiagnosticEntry
            {
                Code = "JSON_STRICT_FAILED",
                Message = $"严格解析失败: {ex.Message}",
                Location = $"行: {ex.LineNumber}, 位置: {ex.BytePositionInLine}"
            });

            try
            {
                var laxOptions = new JsonSerializerOptions
                {
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString,
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var laxContext = new FluentCourseJsonContext(laxOptions);
                manifest = (CourseManifest?)JsonSerializer.Deserialize(bytes, typeof(CourseManifest), laxContext);
                
                warnings.Add(new DiagnosticEntry
                {
                    Code = "JSON_LAX_FALLBACK",
                    Message = "检测到不规范的 JSON 格式或属性，已回退到宽松解析模式。"
                });
            }
            catch (JsonException laxEx)
            {
                errors.Add(new DiagnosticEntry
                {
                    Code = "JSON_LAX_FAILED",
                    Message = $"宽松解析也失败: {laxEx.Message}",
                    Location = $"行: {laxEx.LineNumber}, 位置: {laxEx.BytePositionInLine}"
                });

                var diagCol = new DiagnosticCollection();
                diagCol.Errors.AddRange(errors);
                diagCol.Warnings.AddRange(warnings);

                var wrapperEx = new InvalidDataException("manifest.json 反序列化失败（严格与宽松两轮均失败）。", laxEx);
                wrapperEx.Data["Diagnostics"] = diagCol;
                throw wrapperEx;
            }
        }

        if (manifest == null)
        {
            var diagCol = new DiagnosticCollection();
            diagCol.Errors.Add(new DiagnosticEntry { Code = "JSON_NULL", Message = "反序列化结果为空" });
            var wrapperEx = new InvalidDataException("manifest.json 反序列化结果为空。");
            wrapperEx.Data["Diagnostics"] = diagCol;
            throw wrapperEx;
        }

        if (manifest.FormatVersion > 1)
        {
            var msg = $".fcourse formatVersion {manifest.FormatVersion} is newer than this runtime supports (1).";
            errors.Add(new DiagnosticEntry { Code = "VERSION_UNSUPPORTED", Message = msg });
            
            var diagCol = new DiagnosticCollection();
            diagCol.Errors.AddRange(errors);
            diagCol.Warnings.AddRange(warnings);

            var wrapperEx = new NotSupportedException(msg);
            wrapperEx.Data["Diagnostics"] = diagCol;
            throw wrapperEx;
        }

        manifest.Diagnostics = new DiagnosticCollection();
        manifest.Diagnostics.Errors.AddRange(errors);
        manifest.Diagnostics.Warnings.AddRange(warnings);

        return manifest;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');
    private static string NormalizeToLocal(string path) => path.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

    public void Dispose()
    {
        if (_ownsArchive) _archive?.Dispose();
    }
}

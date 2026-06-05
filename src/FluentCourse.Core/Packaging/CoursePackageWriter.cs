using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentCourse.Core.Json;
using FluentCourse.Core.Models;

namespace FluentCourse.Core.Packaging;

/// <summary>
/// Builds a fresh .fcourse archive from in-memory parts. Used by the editor's
/// "save as" path and by the exporter when bundling a player + course.
/// </summary>
public sealed class CoursePackageWriter : IDisposable
{
    private readonly ZipArchive _archive;
    private readonly bool _ownsStream;
    private readonly Stream _stream;
    private bool _manifestWritten;

    public CoursePackageWriter(string path)
        : this(File.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None), ownsStream: true) { }

    public CoursePackageWriter(Stream stream, bool ownsStream)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: !ownsStream);
    }

    public void WriteManifest(CourseManifest manifest)
    {
        if (_manifestWritten) throw new InvalidOperationException("manifest.json already written.");
        manifest.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(manifest, FluentCourseJsonContext.Default.CourseManifest);
        WriteText("manifest.json", json);
        _manifestWritten = true;
    }

    public void WriteText(string entryPath, string content, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = _archive.CreateEntry(NormalizePath(entryPath), level);
        using var s = entry.Open();
        using var sw = new StreamWriter(s, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        sw.Write(content);
    }

    public void WriteBytes(string entryPath, ReadOnlySpan<byte> bytes, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = _archive.CreateEntry(NormalizePath(entryPath), level);
        using var s = entry.Open();
        s.Write(bytes);
    }

    public void CopyFrom(string entryPath, Stream source, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = _archive.CreateEntry(NormalizePath(entryPath), level);
        using var s = entry.Open();
        source.CopyTo(s);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');

    public void Dispose()
    {
        _archive.Dispose();
        if (_ownsStream) _stream.Dispose();
    }
}

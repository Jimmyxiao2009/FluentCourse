using System.IO.Compression;
using FluentCourse.Core.Packaging;

namespace FluentCourse.Exporter;

/// <summary>
/// CLI exporter:
///   FluentCourse.Exporter bundle &lt;course.fcourse&gt; &lt;player-publish-dir&gt; &lt;output.exe&gt;
/// Produces a single self-extracting EXE that contains both the AOT-published player and
/// the .fcourse archive. Run-time, the player extracts itself to %TEMP% and launches the
/// player with the embedded course.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 1) { PrintHelp(); return 1; }
        try
        {
            return args[0] switch
            {
                "bundle" => Bundle(args),
                "info"   => Info(args),
                _        => Help()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int Bundle(string[] args)
    {
        if (args.Length != 4) { PrintHelp(); return 1; }
        var coursePath = args[1];
        var playerDir = args[2];
        var outputExe = args[3];
        string? tempCourseArchive = null;

        if (!Directory.Exists(playerDir)) throw new DirectoryNotFoundException(playerDir);

        // The "self-extracting EXE" is implemented as a stub that owns a ZIP appended at the tail.
        // Layout:  [stub.exe bytes][zip archive][int64 zipSize][uint64 magic]
        // At launch the stub reads the magic, mmaps the zip, extracts to %TEMP%/FluentCourse_<hash>/
        // and execs FluentCourse.App.exe with --course=embedded.fcourse.
        var stubPath = Path.Combine(playerDir, "FluentCourse.App.exe");
        if (!File.Exists(stubPath)) throw new FileNotFoundException(stubPath);

        var courseArchive = PrepareCourseArchive(coursePath, out tempCourseArchive);
        var outputFullPath = Path.GetFullPath(outputExe);
        var outputDirectory = Path.GetDirectoryName(outputFullPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        try
        {
            using var output = File.Create(outputFullPath);
            using (var stub = File.OpenRead(stubPath)) stub.CopyTo(output);

            var zipStart = output.Position;
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var stubFullPath = Path.GetFullPath(stubPath);
                // Add player runtime alongside the course so the stub has a self-contained payload.
                foreach (var f in Directory.EnumerateFiles(playerDir, "*", SearchOption.AllDirectories))
                {
                    var fileFullPath = Path.GetFullPath(f);
                    if (string.Equals(fileFullPath, stubFullPath, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fileFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var rel = Path.GetRelativePath(playerDir, f).Replace('\\', '/');
                    zip.CreateEntryFromFile(f, $"player/{rel}", CompressionLevel.SmallestSize);
                }
                zip.CreateEntryFromFile(courseArchive, "embedded.fcourse", CompressionLevel.NoCompression);
            }

            var zipSize = output.Position - zipStart;
            using var bw = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
            bw.Write((long)zipSize);
            bw.Write(0x46434F5552534558UL); // 8-byte sentinel; matches BundleBootstrapper.Magic

            Console.WriteLine($"Bundled {coursePath} + player into {outputFullPath} ({output.Length:N0} bytes)");
            return 0;
        }
        finally
        {
            if (tempCourseArchive is not null)
                File.Delete(tempCourseArchive);
        }
    }

    private static int Info(string[] args)
    {
        if (args.Length != 2) { PrintHelp(); return 1; }
        using var pkg = CoursePackage.Open(args[1]);
        var m = pkg.Manifest;
        Console.WriteLine($"Title:      {m.Title}");
        Console.WriteLine($"Author:     {m.Author ?? "(unknown)"}");
        Console.WriteLine($"Pages:      {m.Pages.Count}");
        Console.WriteLine($"Triggers:   {m.Triggers.Count}");
        Console.WriteLine($"Animations: {m.Animations.Count}");
        Console.WriteLine($"Assets:     {m.Assets.Count}");
        return 0;
    }

    private static int Help()
    {
        PrintHelp();
        return 1;
    }

    private static string PrepareCourseArchive(string coursePath, out string? tempCourseArchive)
    {
        tempCourseArchive = null;

        if (Path.GetFileName(coursePath).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            coursePath = Path.GetDirectoryName(coursePath)!;

        if (File.Exists(coursePath))
            return coursePath;

        if (!Directory.Exists(coursePath))
            throw new FileNotFoundException(coursePath);

        var manifestPath = Path.Combine(coursePath, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"manifest.json not found in directory: {coursePath}");

        tempCourseArchive = Path.Combine(Path.GetTempPath(), $"FluentCourse_{Guid.NewGuid():N}.fcourse");
        ZipFile.CreateFromDirectory(coursePath, tempCourseArchive, CompressionLevel.SmallestSize, includeBaseDirectory: false);
        return tempCourseArchive;
    }

    private static void PrintHelp() => Console.WriteLine("""
        FluentCourse.Exporter
          bundle <course.fcourse> <player-publish-dir> <output.exe>
          info   <course.fcourse>
        """);
}

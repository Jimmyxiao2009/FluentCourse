using System.IO.Compression;

namespace FluentCourse.Designer.Services;

public static class StandaloneBundleService
{
    private const ulong Magic = 0x46434F5552534558UL;

    public static void Bundle(string courseArchivePath, string playerDirectory, string outputExePath)
    {
        if (!Directory.Exists(playerDirectory))
            throw new DirectoryNotFoundException(playerDirectory);
        if (!File.Exists(courseArchivePath))
            throw new FileNotFoundException(courseArchivePath);

        var stubPath = Path.Combine(playerDirectory, "FluentCourse.App.exe");
        if (!File.Exists(stubPath))
            throw new FileNotFoundException("播放器目录中找不到 FluentCourse.App.exe", stubPath);

        var outputFullPath = Path.GetFullPath(outputExePath);
        var outputDirectory = Path.GetDirectoryName(outputFullPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        using var output = File.Create(outputFullPath);
        using (var stub = File.OpenRead(stubPath))
            stub.CopyTo(output);

        var zipStart = output.Position;
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var stubFullPath = Path.GetFullPath(stubPath);
            foreach (var file in Directory.EnumerateFiles(playerDirectory, "*", SearchOption.AllDirectories))
            {
                var fileFullPath = Path.GetFullPath(file);
                if (string.Equals(fileFullPath, stubFullPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(playerDirectory, file).Replace('\\', '/');
                zip.CreateEntryFromFile(file, $"player/{relativePath}", CompressionLevel.SmallestSize);
            }

            zip.CreateEntryFromFile(courseArchivePath, "embedded.fcourse", CompressionLevel.NoCompression);
        }

        var zipSize = output.Position - zipStart;
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((long)zipSize);
        writer.Write(Magic);
    }
}

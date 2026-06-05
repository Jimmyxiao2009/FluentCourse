using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace FluentCourse.App;

internal static class BundleBootstrapper
{
    private const ulong Magic = 0x46434F5552534558UL; // 8-byte sentinel; not the ASCII "FCOURSEX" verbatim
    private const int FooterSize = sizeof(long) + sizeof(ulong);
    private const string PlayerExeName = "FluentCourse.App.exe";

    public static bool TryLaunchExtractedPlayer()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return false;

        if (!TryReadPayloadInfo(processPath, out var payload))
            return false;

        var bundleHash = ComputeFileHash(processPath);
        var root = Path.Combine(Path.GetTempPath(), $"FluentCourse_{bundleHash}");
        var playerDir = Path.Combine(root, "player");
        var playerExe = Path.Combine(playerDir, PlayerExeName);
        var embeddedCourse = Path.Combine(root, "embedded.fcourse");

        Directory.CreateDirectory(playerDir);
        EnsurePlayerStub(processPath, playerExe, payload.ZipStart);
        EnsurePayloadExtracted(processPath, root, payload, embeddedCourse);
        LaunchPlayer(playerExe, embeddedCourse);
        return true;
    }

    private static bool TryReadPayloadInfo(string processPath, out PayloadInfo payload)
    {
        payload = default;

        using var stream = OpenProcessImage(processPath);
        if (stream.Length <= FooterSize)
            return false;

        stream.Position = stream.Length - FooterSize;
        using var reader = new BinaryReader(stream);
        var zipSize = reader.ReadInt64();
        var magic = reader.ReadUInt64();
        var zipStart = stream.Length - FooterSize - zipSize;

        if (magic != Magic || zipSize <= 0 || zipStart < 0)
            return false;

        payload = new PayloadInfo(zipStart, zipSize);
        return true;
    }

    private static void EnsurePlayerStub(string processPath, string playerExe, long stubSize)
    {
        if (File.Exists(playerExe) && new FileInfo(playerExe).Length == stubSize)
            return;

        CopyRange(processPath, playerExe, offset: 0, byteCount: stubSize);
    }

    private static void EnsurePayloadExtracted(
        string processPath,
        string root,
        PayloadInfo payload,
        string embeddedCourse)
    {
        var marker = Path.Combine(root, ".ready");
        if (File.Exists(marker) && File.Exists(embeddedCourse))
            return;

        var payloadZip = Path.Combine(root, "payload.zip");
        CopyRange(processPath, payloadZip, payload.ZipStart, payload.ZipSize);

        using (var archive = ZipFile.OpenRead(payloadZip))
        {
            foreach (var entry in archive.Entries)
            {
                var destination = GetSafeDestination(root, entry.FullName);
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }

        File.WriteAllText(marker, DateTimeOffset.UtcNow.ToString("O"));

        try
        {
            File.Delete(payloadZip);
        }
        catch (IOException)
        {
            // The bundle can still run if the cached payload zip remains in temp.
        }
    }

    private static void LaunchPlayer(string playerExe, string embeddedCourse)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = playerExe,
            WorkingDirectory = Path.GetDirectoryName(playerExe)!,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add($"--course={embeddedCourse}");
        Process.Start(startInfo);
    }

    private static FileStream OpenProcessImage(string processPath) =>
        File.Open(processPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static string ComputeFileHash(string path)
    {
        using var stream = OpenProcessImage(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream))[..12].ToLowerInvariant();
    }

    private static void CopyRange(string sourcePath, string destinationPath, long offset, long byteCount)
    {
        using var source = OpenProcessImage(sourcePath);
        using var destination = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        source.Position = offset;
        var buffer = new byte[1024 * 1024];
        var remaining = byteCount;
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of bundle payload.");

            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static string GetSafeDestination(string root, string entryName)
    {
        var destination = Path.GetFullPath(Path.Combine(root, entryName.Replace('/', Path.DirectorySeparatorChar)));
        var rootFullPath = Path.GetFullPath(root);
        var rootPrefix = rootFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootFullPath
            : rootFullPath + Path.DirectorySeparatorChar;

        if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Bundle entry escapes extraction directory: {entryName}");

        return destination;
    }

    private readonly record struct PayloadInfo(long ZipStart, long ZipSize);
}

using System.IO;
using System.IO.Compression;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Import;

public sealed class ZipImportService
{
    public string ExtractToTempDirectory(string zipFilePath)
    {
        var targetDir = Path.Combine(Path.GetTempPath(), "NeoDenSoftware", "GerberImport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);

        var targetRoot = Path.GetFullPath(targetDir + Path.DirectorySeparatorChar);

        using var archive = ZipFile.OpenRead(zipFilePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry

            var destinationPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
            if (!destinationPath.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Zip entry '{entry.FullName}' resolves outside the extraction directory.");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }

        return targetDir;
    }

    public List<ExtractedFile> ExtractAndList(string zipFilePath)
    {
        var targetDir = ExtractToTempDirectory(zipFilePath);
        return Directory.EnumerateFiles(targetDir, "*", SearchOption.AllDirectories)
            .Select(path => new ExtractedFile
            {
                FullPath = path,
                FileName = Path.GetFileName(path),
            })
            .OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

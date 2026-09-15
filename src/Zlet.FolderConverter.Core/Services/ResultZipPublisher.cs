using System.IO.Compression;
using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.Core.Services;

public sealed class ResultZipPublisher
{
    public async Task<ResultZipPublishResult> PublishAsync(
        string stagingRootPath,
        string finalZipPath,
        ConversionSummary summary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var successful = summary.Results
            .Where(result => result.Status == OperationStatus.Succeeded)
            .ToArray();
        if (successful.Length == 0)
        {
            return new(false, "no_successful_outputs");
        }

        var stagingRoot = Path.GetFullPath(stagingRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var finalZip = Path.GetFullPath(finalZipPath);
        var finalDirectory = Path.GetDirectoryName(finalZip)
            ?? throw new InvalidOperationException("ZIP destination has no parent directory.");
        if (File.Exists(finalZip) || Directory.Exists(finalZip))
        {
            return new(false, "zip_target_conflict");
        }

        Directory.CreateDirectory(finalDirectory);
        var temporaryZip = Path.Combine(
            finalDirectory,
            $".{Path.GetFileName(finalZip)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var entries = BuildEntries(stagingRoot, successful);
            await using (var stream = new FileStream(
                             temporaryZip,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var archiveEntry = archive.CreateEntry(
                        entry.EntryName,
                        CompressionLevel.Optimal);
                    await using var target = archiveEntry.Open();
                    await using var source = new FileStream(
                        entry.SourcePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        81920,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await source.CopyToAsync(target, cancellationToken);
                }
            }

            ValidateArchive(temporaryZip, entries.Select(entry => entry.EntryName).ToArray());
            File.Move(temporaryZip, finalZip, overwrite: false);
            return new(true);
        }
        catch (IOException) when (File.Exists(finalZip) || Directory.Exists(finalZip))
        {
            return new(false, "zip_target_conflict");
        }
        finally
        {
            try
            {
                File.Delete(temporaryZip);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static IReadOnlyList<ZipSourceEntry> BuildEntries(
        string stagingRoot,
        IReadOnlyList<ConversionResult> successful)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ZipSourceEntry>(successful.Count);
        foreach (var result in successful)
        {
            var sourcePath = Path.GetFullPath(result.Operation.TargetPath);
            if (!File.Exists(sourcePath)
                || (new FileInfo(sourcePath).Length == 0
                    && !(result.Operation.Target == ConversionTarget.Copy
                        && result.Operation.SourceFormat is SourceFormat.Csv or SourceFormat.Tsv))
                || !sourcePath.StartsWith(
                    stagingRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || HasReparsePoint(sourcePath))
            {
                throw new InvalidDataException("A successful output is unsafe or missing.");
            }

            var entryName = Path.GetRelativePath(stagingRoot, sourcePath)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (!IsSafeEntryName(entryName) || !names.Add(entryName))
            {
                throw new InvalidDataException("ZIP entry path is unsafe or duplicated.");
            }

            entries.Add(new ZipSourceEntry(sourcePath, entryName));

            var targetStem = Path.GetFileNameWithoutExtension(sourcePath);
            var parentDir = Path.GetDirectoryName(sourcePath)!;
            var companionDir = Path.Combine(parentDir, $"{targetStem}_assets");
            if (Directory.Exists(companionDir))
            {
                foreach (var file in Directory.EnumerateFiles(companionDir, "*", SearchOption.AllDirectories))
                {
                    var fileFullPath = Path.GetFullPath(file);
                    if (!fileFullPath.StartsWith(
                            stagingRoot + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase)
                        || HasReparsePoint(fileFullPath))
                    {
                        throw new InvalidDataException("A companion asset output is unsafe.");
                    }

                    var assetEntryName = Path.GetRelativePath(stagingRoot, fileFullPath)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    if (!IsSafeEntryName(assetEntryName) || !names.Add(assetEntryName))
                    {
                        throw new InvalidDataException("ZIP asset entry path is unsafe or duplicated.");
                    }

                    entries.Add(new ZipSourceEntry(fileFullPath, assetEntryName));
                }
            }
        }

        return entries;
    }

    public static bool IsSafeEntryName(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName)
            || Path.IsPathRooted(entryName)
            || entryName.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        var parts = entryName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts.All(part => part is not "." and not "..");
    }

    private static bool HasReparsePoint(string path)
    {
        for (var current = path; !string.IsNullOrWhiteSpace(current); current = Path.GetDirectoryName(current) ?? "")
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateArchive(string path, IReadOnlyList<string> expectedNames)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            throw new InvalidDataException("ZIP output is missing or empty.");
        }

        using var archive = ZipFile.OpenRead(path);
        var actual = archive.Entries.Select(entry => entry.FullName).ToArray();
        if (actual.Length != expectedNames.Count
            || actual.Any(name => !IsSafeEntryName(name))
            || actual.Distinct(StringComparer.OrdinalIgnoreCase).Count() != actual.Length
            || !actual.Order(StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(expectedNames.Order(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("ZIP verification failed.");
        }
    }

    private sealed record ZipSourceEntry(string SourcePath, string EntryName);
}

public sealed record ResultZipPublishResult(bool Created, string ErrorCode = "");

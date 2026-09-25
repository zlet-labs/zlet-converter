namespace Zlet.FolderConverter.Core.Services;

public static class OutputPathGuard
{
    public static bool IsSafeSourcePath(
        string sourcePath,
        string sourceRootPath,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)
            || string.IsNullOrWhiteSpace(sourceRootPath)
            || string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        try
        {
            var sourceRoot = NormalizeDirectory(sourceRootPath);
            var source = Path.GetFullPath(sourcePath);
            if (!File.Exists(source)
                || !IsStrictlyWithin(source, sourceRoot)
                || Path.IsPathRooted(relativePath)
                || ContainsTraversal(relativePath))
            {
                return false;
            }

            var expectedSource = Path.GetFullPath(Path.Combine(sourceRoot, relativePath));
            if (!string.Equals(source, expectedSource, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            for (var current = source;
                 !string.IsNullOrWhiteSpace(current) && IsWithinOrEqual(current, sourceRoot);
                 current = Path.GetDirectoryName(current) ?? string.Empty)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                if (string.Equals(
                        NormalizeDirectory(current),
                        sourceRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return false;
        }
    }

    public static OutputDestinationValidation ValidateFolderDestination(
        string sourceRootPath,
        string outputRootPath)
    {
        if (string.IsNullOrWhiteSpace(sourceRootPath) || string.IsNullOrWhiteSpace(outputRootPath))
        {
            return new(false, "output_path_missing");
        }

        try
        {
            var sourceRoot = NormalizeDirectory(sourceRootPath);
            var outputRoot = NormalizeDirectory(outputRootPath);
            if (string.Equals(sourceRoot, outputRoot, StringComparison.OrdinalIgnoreCase))
            {
                return new(false, "output_equals_source");
            }

            if (sourceRoot.StartsWith(
                    outputRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new(false, "output_is_source_parent");
            }

            if (File.Exists(outputRoot))
            {
                return new(false, "output_is_file");
            }

            return HasReparsePointInExistingPath(outputRoot)
                ? new(false, "output_reparse_path")
                : new(true);
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return new(false, "output_path_invalid");
        }
    }

    public static OutputDestinationValidation ValidateZipDestination(
        string sourceRootPath,
        string zipPath,
        string stagingRootPath)
    {
        if (string.IsNullOrWhiteSpace(sourceRootPath)
            || string.IsNullOrWhiteSpace(zipPath)
            || string.IsNullOrWhiteSpace(stagingRootPath))
        {
            return new(false, "zip_path_missing");
        }

        try
        {
            var fullZip = Path.GetFullPath(zipPath);
            var stagingRoot = NormalizeDirectory(stagingRootPath);
            if (!Path.GetExtension(fullZip).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return new(false, "zip_extension_required");
            }

            if (File.Exists(fullZip) || Directory.Exists(fullZip))
            {
                return new(false, "zip_target_conflict");
            }

            if (IsWithinOrEqual(fullZip, stagingRoot))
            {
                return new(false, "zip_inside_staging");
            }

            var parent = Path.GetDirectoryName(fullZip);
            if (string.IsNullOrWhiteSpace(parent) || File.Exists(parent))
            {
                return new(false, "zip_parent_invalid");
            }

            return HasReparsePointInExistingPath(parent)
                ? new(false, "zip_reparse_path")
                : new(true);
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return new(false, "zip_path_invalid");
        }
    }
    public static bool TryBuildTargetPath(
        string sourceRootPath,
        string outputRootPath,
        string sourcePath,
        string relativePath,
        string targetExtension,
        out string targetPath)
    {
        targetPath = string.Empty;
        try
        {
            var sourceRoot = NormalizeDirectory(sourceRootPath);
            var outputRoot = NormalizeDirectory(outputRootPath);
            var fullSource = Path.GetFullPath(sourcePath);
            if (!IsStrictlyWithin(fullSource, sourceRoot)
                || Path.IsPathRooted(relativePath)
                || ContainsTraversal(relativePath))
            {
                return false;
            }

            var expectedSource = Path.GetFullPath(Path.Combine(sourceRoot, relativePath));
            if (!string.Equals(fullSource, expectedSource, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var targetRelativePath = Path.ChangeExtension(relativePath, targetExtension);
            var candidate = Path.GetFullPath(Path.Combine(outputRoot, targetRelativePath));
            if (!IsStrictlyWithin(candidate, outputRoot))
            {
                return false;
            }

            targetPath = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or IOException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return false;
        }
    }

    public static bool IsSafeTargetPath(string targetPath, string outputRootPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || string.IsNullOrWhiteSpace(outputRootPath))
        {
            return false;
        }

        try
        {
            var outputRoot = NormalizeDirectory(outputRootPath);
            var candidate = Path.GetFullPath(targetPath);
            if (!IsStrictlyWithin(candidate, outputRoot))
            {
                return false;
            }

            for (var directory = Path.GetDirectoryName(candidate);
                 directory is not null && IsWithinOrEqual(directory, outputRoot);
                 directory = Path.GetDirectoryName(directory))
            {
                if (Directory.Exists(directory)
                    && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                if (string.Equals(NormalizeDirectory(directory), outputRoot, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return false;
        }
    }

    private static bool ContainsTraversal(string relativePath)
    {
        var components = relativePath.Split(
            new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        return components.Any(component => component is "." or "..");
    }

    private static bool IsStrictlyWithin(string candidate, string root) =>
        Path.GetFullPath(candidate).StartsWith(
            NormalizeDirectory(root) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsWithinOrEqual(string candidate, string root)
    {
        var normalizedCandidate = NormalizeDirectory(candidate);
        var normalizedRoot = NormalizeDirectory(root);
        return string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase)
               || normalizedCandidate.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasReparsePointInExistingPath(string path)
    {
        for (var current = Path.GetFullPath(path);
             !string.IsNullOrWhiteSpace(current);
             current = Path.GetDirectoryName(current) ?? string.Empty)
        {
            if (Directory.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return false;
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

public sealed record OutputDestinationValidation(bool IsValid, string ErrorCode = "");

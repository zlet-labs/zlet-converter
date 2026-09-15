using System.Security.Cryptography;
using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.Core.Services;

internal sealed record TemporaryOutputProductionResult(
    bool Success,
    string ErrorCode = "",
    string UserMessage = "Не удалось обработать файл.",
    bool TimedOut = false,
    int? ExitCode = null,
    bool HasStandardOutput = false,
    bool HasStandardError = false,
    int? HResult = null);

internal sealed class SafeFileOperationExecutor
{
    private readonly IOutputResultValidator _validator;
    private readonly string _temporaryRoot;

    public SafeFileOperationExecutor(
        IOutputResultValidator validator,
        string? temporaryRoot = null)
    {
        _validator = validator;
        _temporaryRoot = Path.GetFullPath(
            temporaryRoot
            ?? Path.Combine(Path.GetTempPath(), "ZletBatchConverter", "operations"));
    }

    public async Task<ConversionResult> ExecuteAsync(
        PlannedOperation operation,
        ConversionTarget validationTarget,
        Func<string, CancellationToken, Task<TemporaryOutputProductionResult>> produceAsync,
        string successMessage,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var sourceRoot = ResolveSourceRoot(operation);
        if (!OutputPathGuard.IsSafeSourcePath(
                operation.SourcePath,
                sourceRoot,
                operation.RelativePath))
        {
            return Result(
                operation,
                OperationStatus.Failed,
                "Исходный файл небезопасен или находится вне выбранной папки.",
                "unsafe_source");
        }

        if (!OutputPathGuard.IsSafeTargetPath(operation.TargetPath, operation.OutputRootPath))
        {
            return Result(
                operation,
                OperationStatus.Failed,
                "Недопустимый путь результата.",
                "unsafe_target");
        }

        if (File.Exists(operation.TargetPath) || Directory.Exists(operation.TargetPath))
        {
            return Result(
                operation,
                OperationStatus.Conflict,
                "Файл результата уже существует.",
                "target_conflict");
        }

        SourceFileSnapshot snapshot;
        try
        {
            snapshot = await SourceFileSnapshot.CreateAsync(
                operation.SourcePath,
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result(
                operation,
                OperationStatus.Failed,
                "Не удалось открыть исходный файл.",
                "source_unreadable");
        }

        var operationRoot = Path.Combine(_temporaryRoot, Guid.NewGuid().ToString("N"));
        var targetFileName = Path.GetFileName(operation.TargetPath);
        var targetStem = Path.GetFileNameWithoutExtension(operation.TargetPath);
        var temporaryOutput = Path.Combine(
            operationRoot,
            "output",
            targetFileName);
        string? stagingPath = null;
        string? stagingCompanionDir = null;
        string? targetCompanionDir = null;
        var promotedTarget = false;
        var promotedCompanionDir = false;

        var allowEmptyCopy = operation.Target == ConversionTarget.Copy
            && operation.SourceFormat is SourceFormat.Csv or SourceFormat.Tsv or SourceFormat.Txt or SourceFormat.Html && snapshot.Length == 0;
        var allowEmptyOutput = allowEmptyCopy || (operation.Target == ConversionTarget.Markdown && snapshot.Length == 0);
        async Task<OutputValidationResult> ValidateOutputAsync(string path)
        {
            if (!allowEmptyCopy) return _validator.Validate(path, validationTarget, snapshot.Length);
            return await snapshot.IsUnchangedAsync(path, cancellationToken)
                ? new OutputValidationResult(true) : new OutputValidationResult(false, "copy_integrity_mismatch");
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(temporaryOutput)!);
            var production = await produceAsync(temporaryOutput, cancellationToken);
            if (!production.Success)
            {
                return Result(
                    operation,
                    OperationStatus.Failed,
                    production.UserMessage,
                    production.ErrorCode,
                    production.ExitCode,
                    production.TimedOut,
                    production.HasStandardOutput,
                    production.HasStandardError,
                    production.HResult);
            }

            if (!File.Exists(temporaryOutput)
                || (new FileInfo(temporaryOutput).Length == 0 && !allowEmptyOutput))
            {
                return Result(
                    operation,
                    OperationStatus.Failed,
                    "Приложение не создало ожидаемый результат.",
                    "output_missing");
            }

            if (!Path.GetExtension(temporaryOutput).Equals(
                    operation.TargetExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Result(
                    operation,
                    OperationStatus.Failed,
                    "Расширение результата не прошло проверку.",
                    "output_extension_invalid");
            }

            var temporaryValidation = await ValidateOutputAsync(temporaryOutput);
            if (!temporaryValidation.IsValid)
            {
                return Result(
                    operation,
                    OperationStatus.Failed,
                    "Формат результата не прошёл проверку.",
                    temporaryValidation.ErrorCode);
            }

            if (!await snapshot.IsUnchangedAsync(operation.SourcePath, cancellationToken))
            {
                return Result(
                    operation,
                    OperationStatus.Failed,
                    "Исходный файл изменился во время обработки.",
                    "source_changed");
            }

            var targetDirectory = Path.GetDirectoryName(operation.TargetPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                return Result(
                    operation,
                    OperationStatus.Failed,
                    "Недопустимый путь результата.",
                    "target_directory_missing");
            }

            Directory.CreateDirectory(targetDirectory);
            if (!OutputPathGuard.IsSafeTargetPath(operation.TargetPath, operation.OutputRootPath))
            {
                return Result(
                    operation,
                    OperationStatus.Failed,
                    "Недопустимый путь результата.",
                    "unsafe_target_after_create");
            }

            if (File.Exists(operation.TargetPath) || Directory.Exists(operation.TargetPath))
            {
                return Result(
                    operation,
                    OperationStatus.Conflict,
                    "Файл результата уже существует.",
                    "target_conflict");
            }

            var companionDirName = $"{targetStem}_assets";
            var temporaryOutputDir = Path.GetDirectoryName(temporaryOutput)!;
            var temporaryCompanionDir = Path.Combine(temporaryOutputDir, companionDirName);
            var hasCompanionDir = Directory.Exists(temporaryCompanionDir)
                && Directory.EnumerateFileSystemEntries(temporaryCompanionDir).Any();

            if (hasCompanionDir)
            {
                targetCompanionDir = Path.Combine(targetDirectory, companionDirName);
                if (File.Exists(targetCompanionDir) || Directory.Exists(targetCompanionDir))
                {
                    return Result(
                        operation,
                        OperationStatus.Conflict,
                        "Папка с ресурсами результата уже существует.",
                        "target_conflict");
                }

                if (!OutputPathGuard.IsSafeTargetPath(targetCompanionDir, operation.OutputRootPath))
                {
                    return Result(
                        operation,
                        OperationStatus.Failed,
                        "Недопустимый путь результата.",
                        "unsafe_target");
                }
            }

            stagingPath = Path.Combine(
                targetDirectory,
                $".{targetFileName}.{Guid.NewGuid():N}.tmp");
            File.Copy(temporaryOutput, stagingPath, overwrite: false);

            if (hasCompanionDir)
            {
                stagingCompanionDir = Path.Combine(
                    targetDirectory,
                    $".{companionDirName}.{Guid.NewGuid():N}.tmp");
                CopyDirectory(temporaryCompanionDir, stagingCompanionDir);
                if (!ValidateCompanionDirectory(stagingCompanionDir))
                {
                    return Result(
                        operation,
                        OperationStatus.Failed,
                        "Формат ресурсов результата не прошёл проверку.",
                        "companion_assets_invalid");
                }
            }

            var stagingValidation = await ValidateOutputAsync(stagingPath);
            if (!stagingValidation.IsValid)
            {
                return Result(
                    operation,
                    OperationStatus.Failed,
                    "Формат результата не прошёл проверку.",
                    stagingValidation.ErrorCode);
            }

            if (hasCompanionDir && stagingCompanionDir is not null && targetCompanionDir is not null)
            {
                Directory.Move(stagingCompanionDir, targetCompanionDir);
                promotedCompanionDir = true;
                stagingCompanionDir = null;
            }

            File.Move(stagingPath, operation.TargetPath, overwrite: false);
            promotedTarget = true;
            stagingPath = null;

            var finalValidation = await ValidateOutputAsync(operation.TargetPath);
            if (!finalValidation.IsValid)
            {
                if (promotedTarget)
                {
                    TryDeleteFile(operation.TargetPath);
                }
                if (promotedCompanionDir && targetCompanionDir is not null)
                {
                    TryDeleteDirectoryRecursively(targetCompanionDir);
                }
                return Result(
                    operation,
                    OperationStatus.Failed,
                    "Формат результата не прошёл проверку.",
                    finalValidation.ErrorCode);
            }

            return new ConversionResult(operation, OperationStatus.Succeeded, successMessage);
        }
        catch (OperationCanceledException)
        {
            if (promotedTarget)
            {
                TryDeleteFile(operation.TargetPath);
            }
            if (promotedCompanionDir && targetCompanionDir is not null)
            {
                TryDeleteDirectoryRecursively(targetCompanionDir);
            }
            throw;
        }
        catch (IOException) when (File.Exists(operation.TargetPath)
                                  || Directory.Exists(operation.TargetPath)
                                  || (targetCompanionDir is not null && (File.Exists(targetCompanionDir) || Directory.Exists(targetCompanionDir))))
        {
            if (promotedTarget)
            {
                TryDeleteFile(operation.TargetPath);
            }
            if (promotedCompanionDir && targetCompanionDir is not null)
            {
                TryDeleteDirectoryRecursively(targetCompanionDir);
            }
            return Result(
                operation,
                OperationStatus.Conflict,
                "Файл результата уже существует.",
                "target_conflict");
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or InvalidDataException)
        {
            if (promotedTarget)
            {
                TryDeleteFile(operation.TargetPath);
            }
            if (promotedCompanionDir && targetCompanionDir is not null)
            {
                TryDeleteDirectoryRecursively(targetCompanionDir);
            }
            return Result(
                operation,
                OperationStatus.Failed,
                "Не удалось обработать файл.",
                "io_failure");
        }
        finally
        {
            TryDeleteFile(stagingPath);
            if (!string.IsNullOrWhiteSpace(stagingCompanionDir))
            {
                TryDeleteDirectoryRecursively(stagingCompanionDir);
            }
            TryDeleteDirectory(operationRoot);
        }
    }

    private static string ResolveSourceRoot(PlannedOperation operation)
    {
        if (!string.IsNullOrWhiteSpace(operation.SourceRootPath))
        {
            return operation.SourceRootPath;
        }

        var root = Path.GetFullPath(operation.SourcePath);
        var components = operation.RelativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < components.Length; index++)
        {
            root = Path.GetDirectoryName(root) ?? string.Empty;
        }

        return root;
    }

    private static ConversionResult Result(
        PlannedOperation operation,
        OperationStatus status,
        string message,
        string errorCode,
        int? exitCode = null,
        bool timedOut = false,
        bool hasStandardOutput = false,
        bool hasStandardError = false,
        int? hResult = null) =>
        new(
            operation,
            status,
            message,
            new ConversionDiagnostic(
                errorCode,
                exitCode,
                timedOut,
                HasStandardOutput: hasStandardOutput,
                HasStandardError: hasStandardError,
                HResult: hResult));

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void TryDeleteDirectory(string operationRoot)
    {
        try
        {
            var root = _temporaryRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var operation = Path.GetFullPath(operationRoot);
            if (operation.StartsWith(
                    root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(operation))
            {
                Directory.Delete(operation, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or ArgumentException)
        {
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(targetDir, relative));
        }
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            File.Copy(file, Path.Combine(targetDir, relative), overwrite: false);
        }
    }

    private static bool ValidateCompanionDirectory(string directory)
    {
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tiff"
        };

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            if (HasReparsePoint(file)) return false;
            var ext = Path.GetExtension(file);
            if (!allowedExtensions.Contains(ext)) return false;
            var name = Path.GetFileName(file);
            if (name.StartsWith('.') || name.Contains(':')) return false;
        }
        return true;
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

    private static void TryDeleteDirectoryRecursively(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record SourceFileSnapshot(long Length, byte[] Hash)
    {
        public static async Task<SourceFileSnapshot> CreateAsync(
            string path,
            CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return new SourceFileSnapshot(stream.Length, hash);
        }

        public async Task<bool> IsUnchangedAsync(
            string path,
            CancellationToken cancellationToken)
        {
            var current = await CreateAsync(path, cancellationToken);
            return Length == current.Length && Hash.SequenceEqual(current.Hash);
        }
    }
}

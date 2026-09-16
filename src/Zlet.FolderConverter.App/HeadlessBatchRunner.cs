using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zlet.FolderConverter.Core.Models;
using Zlet.FolderConverter.Core.Services;

namespace Zlet.FolderConverter.App;

public sealed class HeadlessBatchConfigurationException(string message) : Exception(message);

public sealed class HeadlessBatchRunner
{
    public const string ReportSchemaVersion = "zlet-converter-headless-report/v1";

    private readonly IFolderScanner _scanner;
    private readonly IConversionPlanner _planner;
    private readonly IConversionProcessor _processor;

    public HeadlessBatchRunner(
        IFolderScanner scanner,
        IConversionPlanner planner,
        IConversionProcessor processor)
    {
        _scanner = scanner;
        _planner = planner;
        _processor = processor;
    }

    public static HeadlessBatchRunner CreateDefault()
    {
        var resolver = new DefaultConversionAdapterResolver();
        return new HeadlessBatchRunner(
            new FileSystemFolderScanner(),
            new ConversionPlanner(resolver),
            new ConversionProcessor(resolver));
    }

    public async Task<int> RunAsync(
        HeadlessBatchCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sourceRoot = Path.GetFullPath(command.SourcePath);
        var destinationRoot = Path.GetFullPath(command.DestinationPath);
        var reportPath = Path.GetFullPath(command.ReportJsonPath);

        if (!Directory.Exists(sourceRoot))
            throw new HeadlessBatchConfigurationException("Source folder does not exist.");
        if (SamePath(sourceRoot, destinationRoot))
            throw new HeadlessBatchConfigurationException("Source and destination folders must be different.");
        if (File.Exists(reportPath) || Directory.Exists(reportPath))
            throw new HeadlessBatchConfigurationException("Report path already exists; headless evidence is never overwritten.");

        var startedUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var scan = await _scanner.ScanAsync(
            sourceRoot,
            command.Recursive,
            destinationRoot,
            reportPath,
            cancellationToken).ConfigureAwait(false);

        var before = new Dictionary<string, HashObservation>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in scan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            before[file.SourcePath] = await TryHashAsync(file.SourcePath, cancellationToken).ConfigureAwait(false);
        }

        var runnableFiles = scan.Files
            .Where(file => SupportsMarkdown(file.Format) && before[file.SourcePath].Success)
            .ToArray();

        var ruleSet = RuleSet.CreateDefault();
        foreach (var format in runnableFiles.Select(file => file.Format).Distinct())
            ruleSet = ruleSet.WithRule(format, ConversionTarget.Markdown);

        var runnableScan = scan with { Files = runnableFiles };
        var plan = _planner.CreatePlan(runnableScan, sourceRoot, destinationRoot, ruleSet);
        var summary = await _processor.ProcessAsync(plan, progress: null, cancellationToken).ConfigureAwait(false);
        var resultsByPath = summary.Results
            .GroupBy(result => result.Operation.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);

        var after = new Dictionary<string, HashObservation>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in scan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            after[file.SourcePath] = await TryHashAsync(file.SourcePath, cancellationToken).ConfigureAwait(false);
        }

        var items = new List<HeadlessBatchItem>(scan.Files.Count);
        foreach (var file in scan.Files.OrderBy(file => NormalizeRelative(file.RelativePath), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var beforeHash = before[file.SourcePath];
            var afterHash = after[file.SourcePath];
            var sourceIntegrity = SourceIntegrity(beforeHash, afterHash);

            ConversionResult? result = null;
            string status;
            string? diagnosticCode;
            string message;
            string? resultRelativePath = null;
            IReadOnlyList<HeadlessDerivedArtifact> artifacts = [];

            if (!beforeHash.Success)
            {
                status = OperationStatus.NotProcessed.ToString();
                diagnosticCode = "source_hash_failed";
                message = "Source identity could not be established before conversion.";
            }
            else if (!SupportsMarkdown(file.Format))
            {
                status = OperationStatus.Unsupported.ToString();
                diagnosticCode = file.Format == SourceFormat.Html
                    ? "html_markdown_unsupported"
                    : "markdown_mapping_unsupported";
                message = file.Format == SourceFormat.Html
                    ? FormatCapabilityCatalog.HtmlRouteBlocker
                    : "Source format does not support the requested Markdown target.";
            }
            else if (!resultsByPath.TryGetValue(file.RelativePath, out result))
            {
                status = OperationStatus.NotProcessed.ToString();
                diagnosticCode = "missing_conversion_result";
                message = "No conversion result was produced for a planned source item.";
            }
            else
            {
                var completedResult = result!;
                status = completedResult.Status.ToString();
                diagnosticCode = completedResult.Diagnostic?.ErrorCode;
                message = completedResult.Message;
                resultRelativePath = string.IsNullOrWhiteSpace(completedResult.Operation.ResultRelativePath)
                    ? RelativeTo(destinationRoot, completedResult.Operation.TargetPath)
                    : NormalizeRelative(completedResult.Operation.ResultRelativePath);
                artifacts = await BuildArtifactsAsync(completedResult, destinationRoot, cancellationToken).ConfigureAwait(false);
            }

            var artifactIntegrity = status == OperationStatus.Succeeded.ToString()
                ? artifacts.Count > 0 && artifacts.All(artifact => artifact.Sha256 is not null)
                    ? "PASS"
                    : "FAIL"
                : artifacts.Any(artifact => artifact.Sha256 is null)
                    ? "FAIL"
                    : "NOT_APPLICABLE";

            items.Add(new HeadlessBatchItem(
                NormalizeRelative(file.RelativePath),
                file.Format.ToString(),
                ConversionTarget.Markdown.ToString(),
                status,
                diagnosticCode,
                message,
                resultRelativePath,
                file.SizeBytes,
                beforeHash.Sha256,
                afterHash.Sha256,
                sourceIntegrity,
                artifactIntegrity,
                artifacts));
        }

        stopwatch.Stop();
        var counts = HeadlessBatchCounts.From(items);
        var hasIssues = scan.Files.Count == 0
                        || scan.Errors.Count > 0
                        || counts.Unsupported > 0
                        || counts.EngineUnavailable > 0
                        || counts.Failed > 0
                        || counts.Conflict > 0
                        || counts.NotProcessed > 0
                        || items.Any(item => item.SourceIntegrity != "PASS" || item.ArtifactIntegrity == "FAIL");

        var report = new HeadlessBatchReport(
            ReportSchemaVersion,
            ProductIdentity.Name,
            ProductIdentity.Version,
            "Markdown",
            command.Recursive,
            Path.GetFileName(sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Path.GetFileName(destinationRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            startedUtc,
            DateTimeOffset.UtcNow,
            stopwatch.ElapsedMilliseconds,
            hasIssues ? "COMPLETED_WITH_ISSUES" : "COMPLETED",
            counts,
            scan.Errors.Select(error => new HeadlessScanError(
                RelativeOrName(sourceRoot, error.Path),
                error.Message)).ToArray(),
            items);

        await WriteReportAsync(reportPath, report, cancellationToken).ConfigureAwait(false);
        return hasIssues ? HeadlessBatchCommand.ExitCompletedWithIssues : HeadlessBatchCommand.ExitSuccess;
    }

    private static bool SupportsMarkdown(SourceFormat format) =>
        FormatCapabilityCatalog.Get(format).Supports(ConversionTarget.Markdown);

    private static async Task<IReadOnlyList<HeadlessDerivedArtifact>> BuildArtifactsAsync(
        ConversionResult result,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        if (result.Status != OperationStatus.Succeeded)
            return [];

        var paths = new List<(string Path, string Kind)>();
        if (!string.IsNullOrWhiteSpace(result.Operation.TargetPath))
            paths.Add((result.Operation.TargetPath, "primary"));
        if (result.CompanionFiles is not null)
            paths.AddRange(result.CompanionFiles.Select(path => (path, "companion")));

        var artifacts = new List<HeadlessDerivedArtifact>(paths.Count);
        foreach (var entry in paths.OrderBy(entry => RelativeTo(destinationRoot, entry.Path), StringComparer.Ordinal))
        {
            var observation = await TryHashAsync(entry.Path, cancellationToken).ConfigureAwait(false);
            artifacts.Add(new HeadlessDerivedArtifact(
                entry.Kind,
                RelativeTo(destinationRoot, entry.Path),
                observation.SizeBytes,
                observation.Sha256));
        }
        return artifacts;
    }

    private static async Task<HashObservation> TryHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return new HashObservation(true, stream.Length, Convert.ToHexString(hash).ToLowerInvariant());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new HashObservation(false, null, null);
        }
    }

    private static string SourceIntegrity(HashObservation before, HashObservation after)
    {
        if (!before.Success || !after.Success)
            return "UNKNOWN";
        return before.SizeBytes == after.SizeBytes
               && string.Equals(before.Sha256, after.Sha256, StringComparison.Ordinal)
            ? "PASS"
            : "FAIL";
    }

    private static async Task WriteReportAsync(
        string reportPath,
        HeadlessBatchReport report,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new HeadlessBatchConfigurationException("Report path must have a parent directory.");
        Directory.CreateDirectory(directory);

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.Serialize(report, options) + Environment.NewLine;
        var temporary = reportPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, reportPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string RelativeTo(string root, string path) =>
        NormalizeRelative(Path.GetRelativePath(root, path));

    private static string RelativeOrName(string root, string path)
    {
        try
        {
            var relative = Path.GetRelativePath(root, path);
            return relative.StartsWith("..", StringComparison.Ordinal)
                ? Path.GetFileName(path)
                : NormalizeRelative(relative);
        }
        catch
        {
            return Path.GetFileName(path);
        }
    }

    private static string NormalizeRelative(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private sealed record HashObservation(bool Success, long? SizeBytes, string? Sha256);
}

public sealed record HeadlessBatchReport(
    string SchemaVersion,
    string ProductName,
    string ProductVersion,
    string RequestedTarget,
    bool Recursive,
    string SourceRootName,
    string DestinationRootName,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    long ElapsedMs,
    string FinalState,
    HeadlessBatchCounts Counts,
    IReadOnlyList<HeadlessScanError> ScanErrors,
    IReadOnlyList<HeadlessBatchItem> Items);

public sealed record HeadlessBatchCounts(
    int Total,
    int Succeeded,
    int Unsupported,
    int EngineUnavailable,
    int Failed,
    int Conflict,
    int Skipped,
    int Cancelled,
    int NotProcessed)
{
    public static HeadlessBatchCounts From(IReadOnlyList<HeadlessBatchItem> items) => new(
        items.Count,
        Count(items, OperationStatus.Succeeded),
        Count(items, OperationStatus.Unsupported),
        Count(items, OperationStatus.EngineUnavailable),
        Count(items, OperationStatus.Failed),
        Count(items, OperationStatus.Conflict),
        Count(items, OperationStatus.Skipped),
        Count(items, OperationStatus.Cancelled),
        Count(items, OperationStatus.NotProcessed));

    private static int Count(IReadOnlyList<HeadlessBatchItem> items, OperationStatus status) =>
        items.Count(item => string.Equals(item.Status, status.ToString(), StringComparison.Ordinal));
}

public sealed record HeadlessBatchItem(
    string SourceRelativePath,
    string SourceFormat,
    string RequestedTarget,
    string Status,
    string? DiagnosticCode,
    string Message,
    string? ResultRelativePath,
    long SourceSizeBytes,
    string? SourceSha256Before,
    string? SourceSha256After,
    string SourceIntegrity,
    string ArtifactIntegrity,
    IReadOnlyList<HeadlessDerivedArtifact> Artifacts);

public sealed record HeadlessDerivedArtifact(
    string Kind,
    string RelativePath,
    long? SizeBytes,
    string? Sha256);

public sealed record HeadlessScanError(string RelativePath, string Message);

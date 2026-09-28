using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Zlet.Quality.Core;

/// <summary>
/// Immutable evidence-run adapter over the direct in-process Quality Core API.
/// Benchmark/CLI callers use this service when they need persisted artifacts;
/// application consumers should use <see cref="QualityEvaluationService"/>.
/// </summary>
public sealed class QualityRunService
{
    private readonly QualityEvaluationService _evaluationService;

    public QualityRunService()
        : this(new QualityEvaluationService())
    {
    }

    public QualityRunService(
        IEnumerable<ISourceInspector> sourceInspectors,
        IMarkdownInspector markdownInspector,
        IQualityComparator qualityComparator)
        : this(new QualityEvaluationService(
            sourceInspectors,
            markdownInspector,
            qualityComparator))
    {
    }

    public QualityRunService(QualityEvaluationService evaluationService)
    {
        _evaluationService = evaluationService
            ?? throw new ArgumentNullException(nameof(evaluationService));
    }

    public Task<QualityRunManifest> CreateSkeletonAsync(
        string sourcePath,
        string markdownPath,
        string outputDirectory,
        string? runId = null,
        CancellationToken cancellationToken = default) =>
        CreateAsync(
            sourcePath,
            markdownPath,
            outputDirectory,
            new QualityRunOptions(RunId: runId),
            cancellationToken);

    public async Task<QualityRunManifest> CreateAsync(
        string sourcePath,
        string markdownPath,
        string outputDirectory,
        QualityRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(markdownPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "Source document was not found.",
                sourcePath);
        }

        if (!File.Exists(markdownPath))
        {
            throw new FileNotFoundException(
                "Markdown output was not found.",
                markdownPath);
        }

        options ??= new QualityRunOptions();

        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        EnsureWritableRunDirectory(fullOutputDirectory);

        var sourceDirectory = Path.Combine(fullOutputDirectory, "source");
        var outputDirectoryPath = Path.Combine(fullOutputDirectory, "output");
        var analysisDirectory = Path.Combine(fullOutputDirectory, "analysis");
        var reportDirectory = Path.Combine(fullOutputDirectory, "report");

        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(outputDirectoryPath);
        Directory.CreateDirectory(analysisDirectory);
        Directory.CreateDirectory(reportDirectory);

        var sourceHash = await ComputeSha256Async(
            sourcePath,
            cancellationToken);
        var markdownHash = await ComputeSha256Async(
            markdownPath,
            cancellationToken);

        var evaluation = await _evaluationService.EvaluateAsync(
            sourcePath,
            markdownPath,
            cancellationToken);

        var corpus = new CorpusIdentity(
            NormalizeRequired(options.CorpusId, "ad-hoc"),
            NormalizeRequired(options.CorpusVersion, "unversioned"));

        var engine = new ConversionEngineIdentity(
            NormalizeRequired(options.EngineName, "unidentified"),
            NormalizeOptional(options.EngineVersion),
            NormalizeOptional(options.EngineBuildIdentifier),
            NormalizeOptional(options.EngineBinarySha256)?.ToLowerInvariant(),
            NormalizeRequired(options.EngineProfile, "unknown"),
            NormalizeOptional(options.EngineConfiguration));

        var evidenceKey = ComputeEvidenceKey(
            sourceHash,
            markdownHash,
            corpus,
            engine,
            evaluation.Pipeline);

        var resolvedRunId = string.IsNullOrWhiteSpace(options.RunId)
            ? $"run-{evidenceKey[..12]}"
            : options.RunId.Trim();

        var sourceArtifactPath = Path.Combine(
            "source",
            Path.GetFileName(sourcePath));
        var markdownArtifactPath = Path.Combine(
            "output",
            Path.GetFileName(markdownPath));

        File.Copy(
            sourcePath,
            Path.Combine(fullOutputDirectory, sourceArtifactPath),
            overwrite: false);
        File.Copy(
            markdownPath,
            Path.Combine(fullOutputDirectory, markdownArtifactPath),
            overwrite: false);

        var sourceFactsPath = Path.Combine(
            "source",
            "sourcefacts.json");
        var markdownFactsPath = Path.Combine(
            "analysis",
            "markdownfacts.json");
        var qualityReportPath = Path.Combine(
            "report",
            "quality-report.json");

        await WriteStageArtifactAsync(
            Path.Combine(fullOutputDirectory, sourceFactsPath),
            QualityCoreVersion.SourceFactsSchemaVersion,
            "source-analysis",
            evaluation.SourceStatus,
            evaluation.SourceFacts,
            cancellationToken);

        await WriteStageArtifactAsync(
            Path.Combine(fullOutputDirectory, markdownFactsPath),
            QualityCoreVersion.MarkdownFactsSchemaVersion,
            "markdown-analysis",
            evaluation.MarkdownStatus,
            evaluation.MarkdownFacts,
            cancellationToken);

        await WriteStageArtifactAsync(
            Path.Combine(fullOutputDirectory, qualityReportPath),
            QualityCoreVersion.QualityReportSchemaVersion,
            "comparison",
            evaluation.ComparisonStatus,
            evaluation.Result,
            cancellationToken);

        var stages = new[]
        {
            await CreateStageAsync(
                "source-analysis",
                evaluation.SourceStatus,
                sourceFactsPath,
                fullOutputDirectory,
                cancellationToken),
            await CreateStageAsync(
                "markdown-analysis",
                evaluation.MarkdownStatus,
                markdownFactsPath,
                fullOutputDirectory,
                cancellationToken),
            await CreateStageAsync(
                "comparison",
                evaluation.ComparisonStatus,
                qualityReportPath,
                fullOutputDirectory,
                cancellationToken)
        };

        var manifest = new QualityRunManifest(
            QualityCoreVersion.RunSchemaVersion,
            QualityCoreVersion.Version,
            resolvedRunId,
            evidenceKey,
            corpus,
            engine,
            CaptureRuntimeIdentity(),
            evaluation.Pipeline,
            new FileArtifact(
                NormalizePath(sourceArtifactPath),
                sourceHash,
                new FileInfo(sourcePath).Length,
                GetFormat(sourcePath)),
            new FileArtifact(
                NormalizePath(markdownArtifactPath),
                markdownHash,
                new FileInfo(markdownPath).Length,
                GetFormat(markdownPath)),
            stages);

        var manifestPath = Path.Combine(
            fullOutputDirectory,
            "run.json");

        await File.WriteAllTextAsync(
            manifestPath,
            QualityJson.Serialize(manifest),
            cancellationToken);

        return manifest;
    }

    private static async Task WriteStageArtifactAsync<T>(
        string outputPath,
        string schemaVersion,
        string stage,
        string status,
        T? document,
        CancellationToken cancellationToken)
        where T : class
    {
        if (document is null)
        {
            await WritePlaceholderAsync(
                outputPath,
                schemaVersion,
                stage,
                status,
                cancellationToken);
            return;
        }

        await File.WriteAllTextAsync(
            outputPath,
            QualityJson.Serialize(document),
            cancellationToken);
    }

    private static async Task<PipelineStage> CreateStageAsync(
        string name,
        string status,
        string relativePath,
        string rootDirectory,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(
            rootDirectory,
            relativePath);

        return new PipelineStage(
            name,
            status,
            NormalizePath(relativePath),
            await ComputeSha256Async(
                fullPath,
                cancellationToken),
            new FileInfo(fullPath).Length);
    }

    private static RuntimeIdentity CaptureRuntimeIdentity() =>
        new(
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription);

    private static void EnsureWritableRunDirectory(
        string outputDirectory)
    {
        if (Directory.Exists(outputDirectory) &&
            Directory.EnumerateFileSystemEntries(outputDirectory).Any())
        {
            throw new IOException(
                $"Run directory already exists and is not empty: {outputDirectory}. " +
                "Historical run artifacts are immutable.");
        }

        Directory.CreateDirectory(outputDirectory);
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);
        return Convert.ToHexString(hash)
            .ToLowerInvariant();
    }

    private static string ComputeEvidenceKey(
        string sourceHash,
        string markdownHash,
        CorpusIdentity corpus,
        ConversionEngineIdentity engine,
        QualityPipelineIdentity pipeline)
    {
        var material = QualityJson.Serialize(new
        {
            schemaVersion = QualityCoreVersion.RunSchemaVersion,
            sourceSha256 = sourceHash,
            markdownSha256 = markdownHash,
            corpus,
            engine,
            pipeline
        });

        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(material));

        return Convert.ToHexString(hash)
            .ToLowerInvariant();
    }

    private static async Task WritePlaceholderAsync(
        string path,
        string schemaVersion,
        string stage,
        string status,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            schemaVersion,
            qualityCoreVersion = QualityCoreVersion.Version,
            stage,
            status
        };

        await File.WriteAllTextAsync(
            path,
            QualityJson.Serialize(payload),
            cancellationToken);
    }

    private static string GetFormat(string path)
    {
        var extension = Path.GetExtension(path)
            .TrimStart('.')
            .ToLowerInvariant();

        return extension is "md" or "markdown"
            ? "markdown"
            : extension;
    }

    private static string NormalizeRequired(
        string? value,
        string fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string NormalizePath(string path) =>
        path.Replace(
            Path.DirectorySeparatorChar,
            '/');
}

namespace Zlet.Quality.Core;

/// <summary>
/// In-memory result of the deterministic Quality Core evaluation pipeline.
/// This contract is intended for direct library consumers such as Zlet Converter.
/// It does not create benchmark/evidence-run artifacts.
/// </summary>
public sealed record QualityEvaluation(
    string ApiVersion,
    QualityPipelineIdentity Pipeline,
    string SourceStatus,
    string MarkdownStatus,
    string ComparisonStatus,
    SourceFactsDocument? SourceFacts,
    MarkdownFactsDocument? MarkdownFacts,
    QualityResultDocument? Result);

/// <summary>
/// Direct embeddable Quality Core entry point.
///
/// The service performs source inspection, Markdown inspection and deterministic
/// comparison in-process. It has no dependency on Python, Docker, the benchmark
/// harness, or subprocess execution.
/// </summary>
public sealed class QualityEvaluationService
{
    public const string ApiVersion = QualityCoreVersion.EvaluationApiVersion;

    private readonly IReadOnlyList<ISourceInspector> _sourceInspectors;
    private readonly IMarkdownInspector _markdownInspector;
    private readonly IQualityComparator _qualityComparator;

    public QualityEvaluationService()
        : this(
            new ISourceInspector[]
            {
                new DocxSourceInspector(),
                new PptxSourceInspector(),
                new XlsxSourceInspector(),
                new PdfSourceInspector(),
                new HtmlSourceInspector(),
                new TxtSourceInspector()
            },
            new MarkdigMarkdownInspector(),
            new ExactQualityComparator())
    {
    }

    public QualityEvaluationService(
        IEnumerable<ISourceInspector> sourceInspectors,
        IMarkdownInspector markdownInspector,
        IQualityComparator qualityComparator)
    {
        _sourceInspectors = sourceInspectors?.ToArray()
            ?? throw new ArgumentNullException(nameof(sourceInspectors));
        _markdownInspector = markdownInspector
            ?? throw new ArgumentNullException(nameof(markdownInspector));
        _qualityComparator = qualityComparator
            ?? throw new ArgumentNullException(nameof(qualityComparator));
    }

    public async Task<QualityEvaluation> EvaluateAsync(
        string sourcePath,
        string markdownPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(markdownPath);

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

        var sourceInspector = _sourceInspectors.FirstOrDefault(candidate =>
            candidate.Supports(sourcePath));

        var sourceInspectorIdentity = sourceInspector is null
            ? new ComponentIdentity(
                $"unsupported:{GetFormat(sourcePath)}",
                "n/a")
            : new ComponentIdentity(
                sourceInspector.InspectorId,
                sourceInspector.InspectorVersion);

        var markdownInspectorIdentity = _markdownInspector.Supports(markdownPath)
            ? new ComponentIdentity(
                _markdownInspector.InspectorId,
                _markdownInspector.InspectorVersion)
            : new ComponentIdentity(
                $"unsupported:{GetFormat(markdownPath)}",
                "n/a");

        var pipeline = new QualityPipelineIdentity(
            QualityCoreVersion.Version,
            sourceInspectorIdentity,
            markdownInspectorIdentity,
            new ComponentIdentity(
                _qualityComparator.ComparatorId,
                _qualityComparator.ComparatorVersion),
            _qualityComparator.MatcherVersion,
            _qualityComparator.MetricsVersion,
            QualityCoreVersion.QualityReportSchemaVersion);

        var sourceInspection = await InspectSourceAsync(
            sourcePath,
            sourceInspector,
            cancellationToken);

        var markdownInspection = await InspectMarkdownAsync(
            markdownPath,
            cancellationToken);

        if (sourceInspection.Document is null)
        {
            return new QualityEvaluation(
                ApiVersion,
                pipeline,
                sourceInspection.Status,
                markdownInspection.Status,
                sourceInspection.Status,
                null,
                markdownInspection.Document,
                null);
        }

        if (markdownInspection.Document is null)
        {
            return new QualityEvaluation(
                ApiVersion,
                pipeline,
                sourceInspection.Status,
                markdownInspection.Status,
                markdownInspection.Status,
                sourceInspection.Document,
                null,
                null);
        }

        var result = _qualityComparator.Compare(
            sourceInspection.Document,
            markdownInspection.Document);

        return new QualityEvaluation(
            ApiVersion,
            pipeline,
            sourceInspection.Status,
            markdownInspection.Status,
            "EVALUATED",
            sourceInspection.Document,
            markdownInspection.Document,
            result);
    }

    private async Task<StageResult<SourceFactsDocument>> InspectSourceAsync(
        string sourcePath,
        ISourceInspector? inspector,
        CancellationToken cancellationToken)
    {
        if (inspector is null)
        {
            return new StageResult<SourceFactsDocument>(
                "NOT_EVALUATED_SOURCE_UNSUPPORTED",
                null);
        }

        try
        {
            var sourceFacts = await inspector.InspectAsync(
                sourcePath,
                cancellationToken);

            return new StageResult<SourceFactsDocument>(
                "EVALUATED",
                sourceFacts);
        }
        catch (Exception exception) when (IsInvalidSourceException(exception))
        {
            return new StageResult<SourceFactsDocument>(
                "NOT_EVALUATED_SOURCE_INVALID",
                null);
        }
    }

    private async Task<StageResult<MarkdownFactsDocument>> InspectMarkdownAsync(
        string markdownPath,
        CancellationToken cancellationToken)
    {
        if (!_markdownInspector.Supports(markdownPath))
        {
            return new StageResult<MarkdownFactsDocument>(
                "NOT_EVALUATED_OUTPUT_UNSUPPORTED",
                null);
        }

        var markdownFacts = await _markdownInspector.InspectAsync(
            markdownPath,
            cancellationToken);

        return new StageResult<MarkdownFactsDocument>(
            "EVALUATED",
            markdownFacts);
    }

    private static bool IsInvalidSourceException(Exception exception) =>
        exception is InvalidDataException
            or FileFormatException
            or System.Xml.XmlException
            or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException;

    private static string GetFormat(string path)
    {
        var extension = Path.GetExtension(path)
            .TrimStart('.')
            .ToLowerInvariant();

        return extension is "md" or "markdown"
            ? "markdown"
            : extension;
    }

    private sealed record StageResult<T>(
        string Status,
        T? Document)
        where T : class;
}

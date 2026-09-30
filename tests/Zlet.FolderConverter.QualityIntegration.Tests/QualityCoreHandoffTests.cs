using Zlet.FolderConverter.Core.Models;
using Zlet.FolderConverter.Core.Services;
using Zlet.FolderConverter.Headless;
using Zlet.Quality.Core;

namespace Zlet.FolderConverter.QualityIntegration.Tests;

public sealed class QualityCoreHandoffTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "zlet-quality-handoff-tests",
        Guid.NewGuid().ToString("N"));

    public QualityCoreHandoffTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task EmbeddedEvaluator_ExactTxtControl_UsesQualifiedSubset()
    {
        var source = Path.Combine(_root, "source.txt");
        var markdown = Path.Combine(_root, "result.md");
        await File.WriteAllTextAsync(source, "Alpha\nBeta");
        await File.WriteAllTextAsync(markdown, "Alpha\nBeta");

        var assessment = await new EmbeddedConversionQualityEvaluator().EvaluateAsync(
            Operation(source, markdown, SourceFormat.Txt),
            CancellationToken.None);

        Assert.Equal("zlet-converter-quality-handoff/0.1", assessment.QualificationProfileId);
        Assert.Equal("QUALIFIED_BOUNDED", assessment.QualificationStatus);
        Assert.Equal("DIAGNOSTIC_ONLY", assessment.ProductDecisionStatus);
        Assert.Equal("0.1.0", assessment.Pipeline!.QualityCoreVersion);
        Assert.Equal("zlet-quality-core-api/0.1", assessment.Pipeline.EvaluationApiVersion);
        Assert.Equal("0.4.0", assessment.Pipeline.ComparatorVersion);
        Assert.Equal("conversion-fidelity/0.4.0", assessment.Pipeline.MetricsVersion);
        Assert.Equal("zlet-cqs/0.3.0", assessment.Pipeline.CqsMethodologyVersion);

        Assert.Equal(1d, Metric(assessment, "text_recall").Value);
        Assert.Equal(1d, Metric(assessment, "text_precision").Value);
        Assert.DoesNotContain(assessment.Metrics, metric => metric.Name == "heading_precision");
        Assert.Empty(assessment.Findings);
        Assert.NotNull(assessment.Projection);
        Assert.Equal("zlet-cqs/0.3.0", assessment.Projection!.MethodologyVersion);
        Assert.Equal("PARTIAL_EVIDENCE", assessment.Projection.Status);
        Assert.Equal(100d, assessment.Projection.FidelityScore);
        Assert.Equal(66.7d, assessment.Projection.EvaluationCoverage);
    }

    [Fact]
    public async Task VendoredCore_OutputOnlyHeadingNoiseRegression_RemainsFixed()
    {
        var source = Path.Combine(_root, "plain.txt");
        var markdown = Path.Combine(_root, "heading.md");
        await File.WriteAllTextAsync(source, "Alpha");
        await File.WriteAllTextAsync(markdown, "# Alpha");

        var evaluation = await new QualityEvaluationService().EvaluateAsync(
            source,
            markdown,
            CancellationToken.None);
        Assert.NotNull(evaluation.SourceFacts);
        Assert.NotNull(evaluation.Result);

        var headingPrecision = Assert.Single(
            evaluation.Result!.Metrics,
            metric => metric.Name == "heading_precision");
        Assert.Equal("EVALUATED", headingPrecision.Status);
        Assert.Equal(0d, headingPrecision.Value);

        var projection = CandidateScoreProjector.Project(
            evaluation.SourceFacts!,
            evaluation.Result!);
        Assert.Equal(75d, projection.Cqs);
        Assert.Equal("SCORED", projection.Status);
        Assert.Equal("ELIGIBLE_DOCUMENT_LEVEL", projection.ComparisonEligibility);
    }

    [Fact]
    public async Task ConversionProcessor_AttachesQuality_ButQualityLossDoesNotFailConversion()
    {
        var source = Path.Combine(_root, "convert.txt");
        var markdown = Path.Combine(_root, "convert.md");
        await File.WriteAllTextAsync(source, "Alpha original");

        var processor = new ConversionProcessor(
            new SingleResolver(new LossyTxtAdapter(markdown)),
            new EmbeddedConversionQualityEvaluator());

        var summary = await processor.ProcessAsync(
            [Operation(source, markdown, SourceFormat.Txt)],
            progress: null,
            CancellationToken.None);

        var result = Assert.Single(summary.Results);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.NotNull(result.Quality);
        Assert.Equal(0d, Metric(result.Quality!, "text_recall").Value);
    }

    [Fact]
    public async Task ConversionProcessor_QualityFailure_IsFailClosedWithoutChangingConversionSuccess()
    {
        var source = Path.Combine(_root, "convert2.txt");
        var markdown = Path.Combine(_root, "convert2.md");
        await File.WriteAllTextAsync(source, "Alpha");

        var processor = new ConversionProcessor(
            new SingleResolver(new LossyTxtAdapter(markdown)),
            new ThrowingQualityEvaluator());

        var result = Assert.Single((await processor.ProcessAsync(
            [Operation(source, markdown, SourceFormat.Txt)],
            progress: null,
            CancellationToken.None)).Results);

        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.NotNull(result.Quality);
        Assert.Equal("NOT_EVALUATED_INTERNAL_ERROR", result.Quality!.ComparisonStatus);
    }

    [Fact]
    public async Task HeadlessReport_ContainsPrivacySafeQualifiedQualitySummary()
    {
        var sourceDir = Path.Combine(_root, "source-dir");
        var destinationDir = Path.Combine(_root, "destination-dir");
        var reportPath = Path.Combine(_root, "headless-report.json");
        Directory.CreateDirectory(sourceDir);
        var source = Path.Combine(sourceDir, "notes.txt");
        await File.WriteAllTextAsync(source, "Alpha\nBeta");

        var resolver = new DefaultConversionAdapterResolver(
        [
            new TxtMarkdownConversionAdapter(new OutputResultValidator())
        ]);
        var runner = new HeadlessBatchRunner(
            new FileSystemFolderScanner(),
            new ConversionPlanner(resolver),
            new ConversionProcessor(
                resolver,
                new EmbeddedConversionQualityEvaluator()));

        var exitCode = await runner.RunAsync(
            new HeadlessBatchCommand(
                sourceDir,
                destinationDir,
                reportPath,
                Recursive: false),
            CancellationToken.None);

        Assert.Equal(HeadlessBatchCommand.ExitSuccess, exitCode);
        var json = await File.ReadAllTextAsync(reportPath);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(
            "zlet-converter-headless-report/v2",
            root.GetProperty("schemaVersion").GetString());
        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        var quality = item.GetProperty("quality");
        Assert.Equal(
            "zlet-converter-quality-handoff/0.1",
            quality.GetProperty("qualificationProfileId").GetString());
        Assert.Equal(
            "QUALIFIED_BOUNDED",
            quality.GetProperty("qualificationStatus").GetString());
        Assert.Equal(
            "DIAGNOSTIC_ONLY",
            quality.GetProperty("productDecisionStatus").GetString());
        Assert.DoesNotContain("sourceFacts", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("markdownFacts", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmbeddedEvaluator_PdfProfile_ExposesCoverageOnly()
    {
        var source = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "F01_simple_text.pdf");
        var markdown = Path.Combine(_root, "pdf.md");
        await File.WriteAllTextAsync(markdown, "Alpha beta gamma");

        var assessment = await new EmbeddedConversionQualityEvaluator().EvaluateAsync(
            Operation(source, markdown, SourceFormat.Pdf),
            CancellationToken.None);

        Assert.Equal("COVERAGE_ONLY", assessment.QualificationStatus);
        Assert.Null(assessment.Projection);
        var coverage = Assert.Single(assessment.Metrics);
        Assert.Equal("evaluation_coverage_exact", coverage.Name);
        Assert.Equal("EVALUATED", coverage.Status);
        Assert.Equal(0d, coverage.Value);
        Assert.Empty(assessment.Findings);
        Assert.Equal("EVALUATED", assessment.ComparisonStatus);
    }

    [Fact]
    public void VendoredCore_CoverageRoundingRegression_RemainsFixed()
    {
        var facts = Enumerable.Range(0, 4001)
            .Select(index =>
            {
                IReadOnlyDictionary<string, FactCertainty>? propertyCertainty =
                    index == 4000
                        ? new Dictionary<string, FactCertainty>
                        {
                            [FactProperty.Order] = FactCertainty.INFERRED
                        }
                        : null;

                return new SourceFact(
                    $"p{index}",
                    FactKind.TEXT_BLOCK,
                    $"Text {index}",
                    index,
                    $"p{index}",
                    FactCertainty.SOURCE_EXACT,
                    null,
                    propertyCertainty);
            })
            .ToArray();

        var source = new SourceFactsDocument(
            QualityCoreVersion.SourceFactsSchemaVersion,
            "handoff-test",
            "1",
            "txt",
            new string('a', 64),
            facts);

        var result = new QualityResultDocument(
            QualityCoreVersion.QualityReportSchemaVersion,
            "test-comparator",
            "1",
            "1",
            "test-metrics/1",
            new string('a', 64),
            new string('b', 64),
            [
                Metric("text_recall", 1d),
                Metric("text_precision", 1d),
                Metric("pairwise_order_accuracy", 1d),
                Metric("output_nonempty", 1d),
                Metric("content_token_length_ratio", 1d)
            ],
            []);

        var projection = CandidateScoreProjector.Project(source, result);
        var order = Assert.Single(
            projection.Families,
            family => family.Family == "reading_order");

        Assert.True(order.Coverage < 100d);
        Assert.Equal(8_002_000, order.SourceOpportunities);
        Assert.Equal(7_998_000, order.EvaluableOpportunities);
        Assert.Equal("PARTIAL_EVIDENCE", projection.Status);
        Assert.Equal(
            "NOT_ELIGIBLE_PARTIAL_EVIDENCE",
            projection.ComparisonEligibility);
    }

    private static ConversionQualityMetric Metric(
        ConversionQualityAssessment assessment,
        string name) =>
        Assert.Single(assessment.Metrics, metric => metric.Name == name);

    private static QualityMetric Metric(string name, double value) =>
        new(
            name,
            "EVALUATED",
            value,
            (int)Math.Round(value * 100),
            100,
            "handoff-test");

    private static PlannedOperation Operation(
        string source,
        string markdown,
        SourceFormat format) =>
        new(
            source,
            Path.GetFileName(source),
            format,
            ConversionTarget.Markdown,
            ".md",
            markdown,
            AdapterAvailable: true,
            OperationStatus.Ready,
            "Ready");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    private sealed class SingleResolver(IConversionAdapter adapter) : IConversionAdapterResolver
    {
        public IConversionAdapter? Resolve(SourceFormat sourceFormat, ConversionTarget target) =>
            adapter.CanConvert(sourceFormat, target) ? adapter : null;
    }

    private sealed class LossyTxtAdapter(string output) : IConversionAdapter
    {
        public bool IsAvailable => true;
        public string AvailabilityMessage => "available";
        public bool CanConvert(SourceFormat sourceFormat, ConversionTarget target) =>
            sourceFormat == SourceFormat.Txt && target == ConversionTarget.Markdown;

        public Task<ConversionResult> ConvertAsync(
            PlannedOperation operation,
            CancellationToken cancellationToken) =>
            ConvertAsync(operation, null, cancellationToken);

        public async Task<ConversionResult> ConvertAsync(
            PlannedOperation operation,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(output, "Beta replacement", cancellationToken);
            return new ConversionResult(
                operation,
                OperationStatus.Succeeded,
                "converted");
        }
    }

    private sealed class ThrowingQualityEvaluator : IConversionQualityEvaluator
    {
        public Task<ConversionQualityAssessment> EvaluateAsync(
            PlannedOperation operation,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("controlled test failure");
    }
}

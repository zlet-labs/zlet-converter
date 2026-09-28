using System.Text.Json;
using Zlet.FolderConverter.Core.Models;
using Zlet.Quality.Core;

namespace Zlet.FolderConverter.Core.Services;

public sealed class EmbeddedConversionQualityEvaluator : IConversionQualityEvaluator
{
    public const string QualificationProfileId = "zlet-converter-quality-handoff/0.1";
    public const string ProductDecisionStatus = "DIAGNOSTIC_ONLY";
    private const string QualificationResourceName =
        "Zlet.FolderConverter.QualityHandoff.v0_1.qualification.json";

    private readonly QualityEvaluationService _service;

    public EmbeddedConversionQualityEvaluator()
        : this(new QualityEvaluationService())
    {
    }

    public EmbeddedConversionQualityEvaluator(QualityEvaluationService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<ConversionQualityAssessment> EvaluateAsync(
        PlannedOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var profile = QualifiedProfile.For(operation.SourceFormat);
        if (profile is null)
        {
            return NotQualified();
        }

        try
        {
            var evaluation = await _service.EvaluateAsync(
                operation.SourcePath,
                operation.TargetPath,
                cancellationToken).ConfigureAwait(false);

            var metrics = evaluation.Result?.Metrics
                .Where(metric => profile.Metrics.Contains(metric.Name))
                .Select(metric => new ConversionQualityMetric(
                    metric.Name,
                    metric.Status,
                    metric.Value,
                    metric.Numerator,
                    metric.Denominator,
                    metric.Method))
                .ToArray()
                ?? [];

            var findings = evaluation.Result?.Findings
                .Where(finding => profile.Findings.Contains(finding.Code))
                .Select(finding => new ConversionQualityFinding(
                    finding.Code,
                    finding.Severity))
                .ToArray()
                ?? [];

            var pipeline = new ConversionQualityPipeline(
                evaluation.Pipeline.QualityCoreVersion,
                evaluation.ApiVersion,
                evaluation.Pipeline.SourceInspector.Id,
                evaluation.Pipeline.SourceInspector.Version,
                evaluation.Pipeline.MarkdownInspector.Id,
                evaluation.Pipeline.MarkdownInspector.Version,
                evaluation.Pipeline.Comparator.Id,
                evaluation.Pipeline.Comparator.Version,
                evaluation.Pipeline.MatcherVersion,
                evaluation.Pipeline.MetricsVersion,
                evaluation.Pipeline.ReportSchemaVersion,
                QualityScoreMethodology.Version);

            return new ConversionQualityAssessment(
                QualificationProfileId,
                profile.Status,
                ProductDecisionStatus,
                evaluation.SourceStatus,
                evaluation.MarkdownStatus,
                evaluation.ComparisonStatus,
                pipeline,
                metrics,
                findings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return InternalError(profile.Status);
        }
    }

    public static ConversionQualityAssessment InternalError(string qualificationStatus) =>
        new(
            QualificationProfileId,
            qualificationStatus,
            ProductDecisionStatus,
            "NOT_EVALUATED_INTERNAL_ERROR",
            "NOT_EVALUATED_INTERNAL_ERROR",
            "NOT_EVALUATED_INTERNAL_ERROR",
            null,
            [],
            []);

    private static ConversionQualityAssessment NotQualified() =>
        new(
            QualificationProfileId,
            "NOT_QUALIFIED",
            ProductDecisionStatus,
            "NOT_EVALUATED_PRODUCT_PROFILE",
            "NOT_EVALUATED_PRODUCT_PROFILE",
            "NOT_EVALUATED_PRODUCT_PROFILE",
            null,
            [],
            []);

    private sealed record Profile(
        string Status,
        HashSet<string> Metrics,
        HashSet<string> Findings);

    private sealed record QualificationDocument(
        string ProfileId,
        Dictionary<string, QualificationFormat> Formats);

    private sealed record QualificationFormat(
        string Status,
        string[] Metrics,
        string[] Findings);

    private static class QualifiedProfile
    {
        private static readonly IReadOnlyDictionary<string, Profile> Profiles = Load();

        public static Profile? For(SourceFormat format)
        {
            var key = format.ToString().ToLowerInvariant();
            return Profiles.TryGetValue(key, out var profile)
                && profile.Status is "QUALIFIED_BOUNDED" or "COVERAGE_ONLY"
                    ? profile
                    : null;
        }

        private static IReadOnlyDictionary<string, Profile> Load()
        {
            using var stream = typeof(EmbeddedConversionQualityEvaluator)
                .Assembly
                .GetManifestResourceStream(QualificationResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded qualification resource not found: {QualificationResourceName}");

            var document = JsonSerializer.Deserialize<QualificationDocument>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? throw new InvalidDataException(
                    "Embedded Quality Core qualification profile is invalid.");

            if (!string.Equals(
                    document.ProfileId,
                    QualificationProfileId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Unexpected qualification profile: {document.ProfileId}");
            }

            return document.Formats.ToDictionary(
                pair => pair.Key,
                pair => new Profile(
                    pair.Value.Status,
                    pair.Value.Metrics.ToHashSet(StringComparer.Ordinal),
                    pair.Value.Findings.ToHashSet(StringComparer.Ordinal)),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}

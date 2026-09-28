namespace Zlet.FolderConverter.Core.Models;

public sealed record ConversionQualityMetric(
    string Name,
    string Status,
    double? Value,
    int Numerator,
    int Denominator,
    string Method);

public sealed record ConversionQualityFinding(
    string Code,
    string Severity);

public sealed record ConversionQualityPipeline(
    string QualityCoreVersion,
    string EvaluationApiVersion,
    string SourceInspectorId,
    string SourceInspectorVersion,
    string MarkdownInspectorId,
    string MarkdownInspectorVersion,
    string ComparatorId,
    string ComparatorVersion,
    string MatcherVersion,
    string MetricsVersion,
    string QualityReportSchemaVersion,
    string CqsMethodologyVersion);

public sealed record ConversionQualityAssessment(
    string QualificationProfileId,
    string QualificationStatus,
    string ProductDecisionStatus,
    string SourceStatus,
    string MarkdownStatus,
    string ComparisonStatus,
    ConversionQualityPipeline? Pipeline,
    IReadOnlyList<ConversionQualityMetric> Metrics,
    IReadOnlyList<ConversionQualityFinding> Findings);

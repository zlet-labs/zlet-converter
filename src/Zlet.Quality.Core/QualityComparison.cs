namespace Zlet.Quality.Core;

public interface IQualityComparator
{
    string ComparatorId { get; }

    string ComparatorVersion { get; }

    string MatcherVersion { get; }

    string MetricsVersion { get; }

    QualityResultDocument Compare(
        SourceFactsDocument source,
        MarkdownFactsDocument output);
}

public sealed record QualityMetric(
    string Name,
    string Status,
    double? Value,
    int Numerator,
    int Denominator,
    string Method);

public sealed record QualityFinding(
    string Code,
    string Severity,
    string? SourceFactId,
    string? OutputFactId,
    string Message);

public sealed record QualityResultDocument(
    string SchemaVersion,
    string ComparatorId,
    string ComparatorVersion,
    string MatcherVersion,
    string MetricsVersion,
    string SourceSha256,
    string MarkdownSha256,
    IReadOnlyList<QualityMetric> Metrics,
    IReadOnlyList<QualityFinding> Findings);

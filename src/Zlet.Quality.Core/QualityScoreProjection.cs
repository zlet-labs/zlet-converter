namespace Zlet.Quality.Core;

public sealed record FamilyScoreProjection(
    string Dimension,
    string Family,
    string Status,
    double? Score,
    double Coverage,
    string? PrimaryMetric,
    int SourceOpportunities,
    int EvaluableOpportunities);

public sealed record DimensionScoreProjection(
    string Dimension,
    string Status,
    double? Score,
    double Coverage,
    int ApplicableFamilies,
    int EvaluatedFamilies);

public sealed record IntegrityGuardProjection(
    string Status,
    double? Score,
    IReadOnlyList<string> Metrics);

public sealed record CandidateScoreProjection(
    string MethodologyVersion,
    string TargetProfileId,
    IReadOnlyList<FamilyScoreProjection> Families,
    IReadOnlyList<DimensionScoreProjection> Dimensions,
    double? BaseQuality,
    IntegrityGuardProjection Integrity,
    double? Cqs,
    string Status)
{
    public string ComparisonEligibility =>
        Status switch
        {
            "SCORED" => "ELIGIBLE_DOCUMENT_LEVEL",
            "PARTIAL_EVIDENCE" => "NOT_ELIGIBLE_PARTIAL_EVIDENCE",
            _ => "NOT_ELIGIBLE"
        };
}

public static class CandidateScoreProjector
{
    public static CandidateScoreProjection Project(
        SourceFactsDocument source,
        QualityResultDocument result)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(result);

        var metrics = result.Metrics.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var families = EvaluationOpportunityBuilder.Build(source)
            .Select(x => ProjectFamily(x, metrics))
            .ToArray();

        var dimensions = families
            .GroupBy(x => x.Dimension, StringComparer.Ordinal)
            .Select(ProjectDimension)
            .ToArray();

        var baseQuality = ProjectBaseQuality(dimensions);
        var integrity = ProjectIntegrityGuard(metrics);

        double? cqs =
            baseQuality is null || integrity.Score is null
                ? null
                : Math.Round(Math.Min(baseQuality.Value, integrity.Score.Value), 1);

        var hasCoverageGap = families
            .Where(x => x.Dimension is "content" or "structure" or "order" or "noise")
            .Where(x => x.Status != "NOT_APPLICABLE")
            .Any(HasIncompleteEvidence);

        var status =
            cqs is null
                ? "NOT_EVALUATED"
                : hasCoverageGap
                    ? "PARTIAL_EVIDENCE"
                    : "SCORED";

        return new CandidateScoreProjection(
            QualityScoreMethodology.Version,
            QualityScoreMethodology.TargetProfile.Id,
            families,
            dimensions,
            baseQuality,
            integrity,
            cqs,
            status);
    }

    private static double? ProjectBaseQuality(
        IReadOnlyList<DimensionScoreProjection> dimensions)
    {
        var values = dimensions
            .Where(x => x.Dimension is "content" or "structure" or "order" or "noise")
            .Where(x => x.Score is not null)
            .Select(x => x.Score!.Value)
            .ToArray();

        return values.Length == 0
            ? null
            : Math.Round(values.Average(), 1);
    }

    private static IntegrityGuardProjection ProjectIntegrityGuard(
        IReadOnlyDictionary<string, QualityMetric> metrics)
    {
        var values = new List<double>();
        var used = new List<string>();

        if (metrics.TryGetValue("output_nonempty", out var nonempty) &&
            nonempty.Status == "EVALUATED" &&
            nonempty.Value is >= 0d and <= 1d)
        {
            values.Add(nonempty.Value.Value * 100d);
            used.Add("output_nonempty");
        }

        if (metrics.TryGetValue("content_token_length_ratio", out var length) &&
            length.Status == "EVALUATED" &&
            length.Value is >= 0d)
        {
            values.Add(Math.Min(1d, length.Value.Value) * 100d);
            used.Add("content_token_length_ratio");
        }

        return values.Count == 0
            ? new IntegrityGuardProjection("NOT_EVALUATED", null, used)
            : new IntegrityGuardProjection("EVALUATED", Math.Round(values.Min(), 1), used);
    }

    private static FamilyScoreProjection ProjectFamily(
        EvaluationOpportunity opportunity,
        IReadOnlyDictionary<string, QualityMetric> metrics)
    {
        var owner = ScoreMetricRegistry.PrimaryOwner(
            opportunity.Dimension,
            opportunity.Family);

        if (opportunity.Status == OpportunityStatus.NOT_APPLICABLE)
        {
            if (owner is not null &&
                opportunity.Dimension == "noise" &&
                metrics.TryGetValue(owner.Metric, out var outputOnlyMetric) &&
                outputOnlyMetric.Status == "EVALUATED" &&
                outputOnlyMetric.Value is >= 0d and <= 1d &&
                outputOnlyMetric.Denominator > 0)
            {
                return Family(
                    opportunity,
                    "EVALUATED",
                    outputOnlyMetric.Value.Value * 100d,
                    100d,
                    owner.Metric);
            }

            return Family(opportunity, "NOT_APPLICABLE", null, 0d, owner?.Metric);
        }

        if (opportunity.Status == OpportunityStatus.NOT_EVALUATED)
        {
            return Family(
                opportunity,
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                null,
                0d,
                owner?.Metric);
        }

        if (owner is null)
        {
            return Family(
                opportunity,
                "NOT_EVALUATED_NO_PRIMARY_OWNER",
                null,
                0d,
                null);
        }

        if (!metrics.TryGetValue(owner.Metric, out var metric) ||
            metric.Status != "EVALUATED" ||
            metric.Value is null ||
            metric.Value < 0d ||
            metric.Value > 1d)
        {
            return Family(
                opportunity,
                "NOT_EVALUATED",
                null,
                0d,
                owner.Metric);
        }

        var coverage =
            opportunity.SourceOpportunities == 0
                ? 0d
                : opportunity.EvaluableOpportunities * 100d /
                  opportunity.SourceOpportunities;

        return Family(
            opportunity,
            "EVALUATED",
            metric.Value.Value * 100d,
            coverage,
            owner.Metric);
    }

    private static FamilyScoreProjection Family(
        EvaluationOpportunity opportunity,
        string status,
        double? score,
        double coverage,
        string? metric) =>
        new(
            opportunity.Dimension,
            opportunity.Family,
            status,
            score,
            coverage,
            metric,
            opportunity.SourceOpportunities,
            opportunity.EvaluableOpportunities);

    private static DimensionScoreProjection ProjectDimension(
        IGrouping<string, FamilyScoreProjection> group)
    {
        var applicable = group
            .Where(x => x.Status != "NOT_APPLICABLE")
            .ToArray();

        var evaluated = applicable
            .Where(x => x.Status == "EVALUATED" && x.Score is not null)
            .ToArray();

        if (applicable.Length == 0)
        {
            return new DimensionScoreProjection(
                group.Key,
                "NOT_APPLICABLE",
                null,
                0d,
                0,
                0);
        }

        var coverage = applicable.Average(x => x.Coverage);
        var hasCoverageGap = applicable.Any(HasIncompleteEvidence);

        if (evaluated.Length == 0)
        {
            return new DimensionScoreProjection(
                group.Key,
                "NOT_EVALUATED",
                null,
                coverage,
                applicable.Length,
                0);
        }

        return new DimensionScoreProjection(
            group.Key,
            hasCoverageGap ? "PARTIAL_EVIDENCE" : "EVALUATED",
            Math.Round(evaluated.Average(x => x.Score!.Value), 1),
            coverage,
            applicable.Length,
            evaluated.Length);
    }

    private static bool HasIncompleteEvidence(FamilyScoreProjection family)
    {
        if (family.Status != "EVALUATED")
        {
            return true;
        }

        // Output-only precision families can be fully evaluable even when the
        // source contains zero opportunities for that structural family.
        if (family.SourceOpportunities == 0)
        {
            return false;
        }

        return family.EvaluableOpportunities < family.SourceOpportunities;
    }
}

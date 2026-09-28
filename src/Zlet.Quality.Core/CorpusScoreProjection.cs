namespace Zlet.Quality.Core;

public sealed record CorpusStratumDefinition(
    string Format,
    string Stratum,
    IReadOnlyList<string> DocumentIds);

public sealed record CorpusProjectionPlan(
    string Id,
    string Version,
    IReadOnlyList<CorpusStratumDefinition> Strata);

public sealed record CorpusDocumentScore(
    string DocumentId,
    string Format,
    string Stratum,
    CandidateScoreProjection Projection);

public sealed record CorpusDocumentProjection(
    string DocumentId,
    string Format,
    string Stratum,
    string Status,
    double? Cqs,
    string ComparisonEligibility);

public sealed record CorpusStratumProjection(
    string Format,
    string Stratum,
    string Status,
    double? Score,
    int ExpectedDocuments,
    int PresentDocuments,
    int EligibleDocuments,
    IReadOnlyList<CorpusDocumentProjection> Documents);

public sealed record CorpusFormatProjection(
    string Format,
    string Status,
    double? Score,
    IReadOnlyList<CorpusStratumProjection> Strata);

public sealed record CorpusScoreProjection(
    string ProjectionVersion,
    string PlanId,
    string PlanVersion,
    string MethodologyVersion,
    string TargetProfileId,
    string Status,
    double? Score,
    IReadOnlyList<CorpusFormatProjection> Formats)
{
    public const string Version = "zlet-cqs-corpus/0.1.0";
    public string ComparisonEligibility => Status == "ELIGIBLE"
        ? "ELIGIBLE_CORPUS_COMPARISON"
        : "NOT_ELIGIBLE_CORPUS_COMPARISON";
}

public sealed record PairedDeltaSummary(
    int Pairs,
    double MeanDelta,
    double MedianDelta,
    double MinDelta,
    double MaxDelta,
    int Positive,
    int Equal,
    int Negative);

public sealed record CorpusComparisonProjection(
    string Status,
    string PlanId,
    string PlanVersion,
    string MethodologyVersion,
    double? LeftScore,
    double? RightScore,
    double? CorpusDelta,
    PairedDeltaSummary? PairedDocuments);

public static class CorpusScoreProjector
{
    public static CorpusScoreProjection Project(
        CorpusProjectionPlan plan,
        IReadOnlyList<CorpusDocumentScore> documents)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(documents);
        ValidatePlan(plan);

        var expected = plan.Strata
            .SelectMany(s => s.DocumentIds.Select(id => (s.Format, s.Stratum, DocumentId: id)))
            .ToArray();
        var expectedIds = expected.Select(x => x.DocumentId).ToHashSet(StringComparer.Ordinal);

        var duplicateInput = documents
            .GroupBy(x => x.DocumentId, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateInput is not null)
            throw new ArgumentException($"Duplicate document id '{duplicateInput.Key}'.", nameof(documents));

        var unexpected = documents.FirstOrDefault(x => !expectedIds.Contains(x.DocumentId));
        if (unexpected is not null)
            throw new ArgumentException($"Unexpected document id '{unexpected.DocumentId}'.", nameof(documents));

        var byId = documents.ToDictionary(x => x.DocumentId, StringComparer.Ordinal);
        var methodologyVersions = documents.Select(x => x.Projection.MethodologyVersion).Distinct(StringComparer.Ordinal).ToArray();
        var targetProfiles = documents.Select(x => x.Projection.TargetProfileId).Distinct(StringComparer.Ordinal).ToArray();
        var methodologyVersion = methodologyVersions.Length == 1 ? methodologyVersions[0] : "MIXED";
        var targetProfileId = targetProfiles.Length == 1 ? targetProfiles[0] : "MIXED";
        var identityCompatible = methodologyVersions.Length <= 1 && targetProfiles.Length <= 1;

        var strata = plan.Strata.Select(definition =>
        {
            var projected = definition.DocumentIds.Select(id =>
            {
                if (!byId.TryGetValue(id, out var document))
                {
                    return new CorpusDocumentProjection(
                        id, definition.Format, definition.Stratum,
                        "MISSING", null, "NOT_ELIGIBLE");
                }

                if (!string.Equals(document.Format, definition.Format, StringComparison.Ordinal) ||
                    !string.Equals(document.Stratum, definition.Stratum, StringComparison.Ordinal))
                {
                    return new CorpusDocumentProjection(
                        id, document.Format, document.Stratum,
                        "PLAN_MISMATCH", document.Projection.Cqs, "NOT_ELIGIBLE");
                }

                return new CorpusDocumentProjection(
                    id, document.Format, document.Stratum,
                    document.Projection.Status,
                    document.Projection.Cqs,
                    document.Projection.ComparisonEligibility);
            }).ToArray();

            var eligible = projected.Count(x =>
                x.ComparisonEligibility == "ELIGIBLE_DOCUMENT_LEVEL" && x.Cqs is not null);
            var present = projected.Count(x => x.Status != "MISSING");
            var isEligible = identityCompatible && eligible == definition.DocumentIds.Count;
            var score = isEligible ? Math.Round(projected.Average(x => x.Cqs!.Value), 1) : (double?)null;
            var status = isEligible
                ? "ELIGIBLE"
                : projected.Any(x => x.Status == "MISSING")
                    ? "NOT_ELIGIBLE_MISSING_DOCUMENT"
                    : projected.Any(x => x.Status == "PLAN_MISMATCH")
                        ? "NOT_ELIGIBLE_PLAN_MISMATCH"
                        : !identityCompatible
                            ? "NOT_ELIGIBLE_MIXED_IDENTITY"
                            : "NOT_ELIGIBLE_DOCUMENT_EVIDENCE";

            return new CorpusStratumProjection(
                definition.Format, definition.Stratum, status, score,
                definition.DocumentIds.Count, present, eligible, projected);
        }).ToArray();

        var formats = strata
            .GroupBy(x => x.Format, StringComparer.Ordinal)
            .Select(group =>
            {
                var members = group.ToArray();
                var eligible = members.All(x => x.Status == "ELIGIBLE" && x.Score is not null);
                return new CorpusFormatProjection(
                    group.Key,
                    eligible ? "ELIGIBLE" : "NOT_ELIGIBLE",
                    eligible ? Math.Round(members.Average(x => x.Score!.Value), 1) : null,
                    members);
            })
            .OrderBy(x => x.Format, StringComparer.Ordinal)
            .ToArray();

        var corpusEligible = identityCompatible && formats.Length > 0 && formats.All(x => x.Status == "ELIGIBLE" && x.Score is not null);
        return new CorpusScoreProjection(
            CorpusScoreProjection.Version,
            plan.Id,
            plan.Version,
            methodologyVersion,
            targetProfileId,
            corpusEligible ? "ELIGIBLE" : "NOT_ELIGIBLE",
            corpusEligible ? Math.Round(formats.Average(x => x.Score!.Value), 1) : null,
            formats);
    }

    public static CorpusComparisonProjection Compare(
        CorpusScoreProjection left,
        CorpusScoreProjection right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var sameContract =
            left.Status == "ELIGIBLE" && right.Status == "ELIGIBLE" &&
            string.Equals(left.PlanId, right.PlanId, StringComparison.Ordinal) &&
            string.Equals(left.PlanVersion, right.PlanVersion, StringComparison.Ordinal) &&
            string.Equals(left.MethodologyVersion, right.MethodologyVersion, StringComparison.Ordinal) &&
            string.Equals(left.TargetProfileId, right.TargetProfileId, StringComparison.Ordinal);

        if (!sameContract)
        {
            return new CorpusComparisonProjection(
                "NOT_COMPARABLE", left.PlanId, left.PlanVersion,
                left.MethodologyVersion, left.Score, right.Score, null, null);
        }

        var leftDocs = Flatten(left).ToDictionary(x => x.DocumentId, StringComparer.Ordinal);
        var rightDocs = Flatten(right).ToDictionary(x => x.DocumentId, StringComparer.Ordinal);
        if (!leftDocs.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(rightDocs.Keys))
        {
            return new CorpusComparisonProjection(
                "NOT_COMPARABLE", left.PlanId, left.PlanVersion,
                left.MethodologyVersion, left.Score, right.Score, null, null);
        }

        var deltas = leftDocs.Keys
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(id => leftDocs[id].Cqs!.Value - rightDocs[id].Cqs!.Value)
            .ToArray();

        return new CorpusComparisonProjection(
            "COMPARABLE",
            left.PlanId,
            left.PlanVersion,
            left.MethodologyVersion,
            left.Score,
            right.Score,
            Math.Round(left.Score!.Value - right.Score!.Value, 1),
            Summarize(deltas));
    }

    private static IEnumerable<CorpusDocumentProjection> Flatten(CorpusScoreProjection projection) =>
        projection.Formats.SelectMany(x => x.Strata).SelectMany(x => x.Documents);

    private static PairedDeltaSummary Summarize(double[] values)
    {
        var ordered = values.OrderBy(x => x).ToArray();
        var median = ordered.Length % 2 == 1
            ? ordered[ordered.Length / 2]
            : (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2d;

        return new PairedDeltaSummary(
            values.Length,
            Math.Round(values.Average(), 2),
            Math.Round(median, 2),
            Math.Round(values.Min(), 2),
            Math.Round(values.Max(), 2),
            values.Count(x => x > 0d),
            values.Count(x => x == 0d),
            values.Count(x => x < 0d));
    }

    private static void ValidatePlan(CorpusProjectionPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Id) || string.IsNullOrWhiteSpace(plan.Version))
            throw new ArgumentException("Corpus projection plan id/version are required.", nameof(plan));
        if (plan.Strata.Count == 0)
            throw new ArgumentException("Corpus projection plan must declare at least one stratum.", nameof(plan));

        var empty = plan.Strata.FirstOrDefault(x =>
            string.IsNullOrWhiteSpace(x.Format) || string.IsNullOrWhiteSpace(x.Stratum) || x.DocumentIds.Count == 0);
        if (empty is not null)
            throw new ArgumentException("Every stratum requires format, id and document ids.", nameof(plan));

        var duplicateStratum = plan.Strata
            .GroupBy(x => (x.Format, x.Stratum))
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateStratum is not null)
            throw new ArgumentException("Duplicate format/stratum definition.", nameof(plan));

        var duplicateDocument = plan.Strata
            .SelectMany(x => x.DocumentIds)
            .GroupBy(x => x, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateDocument is not null)
            throw new ArgumentException($"Document '{duplicateDocument.Key}' appears more than once in the plan.", nameof(plan));
    }
}

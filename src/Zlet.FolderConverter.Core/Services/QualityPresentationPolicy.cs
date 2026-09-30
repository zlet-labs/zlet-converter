using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.Core.Services;

public sealed record QualityPresentation(string Status, double? FidelityScore, double? EvaluationCoverage, bool IsFullyEvaluated);

public static class QualityPresentationPolicy
{
    public static QualityPresentation Evaluate(ConversionQualityAssessment? quality, QualityCheckPolicy policy)
    {
        policy = policy.Normalize();
        if (!policy.Enabled || quality is null) return new("NOT_EVALUATED", null, null, false);
        if (quality.QualificationStatus == "COVERAGE_ONLY") return new("PARTIAL", null, null, false);
        var p = quality.Projection;
        if (p is null || p.Status == "NOT_EVALUATED" || p.FidelityScore is null)
            return new("NOT_EVALUATED", null, p?.EvaluationCoverage, false);
        if (p.Status != "SCORED" || p.EvaluationCoverage < policy.MinimumCoverageForOk)
            return new("REVIEW", p.FidelityScore, p.EvaluationCoverage, false);
        var status = p.FidelityScore >= policy.OkThreshold ? "OK" : p.FidelityScore >= policy.ReviewThreshold ? "REVIEW" : "FAIL";
        return new(status, p.FidelityScore, p.EvaluationCoverage, true);
    }
}

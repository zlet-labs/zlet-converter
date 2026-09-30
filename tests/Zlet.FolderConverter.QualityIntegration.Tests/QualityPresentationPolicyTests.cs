using Zlet.FolderConverter.Core.Models;
using Zlet.FolderConverter.Core.Services;

namespace Zlet.FolderConverter.QualityIntegration.Tests;

public sealed class QualityPresentationPolicyTests
{
    private static ConversionQualityAssessment Assessment(string qualification, string projectionStatus, double? score, double coverage) =>
        new("profile", qualification, "DIAGNOSTIC_ONLY", "OK", "OK", "OK", null, [], [],
            qualification == "COVERAGE_ONLY" ? null : new ConversionQualityProjection("zlet-cqs/0.3.0", projectionStatus, score, coverage, []));

    [Fact]
    public void ScoredDocument_UsesConfiguredThresholds()
    {
        var policy = QualityCheckPolicy.Default;
        Assert.Equal("OK", QualityPresentationPolicy.Evaluate(Assessment("QUALIFIED_BOUNDED", "SCORED", 95, 100), policy).Status);
        Assert.Equal("REVIEW", QualityPresentationPolicy.Evaluate(Assessment("QUALIFIED_BOUNDED", "SCORED", 80, 100), policy).Status);
        Assert.Equal("FAIL", QualityPresentationPolicy.Evaluate(Assessment("QUALIFIED_BOUNDED", "SCORED", 50, 100), policy).Status);
    }

    [Fact]
    public void PartialEvidence_NeverBecomesOkEvenWithHighScore()
    {
        var result = QualityPresentationPolicy.Evaluate(Assessment("QUALIFIED_BOUNDED", "PARTIAL_EVIDENCE", 100, 99), QualityCheckPolicy.Default);
        Assert.Equal("REVIEW", result.Status);
        Assert.False(result.IsFullyEvaluated);
    }

    [Fact]
    public void CoverageOnly_NeverInventsFidelityScore()
    {
        var result = QualityPresentationPolicy.Evaluate(Assessment("COVERAGE_ONLY", "NOT_EVALUATED", null, 100), QualityCheckPolicy.Default);
        Assert.Equal("PARTIAL", result.Status);
        Assert.Null(result.FidelityScore);
    }

    [Fact]
    public void MinimumCoverageBlocksOk()
    {
        var result = QualityPresentationPolicy.Evaluate(Assessment("QUALIFIED_BOUNDED", "SCORED", 99, 80), QualityCheckPolicy.Default);
        Assert.Equal("REVIEW", result.Status);
    }
}

namespace Zlet.FolderConverter.Core.Models;

public sealed record QualityCheckPolicy(
    bool Enabled = true,
    bool ShowAfterConversion = true,
    double OkThreshold = 90d,
    double ReviewThreshold = 70d,
    double MinimumCoverageForOk = 90d,
    bool ShowDetailedMetrics = true)
{
    public static QualityCheckPolicy Default { get; } = new();

    public QualityCheckPolicy Normalize() => this with
    {
        OkThreshold = Math.Clamp(OkThreshold, 0d, 100d),
        ReviewThreshold = Math.Clamp(Math.Min(ReviewThreshold, OkThreshold), 0d, 100d),
        MinimumCoverageForOk = Math.Clamp(MinimumCoverageForOk, 0d, 100d)
    };
}

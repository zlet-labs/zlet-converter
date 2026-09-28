namespace Zlet.Quality.Core;

// Experimental projection only. This is deliberately not the canonical CQS contract.
// ZCL-026 research requires TargetProfile -> EvaluationOpportunity -> primary families
// before a public/versioned 0-100 score can be frozen.
public sealed record QualityScoreDimension(
    string Name,
    string Status,
    double? Score,
    int EvaluatedMetrics,
    int EligibleMetrics);

public sealed record QualityScoreDocument(
    string ScoreVersion,
    string Status,
    double? Score,
    double EvaluationCoverage,
    IReadOnlyList<QualityScoreDimension> Dimensions);

public static class QualityScoreCalculator
{
    public const string Version = "experimental-direct-metric-average/0.1";

    // Diagnostic prototype mapping. Do not interpret entries as primary scoring families.
    private static readonly IReadOnlyDictionary<string, string[]> DimensionMetrics =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["content"] = ["text_recall", "text_precision"],
            ["structure"] = ["heading_recall", "heading_precision", "heading_level_accuracy", "list_item_recall", "list_item_precision", "list_level_accuracy", "table_count_recall", "table_count_precision", "table_cell_recall", "table_cell_precision", "table_cell_position_accuracy", "link_target_recall", "link_anchor_text_recall", "image_reference_count_recall", "image_reference_count_precision"],
            ["order"] = ["order_anchor_coverage", "pairwise_order_accuracy"],
            ["integrity"] = ["output_nonempty"],
            ["noise"] = []
        };

    public static QualityScoreDocument Calculate(QualityResultDocument result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var byName = result.Metrics.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var dimensions = new List<QualityScoreDimension>();
        var allValues = new List<double>();
        var eligible = 0;
        var evaluated = 0;

        foreach (var dimension in DimensionMetrics)
        {
            var values = new List<double>();
            var dimensionEligible = 0;
            foreach (var name in dimension.Value)
            {
                if (!byName.TryGetValue(name, out var metric) || metric.Status == "NOT_APPLICABLE")
                    continue;

                dimensionEligible++;
                eligible++;
                if (metric.Status == "EVALUATED" && metric.Value is >= 0d and <= 1d)
                {
                    values.Add(metric.Value.Value);
                    allValues.Add(metric.Value.Value);
                    evaluated++;
                }
            }

            dimensions.Add(new QualityScoreDimension(
                dimension.Key,
                values.Count == 0 ? "NOT_EVALUATED" : "EVALUATED",
                values.Count == 0 ? null : Math.Round(values.Average() * 100d, 1),
                values.Count,
                dimensionEligible));
        }

        var coverage = eligible == 0
            ? 0d
            : Math.Round((double)evaluated / eligible * 100d, 1);

        return new QualityScoreDocument(
            Version,
            allValues.Count == 0 ? "NOT_EVALUATED" : "EXPERIMENTAL",
            allValues.Count == 0 ? null : Math.Round(allValues.Average() * 100d, 1),
            coverage,
            dimensions);
    }
}

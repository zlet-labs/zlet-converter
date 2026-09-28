namespace Zlet.Quality.Core;

public enum ScoreMetricRole { PRIMARY, DIAGNOSTIC }
public enum OpportunityStatus { APPLICABLE, NOT_APPLICABLE, NOT_EVALUATED }

public sealed record MarkdownTargetProfile(
    string Id,
    string Dialect,
    bool Tables,
    bool Links,
    bool Images,
    bool RawHtml,
    bool Formulas,
    string Normalization);

public static class QualityScoreMethodology
{
    public const string Version = "zlet-cqs/0.3.0";

    public static readonly MarkdownTargetProfile TargetProfile = new(
        "zlet-markdown-target/0.1",
        "Markdig advanced extensions",
        true,
        true,
        true,
        false,
        false,
        "ExactQualityComparator.NormalizeText/0.2");
}

public sealed record EvaluationOpportunity(
    string Dimension,
    string Family,
    OpportunityStatus Status,
    int SourceOpportunities,
    int EvaluableOpportunities,
    IReadOnlyList<string> SourceFactIds);

public sealed record ScoreMetricOwnership(
    string Metric,
    string Dimension,
    string Family,
    ScoreMetricRole Role);

public static class ScoreMetricRegistry
{
    public static readonly IReadOnlyList<ScoreMetricOwnership> Metrics =
    [
        new("text_recall", "content", "text", ScoreMetricRole.PRIMARY),
        new("text_precision", "noise", "extra_content", ScoreMetricRole.PRIMARY),

        new("heading_recall", "structure", "headings", ScoreMetricRole.PRIMARY),
        new("heading_level_accuracy", "structure", "heading_hierarchy", ScoreMetricRole.PRIMARY),
        new("heading_precision", "noise", "extra_headings", ScoreMetricRole.PRIMARY),

        new("list_item_recall", "structure", "lists", ScoreMetricRole.PRIMARY),
        new("list_level_accuracy", "structure", "list_hierarchy", ScoreMetricRole.PRIMARY),
        new("list_item_precision", "noise", "extra_lists", ScoreMetricRole.PRIMARY),

        new("table_cell_recall", "structure", "tables", ScoreMetricRole.PRIMARY),
        new("table_cell_position_accuracy", "structure", "table_geometry", ScoreMetricRole.PRIMARY),
        new("table_cell_precision", "noise", "extra_tables", ScoreMetricRole.PRIMARY),

        new("link_target_recall", "structure", "links", ScoreMetricRole.PRIMARY),
        new("link_anchor_text_recall", "structure", "links", ScoreMetricRole.DIAGNOSTIC),
        new("link_target_precision", "noise", "extra_links", ScoreMetricRole.PRIMARY),

        new("image_reference_count_recall", "structure", "images", ScoreMetricRole.PRIMARY),
        new("image_reference_count_precision", "noise", "extra_images", ScoreMetricRole.PRIMARY),

        new("pairwise_order_accuracy", "order", "reading_order", ScoreMetricRole.PRIMARY),
        new("order_anchor_coverage", "order", "reading_order", ScoreMetricRole.DIAGNOSTIC),

        new("output_nonempty", "integrity", "nonempty", ScoreMetricRole.DIAGNOSTIC),
        new("duplicate_or_extra_content_count", "noise", "extra_content", ScoreMetricRole.DIAGNOSTIC),
        new("content_token_length_ratio", "integrity", "length", ScoreMetricRole.DIAGNOSTIC)
    ];

    public static ScoreMetricOwnership? PrimaryOwner(string dimension, string family) =>
        Metrics.SingleOrDefault(x =>
            x.Dimension == dimension &&
            x.Family == family &&
            x.Role == ScoreMetricRole.PRIMARY);
}

public static class EvaluationOpportunityBuilder
{
    private static readonly FactKind[] OrderedKinds =
    [
        FactKind.TEXT_BLOCK,
        FactKind.HEADING,
        FactKind.LIST_ITEM,
        FactKind.TABLE_CELL
    ];

    public static IReadOnlyList<EvaluationOpportunity> Build(SourceFactsDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            ForKinds(source, "content", "text", FactProperty.Text,
                FactKind.TEXT_BLOCK, FactKind.HEADING, FactKind.LIST_ITEM, FactKind.TABLE_CELL),

            ForKinds(source, "structure", "headings", FactProperty.Text, FactKind.HEADING),
            ForAttributes(source, "structure", "heading_hierarchy", FactKind.HEADING, FactProperty.Level, "level"),
            ForKinds(source, "structure", "lists", FactProperty.Text, FactKind.LIST_ITEM),
            ForAttributes(source, "structure", "list_hierarchy", FactKind.LIST_ITEM, FactProperty.Level, "level"),
            ForKinds(source, "structure", "tables", FactProperty.Text, FactKind.TABLE_CELL),
            ForAttributes(source, "structure", "table_geometry", FactKind.TABLE_CELL, FactProperty.Position, "row", "column"),
            ForKinds(source, "structure", "links", FactProperty.Target, FactKind.LINK),
            ForKinds(source, "structure", "images", FactProperty.Presence, FactKind.IMAGE),

            ForKinds(source, "noise", "extra_content", FactProperty.Text,
                FactKind.TEXT_BLOCK, FactKind.HEADING, FactKind.LIST_ITEM, FactKind.TABLE_CELL),
            ForKinds(source, "noise", "extra_headings", FactProperty.Text, FactKind.HEADING),
            ForKinds(source, "noise", "extra_lists", FactProperty.Text, FactKind.LIST_ITEM),
            ForKinds(source, "noise", "extra_tables", FactProperty.Text, FactKind.TABLE_CELL),
            ForKinds(source, "noise", "extra_links", FactProperty.Target, FactKind.LINK),
            ForKinds(source, "noise", "extra_images", FactProperty.Presence, FactKind.IMAGE),

            ForOrder(source)
        ];
    }

    private static EvaluationOpportunity ForKinds(
        SourceFactsDocument source,
        string dimension,
        string family,
        string property,
        params FactKind[] kinds)
    {
        var matching = source.Facts
            .Where(f => kinds.Contains(f.Kind))
            .ToArray();

        var evaluable = matching
            .Where(f => SourceFactEvidence.IsExact(f, property))
            .ToArray();

        return BuildOpportunity(
            dimension,
            family,
            matching.Length,
            evaluable.Select(f => f.Id).ToArray());
    }

    private static EvaluationOpportunity ForAttributes(
        SourceFactsDocument source,
        string dimension,
        string family,
        FactKind kind,
        string property,
        params string[] attributes)
    {
        var matching = source.Facts
            .Where(f => f.Kind == kind)
            .ToArray();

        var evaluable = matching
            .Where(f => SourceFactEvidence.IsExact(f, property))
            .Where(f => HasAttributes(f.Attributes, attributes))
            .ToArray();

        return BuildOpportunity(
            dimension,
            family,
            matching.Length,
            evaluable.Select(f => f.Id).ToArray());
    }

    private static EvaluationOpportunity ForOrder(SourceFactsDocument source)
    {
        var sourceAnchors = source.Facts
            .Where(f => OrderedKinds.Contains(f.Kind) && !string.IsNullOrWhiteSpace(f.Text))
            .ToArray();

        var evaluableAnchors = sourceAnchors
            .Where(f => SourceFactEvidence.IsExact(f, FactProperty.Order))
            .ToArray();

        var sourcePairs = PairCount(sourceAnchors.Length);
        var evaluablePairs = PairCount(evaluableAnchors.Length);

        var status =
            sourcePairs == 0
                ? OpportunityStatus.NOT_APPLICABLE
                : evaluablePairs == 0
                    ? OpportunityStatus.NOT_EVALUATED
                    : OpportunityStatus.APPLICABLE;

        return new EvaluationOpportunity(
            "order",
            "reading_order",
            status,
            sourcePairs,
            evaluablePairs,
            evaluableAnchors.Select(f => f.Id).ToArray());
    }

    private static EvaluationOpportunity BuildOpportunity(
        string dimension,
        string family,
        int sourceOpportunities,
        IReadOnlyList<string> evaluableIds)
    {
        var status =
            sourceOpportunities == 0
                ? OpportunityStatus.NOT_APPLICABLE
                : evaluableIds.Count == 0
                    ? OpportunityStatus.NOT_EVALUATED
                    : OpportunityStatus.APPLICABLE;

        return new EvaluationOpportunity(
            dimension,
            family,
            status,
            sourceOpportunities,
            evaluableIds.Count,
            evaluableIds);
    }

    private static bool HasAttributes(
        IReadOnlyDictionary<string, string>? attributes,
        IReadOnlyList<string> required) =>
        attributes is not null &&
        required.All(key =>
            attributes.TryGetValue(key, out var value) &&
            !string.IsNullOrWhiteSpace(value));

    private static int PairCount(int anchors) =>
        anchors < 2 ? 0 : anchors * (anchors - 1) / 2;
}

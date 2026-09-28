using System.Text;
using System.Text.RegularExpressions;

namespace Zlet.Quality.Core;

public sealed partial class ExactQualityComparator : IQualityComparator
{
    public const string Id = "zlet-exact-quality-comparator";
    public const string Version = "0.4.0";
    public const string MatcherIdentity = "exact-normalized-text/0.1";
    public const string MetricsIdentity = "conversion-fidelity/0.4.0";

    private static readonly HashSet<FactKind> ContentKinds =
    [
        FactKind.TEXT_BLOCK,
        FactKind.HEADING,
        FactKind.LIST_ITEM,
        FactKind.TABLE_CELL
    ];

    private static readonly HashSet<FactKind> OrderedKinds =
    [
        FactKind.TEXT_BLOCK,
        FactKind.HEADING,
        FactKind.LIST_ITEM,
        FactKind.TABLE_CELL
    ];

    public string ComparatorId => Id;

    public string ComparatorVersion => Version;

    public string MatcherVersion => MatcherIdentity;

    public string MetricsVersion => MetricsIdentity;

    public QualityResultDocument Compare(
        SourceFactsDocument source,
        MarkdownFactsDocument output)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        var metrics = new List<QualityMetric>();
        var findings = new List<QualityFinding>();

        AddEvaluationCoverageMetric(source, metrics);
        AddTextMetrics(source, output, metrics);
        AddExactKindMetrics(FactKind.HEADING, "heading", source, output, metrics, findings);
        AddHeadingLevelMetric(source, output, metrics);
        AddExactKindMetrics(FactKind.LIST_ITEM, "list_item", source, output, metrics, findings);
        AddCountMetrics(FactKind.TABLE, "table", source, output, metrics);
        AddExactKindMetrics(FactKind.TABLE_CELL, "table_cell", source, output, metrics, findings);
        AddLinkMetrics(source, output, metrics, findings);
        AddLinkAnchorMetric(source, output, metrics);
        AddImageMetrics(source, output, metrics, findings);
        AddListLevelMetric(source, output, metrics);
        AddTablePositionMetric(source, output, metrics);
        AddIntegrityMetrics(source, output, metrics, findings);
        AddOrderMetrics(source, output, metrics);

        return new QualityResultDocument(
            QualityCoreVersion.QualityReportSchemaVersion,
            Id,
            Version,
            MatcherIdentity,
            MetricsIdentity,
            source.SourceSha256,
            output.MarkdownSha256,
            metrics,
            findings);
    }

    private static void AddTextMetrics(
        SourceFactsDocument source,
        MarkdownFactsDocument output,
        List<QualityMetric> metrics)
    {
        var allSourceFacts = source.Facts
            .Where(fact => ContentKinds.Contains(fact.Kind))
            .ToList();

        var exactSourceFacts = allSourceFacts
            .Where(fact => SourceFactEvidence.IsExact(fact, FactProperty.Text))
            .ToList();

        var sourceTokens = exactSourceFacts
            .SelectMany(fact => Tokenize(fact.Text))
            .ToList();

        var outputTokens = output.Facts
            .Where(fact => ContentKinds.Contains(fact.Kind))
            .SelectMany(fact => Tokenize(fact.Text))
            .ToList();

        if (allSourceFacts.Count > 0 && exactSourceFacts.Count == 0)
        {
            metrics.Add(NotEvaluatedMetric(
                "text_recall",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "unicode_nfc_case_sensitive_token_multiset"));
            metrics.Add(NotEvaluatedMetric(
                "text_precision",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "unicode_nfc_case_sensitive_token_multiset"));
            return;
        }

        var matched = MultisetIntersectionCount(sourceTokens, outputTokens);

        metrics.Add(RatioMetric(
            "text_recall",
            matched,
            sourceTokens.Count,
            "unicode_nfc_case_sensitive_token_multiset"));

        if (allSourceFacts.Any(
                fact => !SourceFactEvidence.IsExact(fact, FactProperty.Text)))
        {
            metrics.Add(NotEvaluatedMetric(
                "text_precision",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "unicode_nfc_case_sensitive_token_multiset"));
        }
        else
        {
            metrics.Add(RatioMetric(
                "text_precision",
                matched,
                outputTokens.Count,
                "unicode_nfc_case_sensitive_token_multiset"));
        }
    }

    private static void AddExactKindMetrics(
        FactKind kind,
        string metricPrefix,
        SourceFactsDocument source,
        MarkdownFactsDocument output,
        List<QualityMetric> metrics,
        List<QualityFinding> findings)
    {
        var allSourceFacts = source.Facts
            .Where(fact => fact.Kind == kind)
            .OrderBy(fact => fact.Order)
            .ToList();

        var sourceFacts = allSourceFacts
            .Where(fact => SourceFactEvidence.IsExact(fact, FactProperty.Text))
            .ToList();

        var outputFacts = output.Facts
            .Where(fact => fact.Kind == kind)
            .OrderBy(fact => fact.Order)
            .ToList();

        if (allSourceFacts.Count > 0 && sourceFacts.Count == 0)
        {
            metrics.Add(NotEvaluatedMetric(
                $"{metricPrefix}_recall",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "exact_normalized_text_one_to_one"));
            metrics.Add(NotEvaluatedMetric(
                $"{metricPrefix}_precision",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "exact_normalized_text_one_to_one"));
            return;
        }

        var match = MatchByNormalizedText(sourceFacts, outputFacts);

        metrics.Add(RatioMetric(
            $"{metricPrefix}_recall",
            match.Matches.Count,
            sourceFacts.Count,
            "exact_normalized_text_one_to_one"));

        if (allSourceFacts.Any(
                fact => !SourceFactEvidence.IsExact(fact, FactProperty.Text)))
        {
            metrics.Add(NotEvaluatedMetric(
                $"{metricPrefix}_precision",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "exact_normalized_text_one_to_one"));
        }
        else
        {
            metrics.Add(RatioMetric(
                $"{metricPrefix}_precision",
                match.Matches.Count,
                outputFacts.Count,
                "exact_normalized_text_one_to_one"));
        }

        foreach (var sourceFact in sourceFacts.Where(
                     fact => !match.MatchedSourceIds.Contains(fact.Id)))
        {
            findings.Add(new QualityFinding(
                $"MISSING_{kind}",
                "warning",
                sourceFact.Id,
                null,
                $"Source {kind} fact was not matched in Markdown output."));
        }
    }

    private static void AddEvaluationCoverageMetric(
        SourceFactsDocument source,
        List<QualityMetric> metrics)
    {
        var exact = source.Facts.Count(IsFactExact);
        metrics.Add(RatioMetric(
            "evaluation_coverage_exact",
            exact,
            source.Facts.Count,
            "source_fact_certainty_coverage"));
    }

    private static void AddIntegrityMetrics(
        SourceFactsDocument source,
        MarkdownFactsDocument output,
        List<QualityMetric> metrics,
        List<QualityFinding> findings)
    {
        var sourceSemantic = source.Facts.ToList();
        var exactSourceSemantic = sourceSemantic
            .Where(fact => SourceFactEvidence.IsExact(fact, FactProperty.Presence))
            .ToList();
        var outputSemantic = output.Facts.ToList();

        if (sourceSemantic.Count == 0)
        {
            metrics.Add(new QualityMetric(
                "output_nonempty",
                "NOT_APPLICABLE",
                null,
                0,
                0,
                "source_aware_markdown_semantic_fact_presence"));
        }
        else if (exactSourceSemantic.Count == 0)
        {
            metrics.Add(NotEvaluatedMetric(
                "output_nonempty",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "source_aware_markdown_semantic_fact_presence"));
        }
        else
        {
            var nonempty = outputSemantic.Count > 0;
            metrics.Add(new QualityMetric(
                "output_nonempty",
                "EVALUATED",
                nonempty ? 1d : 0d,
                nonempty ? 1 : 0,
                1,
                "source_aware_markdown_semantic_fact_presence"));

            if (!nonempty)
            {
                findings.Add(new QualityFinding(
                    "EMPTY_OUTPUT",
                    "error",
                    null,
                    null,
                    "Source contains exact representable facts but Markdown output has no semantic facts."));
            }
        }

        var allSourceContent = source.Facts
            .Where(f => ContentKinds.Contains(f.Kind) && !string.IsNullOrWhiteSpace(f.Text))
            .ToList();

        var sourceContent = allSourceContent
            .Where(fact => SourceFactEvidence.IsExact(fact, FactProperty.Text))
            .ToList();

        var outputContent = output.Facts
            .Where(f => ContentKinds.Contains(f.Kind) && !string.IsNullOrWhiteSpace(f.Text))
            .ToList();

        var sourceTokens = sourceContent
            .SelectMany(f => Tokenize(f.Text))
            .ToList();

        var outputTokens = outputContent
            .SelectMany(f => Tokenize(f.Text))
            .ToList();

        if (allSourceContent.Any(
                fact => !SourceFactEvidence.IsExact(fact, FactProperty.Text)))
        {
            metrics.Add(NotEvaluatedMetric(
                "duplicate_or_extra_content_count",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "token_multiset_excess_count"));
            metrics.Add(NotEvaluatedMetric(
                "content_token_length_ratio",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "output_content_tokens/source_content_tokens"));
            return;
        }

        var matchedTokens = MultisetIntersectionCount(sourceTokens, outputTokens);
        var extras = outputTokens.Count - matchedTokens;

        metrics.Add(new QualityMetric(
            "duplicate_or_extra_content_count",
            "EVALUATED",
            extras,
            extras,
            outputTokens.Count,
            "token_multiset_excess_count"));

        if (extras > 0)
        {
            findings.Add(new QualityFinding(
                "EXTRA_OR_DUPLICATED_CONTENT",
                "warning",
                null,
                null,
                $"Markdown contains {extras} content token(s) beyond source multiplicity."));
        }

        metrics.Add(RatioMetric(
            "content_token_length_ratio",
            outputTokens.Count,
            sourceTokens.Count,
            "output_content_tokens/source_content_tokens"));
    }

    private static void AddListLevelMetric(
        SourceFactsDocument source,
        MarkdownFactsDocument output,
        List<QualityMetric> metrics)
    {
        AddExactAttributeAgreementMetric(
            "list_level_accuracy",
            FactKind.LIST_ITEM,
            FactProperty.Level,
            ["level"],
            "matched_list_item_exact_level",
            source,
            output,
            metrics);
    }

    private static void AddTablePositionMetric(
        SourceFactsDocument source,
        MarkdownFactsDocument output,
        List<QualityMetric> metrics)
    {
        AddExactAttributeAgreementMetric(
            "table_cell_position_accuracy",
            FactKind.TABLE_CELL,
            FactProperty.Position,
            ["row", "column"],
            "matched_cell_exact_row_column",
            source,
            output,
            metrics);
    }

    private static void AddLinkAnchorMetric(
        SourceFactsDocument source,
        MarkdownFactsDocument output,
        List<QualityMetric> metrics)
    {
        var allSource = source.Facts
            .Where(f => f.Kind == FactKind.LINK && !string.IsNullOrWhiteSpace(f.Text))
            .ToList();

        var sourceFacts = allSource
            .Where(fact => SourceFactEvidence.IsExact(fact, FactProperty.Text))
            .ToList();

        var outputFacts = output.Facts
            .Where(f => f.Kind == FactKind.LINK && !string.IsNullOrWhiteSpace(f.Text))
            .ToList();

        if (allSource.Count > 0 && sourceFacts.Count == 0)
        {
            metrics.Add(NotEvaluatedMetric(
                "link_anchor_text_recall",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "exact_normalized_anchor_text"));
            return;
        }

        var match = MatchByNormalizedText(sourceFacts, outputFacts);
        metrics.Add(RatioMetric(
            "link_anchor_text_recall",
            match.Matches.Count,
            sourceFacts.Count,
            "exact_normalized_anchor_text"));
    }

    private static void AddImageMetrics(
        SourceFactsDocument source,
        MarkdownFactsDocument output,
        List<QualityMetric> metrics,
        List<QualityFinding> findings)
    {
        var allSource = source.Facts
            .Where(f => f.Kind == FactKind.IMAGE)
            .ToList();

        var sourceFacts = allSource
            .Where(fact => SourceFactEvidence.IsExact(fact, FactProperty.Presence))
            .ToList();

        var outputFacts = output.Facts
            .Where(f => f.Kind == FactKind.IMAGE)
            .ToList();

        if (allSource.Count > 0 && sourceFacts.Count == 0)
        {
            metrics.Add(NotEvaluatedMetric(
                "image_reference_count_recall",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "count_only_v0_3"));
            metrics.Add(NotEvaluatedMetric(
                "image_reference_count_precision",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "count_only_v0_3"));
            return;
        }

        var matched = Math.Min(sourceFacts.Count, outputFacts.Count);

        metrics.Add(RatioMetric(
            "image_reference_count_recall",
            matched,
            sourceFacts.Count,
            "count_only_v0_3"));

        if (allSource.Any(
                fact => !SourceFactEvidence.IsExact(fact, FactProperty.Presence)))
        {
            metrics.Add(NotEvaluatedMetric(
                "image_reference_count_precision",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "count_only_v0_3"));
        }
        else
        {
            metrics.Add(RatioMetric(
                "image_reference_count_precision",
                matched,
                outputFacts.Count,
                "count_only_v0_3"));
        }

        if (sourceFacts.Count > outputFacts.Count)
        {
            findings.Add(new QualityFinding(
                "MISSING_IMAGE_REFERENCE",
                "warning",
                null,
                null,
                $"Markdown has {sourceFacts.Count - outputFacts.Count} fewer exact image reference(s) than source."));
        }
    }

    private static void AddHeadingLevelMetric(
        SourceFactsDocument source,
        MarkdownFactsDocument output,
        List<QualityMetric> metrics)
    {
        AddExactAttributeAgreementMetric(
            "heading_level_accuracy",
            FactKind.HEADING,
            FactProperty.Level,
            ["level"],
            "matched_heading_exact_level",
            source,
            output,
            metrics);
    }

    private static void AddCountMetrics(
        FactKind kind,
        string metricPrefix,
        SourceFactsDocument source,
        MarkdownFactsDocument output,
        List<QualityMetric> metrics)
    {
        var allSource = source.Facts
            .Where(fact => fact.Kind == kind)
            .ToList();

        var sourceFacts = allSource
            .Where(fact => SourceFactEvidence.IsExact(fact, FactProperty.Presence))
            .ToList();

        var outputCount = output.Facts.Count(fact => fact.Kind == kind);

        if (allSource.Count > 0 && sourceFacts.Count == 0)
        {
            metrics.Add(NotEvaluatedMetric(
                $"{metricPrefix}_count_recall",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "count_only_v0_2"));
            metrics.Add(NotEvaluatedMetric(
                $"{metricPrefix}_count_precision",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "count_only_v0_2"));
            return;
        }

        var matched = Math.Min(sourceFacts.Count, outputCount);

        metrics.Add(RatioMetric(
            $"{metricPrefix}_count_recall",
            matched,
            sourceFacts.Count,
            "count_only_v0_2"));

        if (allSource.Any(
                fact => !SourceFactEvidence.IsExact(fact, FactProperty.Presence)))
        {
            metrics.Add(NotEvaluatedMetric(
                $"{metricPrefix}_count_precision",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "count_only_v0_2"));
        }
        else
        {
            metrics.Add(RatioMetric(
                $"{metricPrefix}_count_precision",
                matched,
                outputCount,
                "count_only_v0_2"));
        }
    }

    private static void AddLinkMetrics(
        SourceFactsDocument source,
        MarkdownFactsDocument output,
        List<QualityMetric> metrics,
        List<QualityFinding> findings)
    {
        var allSourceFacts = source.Facts
            .Where(fact => fact.Kind == FactKind.LINK)
            .ToList();

        var sourceFacts = allSourceFacts
            .Where(fact => SourceFactEvidence.IsExact(fact, FactProperty.Target))
            .ToList();

        var sourceLinks = sourceFacts
            .Select(fact => new LinkValue(
                fact.Id,
                CanonicalizeUri(GetAttribute(fact.Attributes, "target"))))
            .Where(value => !string.IsNullOrWhiteSpace(value.Target))
            .ToList();

        var outputLinks = output.Facts
            .Where(fact => fact.Kind == FactKind.LINK)
            .Select(fact => new LinkValue(
                fact.Id,
                CanonicalizeUri(GetAttribute(fact.Attributes, "target"))))
            .Where(value => !string.IsNullOrWhiteSpace(value.Target))
            .ToList();

        if (allSourceFacts.Count > 0 && sourceFacts.Count == 0)
        {
            metrics.Add(NotEvaluatedMetric(
                "link_target_recall",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "canonical_uri_multiset"));
            metrics.Add(NotEvaluatedMetric(
                "link_target_precision",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "canonical_uri_multiset"));
            return;
        }

        var outputCounts = outputLinks
            .GroupBy(value => value.Target, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);

        var matched = 0;
        foreach (var sourceLink in sourceLinks)
        {
            if (outputCounts.TryGetValue(sourceLink.Target, out var count) && count > 0)
            {
                matched++;
                outputCounts[sourceLink.Target] = count - 1;
            }
            else
            {
                findings.Add(new QualityFinding(
                    "MISSING_LINK_TARGET",
                    "warning",
                    sourceLink.FactId,
                    null,
                    $"Source exact link target '{sourceLink.Target}' was not matched in Markdown output."));
            }
        }

        metrics.Add(RatioMetric(
            "link_target_recall",
            matched,
            sourceLinks.Count,
            "canonical_uri_multiset"));

        if (allSourceFacts.Any(
                fact => !SourceFactEvidence.IsExact(fact, FactProperty.Target)))
        {
            metrics.Add(NotEvaluatedMetric(
                "link_target_precision",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "canonical_uri_multiset"));
        }
        else
        {
            metrics.Add(RatioMetric(
                "link_target_precision",
                matched,
                outputLinks.Count,
                "canonical_uri_multiset"));
        }
    }

    private static void AddOrderMetrics(
        SourceFactsDocument source,
        MarkdownFactsDocument output,
        List<QualityMetric> metrics)
    {
        var allSourceFacts = source.Facts
            .Where(fact =>
                OrderedKinds.Contains(fact.Kind) &&
                !string.IsNullOrWhiteSpace(fact.Text))
            .OrderBy(fact => fact.Order)
            .ToList();

        var sourceFacts = allSourceFacts
            .Where(fact => SourceFactEvidence.IsExact(fact, FactProperty.Order))
            .ToList();

        var outputFacts = output.Facts
            .Where(fact =>
                OrderedKinds.Contains(fact.Kind) &&
                !string.IsNullOrWhiteSpace(fact.Text))
            .OrderBy(fact => fact.Order)
            .ToList();

        if (sourceFacts.Count == 0)
        {
            metrics.Add(allSourceFacts.Count == 0
                ? new QualityMetric(
                    "order_anchor_coverage",
                    "NOT_APPLICABLE",
                    null,
                    0,
                    0,
                    "exact_kind_text_anchor")
                : NotEvaluatedMetric(
                    "order_anchor_coverage",
                    "NOT_EVALUATED_SOURCE_CERTAINTY",
                    "exact_kind_text_anchor"));
        }

        var outputQueues = outputFacts
            .GroupBy(fact => MatchKey(fact.Kind, fact.Text))
            .ToDictionary(
                group => group.Key,
                group => new Queue<MarkdownFact>(
                    group.OrderBy(fact => fact.Order)),
                StringComparer.Ordinal);

        var matched = new List<(int SourceOrder, int OutputOrder)>();

        foreach (var sourceFact in sourceFacts)
        {
            var key = MatchKey(sourceFact.Kind, sourceFact.Text);
            if (outputQueues.TryGetValue(key, out var queue) && queue.Count > 0)
            {
                matched.Add((sourceFact.Order, queue.Dequeue().Order));
            }
        }

        if (sourceFacts.Count > 0)
        {
            metrics.Add(RatioMetric(
                "order_anchor_coverage",
                matched.Count,
                sourceFacts.Count,
                "exact_kind_text_anchor"));
        }

        if (allSourceFacts.Count < 2)
        {
            metrics.Add(new QualityMetric(
                "pairwise_order_accuracy",
                "NOT_APPLICABLE",
                null,
                0,
                0,
                "pairwise_relative_order"));
            return;
        }

        if (sourceFacts.Count < 2)
        {
            metrics.Add(NotEvaluatedMetric(
                "pairwise_order_accuracy",
                "NOT_EVALUATED_SOURCE_CERTAINTY",
                "pairwise_relative_order"));
            return;
        }

        if (matched.Count < 2)
        {
            metrics.Add(NotEvaluatedMetric(
                "pairwise_order_accuracy",
                "NOT_EVALUATED_INSUFFICIENT_ANCHORS",
                "pairwise_relative_order"));
            return;
        }

        var totalPairs = 0;
        var concordantPairs = 0;

        for (var left = 0; left < matched.Count - 1; left++)
        {
            for (var right = left + 1; right < matched.Count; right++)
            {
                totalPairs++;

                var sourceDelta = matched[left].SourceOrder
                    .CompareTo(matched[right].SourceOrder);
                var outputDelta = matched[left].OutputOrder
                    .CompareTo(matched[right].OutputOrder);

                if (sourceDelta == outputDelta)
                {
                    concordantPairs++;
                }
            }
        }

        metrics.Add(RatioMetric(
            "pairwise_order_accuracy",
            concordantPairs,
            totalPairs,
            "pairwise_relative_order"));
    }

    private static void AddExactAttributeAgreementMetric(
        string metricName,
        FactKind kind,
        string property,
        IReadOnlyList<string> attributes,
        string method,
        SourceFactsDocument source,
        MarkdownFactsDocument output,
        List<QualityMetric> metrics)
    {
        var allSource = source.Facts
            .Where(f => f.Kind == kind)
            .OrderBy(f => f.Order)
            .ToList();

        var sourceFacts = allSource
            .Where(fact => SourceFactEvidence.IsExact(fact, property))
            .Where(f => HasAttributes(f.Attributes, attributes))
            .ToList();

        if (allSource.Count == 0)
        {
            metrics.Add(new QualityMetric(
                metricName,
                "NOT_APPLICABLE",
                null,
                0,
                0,
                method));
            return;
        }

        if (sourceFacts.Count == 0)
        {
            metrics.Add(NotEvaluatedMetric(
                metricName,
                "NOT_EVALUATED_SOURCE_EVIDENCE",
                method));
            return;
        }

        var outputFacts = output.Facts
            .Where(f => f.Kind == kind)
            .OrderBy(f => f.Order)
            .ToList();

        var match = MatchByNormalizedText(sourceFacts, outputFacts);
        if (match.Matches.Count == 0)
        {
            metrics.Add(NotEvaluatedMetric(
                metricName,
                "NOT_EVALUATED_NO_MATCHED_FACTS",
                method));
            return;
        }

        var correct = match.Matches.Count(pair =>
            attributes.All(key =>
                TryGetAttribute(pair.Source.Attributes, key, out var sourceValue) &&
                TryGetAttribute(pair.Output.Attributes, key, out var outputValue) &&
                string.Equals(sourceValue, outputValue, StringComparison.Ordinal)));

        metrics.Add(RatioMetric(
            metricName,
            correct,
            match.Matches.Count,
            method));
    }

    private static MatchResult MatchByNormalizedText(
        IReadOnlyList<SourceFact> sourceFacts,
        IReadOnlyList<MarkdownFact> outputFacts)
    {
        var outputQueues = outputFacts
            .GroupBy(fact => NormalizeText(fact.Text))
            .ToDictionary(
                group => group.Key,
                group => new Queue<MarkdownFact>(group.OrderBy(fact => fact.Order)),
                StringComparer.Ordinal);

        var matches = new List<FactPair>();
        var matchedSourceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sourceFact in sourceFacts)
        {
            var normalized = NormalizeText(sourceFact.Text);
            if (string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            if (outputQueues.TryGetValue(normalized, out var queue) && queue.Count > 0)
            {
                var outputFact = queue.Dequeue();
                matches.Add(new FactPair(sourceFact, outputFact));
                matchedSourceIds.Add(sourceFact.Id);
            }
        }

        return new MatchResult(matches, matchedSourceIds);
    }

    private static QualityMetric NotEvaluatedMetric(
        string name,
        string status,
        string method) =>
        new(name, status, null, 0, 0, method);

    private static bool IsFactExact(SourceFact fact) =>
        fact.Certainty is FactCertainty.SOURCE_EXACT or FactCertainty.DERIVED_EXACT;

    private static bool HasAttributes(
        IReadOnlyDictionary<string, string>? attributes,
        IReadOnlyList<string> required) =>
        attributes is not null &&
        required.All(key =>
            attributes.TryGetValue(key, out var value) &&
            !string.IsNullOrWhiteSpace(value));

    private static QualityMetric RatioMetric(
        string name,
        int numerator,
        int denominator,
        string method)
    {
        if (denominator == 0)
        {
            return new QualityMetric(
                name,
                "NOT_APPLICABLE",
                null,
                numerator,
                denominator,
                method);
        }

        return new QualityMetric(
            name,
            "EVALUATED",
            (double)numerator / denominator,
            numerator,
            denominator,
            method);
    }

    private static IReadOnlyList<string> Tokenize(string? text)
    {
        var normalized = NormalizeText(text);
        if (string.IsNullOrEmpty(normalized))
        {
            return Array.Empty<string>();
        }

        return TokenRegex()
            .Matches(normalized)
            .Select(match => match.Value)
            .ToArray();
    }

    private static int MultisetIntersectionCount(
        IReadOnlyList<string> source,
        IReadOnlyList<string> output)
    {
        var counts = output
            .GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var matched = 0;
        foreach (var token in source)
        {
            if (counts.TryGetValue(token, out var count) && count > 0)
            {
                matched++;
                counts[token] = count - 1;
            }
        }

        return matched;
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Normalize(NormalizationForm.FormC);
        return WhitespaceRegex().Replace(normalized, " ").Trim();
    }

    private static string MatchKey(FactKind kind, string? text) =>
        $"{kind}\u001f{NormalizeText(text)}";

    private static string? GetAttribute(
        IReadOnlyDictionary<string, string>? attributes,
        string key) =>
        attributes is not null && attributes.TryGetValue(key, out var value)
            ? value
            : null;

    private static bool TryGetAttribute(
        IReadOnlyDictionary<string, string>? attributes,
        string key,
        out string value)
    {
        var candidate = GetAttribute(attributes, key);
        if (candidate is null)
        {
            value = string.Empty;
            return false;
        }

        value = candidate;
        return true;
    }

    private static string CanonicalizeUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return uri.AbsoluteUri;
        }

        return value.Trim();
    }

    private sealed record FactPair(SourceFact Source, MarkdownFact Output);

    private sealed record MatchResult(
        IReadOnlyList<FactPair> Matches,
        IReadOnlySet<string> MatchedSourceIds);

    private sealed record LinkValue(string FactId, string Target);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\p{L}[\p{L}\p{M}\p{N}'’\-]*|\p{N}+(?:[.,]\p{N}+)?|[^\s]", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}

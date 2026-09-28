using System.Text.Json.Serialization;

namespace Zlet.Quality.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FactKind
{
    TEXT_BLOCK,
    HEADING,
    LIST_ITEM,
    TABLE,
    TABLE_CELL,
    LINK,
    IMAGE
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FactCertainty
{
    SOURCE_EXACT,
    DERIVED_EXACT,
    INFERRED,
    UNAVAILABLE
}

public static class FactProperty
{
    public const string Presence = "presence";
    public const string Text = "text";
    public const string Order = "order";
    public const string Level = "level";
    public const string Position = "position";
    public const string Target = "target";
}

public static class SourceFactEvidence
{
    public static FactCertainty CertaintyFor(SourceFact fact, string property)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentException.ThrowIfNullOrWhiteSpace(property);

        return fact.PropertyCertainty is not null &&
               fact.PropertyCertainty.TryGetValue(property, out var certainty)
            ? certainty
            : fact.Certainty;
    }

    public static bool IsExact(SourceFact fact, string property) =>
        CertaintyFor(fact, property) is
            FactCertainty.SOURCE_EXACT or FactCertainty.DERIVED_EXACT;
}

public sealed record SourceFact(
    string Id,
    FactKind Kind,
    string? Text,
    int Order,
    string? Location,
    FactCertainty Certainty,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyDictionary<string, FactCertainty>? PropertyCertainty = null);

public sealed record MarkdownFact(
    string Id,
    FactKind Kind,
    string? Text,
    int Order,
    string? Location,
    IReadOnlyDictionary<string, string>? Attributes = null);

public sealed record SourceFactsDocument(
    string SchemaVersion,
    string InspectorId,
    string InspectorVersion,
    string SourceFormat,
    string SourceSha256,
    IReadOnlyList<SourceFact> Facts);

public sealed record MarkdownFactsDocument(
    string SchemaVersion,
    string InspectorId,
    string InspectorVersion,
    string MarkdownSha256,
    IReadOnlyList<MarkdownFact> Facts);

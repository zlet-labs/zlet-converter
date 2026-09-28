using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Zlet.Quality.Core;

public sealed class PptxSourceInspector : ISourceInspector
{
    public const string Id = "zlet-pptx-openxml";
    public const string Version = "0.2.0";

    public string InspectorId => Id;
    public string InspectorVersion => Version;

    public bool Supports(string sourcePath) =>
        string.Equals(Path.GetExtension(sourcePath), ".pptx", StringComparison.OrdinalIgnoreCase);

    public async Task<SourceFactsDocument> InspectAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "Source presentation was not found.",
                sourcePath);
        }

        if (!Supports(sourcePath))
        {
            throw new NotSupportedException(
                $"PPTX inspector does not support '{Path.GetExtension(sourcePath)}'.");
        }

        var hash = await ComputeSha256Async(sourcePath, cancellationToken);
        var facts = new List<SourceFact>();
        var order = 0;

        using var document = PresentationDocument.Open(sourcePath, false);
        var presentationPart = document.PresentationPart ??
            throw new InvalidDataException("PPTX has no PresentationPart.");

        var slideIds = presentationPart.Presentation?.SlideIdList?
            .Elements<P.SlideId>()
            .ToList() ?? [];

        for (var slideIndex = 0; slideIndex < slideIds.Count; slideIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relationshipId = slideIds[slideIndex].RelationshipId?.Value ??
                throw new InvalidDataException(
                    $"Slide {slideIndex + 1} has no relationship id.");

            var slidePart =
                (SlidePart)presentationPart.GetPartById(relationshipId);

            var shapes =
                slidePart.Slide?.CommonSlideData?.ShapeTree?.ChildElements ?? [];

            var shapeIndex = 0;
            foreach (var element in shapes)
            {
                if (element is not P.Shape shape)
                {
                    continue;
                }

                shapeIndex++;
                var paragraphs =
                    shape.TextBody?.Elements<A.Paragraph>().ToList() ?? [];

                for (var paragraphIndex = 0;
                     paragraphIndex < paragraphs.Count;
                     paragraphIndex++)
                {
                    var text = ExtractText(paragraphs[paragraphIndex]);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var attributes = new Dictionary<string, string>
                    {
                        ["slide"] = (slideIndex + 1).ToString(),
                        ["shape"] = shapeIndex.ToString()
                    };

                    var kind = FactKind.TEXT_BLOCK;
                    var certainty = FactCertainty.SOURCE_EXACT;

                    var placeholderType = shape.NonVisualShapeProperties?
                        .ApplicationNonVisualDrawingProperties?
                        .PlaceholderShape?
                        .Type?
                        .Value;

                    if (placeholderType == P.PlaceholderValues.Title ||
                        placeholderType == P.PlaceholderValues.CenteredTitle)
                    {
                        kind = FactKind.HEADING;
                        attributes["level"] = "1";
                        certainty = FactCertainty.DERIVED_EXACT;
                    }

                    var paragraphProperties =
                        paragraphs[paragraphIndex].ParagraphProperties;

                    if (kind != FactKind.HEADING &&
                        paragraphProperties is not null &&
                        (paragraphProperties.GetFirstChild<A.CharacterBullet>() is not null ||
                         paragraphProperties.GetFirstChild<A.AutoNumberedBullet>() is not null))
                    {
                        kind = FactKind.LIST_ITEM;
                        attributes["level"] =
                            (paragraphProperties.Level?.Value ?? 0).ToString();
                    }

                    facts.Add(new SourceFact(
                        $"{Prefix(kind)}:{order + 1}",
                        kind,
                        text,
                        order++,
                        $"ppt/slides/slide{slideIndex + 1}.xml/shape[{shapeIndex}]/p[{paragraphIndex + 1}]",
                        certainty,
                        attributes,
                        new Dictionary<string, FactCertainty>
                        {
                            [FactProperty.Order] = FactCertainty.UNAVAILABLE
                        }));
                }
            }
        }

        return new SourceFactsDocument(
            QualityCoreVersion.SourceFactsSchemaVersion,
            Id,
            Version,
            "pptx",
            hash,
            facts);
    }

    private static string ExtractText(A.Paragraph paragraph)
    {
        var builder = new StringBuilder();

        foreach (var descendant in paragraph.Descendants())
        {
            if (descendant is A.Text text)
            {
                builder.Append(text.Text);
            }
            else if (descendant is A.Break)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string Prefix(FactKind kind) =>
        kind switch
        {
            FactKind.HEADING => "heading",
            FactKind.LIST_ITEM => "list",
            _ => "text"
        };

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

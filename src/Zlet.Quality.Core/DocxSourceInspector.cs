using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Blip = DocumentFormat.OpenXml.Drawing.Blip;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Zlet.Quality.Core;

public sealed partial class DocxSourceInspector : ISourceInspector
{
    public const string Id = "zlet-docx-openxml";
    public const string Version = "0.1.2";

    public string InspectorId => Id;

    public string InspectorVersion => Version;

    public bool Supports(string sourcePath) =>
        string.Equals(
            Path.GetExtension(sourcePath),
            ".docx",
            StringComparison.OrdinalIgnoreCase);

    public async Task<SourceFactsDocument> InspectAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source document was not found.", sourcePath);
        }

        if (!Supports(sourcePath))
        {
            throw new NotSupportedException(
                $"DOCX inspector does not support '{Path.GetExtension(sourcePath)}'.");
        }

        var sourceHash = await ComputeSha256Async(sourcePath, cancellationToken);
        var facts = new List<SourceFact>();

        using var document = WordprocessingDocument.Open(sourcePath, false);
        var mainPart = document.MainDocumentPart
            ?? throw new InvalidDataException("DOCX has no MainDocumentPart.");
        var mainDocument = mainPart.Document
            ?? throw new InvalidDataException("DOCX MainDocumentPart has no document.");
        var body = mainDocument.Body
            ?? throw new InvalidDataException("DOCX main document has no body.");

        var order = 0;
        var paragraphIndex = 0;
        var tableIndex = 0;
        var linkIndex = 0;
        var imageIndex = 0;

        foreach (var child in body.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (child)
            {
                case Paragraph paragraph:
                    paragraphIndex++;
                    InspectParagraph(
                        paragraph,
                        mainPart,
                        $"word/document.xml/body/p[{paragraphIndex}]",
                        facts,
                        ref order,
                        ref linkIndex,
                        ref imageIndex);
                    break;

                case Table table:
                    tableIndex++;
                    InspectTable(
                        table,
                        mainPart,
                        tableIndex,
                        facts,
                        ref order,
                        ref linkIndex,
                        ref imageIndex);
                    break;
            }
        }

        return new SourceFactsDocument(
            QualityCoreVersion.SourceFactsSchemaVersion,
            Id,
            Version,
            "docx",
            sourceHash,
            facts);
    }

    private static void InspectParagraph(
        Paragraph paragraph,
        MainDocumentPart mainPart,
        string location,
        List<SourceFact> facts,
        ref int order,
        ref int linkIndex,
        ref int imageIndex)
    {
        var text = ExtractText(paragraph);
        var attributes = new Dictionary<string, string>();

        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(styleId))
        {
            attributes["styleId"] = styleId;
        }

        FactKind kind;
        FactCertainty certainty;

        if (TryGetHeadingLevel(paragraph, mainPart, out var headingLevel, out certainty))
        {
            kind = FactKind.HEADING;
            attributes["level"] = headingLevel.ToString();
        }
        else if (TryGetListInfo(
                     paragraph,
                     mainPart,
                     out var listId,
                     out var listLevel,
                     out var numberingSource,
                     out certainty))
        {
            kind = FactKind.LIST_ITEM;
            attributes["numId"] = listId;
            attributes["numberingSource"] = numberingSource;
            if (!string.IsNullOrWhiteSpace(listLevel))
            {
                attributes["level"] = listLevel;
            }
        }
        else
        {
            kind = FactKind.TEXT_BLOCK;
            certainty = FactCertainty.SOURCE_EXACT;
        }

        facts.Add(new SourceFact(
            $"{KindPrefix(kind)}:{order + 1}",
            kind,
            text,
            order++,
            location,
            certainty,
            attributes.Count == 0 ? null : attributes));

        InspectLinks(
            paragraph,
            mainPart,
            location,
            facts,
            ref order,
            ref linkIndex);

        InspectImages(
            paragraph,
            mainPart,
            location,
            facts,
            ref order,
            ref imageIndex);
    }

    private static void InspectTable(
        Table table,
        MainDocumentPart mainPart,
        int tableIndex,
        List<SourceFact> facts,
        ref int order,
        ref int linkIndex,
        ref int imageIndex)
    {
        var rows = table.Elements<TableRow>().ToList();
        var columnCount = GetTableColumnCount(table, rows);

        facts.Add(new SourceFact(
            $"table:{tableIndex}",
            FactKind.TABLE,
            null,
            order++,
            $"word/document.xml/body/tbl[{tableIndex}]",
            FactCertainty.SOURCE_EXACT,
            new Dictionary<string, string>
            {
                ["rows"] = rows.Count.ToString(),
                ["columns"] = columnCount.ToString()
            }));

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var gridColumn = 0;
            var cells = rows[rowIndex].Elements<TableCell>().ToList();

            for (var physicalCellIndex = 0; physicalCellIndex < cells.Count; physicalCellIndex++)
            {
                var cell = cells[physicalCellIndex];
                var span = GetColumnSpan(cell);
                var location =
                    $"word/document.xml/body/tbl[{tableIndex}]/tr[{rowIndex + 1}]/tc[{physicalCellIndex + 1}]";

                var attributes = new Dictionary<string, string>
                {
                    ["tableId"] = $"table:{tableIndex}",
                    ["row"] = rowIndex.ToString(),
                    ["column"] = gridColumn.ToString(),
                    ["columnSpan"] = span.ToString()
                };

                var verticalMerge = cell.TableCellProperties?.VerticalMerge;
                if (verticalMerge is not null)
                {
                    attributes["verticalMerge"] =
                        verticalMerge.Val?.Value.ToString() ?? "continue";
                }

                facts.Add(new SourceFact(
                    $"table:{tableIndex}:cell:{rowIndex}:{gridColumn}",
                    FactKind.TABLE_CELL,
                    ExtractText(cell),
                    order++,
                    location,
                    FactCertainty.SOURCE_EXACT,
                    attributes));

                InspectLinks(
                    cell,
                    mainPart,
                    location,
                    facts,
                    ref order,
                    ref linkIndex);

                InspectImages(
                    cell,
                    mainPart,
                    location,
                    facts,
                    ref order,
                    ref imageIndex);

                gridColumn += span;
            }
        }
    }

    private static bool TryGetHeadingLevel(
        Paragraph paragraph,
        MainDocumentPart mainPart,
        out int level,
        out FactCertainty certainty)
    {
        var directOutline = paragraph.ParagraphProperties?.OutlineLevel?.Val?.Value;
        if (directOutline is not null)
        {
            level = checked((int)directOutline.Value + 1);
            certainty = FactCertainty.SOURCE_EXACT;
            return true;
        }

        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (string.IsNullOrWhiteSpace(styleId))
        {
            level = 0;
            certainty = FactCertainty.UNAVAILABLE;
            return false;
        }

        var style = mainPart.StyleDefinitionsPart?
            .Styles?
            .Elements<Style>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.StyleId?.Value,
                    styleId,
                    StringComparison.OrdinalIgnoreCase));

        var styleOutline = style?.StyleParagraphProperties?.OutlineLevel?.Val?.Value;
        if (styleOutline is not null)
        {
            level = checked((int)styleOutline.Value + 1);
            certainty = FactCertainty.DERIVED_EXACT;
            return true;
        }

        var styleName = style?.StyleName?.Val?.Value;
        if (TryParseHeadingLevel(styleId, out level) ||
            TryParseHeadingLevel(styleName, out level))
        {
            certainty = FactCertainty.DERIVED_EXACT;
            return true;
        }

        level = 0;
        certainty = FactCertainty.UNAVAILABLE;
        return false;
    }

    private static bool TryParseHeadingLevel(string? value, out int level)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            level = 0;
            return false;
        }

        var match = HeadingStyleRegex().Match(value.Trim());
        level = 0;

        return match.Success &&
               int.TryParse(match.Groups[1].Value, out level) &&
               level is >= 1 and <= 9;
    }

    private static bool TryGetListInfo(
        Paragraph paragraph,
        MainDocumentPart mainPart,
        out string listId,
        out string listLevel,
        out string numberingSource,
        out FactCertainty certainty)
    {
        var directNumbering = paragraph.ParagraphProperties?.NumberingProperties;
        var directNumId = directNumbering?.NumberingId?.Val?.Value;
        if (directNumId is not null)
        {
            if (directNumId.Value == 0)
            {
                return NoList(out listId, out listLevel, out numberingSource, out certainty);
            }

            listId = directNumId.Value.ToString();
            numberingSource = "paragraph";
            certainty = FactCertainty.SOURCE_EXACT;

            var directLevel = directNumbering?.NumberingLevelReference?.Val?.Value;
            if (directLevel is not null)
            {
                listLevel = directLevel.Value.ToString();
                return true;
            }

            var paragraphStyleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            listLevel = TryResolveNumberingLevelForStyle(
                mainPart,
                directNumId.Value,
                paragraphStyleId,
                paragraphStyleId,
                out var resolvedDirectLevel)
                ? resolvedDirectLevel
                : string.Empty;
            return true;
        }

        var appliedStyleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (string.IsNullOrWhiteSpace(appliedStyleId))
        {
            return NoList(out listId, out listLevel, out numberingSource, out certainty);
        }

        if (!TryResolveStyleNumbering(
                mainPart,
                appliedStyleId,
                out var styleNumId,
                out var numberingStyleId))
        {
            return NoList(out listId, out listLevel, out numberingSource, out certainty);
        }

        if (styleNumId == 0)
        {
            return NoList(out listId, out listLevel, out numberingSource, out certainty);
        }

        listId = styleNumId.ToString();
        numberingSource = $"style:{numberingStyleId}";
        certainty = FactCertainty.DERIVED_EXACT;
        listLevel = TryResolveNumberingLevelForStyle(
            mainPart,
            styleNumId,
            appliedStyleId,
            numberingStyleId,
            out var resolvedStyleLevel)
            ? resolvedStyleLevel
            : string.Empty;
        return true;
    }

    private static bool TryResolveStyleNumbering(
        MainDocumentPart mainPart,
        string appliedStyleId,
        out int numId,
        out string numberingStyleId)
    {
        var styles = mainPart.StyleDefinitionsPart?.Styles;
        if (styles is null)
        {
            numId = 0;
            numberingStyleId = string.Empty;
            return false;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentStyleId = appliedStyleId;

        while (!string.IsNullOrWhiteSpace(currentStyleId) && visited.Add(currentStyleId))
        {
            var style = styles.Elements<Style>().FirstOrDefault(candidate =>
                string.Equals(
                    candidate.StyleId?.Value,
                    currentStyleId,
                    StringComparison.OrdinalIgnoreCase));

            if (style is null)
            {
                break;
            }

            var styleNumId = style.StyleParagraphProperties?
                .NumberingProperties?
                .NumberingId?
                .Val?
                .Value;
            if (styleNumId is not null)
            {
                numId = styleNumId.Value;
                numberingStyleId = currentStyleId;
                return true;
            }

            currentStyleId = style.BasedOn?.Val?.Value ?? string.Empty;
        }

        numId = 0;
        numberingStyleId = string.Empty;
        return false;
    }

    private static bool TryResolveNumberingLevelForStyle(
        MainDocumentPart mainPart,
        int numId,
        string? appliedStyleId,
        string? numberingStyleId,
        out string level)
    {
        level = string.Empty;
        var numbering = mainPart.NumberingDefinitionsPart?.Numbering;
        if (numbering is null)
        {
            return false;
        }

        var instance = numbering.Elements<NumberingInstance>()
            .FirstOrDefault(candidate => candidate.NumberID?.Value == numId);
        var abstractNumId = instance?.AbstractNumId?.Val?.Value;
        if (abstractNumId is null)
        {
            return false;
        }

        var abstractNumbering = numbering.Elements<AbstractNum>()
            .FirstOrDefault(candidate => candidate.AbstractNumberId?.Value == abstractNumId.Value);
        if (abstractNumbering is null)
        {
            return false;
        }

        var styleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(appliedStyleId))
        {
            styleIds.Add(appliedStyleId);
        }
        if (!string.IsNullOrWhiteSpace(numberingStyleId))
        {
            styleIds.Add(numberingStyleId);
        }

        foreach (var numberingLevel in abstractNumbering.Elements<Level>())
        {
            var levelStyleId = numberingLevel.ParagraphStyleIdInLevel?.Val?.Value;
            if (string.IsNullOrWhiteSpace(levelStyleId) || !styleIds.Contains(levelStyleId))
            {
                continue;
            }

            var levelIndex = numberingLevel.LevelIndex?.Value;
            if (levelIndex is null)
            {
                continue;
            }

            level = levelIndex.Value.ToString();
            return true;
        }

        return false;
    }

    private static bool NoList(
        out string listId,
        out string listLevel,
        out string numberingSource,
        out FactCertainty certainty)
    {
        listId = string.Empty;
        listLevel = string.Empty;
        numberingSource = string.Empty;
        certainty = FactCertainty.UNAVAILABLE;
        return false;
    }

    private static void InspectLinks(
        DocumentFormat.OpenXml.OpenXmlElement root,
        MainDocumentPart mainPart,
        string location,
        List<SourceFact> facts,
        ref int order,
        ref int linkIndex)
    {
        foreach (var hyperlink in root.Descendants<Hyperlink>())
        {
            linkIndex++;
            var attributes = new Dictionary<string, string>();

            var relationshipId = hyperlink.Id?.Value;
            if (!string.IsNullOrWhiteSpace(relationshipId))
            {
                attributes["relationshipId"] = relationshipId;
                var relationship = mainPart.HyperlinkRelationships
                    .FirstOrDefault(candidate => candidate.Id == relationshipId);
                if (relationship is not null)
                {
                    attributes["target"] = relationship.Uri.ToString();
                }
            }

            var anchor = hyperlink.Anchor?.Value;
            if (!string.IsNullOrWhiteSpace(anchor))
            {
                attributes["anchor"] = anchor;
            }

            facts.Add(new SourceFact(
                $"link:{linkIndex}",
                FactKind.LINK,
                ExtractText(hyperlink),
                order++,
                $"{location}/hyperlink[{linkIndex}]",
                FactCertainty.SOURCE_EXACT,
                attributes.Count == 0 ? null : attributes));
        }
    }

    private static void InspectImages(
        DocumentFormat.OpenXml.OpenXmlElement root,
        MainDocumentPart mainPart,
        string location,
        List<SourceFact> facts,
        ref int order,
        ref int imageIndex)
    {
        foreach (var blip in root.Descendants<Blip>())
        {
            imageIndex++;
            var relationshipId = blip.Embed?.Value;
            var attributes = new Dictionary<string, string>();

            if (!string.IsNullOrWhiteSpace(relationshipId))
            {
                attributes["relationshipId"] = relationshipId;

                try
                {
                    var part = mainPart.GetPartById(relationshipId);
                    attributes["partUri"] = part.Uri.ToString();
                    attributes["contentType"] = part.ContentType;
                }
                catch (ArgumentOutOfRangeException)
                {
                    attributes["relationshipStatus"] = "missing";
                }
            }

            facts.Add(new SourceFact(
                $"image:{imageIndex}",
                FactKind.IMAGE,
                null,
                order++,
                $"{location}/image[{imageIndex}]",
                FactCertainty.SOURCE_EXACT,
                attributes.Count == 0 ? null : attributes));
        }
    }

    private static int GetTableColumnCount(
        Table table,
        IReadOnlyList<TableRow> rows)
    {
        var gridColumns = table.GetFirstChild<TableGrid>()?
            .Elements<GridColumn>()
            .Count();

        if (gridColumns is > 0)
        {
            return gridColumns.Value;
        }

        return rows.Count == 0
            ? 0
            : rows.Max(row =>
                row.Elements<TableCell>()
                    .Sum(GetColumnSpan));
    }

    private static int GetColumnSpan(TableCell cell)
    {
        var span = cell.TableCellProperties?.GridSpan?.Val?.Value;
        return span is > 0 ? checked((int)span.Value) : 1;
    }

    private static string ExtractText(DocumentFormat.OpenXml.OpenXmlElement element)
    {
        var builder = new StringBuilder();

        foreach (var descendant in element.Descendants())
        {
            switch (descendant)
            {
                case Text text:
                    builder.Append(text.Text);
                    break;
                case Break:
                case CarriageReturn:
                    builder.Append('\n');
                    break;
                case TabChar:
                    builder.Append('\t');
                    break;
            }
        }

        return builder.ToString();
    }

    private static string KindPrefix(FactKind kind) =>
        kind switch
        {
            FactKind.HEADING => "heading",
            FactKind.LIST_ITEM => "list-item",
            _ => "text-block"
        };

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [GeneratedRegex(@"^heading\s*([1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingStyleRegex();
}
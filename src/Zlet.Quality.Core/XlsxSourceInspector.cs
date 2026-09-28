using System.Globalization;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace Zlet.Quality.Core;

public sealed class XlsxSourceInspector : ISourceInspector
{
    public const string Id = "zlet-xlsx-openxml";
    public const string Version = "0.2.0";

    public string InspectorId => Id;
    public string InspectorVersion => Version;

    public bool Supports(string sourcePath) =>
        string.Equals(
            Path.GetExtension(sourcePath),
            ".xlsx",
            StringComparison.OrdinalIgnoreCase);

    public async Task<SourceFactsDocument> InspectAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "Source workbook was not found.",
                sourcePath);
        }

        if (!Supports(sourcePath))
        {
            throw new NotSupportedException(
                $"XLSX inspector does not support '{Path.GetExtension(sourcePath)}'.");
        }

        var hash = await HashAsync(sourcePath, cancellationToken);
        var facts = new List<SourceFact>();
        var order = 0;
        var tableNo = 0;

        using var document = SpreadsheetDocument.Open(sourcePath, false);
        var workbookPart = document.WorkbookPart ??
            throw new InvalidDataException("XLSX has no WorkbookPart.");

        var sharedStrings =
            workbookPart.SharedStringTablePart?.SharedStringTable;
        var stylesheet =
            workbookPart.WorkbookStylesPart?.Stylesheet;

        var sheets =
            workbookPart.Workbook?.Sheets?.Elements<S.Sheet>().ToList() ?? [];

        for (var sheetIndex = 0; sheetIndex < sheets.Count; sheetIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sheet = sheets[sheetIndex];
            var relationshipId = sheet.Id?.Value ??
                throw new InvalidDataException(
                    $"Sheet {sheetIndex + 1} has no relationship id.");

            var worksheetPart =
                (WorksheetPart)workbookPart.GetPartById(relationshipId);

            var rows = worksheetPart.Worksheet?
                .GetFirstChild<S.SheetData>()?
                .Elements<S.Row>()
                .ToList() ?? [];

            if (rows.Count == 0)
            {
                continue;
            }

            tableNo++;

            var cells = rows
                .SelectMany(row => row.Elements<S.Cell>())
                .ToList();

            var maxColumn = cells
                .Select(cell => ColumnIndex(cell.CellReference?.Value))
                .DefaultIfEmpty(0)
                .Max() + 1;

            var maxRow = cells
                .Select(cell => CellRowIndex(cell.CellReference?.Value))
                .Where(index => index >= 0)
                .DefaultIfEmpty(rows.Count - 1)
                .Max() + 1;

            var sheetName =
                sheet.Name?.Value ?? $"Sheet{sheetIndex + 1}";

            facts.Add(new SourceFact(
                $"table:{tableNo}",
                FactKind.TABLE,
                null,
                order++,
                $"xl/worksheets/sheet{sheetIndex + 1}.xml",
                FactCertainty.SOURCE_EXACT,
                new Dictionary<string, string>
                {
                    ["sheet"] = sheetName,
                    ["rows"] = maxRow.ToString(CultureInfo.InvariantCulture),
                    ["nonEmptyRows"] = rows.Count.ToString(CultureInfo.InvariantCulture),
                    ["columns"] = maxColumn.ToString(CultureInfo.InvariantCulture)
                }));

            for (var enumeratedRow = 0;
                 enumeratedRow < rows.Count;
                 enumeratedRow++)
            {
                foreach (var cell in rows[enumeratedRow].Elements<S.Cell>())
                {
                    var reference = cell.CellReference?.Value ?? "";
                    var row = CellRowIndex(reference);

                    if (row < 0)
                    {
                        row = rows[enumeratedRow].RowIndex?.Value is uint rowIndex
                            ? checked((int)rowIndex - 1)
                            : enumeratedRow;
                    }

                    var column = ColumnIndex(reference);
                    var evidence = CellText(
                        cell,
                        sharedStrings,
                        stylesheet);

                    var attributes = new Dictionary<string, string>
                    {
                        ["tableId"] = $"table:{tableNo}",
                        ["row"] = row.ToString(CultureInfo.InvariantCulture),
                        ["column"] = column.ToString(CultureInfo.InvariantCulture),
                        ["sheet"] = sheetName,
                        ["valueSemantics"] = evidence.Semantics
                    };

                    if (evidence.NumberFormatId is not null)
                    {
                        attributes["numberFormatId"] =
                            evidence.NumberFormatId.Value.ToString(
                                CultureInfo.InvariantCulture);
                    }

                    facts.Add(new SourceFact(
                        $"table:{tableNo}:cell:{row}:{column}",
                        FactKind.TABLE_CELL,
                        evidence.Text,
                        order++,
                        $"xl/worksheets/sheet{sheetIndex + 1}.xml/{reference}",
                        FactCertainty.SOURCE_EXACT,
                        attributes,
                        new Dictionary<string, FactCertainty>
                        {
                            [FactProperty.Text] = evidence.TextCertainty,
                            [FactProperty.Position] = FactCertainty.SOURCE_EXACT,
                            [FactProperty.Order] = FactCertainty.SOURCE_EXACT,
                            [FactProperty.Presence] = FactCertainty.SOURCE_EXACT
                        }));
                }
            }
        }

        return new SourceFactsDocument(
            QualityCoreVersion.SourceFactsSchemaVersion,
            Id,
            Version,
            "xlsx",
            hash,
            facts);
    }

    private static CellTextEvidence CellText(
        S.Cell cell,
        S.SharedStringTable? sharedStrings,
        S.Stylesheet? stylesheet)
    {
        var raw = cell.CellValue?.InnerText ?? cell.InnerText ?? "";
        var numberFormatId = NumberFormatId(cell, stylesheet);

        if (cell.CellFormula is not null)
        {
            return new CellTextEvidence(
                raw,
                FactCertainty.INFERRED,
                "cached-formula-display-unverified",
                numberFormatId);
        }

        if (cell.DataType?.Value == S.CellValues.SharedString &&
            int.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var sharedStringIndex) &&
            sharedStrings is not null)
        {
            return new CellTextEvidence(
                sharedStrings.Elements<S.SharedStringItem>()
                    .ElementAtOrDefault(sharedStringIndex)?
                    .InnerText ?? "",
                FactCertainty.SOURCE_EXACT,
                "displayed-string",
                numberFormatId);
        }

        if (cell.DataType?.Value == S.CellValues.InlineString)
        {
            return new CellTextEvidence(
                cell.InlineString?.InnerText ?? "",
                FactCertainty.SOURCE_EXACT,
                "displayed-string",
                numberFormatId);
        }

        if (cell.DataType?.Value == S.CellValues.String)
        {
            return new CellTextEvidence(
                raw,
                FactCertainty.SOURCE_EXACT,
                "displayed-string",
                numberFormatId);
        }

        if (cell.DataType?.Value == S.CellValues.Boolean)
        {
            var text = raw switch
            {
                "1" => "TRUE",
                "0" => "FALSE",
                _ => raw
            };

            return new CellTextEvidence(
                text,
                raw is "1" or "0"
                    ? FactCertainty.DERIVED_EXACT
                    : FactCertainty.INFERRED,
                "displayed-boolean",
                numberFormatId);
        }

        if (cell.DataType?.Value == S.CellValues.Error)
        {
            return new CellTextEvidence(
                raw,
                FactCertainty.SOURCE_EXACT,
                "displayed-error",
                numberFormatId);
        }

        if (IsSafeGeneralInteger(raw, numberFormatId))
        {
            return new CellTextEvidence(
                raw,
                FactCertainty.DERIVED_EXACT,
                "displayed-general-integer",
                numberFormatId);
        }

        return new CellTextEvidence(
            raw,
            FactCertainty.INFERRED,
            numberFormatId is null or 0
                ? "displayed-general-numeric-unverified"
                : "displayed-formatted-numeric-unverified",
            numberFormatId);
    }

    private static uint? NumberFormatId(
        S.Cell cell,
        S.Stylesheet? stylesheet)
    {
        if (cell.StyleIndex?.Value is not uint styleIndex ||
            stylesheet?.CellFormats is null)
        {
            return null;
        }

        var cellFormat = stylesheet.CellFormats
            .Elements<S.CellFormat>()
            .ElementAtOrDefault(checked((int)styleIndex));

        return cellFormat?.NumberFormatId?.Value;
    }

    private static bool IsSafeGeneralInteger(
        string raw,
        uint? numberFormatId)
    {
        if (numberFormatId is not null && numberFormatId != 0)
        {
            return false;
        }

        var digits = raw.TrimStart('+', '-');
        if (digits.Length is 0 or > 11 ||
            digits.Any(character => !char.IsDigit(character)))
        {
            return false;
        }

        return long.TryParse(
            raw,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out _);
    }

    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return 0;
        }

        var value = 0;
        foreach (var character in reference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            value =
                value * 26 +
                (char.ToUpperInvariant(character) - 'A' + 1);
        }

        return Math.Max(0, value - 1);
    }

    private static int CellRowIndex(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return -1;
        }

        var digits = new string(
            reference
                .SkipWhile(char.IsLetter)
                .TakeWhile(char.IsDigit)
                .ToArray());

        return int.TryParse(
            digits,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var oneBased) &&
            oneBased > 0
                ? oneBased - 1
                : -1;
    }

    private static async Task<string> HashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
    }

    private sealed record CellTextEvidence(
        string Text,
        FactCertainty TextCertainty,
        string Semantics,
        uint? NumberFormatId);
}

using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Zlet.Quality.Core;

/// <summary>Deterministic HTML DOM inspector with explicit semantic ownership.</summary>
public sealed partial class HtmlSourceInspector : ISourceInspector
{
    public const string Id = "zlet-html-anglesharp";
    public const string Version = "0.2.0";

    public string InspectorId => Id;
    public string InspectorVersion => Version;

    public bool Supports(string sourcePath) =>
        string.Equals(Path.GetExtension(sourcePath), ".html", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(sourcePath), ".htm", StringComparison.OrdinalIgnoreCase);

    public async Task<SourceFactsDocument> InspectAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source HTML was not found.", sourcePath);
        }

        if (!Supports(sourcePath))
        {
            throw new NotSupportedException("HTML inspector supports .html/.htm only.");
        }

        var hash = await HashAsync(sourcePath, cancellationToken);
        var html = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        var doc = await new HtmlParser().ParseDocumentAsync(html, cancellationToken);
        var facts = new List<SourceFact>();
        var order = 0;
        var tableNo = 0;

        foreach (var element in doc.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (element.Ancestors().OfType<IElement>()
                .Any(ancestor => ancestor.TagName is "SCRIPT" or "STYLE" or "TEMPLATE"))
            {
                continue;
            }

            var tag = element.TagName.ToLowerInvariant();
            var location = PathOf(element);

            if (tag.Length == 2 &&
                tag[0] == 'h' &&
                tag[1] is >= '1' and <= '6' &&
                !InsideSemanticContainer(element))
            {
                var text = Norm(element.TextContent);
                if (text.Length > 0)
                {
                    facts.Add(F(
                        $"heading:{order + 1}",
                        FactKind.HEADING,
                        text,
                        order++,
                        location,
                        new() { ["level"] = tag[1].ToString() }));
                }
            }
            else if (tag == "p" && !InsideSemanticContainer(element))
            {
                var text = Norm(element.TextContent);
                if (text.Length > 0)
                {
                    facts.Add(F(
                        $"text:{order + 1}",
                        FactKind.TEXT_BLOCK,
                        text,
                        order++,
                        location));
                }
            }
            else if (tag == "li" && !InsideTableCell(element))
            {
                var text = OwnedText(
                    element,
                    child => child.TagName is "UL" or "OL");

                if (text.Length > 0)
                {
                    var level = Math.Max(
                        0,
                        element.Ancestors().OfType<IElement>()
                            .Count(ancestor => ancestor.TagName is "UL" or "OL") - 1);

                    facts.Add(F(
                        $"list:{order + 1}",
                        FactKind.LIST_ITEM,
                        text,
                        order++,
                        location,
                        new() { ["level"] = level.ToString() }));
                }
            }
            else if (tag == "table")
            {
                tableNo++;
                var rows = element.QuerySelectorAll(
                    ":scope > thead > tr, :scope > tbody > tr, :scope > tfoot > tr, :scope > tr");
                var columns = rows
                    .Select(row => row.Children.Count(child => child.TagName is "TD" or "TH"))
                    .DefaultIfEmpty(0)
                    .Max();

                facts.Add(F(
                    $"table:{tableNo}",
                    FactKind.TABLE,
                    null,
                    order++,
                    location,
                    new()
                    {
                        ["rows"] = rows.Length.ToString(),
                        ["columns"] = columns.ToString()
                    }));

                var rowIndex = 0;
                foreach (var row in rows)
                {
                    var columnIndex = 0;
                    foreach (var cell in row.Children.Where(
                                 child => child.TagName is "TD" or "TH"))
                    {
                        var text = OwnedText(
                            cell,
                            child => child.TagName == "TABLE");

                        facts.Add(F(
                            $"table:{tableNo}:cell:{rowIndex}:{columnIndex}",
                            FactKind.TABLE_CELL,
                            text,
                            order++,
                            PathOf(cell),
                            new()
                            {
                                ["tableId"] = $"table:{tableNo}",
                                ["row"] = rowIndex.ToString(),
                                ["column"] = columnIndex.ToString()
                            }));
                        columnIndex++;
                    }

                    rowIndex++;
                }
            }
            else if (tag == "a" && element.HasAttribute("href"))
            {
                facts.Add(F(
                    $"link:{order + 1}",
                    FactKind.LINK,
                    Norm(element.TextContent),
                    order++,
                    location,
                    new() { ["target"] = element.GetAttribute("href") ?? "" }));
            }
            else if (tag == "img" && element.HasAttribute("src"))
            {
                facts.Add(F(
                    $"image:{order + 1}",
                    FactKind.IMAGE,
                    element.GetAttribute("alt"),
                    order++,
                    location,
                    new() { ["source"] = element.GetAttribute("src") ?? "" }));
            }
        }

        return new SourceFactsDocument(
            QualityCoreVersion.SourceFactsSchemaVersion,
            Id,
            Version,
            "html",
            hash,
            facts);
    }

    private static bool InsideSemanticContainer(IElement element) =>
        element.Ancestors().OfType<IElement>()
            .Any(ancestor => ancestor.TagName is "LI" or "TD" or "TH");

    private static bool InsideTableCell(IElement element) =>
        element.Ancestors().OfType<IElement>()
            .Any(ancestor => ancestor.TagName is "TD" or "TH");

    private static string OwnedText(
        IElement owner,
        Func<IElement, bool> stopAt)
    {
        var parts = new List<string>();

        foreach (var child in owner.ChildNodes)
        {
            Visit(child);
        }

        return Norm(string.Join(" ", parts));

        void Visit(INode node)
        {
            if (node is IElement element && stopAt(element))
            {
                return;
            }

            if (node.NodeType == NodeType.Text)
            {
                parts.Add(node.TextContent);
                return;
            }

            foreach (var child in node.ChildNodes)
            {
                Visit(child);
            }
        }
    }

    private static SourceFact F(
        string id,
        FactKind kind,
        string? text,
        int order,
        string location,
        Dictionary<string, string>? attributes = null) =>
        new(
            id,
            kind,
            text,
            order,
            location,
            FactCertainty.SOURCE_EXACT,
            attributes);

    private static string PathOf(IElement element)
    {
        var parts = new Stack<string>();
        IElement? current = element;

        while (current is not null && current.TagName != "HTML")
        {
            var index = current.ParentElement?.Children
                .Where(child => child.TagName == current.TagName)
                .ToList()
                .IndexOf(current) ?? 0;

            parts.Push($"{current.TagName.ToLowerInvariant()}[{index + 1}]");
            current = current.ParentElement;
        }

        return "html/" + string.Join("/", parts);
    }

    private static string Norm(string? value) =>
        Ws().Replace(value ?? "", " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Ws();

    private static async Task<string> HashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
    }
}

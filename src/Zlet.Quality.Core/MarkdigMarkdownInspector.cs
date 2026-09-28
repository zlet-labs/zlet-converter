using System.Security.Cryptography;
using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Zlet.Quality.Core;

public sealed class MarkdigMarkdownInspector : IMarkdownInspector
{
    public const string Id = "zlet-markdown-markdig";
    public const string Version = "0.1.0";

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

    public string InspectorId => Id;

    public string InspectorVersion => Version;

    public bool Supports(string markdownPath)
    {
        var extension = Path.GetExtension(markdownPath);
        return string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<MarkdownFactsDocument> InspectAsync(
        string markdownPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdownPath);

        if (!File.Exists(markdownPath))
        {
            throw new FileNotFoundException("Markdown file was not found.", markdownPath);
        }

        if (!Supports(markdownPath))
        {
            throw new NotSupportedException(
                $"Markdown inspector does not support '{Path.GetExtension(markdownPath)}'.");
        }

        var markdown = await File.ReadAllTextAsync(markdownPath, cancellationToken);
        var markdownHash = await ComputeSha256Async(markdownPath, cancellationToken);
        var document = Markdown.Parse(markdown, Pipeline);

        var facts = new List<MarkdownFact>();
        var state = new InspectionState();

        foreach (var block in document)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectBlock(block, facts, state, listDepth: 0);
        }

        return new MarkdownFactsDocument(
            QualityCoreVersion.MarkdownFactsSchemaVersion,
            Id,
            Version,
            markdownHash,
            facts);
    }

    private static void InspectBlock(
        Block block,
        List<MarkdownFact> facts,
        InspectionState state,
        int listDepth)
    {
        switch (block)
        {
            case HeadingBlock heading:
                facts.Add(new MarkdownFact(
                    $"heading:{++state.HeadingIndex}",
                    FactKind.HEADING,
                    ExtractInlineText(heading.Inline),
                    state.Order++,
                    Location(heading),
                    new Dictionary<string, string>
                    {
                        ["level"] = heading.Level.ToString()
                    }));

                InspectInlineReferences(heading.Inline, facts, state);
                break;

            case ParagraphBlock paragraph:
                facts.Add(new MarkdownFact(
                    $"text-block:{++state.TextBlockIndex}",
                    FactKind.TEXT_BLOCK,
                    ExtractInlineText(paragraph.Inline),
                    state.Order++,
                    Location(paragraph)));

                InspectInlineReferences(paragraph.Inline, facts, state);
                break;

            case ListBlock list:
                InspectList(list, facts, state, listDepth);
                break;

            case Table table:
                InspectTable(table, facts, state);
                break;

            case ContainerBlock container:
                foreach (var child in container)
                {
                    InspectBlock(child, facts, state, listDepth);
                }
                break;
        }
    }

    private static void InspectList(
        ListBlock list,
        List<MarkdownFact> facts,
        InspectionState state,
        int listDepth)
    {
        foreach (var block in list)
        {
            if (block is not ListItemBlock item)
            {
                continue;
            }

            var itemText = ExtractDirectListItemText(item);
            var itemId = $"list-item:{++state.ListItemIndex}";

            facts.Add(new MarkdownFact(
                itemId,
                FactKind.LIST_ITEM,
                itemText,
                state.Order++,
                Location(item),
                new Dictionary<string, string>
                {
                    ["level"] = listDepth.ToString(),
                    ["ordered"] = list.IsOrdered ? "true" : "false"
                }));

            foreach (var child in item)
            {
                if (child is LeafBlock leaf)
                {
                    InspectInlineReferences(leaf.Inline, facts, state);
                }
                else if (child is ListBlock nestedList)
                {
                    InspectList(nestedList, facts, state, listDepth + 1);
                }
            }
        }
    }

    private static void InspectTable(
        Table table,
        List<MarkdownFact> facts,
        InspectionState state)
    {
        var tableId = $"table:{++state.TableIndex}";
        var rows = table.OfType<TableRow>().ToList();
        var columns = rows.Count == 0
            ? 0
            : rows.Max(row =>
                row.OfType<TableCell>()
                    .Sum(cell => Math.Max(1, cell.ColumnSpan)));

        facts.Add(new MarkdownFact(
            tableId,
            FactKind.TABLE,
            null,
            state.Order++,
            Location(table),
            new Dictionary<string, string>
            {
                ["rows"] = rows.Count.ToString(),
                ["columns"] = columns.ToString()
            }));

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var columnIndex = 0;

            foreach (var cell in rows[rowIndex].OfType<TableCell>())
            {
                var columnSpan = Math.Max(1, cell.ColumnSpan);
                var attributes = new Dictionary<string, string>
                {
                    ["tableId"] = tableId,
                    ["row"] = rowIndex.ToString(),
                    ["column"] = columnIndex.ToString(),
                    ["columnSpan"] = columnSpan.ToString(),
                    ["rowSpan"] = Math.Max(1, cell.RowSpan).ToString(),
                    ["header"] = rows[rowIndex].IsHeader ? "true" : "false"
                };

                facts.Add(new MarkdownFact(
                    $"{tableId}:cell:{rowIndex}:{columnIndex}",
                    FactKind.TABLE_CELL,
                    ExtractBlockText(cell),
                    state.Order++,
                    Location(cell),
                    attributes));

                InspectReferencesInContainer(cell, facts, state);
                columnIndex += columnSpan;
            }
        }
    }

    private static void InspectReferencesInContainer(
        ContainerBlock container,
        List<MarkdownFact> facts,
        InspectionState state)
    {
        foreach (var block in container)
        {
            if (block is LeafBlock leaf)
            {
                InspectInlineReferences(leaf.Inline, facts, state);
            }
            else if (block is ContainerBlock nested)
            {
                InspectReferencesInContainer(nested, facts, state);
            }
        }
    }

    private static void InspectInlineReferences(
        ContainerInline? inline,
        List<MarkdownFact> facts,
        InspectionState state)
    {
        if (inline is null)
        {
            return;
        }

        foreach (var link in inline.FindDescendants<LinkInline>())
        {
            var kind = link.IsImage ? FactKind.IMAGE : FactKind.LINK;
            var id = link.IsImage
                ? $"image:{++state.ImageIndex}"
                : $"link:{++state.LinkIndex}";

            var attributes = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(link.Url))
            {
                attributes["target"] = link.Url!;
            }

            if (!string.IsNullOrWhiteSpace(link.Title))
            {
                attributes["title"] = link.Title!;
            }

            facts.Add(new MarkdownFact(
                id,
                kind,
                ExtractInlineText(link),
                state.Order++,
                Location(link),
                attributes.Count == 0 ? null : attributes));
        }
    }

    private static string ExtractDirectListItemText(ListItemBlock item)
    {
        var parts = new List<string>();

        foreach (var child in item)
        {
            if (child is LeafBlock leaf)
            {
                var text = ExtractInlineText(leaf.Inline);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text);
                }
            }
        }

        return string.Join("\n", parts);
    }

    private static string ExtractBlockText(ContainerBlock container)
    {
        var parts = new List<string>();

        foreach (var block in container)
        {
            switch (block)
            {
                case LeafBlock leaf:
                {
                    var text = ExtractInlineText(leaf.Inline);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        parts.Add(text);
                    }
                    break;
                }

                case ContainerBlock nested:
                {
                    var text = ExtractBlockText(nested);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        parts.Add(text);
                    }
                    break;
                }
            }
        }

        return string.Join("\n", parts);
    }

    private static string ExtractInlineText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var inline in container)
        {
            AppendInlineText(inline, builder);
        }

        return builder.ToString();
    }

    private static void AppendInlineText(Inline inline, StringBuilder builder)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content.ToString());
                break;

            case CodeInline code:
                builder.Append(code.Content);
                break;

            case LineBreakInline:
                builder.Append('\n');
                break;

            case ContainerInline container:
                foreach (var child in container)
                {
                    AppendInlineText(child, builder);
                }
                break;
        }
    }

    private static string Location(MarkdownObject markdownObject) =>
        $"span:{markdownObject.Span.Start}-{markdownObject.Span.End}";

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class InspectionState
    {
        public int Order { get; set; }
        public int TextBlockIndex { get; set; }
        public int HeadingIndex { get; set; }
        public int ListItemIndex { get; set; }
        public int TableIndex { get; set; }
        public int LinkIndex { get; set; }
        public int ImageIndex { get; set; }
    }
}

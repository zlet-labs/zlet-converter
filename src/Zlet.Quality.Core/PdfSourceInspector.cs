using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Zlet.Quality.Core;

/// <summary>
/// Deterministic PDF v0.1 inspector. It deliberately exposes only extracted text blocks.
/// PDF layout/reading order and semantic roles are not source-exact in an untagged content stream,
/// so these facts are INFERRED and no heading/list/table semantics are guessed.
/// </summary>
public sealed partial class PdfSourceInspector : ISourceInspector
{
    public const string Id = "zlet-pdf-pdfpig";
    public const string Version = "0.1.0";
    public string InspectorId => Id;
    public string InspectorVersion => Version;
    public bool Supports(string sourcePath) => string.Equals(Path.GetExtension(sourcePath), ".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<SourceFactsDocument> InspectAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Source PDF was not found.", sourcePath);
        if (!Supports(sourcePath)) throw new NotSupportedException($"PDF inspector does not support '{Path.GetExtension(sourcePath)}'.");

        var hash = await HashAsync(sourcePath, cancellationToken);
        var facts = new List<SourceFact>();
        var order = 0;
        using var document = PdfDocument.Open(sourcePath);
        for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.GetPage(pageNumber);
            var text = Normalize(ContentOrderTextExtractor.GetText(page));
            if (text.Length == 0) continue;
            facts.Add(new SourceFact(
                $"text:{order + 1}", FactKind.TEXT_BLOCK, text, order++, $"pdf/page[{pageNumber}]",
                FactCertainty.INFERRED,
                new Dictionary<string,string> { ["page"] = pageNumber.ToString(), ["extraction"] = "content-order" }));
        }
        return new SourceFactsDocument(QualityCoreVersion.SourceFactsSchemaVersion, Id, Version, "pdf", hash, facts);
    }

    private static string Normalize(string value) => Whitespace().Replace(value.Replace("\r\n", "\n").Replace('\r', '\n'), " ").Trim();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
    private static async Task<string> HashAsync(string path, CancellationToken ct) { await using var s = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(s, ct)).ToLowerInvariant(); }
}

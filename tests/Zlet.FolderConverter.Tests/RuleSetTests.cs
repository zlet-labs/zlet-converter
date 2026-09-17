using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.Tests;

public sealed class RuleSetTests
{
    [Theory]
    [InlineData(SourceFormat.Json, ConversionTarget.Txt)]
    [InlineData(SourceFormat.Doc, ConversionTarget.Docx)]
    [InlineData(SourceFormat.Xls, ConversionTarget.Xlsx)]
    [InlineData(SourceFormat.Ppt, ConversionTarget.Pptx)]
    [InlineData(SourceFormat.Docx, ConversionTarget.Markdown)]
    [InlineData(SourceFormat.Xlsx, ConversionTarget.Markdown)]
    [InlineData(SourceFormat.Pptx, ConversionTarget.Markdown)]
    [InlineData(SourceFormat.Odt, ConversionTarget.Skip)]
    [InlineData(SourceFormat.Ods, ConversionTarget.Skip)]
    [InlineData(SourceFormat.Odp, ConversionTarget.Skip)]
    [InlineData(SourceFormat.Pdf, ConversionTarget.Markdown)]
    [InlineData(SourceFormat.Html, ConversionTarget.Copy)]
    [InlineData(SourceFormat.Txt, ConversionTarget.Markdown)]
    [InlineData(SourceFormat.Image, ConversionTarget.Copy)]
    [InlineData(SourceFormat.Archive, ConversionTarget.Skip)]
    [InlineData(SourceFormat.Unknown, ConversionTarget.Skip)]
    public void Default_rules_match_product_defaults(
        SourceFormat source,
        ConversionTarget expectedTarget)
    {
        Assert.Equal(expectedTarget, RuleSet.CreateDefault().GetRule(source).Target);
    }

    [Theory]
    [InlineData(SourceFormat.Json, ConversionTarget.Txt)]
    [InlineData(SourceFormat.Json, ConversionTarget.Markdown)]
    [InlineData(SourceFormat.Doc, ConversionTarget.Docx)]
    [InlineData(SourceFormat.Doc, ConversionTarget.Markdown)]
    [InlineData(SourceFormat.Xls, ConversionTarget.Xlsx)]
    [InlineData(SourceFormat.Xls, ConversionTarget.Markdown)]
    [InlineData(SourceFormat.Ppt, ConversionTarget.Pptx)]
    [InlineData(SourceFormat.Ppt, ConversionTarget.Markdown)]
    [InlineData(SourceFormat.Docx, ConversionTarget.Copy)]
    [InlineData(SourceFormat.Docx, ConversionTarget.Markdown)]
    [InlineData(SourceFormat.Xlsx, ConversionTarget.Copy)]
    [InlineData(SourceFormat.Xlsx, ConversionTarget.Markdown)]
    [InlineData(SourceFormat.Pptx, ConversionTarget.Copy)]
    [InlineData(SourceFormat.Pptx, ConversionTarget.Markdown)]
    [InlineData(SourceFormat.Pdf, ConversionTarget.Markdown)]
    [InlineData(SourceFormat.Txt, ConversionTarget.Markdown)]
    public void Rules_accept_required_mappings(SourceFormat source, ConversionTarget target)
    {
        var rules = RuleSet.CreateDefault().WithRule(source, target);

        Assert.Equal(target, rules.GetRule(source).Target);
    }

    [Fact]
    public void Html_to_markdown_is_blocked_by_html_route_blocker()
    {
        var capability = FormatCapabilityCatalog.Get(SourceFormat.Html);
        Assert.False(capability.Supports(ConversionTarget.Markdown));
        Assert.False(string.IsNullOrWhiteSpace(FormatCapabilityCatalog.HtmlRouteBlocker));
        Assert.Throws<ArgumentException>(
            () => RuleSet.CreateDefault().WithRule(SourceFormat.Html, ConversionTarget.Markdown));
    }

    [Fact]
    public void Xlsx_supports_markdown_copy_csv_tsv_and_skip()
    {
        var capability = FormatCapabilityCatalog.Get(SourceFormat.Xlsx);

        Assert.Equal([ConversionTarget.Markdown, ConversionTarget.Copy, ConversionTarget.Csv, ConversionTarget.Tsv, ConversionTarget.Skip], capability.AllowedTargets);
    }

    [Fact]
    public void Unsupported_mapping_is_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => RuleSet.CreateDefault().WithRule(SourceFormat.Pdf, ConversionTarget.Txt));
    }
}

using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.Tests;

public class FormatCapabilityCatalogTests
{
    [Theory]
    [InlineData(SourceFormat.Docx)]
    [InlineData(SourceFormat.Xlsx)]
    [InlineData(SourceFormat.Pptx)]
    [InlineData(SourceFormat.Pdf)]
    [InlineData(SourceFormat.Txt)]
    public void DocumentFormats_DefaultTarget_ShouldBeMarkdown(SourceFormat sourceFormat)
    {
        var capability = FormatCapabilityCatalog.Get(sourceFormat);

        Assert.Equal(ConversionTarget.Markdown, capability.DefaultTarget);
    }

    [Theory]
    [InlineData(SourceFormat.Doc)]
    [InlineData(SourceFormat.Xls)]
    [InlineData(SourceFormat.Ppt)]
    public void LegacyOfficeFormats_DefaultTarget_ShouldRemainModernOfficeConversion(SourceFormat sourceFormat)
    {
        var capability = FormatCapabilityCatalog.Get(sourceFormat);

        Assert.NotEqual(ConversionTarget.Copy, capability.DefaultTarget);
    }
}

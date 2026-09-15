using System.Text;
using Xunit;
using Zlet.FolderConverter.Core.Models;
using Zlet.FolderConverter.Core.Services;

namespace Zlet.FolderConverter.Tests;

public sealed class TxtMarkdownConversionAdapterTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        "zlet-txt-adapter-tests",
        Guid.NewGuid().ToString("N"));

    public TxtMarkdownConversionAdapterTests() => Directory.CreateDirectory(_rootPath);

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    [Fact]
    public void CanConvert_only_supports_txt_to_markdown()
    {
        var adapter = new TxtMarkdownConversionAdapter(new OutputResultValidator());

        Assert.True(adapter.CanConvert(SourceFormat.Txt, ConversionTarget.Markdown));
        Assert.False(adapter.CanConvert(SourceFormat.Docx, ConversionTarget.Markdown));
        Assert.False(adapter.CanConvert(SourceFormat.Pdf, ConversionTarget.Markdown));
        Assert.False(adapter.CanConvert(SourceFormat.Html, ConversionTarget.Markdown));
        Assert.False(adapter.CanConvert(SourceFormat.Txt, ConversionTarget.Copy));
    }

    [Fact]
    public async Task Converts_plain_txt_normalizing_newlines()
    {
        var sourcePath = Path.Combine(_rootPath, "notes.txt");
        var text = "Line 1\r\nLine 2\r\nLine 3\n";
        await File.WriteAllTextAsync(sourcePath, text, new UTF8Encoding(false));

        var operation = CreateOperation(sourcePath, "notes.md");
        var adapter = new TxtMarkdownConversionAdapter(new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.True(File.Exists(operation.TargetPath));
        var content = await File.ReadAllTextAsync(operation.TargetPath);
        Assert.Equal("Line 1\nLine 2\nLine 3\n", content);
    }

    [Fact]
    public async Task Converts_txt_with_utf8_bom()
    {
        var sourcePath = Path.Combine(_rootPath, "bom.txt");
        var text = "# Markdown heading in text\nText with Cyrillic: Привет мир";
        await File.WriteAllTextAsync(sourcePath, text, new UTF8Encoding(true));

        var operation = CreateOperation(sourcePath, "bom.md");
        var adapter = new TxtMarkdownConversionAdapter(new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.True(File.Exists(operation.TargetPath));
        var content = await File.ReadAllTextAsync(operation.TargetPath);
        Assert.Contains("Привет мир", content);
    }

    [Fact]
    public async Task Rejects_bomless_non_utf8_with_text_encoding_unsupported()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var win1251 = Encoding.GetEncoding(1251);

        var sourcePath = Path.Combine(_rootPath, "win1251.txt");
        var text = "Текст в кодировке Windows-1251";
        await File.WriteAllBytesAsync(sourcePath, win1251.GetBytes(text));

        var operation = CreateOperation(sourcePath, "win1251.md");
        var adapter = new TxtMarkdownConversionAdapter(new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal("text_encoding_unsupported", result.Diagnostic?.ErrorCode);
        Assert.False(File.Exists(operation.TargetPath));
    }

    [Fact]
    public async Task Conflict_policy_protects_existing_target()
    {
        var sourcePath = Path.Combine(_rootPath, "source.txt");
        await File.WriteAllTextAsync(sourcePath, "new text");

        var operation = CreateOperation(sourcePath, "target.md");
        Directory.CreateDirectory(Path.GetDirectoryName(operation.TargetPath)!);
        await File.WriteAllTextAsync(operation.TargetPath, "existing text");

        var adapter = new TxtMarkdownConversionAdapter(new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Conflict, result.Status);
        Assert.Equal("existing text", await File.ReadAllTextAsync(operation.TargetPath));
    }

    [Fact]
    public async Task Unsafe_source_is_rejected()
    {
        var outside = Path.Combine(Path.GetTempPath(), "outside_" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outside, "outside");
        try
        {
            var operation = new PlannedOperation(
                outside,
                "outside.txt",
                SourceFormat.Txt,
                ConversionTarget.Markdown,
                ".md",
                Path.Combine(_rootPath, "_converted", "outside.md"),
                true,
                OperationStatus.Ready,
                "ready",
                Path.Combine(_rootPath, "_converted"));

            var adapter = new TxtMarkdownConversionAdapter(new OutputResultValidator());
            var result = await adapter.ConvertAsync(operation, CancellationToken.None);

            Assert.Equal(OperationStatus.Failed, result.Status);
            Assert.Equal("unsafe_source", result.Diagnostic?.ErrorCode);
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    private PlannedOperation CreateOperation(string sourcePath, string outputName)
    {
        var targetDir = Path.Combine(_rootPath, "_converted");
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, outputName);
        return new PlannedOperation(
            sourcePath,
            Path.GetFileName(sourcePath),
            SourceFormat.Txt,
            ConversionTarget.Markdown,
            ".md",
            targetPath,
            true,
            OperationStatus.Ready,
            "ready",
            targetDir);
    }
}

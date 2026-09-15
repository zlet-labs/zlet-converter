using System.Security.Cryptography;
using System.Text;
using Xunit;
using Zlet.FolderConverter.Core.Models;
using Zlet.FolderConverter.Core.Services;

namespace Zlet.FolderConverter.Tests;

public sealed class AnydocMarkdownConversionAdapterTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        "zlet-anydoc-adapter-tests",
        Guid.NewGuid().ToString("N"));

    public AnydocMarkdownConversionAdapterTests() => Directory.CreateDirectory(_rootPath);

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    [Fact]
    public void CanConvert_supports_office_and_pdf_and_rejects_html_and_txt()
    {
        var runner = new FakeAnydocWorkerRunner(new AnydocWorkerExecutionResult(true));
        var adapter = new AnydocMarkdownConversionAdapter(runner, new OutputResultValidator());

        Assert.True(adapter.CanConvert(SourceFormat.Doc, ConversionTarget.Markdown));
        Assert.True(adapter.CanConvert(SourceFormat.Docx, ConversionTarget.Markdown));
        Assert.True(adapter.CanConvert(SourceFormat.Xls, ConversionTarget.Markdown));
        Assert.True(adapter.CanConvert(SourceFormat.Xlsx, ConversionTarget.Markdown));
        Assert.True(adapter.CanConvert(SourceFormat.Ppt, ConversionTarget.Markdown));
        Assert.True(adapter.CanConvert(SourceFormat.Pptx, ConversionTarget.Markdown));
        Assert.True(adapter.CanConvert(SourceFormat.Pdf, ConversionTarget.Markdown));

        // Txt has dedicated adapter, Html is blocked
        Assert.False(adapter.CanConvert(SourceFormat.Txt, ConversionTarget.Markdown));
        Assert.False(adapter.CanConvert(SourceFormat.Html, ConversionTarget.Markdown));
        Assert.False(adapter.CanConvert(SourceFormat.Docx, ConversionTarget.Docx));
    }

    [MarkdownIntegrationFact]
    public async Task Converts_docx_to_markdown_successfully()
    {
        var fixture = GetFixturePath("F08_structured.docx");
        var sourcePath = CopyFixture(fixture, "report.docx");
        var operation = CreateOperation(sourcePath, "report.md", SourceFormat.Docx);
        var runner = new AnydocWorkerProcessRunner();

        var adapter = new AnydocMarkdownConversionAdapter(runner, new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.True(File.Exists(operation.TargetPath));
        var content = await File.ReadAllTextAsync(operation.TargetPath, Encoding.UTF8);
        Assert.NotEmpty(content);
        Assert.Contains("Architecture Review", content);
    }

    [MarkdownIntegrationFact]
    public async Task Converts_pptx_to_markdown_successfully()
    {
        var fixture = GetFixturePath("F09_slides.pptx");
        var sourcePath = CopyFixture(fixture, "slides.pptx");
        var operation = CreateOperation(sourcePath, "slides.md", SourceFormat.Pptx);
        var runner = new AnydocWorkerProcessRunner();

        var adapter = new AnydocMarkdownConversionAdapter(runner, new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.True(File.Exists(operation.TargetPath));
        var content = await File.ReadAllTextAsync(operation.TargetPath, Encoding.UTF8);
        Assert.NotEmpty(content);
    }

    [MarkdownIntegrationFact]
    public async Task Converts_xlsx_to_markdown_with_sheet_headings_and_tables()
    {
        var fixture = GetFixturePath("F10_sheets.xlsx");
        var sourcePath = CopyFixture(fixture, "sheets.xlsx");
        var operation = CreateOperation(sourcePath, "sheets.md", SourceFormat.Xlsx);
        var runner = new AnydocWorkerProcessRunner();

        var adapter = new AnydocMarkdownConversionAdapter(runner, new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.True(File.Exists(operation.TargetPath));
        var content = await File.ReadAllTextAsync(operation.TargetPath, Encoding.UTF8);
        Assert.NotEmpty(content);
        Assert.Contains("## ", content);
        Assert.Contains("|", content);
    }

    [MarkdownIntegrationFact]
    public async Task Converts_digital_pdf_to_markdown_successfully()
    {
        var fixture = GetFixturePath("F01_simple_text.pdf");
        var sourcePath = CopyFixture(fixture, "document.pdf");
        var operation = CreateOperation(sourcePath, "document.md", SourceFormat.Pdf);
        var runner = new AnydocWorkerProcessRunner();

        var adapter = new AnydocMarkdownConversionAdapter(runner, new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.True(File.Exists(operation.TargetPath));
        var content = await File.ReadAllTextAsync(operation.TargetPath, Encoding.UTF8);
        Assert.NotEmpty(content);
    }

    [MarkdownIntegrationFact]
    public async Task Scanned_pdf_fails_gracefully_with_pdf_specialist_required_code()
    {
        var fixture = GetFixturePath("F06_scanned.pdf");
        var sourcePath = CopyFixture(fixture, "scanned.pdf");
        var operation = CreateOperation(sourcePath, "scanned.md", SourceFormat.Pdf);
        var runner = new AnydocWorkerProcessRunner();

        var adapter = new AnydocMarkdownConversionAdapter(runner, new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal("pdf_specialist_required", result.Diagnostic?.ErrorCode);
        Assert.False(File.Exists(operation.TargetPath));
    }

    [Fact]
    public async Task Conflict_policy_protects_existing_target_file()
    {
        var sourcePath = Path.Combine(_rootPath, "conflict_source.docx");
        await File.WriteAllTextAsync(sourcePath, "new source content");
        var operation = CreateOperation(sourcePath, "target.md", SourceFormat.Docx);

        Directory.CreateDirectory(Path.GetDirectoryName(operation.TargetPath)!);
        await File.WriteAllTextAsync(operation.TargetPath, "pre-existing target content");

        var mockRunner = new FakeAnydocWorkerRunner(new AnydocWorkerExecutionResult(true));
        var adapter = new AnydocMarkdownConversionAdapter(mockRunner, new OutputResultValidator());

        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Conflict, result.Status);
        Assert.Equal("pre-existing target content", await File.ReadAllTextAsync(operation.TargetPath));
    }

    [Fact]
    public async Task Unsafe_source_path_is_rejected()
    {
        var outsideSource = Path.Combine(Path.GetTempPath(), "outside_source.docx");
        await File.WriteAllTextAsync(outsideSource, "outside content");
        try
        {
            var operation = new PlannedOperation(
                outsideSource,
                "outside.docx",
                SourceFormat.Docx,
                ConversionTarget.Markdown,
                ".md",
                Path.Combine(_rootPath, "_converted", "outside.md"),
                true,
                OperationStatus.Ready,
                "ready",
                Path.Combine(_rootPath, "_converted"));

            var mockRunner = new FakeAnydocWorkerRunner(new AnydocWorkerExecutionResult(true));
            var adapter = new AnydocMarkdownConversionAdapter(mockRunner, new OutputResultValidator());

            var result = await adapter.ConvertAsync(operation, CancellationToken.None);

            Assert.Equal(OperationStatus.Failed, result.Status);
            Assert.Equal("unsafe_source", result.Diagnostic?.ErrorCode);
        }
        finally
        {
            if (File.Exists(outsideSource)) File.Delete(outsideSource);
        }
    }

    [Fact]
    public async Task Unsafe_target_path_is_rejected()
    {
        var sourcePath = Path.Combine(_rootPath, "safe_source.docx");
        await File.WriteAllTextAsync(sourcePath, "safe source content");

        var operation = new PlannedOperation(
            sourcePath,
            "safe_source.docx",
            SourceFormat.Docx,
            ConversionTarget.Markdown,
            ".md",
            Path.Combine(Path.GetTempPath(), "unsafe_leak.md"),
            true,
            OperationStatus.Ready,
            "ready",
            Path.Combine(_rootPath, "_converted"));

        var mockRunner = new FakeAnydocWorkerRunner(new AnydocWorkerExecutionResult(true));
        var adapter = new AnydocMarkdownConversionAdapter(mockRunner, new OutputResultValidator());

        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal("unsafe_target", result.Diagnostic?.ErrorCode);
    }

    [MarkdownIntegrationFact]
    public async Task Source_file_hash_is_unchanged_after_conversion()
    {
        var fixture = GetFixturePath("F08_structured.docx");
        var sourcePath = CopyFixture(fixture, "sample.docx");
        var hashBefore = ComputeSha256(sourcePath);
        var operation = CreateOperation(sourcePath, "sample.md", SourceFormat.Docx);
        var runner = new AnydocWorkerProcessRunner();

        var adapter = new AnydocMarkdownConversionAdapter(runner, new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Succeeded, result.Status);
        var hashAfter = ComputeSha256(sourcePath);
        Assert.Equal(hashBefore, hashAfter);
    }

    [Fact]
    public async Task Mock_runner_error_returns_failed_status_with_diagnostic()
    {
        var sourcePath = Path.Combine(_rootPath, "sample.docx");
        await File.WriteAllTextAsync(sourcePath, "dummy content", Encoding.UTF8);
        var operation = CreateOperation(sourcePath, "sample.md", SourceFormat.Docx);
        var mockRunner = new FakeAnydocWorkerRunner(new AnydocWorkerExecutionResult(
            Success: false,
            ErrorCode: "conversion_failed",
            ErrorMessage: "Custom conversion failure"));

        var adapter = new AnydocMarkdownConversionAdapter(mockRunner, new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal("conversion_failed", result.Diagnostic?.ErrorCode);
        Assert.Equal("Custom conversion failure", result.Message);
    }

    private PlannedOperation CreateOperation(string sourcePath, string outputName, SourceFormat format)
    {
        var targetDir = Path.Combine(_rootPath, "_converted");
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, outputName);
        return new PlannedOperation(
            sourcePath,
            Path.GetFileName(sourcePath),
            format,
            ConversionTarget.Markdown,
            ".md",
            targetPath,
            true,
            OperationStatus.Ready,
            "ready",
            targetDir);
    }

    private string CopyFixture(string fixturePath, string destinationName)
    {
        var destinationPath = Path.Combine(_rootPath, destinationName);
        File.Copy(fixturePath, destinationPath, overwrite: true);
        return destinationPath;
    }

    private static string GetFixturePath(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "evaluation", "fixtures", name);
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException($"Fixture '{name}' not found.");
    }

    private static string ComputeSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    private sealed class FakeAnydocWorkerRunner(AnydocWorkerExecutionResult result) : IAnydocWorkerRunner
    {
        public bool IsAvailable => true;
        public string AvailabilityMessage => "Available";

        public Task BeginBatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EndBatchAsync() => Task.CompletedTask;

        public Task<AnydocWorkerExecutionResult> RunAsync(AnydocWorkerRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}

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
            ErrorMessage: "Custom conversion failure with secret path: C:\\Users\\SecretUser\\secret.docx"));

        var adapter = new AnydocMarkdownConversionAdapter(mockRunner, new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal("conversion_failed", result.Diagnostic?.ErrorCode);
        Assert.Equal("Не удалось преобразовать документ в Markdown.", result.Message);
        Assert.DoesNotContain("SecretUser", result.Message);
    }

    [Theory]
    [InlineData("read_error", "Не удалось прочитать исходный документ.")]
    [InlineData("write_error", "Не удалось записать файл результата Markdown.")]
    [InlineData("asset_export_error", "Не удалось извлечь встроенные изображения документа.")]
    [InlineData("anydoc_version_incompatible", "Версия компонента Markdown несовместима с приложением.")]
    [InlineData("anydoc_protocol_error", "Ошибка протокола взаимодействия с компонентом Markdown.")]
    [InlineData("anydoc_worker_failure", "Процесс Markdown сообщил о внутренней ошибке.")]
    public async Task Privacy_sensitive_errors_map_to_safe_messages(string errorCode, string expectedMessage)
    {
        var sourcePath = Path.Combine(_rootPath, $"{errorCode}.docx");
        await File.WriteAllTextAsync(sourcePath, "dummy content", Encoding.UTF8);
        var operation = CreateOperation(sourcePath, $"{errorCode}.md", SourceFormat.Docx);
        var mockRunner = new FakeAnydocWorkerRunner(new AnydocWorkerExecutionResult(
            Success: false,
            ErrorCode: errorCode,
            ErrorMessage: $"Sensitive error details: C:\\Users\\Secret\\{errorCode}.docx"));

        var adapter = new AnydocMarkdownConversionAdapter(mockRunner, new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal(errorCode, result.Diagnostic?.ErrorCode);
        Assert.Equal(expectedMessage, result.Message);
        Assert.DoesNotContain("Secret", result.Message);
    }

    [Fact]
    public async Task Companion_asset_directory_is_promoted_transactionally_alongside_markdown()
    {
        var sourcePath = Path.Combine(_rootPath, "doc_with_assets.docx");
        await File.WriteAllTextAsync(sourcePath, "dummy content");
        var operation = CreateOperation(sourcePath, "article.md", SourceFormat.Docx);

        var mockRunner = new FakeAnydocWorkerRunner(async req =>
        {
            await File.WriteAllTextAsync(req.OutputPath, "# Article\n\n![diagram](article_assets/image-001.png)\n");
            var assetDir = Path.Combine(Path.GetDirectoryName(req.OutputPath)!, req.AssetDir ?? "article_assets");
            Directory.CreateDirectory(assetDir);
            await File.WriteAllBytesAsync(Path.Combine(assetDir, "image-001.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            return new AnydocWorkerExecutionResult(true);
        });

        var adapter = new AnydocMarkdownConversionAdapter(mockRunner, new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.True(File.Exists(operation.TargetPath));
        var content = await File.ReadAllTextAsync(operation.TargetPath);
        Assert.Contains("article_assets/image-001.png", content);

        var targetAssetsDir = Path.Combine(Path.GetDirectoryName(operation.TargetPath)!, "article_assets");
        Assert.True(Directory.Exists(targetAssetsDir));
        var targetAssetFile = Path.Combine(targetAssetsDir, "image-001.png");
        Assert.True(File.Exists(targetAssetFile));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, await File.ReadAllBytesAsync(targetAssetFile));
    }

    [Fact]
    public async Task Companion_asset_directory_collision_returns_conflict_status()
    {
        var sourcePath = Path.Combine(_rootPath, "doc_collision.docx");
        await File.WriteAllTextAsync(sourcePath, "dummy content");
        var operation = CreateOperation(sourcePath, "colliding.md", SourceFormat.Docx);

        var existingCompanion = Path.Combine(Path.GetDirectoryName(operation.TargetPath)!, "colliding_assets");
        Directory.CreateDirectory(existingCompanion);
        await File.WriteAllTextAsync(Path.Combine(existingCompanion, "pre_existing.txt"), "do not touch");

        var mockRunner = new FakeAnydocWorkerRunner(async req =>
        {
            await File.WriteAllTextAsync(req.OutputPath, "# Content\n");
            var assetDir = Path.Combine(Path.GetDirectoryName(req.OutputPath)!, req.AssetDir ?? "colliding_assets");
            Directory.CreateDirectory(assetDir);
            await File.WriteAllBytesAsync(Path.Combine(assetDir, "image-001.png"), new byte[] { 1, 2, 3 });
            return new AnydocWorkerExecutionResult(true);
        });

        var adapter = new AnydocMarkdownConversionAdapter(mockRunner, new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Conflict, result.Status);
        Assert.Equal("target_conflict", result.Diagnostic?.ErrorCode);
        Assert.False(File.Exists(operation.TargetPath));
        Assert.True(File.Exists(Path.Combine(existingCompanion, "pre_existing.txt")));
        Assert.False(File.Exists(Path.Combine(existingCompanion, "image-001.png")));
    }

    [Fact]
    public async Task Companion_assets_are_rolled_back_if_conversion_fails()
    {
        var sourcePath = Path.Combine(_rootPath, "doc_fail.docx");
        await File.WriteAllTextAsync(sourcePath, "dummy content");
        var operation = CreateOperation(sourcePath, "failed_doc.md", SourceFormat.Docx);

        var mockRunner = new FakeAnydocWorkerRunner(async req =>
        {
            var assetDir = Path.Combine(Path.GetDirectoryName(req.OutputPath)!, req.AssetDir ?? "failed_doc_assets");
            Directory.CreateDirectory(assetDir);
            await File.WriteAllBytesAsync(Path.Combine(assetDir, "image-001.png"), new byte[] { 1, 2, 3 });
            return new AnydocWorkerExecutionResult(false, "conversion_failed", "failed");
        });

        var adapter = new AnydocMarkdownConversionAdapter(mockRunner, new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, CancellationToken.None);

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.False(File.Exists(operation.TargetPath));
        var targetAssetsDir = Path.Combine(Path.GetDirectoryName(operation.TargetPath)!, "failed_doc_assets");
        Assert.False(Directory.Exists(targetAssetsDir));
    }

    [Fact]
    public async Task Companion_assets_are_recursively_included_in_zip_export()
    {
        var sourcePath = Path.Combine(_rootPath, "doc_for_zip.docx");
        await File.WriteAllTextAsync(sourcePath, "dummy content");
        var operation = CreateOperation(sourcePath, "zipped_doc.md", SourceFormat.Docx);

        var mockRunner = new FakeAnydocWorkerRunner(async req =>
        {
            await File.WriteAllTextAsync(req.OutputPath, "# Zipped\n![pic](zipped_doc_assets/image-001.png)\n");
            var assetDir = Path.Combine(Path.GetDirectoryName(req.OutputPath)!, req.AssetDir ?? "zipped_doc_assets");
            Directory.CreateDirectory(assetDir);
            await File.WriteAllBytesAsync(Path.Combine(assetDir, "image-001.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            return new AnydocWorkerExecutionResult(true);
        });

        var adapter = new AnydocMarkdownConversionAdapter(mockRunner, new OutputResultValidator());
        var convResult = await adapter.ConvertAsync(operation, CancellationToken.None);
        Assert.Equal(OperationStatus.Succeeded, convResult.Status);

        var summary = new ConversionSummary(
            Succeeded: 1,
            Conflicts: 0,
            Failed: 0,
            Skipped: 0,
            EngineUnavailable: 0,
            Unsupported: 0,
            Results: new[] { convResult });

        var stagingRoot = Path.GetDirectoryName(operation.TargetPath)!;
        var zipPath = Path.Combine(_rootPath, "output.zip");
        var publisher = new ResultZipPublisher();
        var pubResult = await publisher.PublishAsync(stagingRoot, zipPath, summary, CancellationToken.None);

        Assert.True(pubResult.Created);
        Assert.True(File.Exists(zipPath));

        using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
        var entryNames = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("zipped_doc.md", entryNames);
        Assert.Contains("zipped_doc_assets/image-001.png", entryNames);
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

    private sealed class FakeAnydocWorkerRunner : IAnydocWorkerRunner
    {
        private readonly AnydocWorkerExecutionResult? _fixedResult;
        private readonly Func<AnydocWorkerRequest, Task<AnydocWorkerExecutionResult>>? _handler;

        public FakeAnydocWorkerRunner(AnydocWorkerExecutionResult result) => _fixedResult = result;
        public FakeAnydocWorkerRunner(Func<AnydocWorkerRequest, Task<AnydocWorkerExecutionResult>> handler) => _handler = handler;

        public bool IsAvailable => true;
        public string AvailabilityMessage => "Available";

        public Task BeginBatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EndBatchAsync() => Task.CompletedTask;

        public Task<AnydocWorkerExecutionResult> RunAsync(AnydocWorkerRequest request, CancellationToken cancellationToken)
        {
            if (_handler is not null)
            {
                return _handler(request);
            }
            return Task.FromResult(_fixedResult ?? new AnydocWorkerExecutionResult(true));
        }
    }
}

using Zlet.FolderConverter.Core.Models;
using Zlet.FolderConverter.Core.Services;

namespace Zlet.FolderConverter.Tests;

public sealed class ConversionPlannerTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        "zlet-folder-converter-planner-tests",
        Guid.NewGuid().ToString("N"));

    public ConversionPlannerTests() => Directory.CreateDirectory(_rootPath);

    [Theory]
    [InlineData(SourceFormat.Json, ConversionTarget.Txt, ".txt")]
    [InlineData(SourceFormat.Json, ConversionTarget.Markdown, ".md")]
    [InlineData(SourceFormat.Doc, ConversionTarget.Docx, ".docx")]
    [InlineData(SourceFormat.Xls, ConversionTarget.Xlsx, ".xlsx")]
    [InlineData(SourceFormat.Ppt, ConversionTarget.Pptx, ".pptx")]
    [InlineData(SourceFormat.Docx, ConversionTarget.Copy, ".docx")]
    [InlineData(SourceFormat.Xlsx, ConversionTarget.Copy, ".xlsx")]
    [InlineData(SourceFormat.Pptx, ConversionTarget.Copy, ".pptx")]
    public void CreatePlan_builds_required_source_target_mappings(
        SourceFormat source,
        ConversionTarget target,
        string expectedExtension)
    {
        var resolver = new TestResolver(new TestAdapter(source, target, available: true));
        var relativePath = target == ConversionTarget.Copy
            ? $"nested source{expectedExtension}"
            : "nested source.file";
        var operation = CreateOperation(source, relativePath, target, resolver);

        Assert.Equal(OperationStatus.Ready, operation.Status);
        Assert.Equal(expectedExtension, operation.TargetExtension);
        Assert.Equal(
            Path.Combine(_rootPath, "_converted", $"nested source{expectedExtension}"),
            operation.TargetPath);
    }

    [Fact]
    public void CreatePlan_preserves_nested_relative_paths()
    {
        var relativePath = Path.Combine("архив договоров", "old file.doc");
        var operation = CreateOperation(
            SourceFormat.Doc,
            relativePath,
            ConversionTarget.Docx,
            new TestResolver(new TestAdapter(
                SourceFormat.Doc,
                ConversionTarget.Docx,
                available: true)));

        Assert.Equal(
            Path.Combine(_rootPath, "_converted", "архив договоров", "old file.docx"),
            operation.TargetPath);
    }

    [Fact]
    public void CreatePlan_marks_file_conflict()
    {
        var target = Path.Combine(_rootPath, "_converted", "source.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "existing");

        var operation = CreateOperation(
            SourceFormat.Doc,
            "source.doc",
            ConversionTarget.Docx,
            new TestResolver(new TestAdapter(
                SourceFormat.Doc,
                ConversionTarget.Docx,
                available: true)));

        Assert.Equal(OperationStatus.Conflict, operation.Status);
        Assert.Equal("existing", File.ReadAllText(target));
    }

    [Fact]
    public void CreatePlan_marks_directory_conflict()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "_converted", "source.txt"));

        var operation = CreateOperation(
            SourceFormat.Json,
            "source.json",
            ConversionTarget.Txt,
            new DefaultConversionAdapterResolver());

        Assert.Equal(OperationStatus.Conflict, operation.Status);
    }

    [Fact]
    public void CreatePlan_marks_engine_unavailable()
    {
        var operation = CreateOperation(
            SourceFormat.Doc,
            "source.doc",
            ConversionTarget.Docx,
            new TestResolver(new TestAdapter(
                SourceFormat.Doc,
                ConversionTarget.Docx,
                available: false)));

        Assert.Equal(OperationStatus.EngineUnavailable, operation.Status);
        Assert.False(operation.AdapterAvailable);
    }

    [Theory]
    [InlineData(SourceFormat.Docx)]
    [InlineData(SourceFormat.Xlsx)]
    [InlineData(SourceFormat.Pptx)]
    public void CreatePlan_copies_modern_formats_by_default(SourceFormat source)
    {
        var operation = CreateOperation(
            source,
            $"source.{source.ToString().ToLowerInvariant()}",
            ConversionTarget.Copy,
            new TestResolver(new TestAdapter(
                source,
                ConversionTarget.Copy,
                available: true)));

        Assert.Equal(OperationStatus.Ready, operation.Status);
        Assert.Equal("Будет скопирован без изменений.", operation.Message);
        Assert.Equal(Path.GetExtension(operation.SourcePath), operation.TargetExtension);
    }

    [Fact]
    public void CreatePlan_skips_unknown_format_by_default()
    {
        var operation = CreateOperation(
            SourceFormat.Unknown,
            "source.custom",
            ConversionTarget.Skip,
            new TestResolver());

        Assert.Equal(OperationStatus.Skipped, operation.Status);
        Assert.Equal(string.Empty, operation.TargetPath);
    }

    [Fact]
    public void CreatePlan_rejects_path_traversal()
    {
        var outsidePath = Path.Combine(_rootPath, "..", "outside.doc");
        var scan = new ScanResult(
            _rootPath,
            [new ScannedFile(outsidePath, Path.Combine("..", "outside.doc"), SourceFormat.Doc)],
            []);
        var rules = RuleSet.CreateDefault();
        var planner = new ConversionPlanner(new TestResolver(new TestAdapter(
            SourceFormat.Doc,
            ConversionTarget.Docx,
            available: true)));

        var operation = Assert.Single(planner.CreatePlan(scan, _rootPath, rules));

        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal(string.Empty, operation.TargetPath);
    }

    [Fact]
    public void OutputPathGuard_rejects_target_outside_output_root()
    {
        var outputRoot = Path.Combine(_rootPath, "_converted");
        var outside = Path.Combine(_rootPath, "outside.txt");

        Assert.False(OutputPathGuard.IsSafeTargetPath(outside, outputRoot));
    }

    [Fact]
    public async Task Processor_does_not_create_output_for_skipped_operation()
    {
        var source = Path.Combine(_rootPath, "manual.pdf");
        File.WriteAllText(source, "synthetic");
        var operation = CreateOperation(
            SourceFormat.Pdf,
            "manual.pdf",
            ConversionTarget.Skip,
            new TestResolver());

        var summary = await new ConversionProcessor(new TestResolver())
            .ProcessAsync([operation], progress: null, CancellationToken.None);

        Assert.Equal(1, summary.Skipped);
        Assert.False(Directory.Exists(Path.Combine(_rootPath, "_converted")));
        Assert.Equal("synthetic", File.ReadAllText(source));
    }

    [Theory]
    [InlineData(OperationStatus.Ready)]
    [InlineData(OperationStatus.Skipped)]
    [InlineData(OperationStatus.Converting)]
    [InlineData(OperationStatus.Succeeded)]
    [InlineData(OperationStatus.Conflict)]
    [InlineData(OperationStatus.Failed)]
    [InlineData(OperationStatus.EngineUnavailable)]
    [InlineData(OperationStatus.Unsupported)]
    public void OperationStatus_contains_required_statuses(OperationStatus status)
    {
        Assert.True(Enum.IsDefined(status));
    }

    [Fact]
    public void CreatePlan_disambiguates_same_stem_markdown_collisions()
    {
        var file1 = Path.Combine(_rootPath, "doc.docx");
        var file2 = Path.Combine(_rootPath, "doc.pdf");
        File.WriteAllText(file1, "doc1");
        File.WriteAllText(file2, "doc2");

        var scan = new ScanResult(
            _rootPath,
            [
                new ScannedFile(file1, "doc.docx", SourceFormat.Docx),
                new ScannedFile(file2, "doc.pdf", SourceFormat.Pdf)
            ],
            []);

        var resolver = new TestResolver(
            new TestAdapter(SourceFormat.Docx, ConversionTarget.Markdown, available: true),
            new TestAdapter(SourceFormat.Pdf, ConversionTarget.Markdown, available: true));

        var rules = RuleSet.CreateDefault()
            .WithRule(SourceFormat.Docx, ConversionTarget.Markdown)
            .WithRule(SourceFormat.Pdf, ConversionTarget.Markdown);

        var planner = new ConversionPlanner(resolver);
        var plan = planner.CreatePlan(scan, _rootPath, rules);

        Assert.Equal(2, plan.Count);
        Assert.Equal(Path.Combine(_rootPath, "_converted", "doc.md"), plan[0].TargetPath);
        Assert.Equal(Path.Combine(_rootPath, "_converted", "doc-2.md"), plan[1].TargetPath);
        Assert.Equal(OperationStatus.Ready, plan[0].Status);
        Assert.Equal(OperationStatus.Ready, plan[1].Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private PlannedOperation CreateOperation(
        SourceFormat source,
        string relativePath,
        ConversionTarget target,
        IConversionAdapterResolver resolver)
    {
        var sourcePath = Path.Combine(_rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        if (!File.Exists(sourcePath))
        {
            File.WriteAllText(sourcePath, "synthetic");
        }

        var scan = new ScanResult(
            _rootPath,
            [new ScannedFile(sourcePath, relativePath, source)],
            []);
        var rules = RuleSet.CreateDefault().WithRule(source, target);
        return Assert.Single(new ConversionPlanner(resolver).CreatePlan(scan, _rootPath, rules));
    }

    private sealed class TestResolver(params IConversionAdapter[] adapters) : IConversionAdapterResolver
    {
        public IConversionAdapter? Resolve(SourceFormat sourceFormat, ConversionTarget target) =>
            adapters.FirstOrDefault(adapter => adapter.CanConvert(sourceFormat, target));
    }

    private sealed class TestAdapter(
        SourceFormat source,
        ConversionTarget target,
        bool available) : IConversionAdapter
    {
        public bool IsAvailable => available;
        public string AvailabilityMessage => available ? "available" : "unavailable";

        public bool CanConvert(SourceFormat sourceFormat, ConversionTarget conversionTarget) =>
            sourceFormat == source && conversionTarget == target;

        public Task<ConversionResult> ConvertAsync(
            PlannedOperation operation,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ConversionResult(
                operation,
                OperationStatus.Succeeded,
                "test"));
    }
}

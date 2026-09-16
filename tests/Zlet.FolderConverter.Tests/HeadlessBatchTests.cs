using System.Text.Json;
using Zlet.FolderConverter.App;
using Zlet.FolderConverter.Core.Models;
using Zlet.FolderConverter.Core.Services;

namespace Zlet.FolderConverter.Tests;

public sealed class HeadlessBatchTests
{
    [Fact]
    public void Command_parses_required_options_and_rejects_unknown_target()
    {
        var args = new[]
        {
            "batch",
            "--source", "C:\\input",
            "--destination=C:\\output",
            "--target", "markdown",
            "--report-json", "C:\\evidence\\report.json",
            "--recursive", "false"
        };

        Assert.True(HeadlessBatchCommand.TryParse(args, out var command, out var error), error);
        Assert.NotNull(command);
        Assert.Equal("C:\\input", command!.SourcePath);
        Assert.Equal("C:\\output", command.DestinationPath);
        Assert.Equal("C:\\evidence\\report.json", command.ReportJsonPath);
        Assert.False(command.Recursive);

        var bad = args.ToArray();
        bad[5] = "txt";
        Assert.False(HeadlessBatchCommand.TryParse(bad, out _, out var badError));
        Assert.Contains("markdown", badError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mixed_markdown_batch_reports_supported_routes_and_explicit_html_unsupported()
    {
        using var temp = new TestDirectory();
        var source = Path.Combine(temp.Path, "source");
        var destination = Path.Combine(temp.Path, "result");
        var report = Path.Combine(temp.Path, "evidence", "report.json");
        Directory.CreateDirectory(source);

        var names = new[]
        {
            "document.docx",
            "sheet.xlsx",
            "slides.pptx",
            "searchable.pdf",
            "notes.txt",
            "page.html"
        };
        foreach (var name in names)
            await File.WriteAllTextAsync(Path.Combine(source, name), $"fixture:{name}");

        var sourceBefore = names.ToDictionary(
            name => name,
            name => File.ReadAllBytes(Path.Combine(source, name)));

        var resolver = new DefaultConversionAdapterResolver([new FakeMarkdownAdapter()]);
        var runner = new HeadlessBatchRunner(
            new FileSystemFolderScanner(),
            new ConversionPlanner(resolver),
            new ConversionProcessor(resolver));
        var command = new HeadlessBatchCommand(source, destination, report, Recursive: true);

        var exitCode = await runner.RunAsync(command, CancellationToken.None);

        Assert.Equal(HeadlessBatchCommand.ExitCompletedWithIssues, exitCode);
        Assert.True(File.Exists(report));

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        var root = json.RootElement;
        Assert.Equal(HeadlessBatchRunner.ReportSchemaVersion, root.GetProperty("schemaVersion").GetString());
        Assert.Equal("COMPLETED_WITH_ISSUES", root.GetProperty("finalState").GetString());
        Assert.Equal(6, root.GetProperty("counts").GetProperty("total").GetInt32());
        Assert.Equal(5, root.GetProperty("counts").GetProperty("succeeded").GetInt32());
        Assert.Equal(1, root.GetProperty("counts").GetProperty("unsupported").GetInt32());

        var items = root.GetProperty("items").EnumerateArray().ToArray();
        var html = items.Single(item => item.GetProperty("sourceRelativePath").GetString() == "page.html");
        Assert.Equal(OperationStatus.Unsupported.ToString(), html.GetProperty("status").GetString());
        Assert.Equal("html_markdown_unsupported", html.GetProperty("diagnosticCode").GetString());
        Assert.Equal("PASS", html.GetProperty("sourceIntegrity").GetString());
        Assert.Empty(html.GetProperty("artifacts").EnumerateArray());
        Assert.False(File.Exists(Path.Combine(destination, "page.md")));

        foreach (var item in items.Where(item => item.GetProperty("status").GetString() == OperationStatus.Succeeded.ToString()))
        {
            Assert.Equal("PASS", item.GetProperty("sourceIntegrity").GetString());
            Assert.Equal("PASS", item.GetProperty("artifactIntegrity").GetString());
            var artifacts = item.GetProperty("artifacts").EnumerateArray().ToArray();
            Assert.Single(artifacts);
            Assert.Equal("primary", artifacts[0].GetProperty("kind").GetString());
            Assert.Equal(64, artifacts[0].GetProperty("sha256").GetString()!.Length);
        }

        foreach (var name in names)
            Assert.Equal(sourceBefore[name], File.ReadAllBytes(Path.Combine(source, name)));
    }

    [Fact]
    public async Task Existing_report_is_not_overwritten()
    {
        using var temp = new TestDirectory();
        var source = Path.Combine(temp.Path, "source");
        var destination = Path.Combine(temp.Path, "result");
        var report = Path.Combine(temp.Path, "report.json");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "notes.txt"), "hello");
        await File.WriteAllTextAsync(report, "historical evidence");

        var resolver = new DefaultConversionAdapterResolver([new FakeMarkdownAdapter()]);
        var runner = new HeadlessBatchRunner(
            new FileSystemFolderScanner(),
            new ConversionPlanner(resolver),
            new ConversionProcessor(resolver));

        await Assert.ThrowsAsync<HeadlessBatchConfigurationException>(() =>
            runner.RunAsync(
                new HeadlessBatchCommand(source, destination, report, Recursive: true),
                CancellationToken.None));
        Assert.Equal("historical evidence", await File.ReadAllTextAsync(report));
    }

    private sealed class FakeMarkdownAdapter : IConversionAdapter
    {
        public bool IsAvailable => true;
        public string AvailabilityMessage => string.Empty;

        public bool CanConvert(SourceFormat sourceFormat, ConversionTarget target) =>
            target == ConversionTarget.Markdown
            && sourceFormat is SourceFormat.Doc
                or SourceFormat.Docx
                or SourceFormat.Xls
                or SourceFormat.Xlsx
                or SourceFormat.Ppt
                or SourceFormat.Pptx
                or SourceFormat.Pdf
                or SourceFormat.Txt
                or SourceFormat.Json;

        public async Task<ConversionResult> ConvertAsync(
            PlannedOperation operation,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(operation.TargetPath)!);
            await File.WriteAllTextAsync(
                operation.TargetPath,
                $"# converted {operation.RelativePath}\n",
                cancellationToken);
            return new ConversionResult(operation, OperationStatus.Succeeded, "converted");
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zlet-headless-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}

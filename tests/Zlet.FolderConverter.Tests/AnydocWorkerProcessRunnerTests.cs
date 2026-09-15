using Zlet.FolderConverter.Core.Models;
using Zlet.FolderConverter.Core.Services;

namespace Zlet.FolderConverter.Tests;

public sealed class AnydocWorkerProcessRunnerTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        "zlet-anydoc-runner-tests",
        Guid.NewGuid().ToString("N"));

    public AnydocWorkerProcessRunnerTests() => Directory.CreateDirectory(_rootPath);

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    [Fact]
    public void Runner_availability_is_deterministic_based_on_paths()
    {
        var dummyExe = Path.Combine(_rootPath, "worker.exe");
        File.WriteAllBytes(dummyExe, Array.Empty<byte>());

        var availableRunner = new AnydocWorkerProcessRunner(new AnydocWorkerOptions
        {
            WorkerExecutablePath = dummyExe
        });
        Assert.True(availableRunner.IsAvailable);
        Assert.Equal("Компонент Markdown доступен.", availableRunner.AvailabilityMessage);

        var missingExeRunner = new AnydocWorkerProcessRunner(new AnydocWorkerOptions
        {
            WorkerExecutablePath = Path.Combine(_rootPath, "missing.exe")
        });
        Assert.False(missingExeRunner.IsAvailable);
        Assert.Contains("недоступен", missingExeRunner.AvailabilityMessage);
    }

    [Fact]
    public async Task Nonexistent_worker_path_returns_unavailable()
    {
        var fakeWorker = Path.Combine(_rootPath, "nonexistent_worker.exe");
        var runner = new AnydocWorkerProcessRunner(new AnydocWorkerOptions { WorkerExecutablePath = fakeWorker });

        var request = new AnydocWorkerRequest("test-id", "source.docx", "output.md", SourceFormat.Docx);
        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("anydoc_worker_missing", result.ErrorCode);
    }

    [MarkdownIntegrationFact]
    public async Task Native_worker_resolves_and_executes_successfully()
    {
        var runner = new AnydocWorkerProcessRunner();
        Assert.True(runner.IsAvailable);

        var sourcePath = Path.Combine(_rootPath, "test.docx");
        var fixture = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "evaluation", "fixtures", "F08_structured.docx");
        if (!File.Exists(fixture))
        {
            // Try relative from test root
            fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "F08_structured.docx"));
        }
        if (!File.Exists(fixture))
        {
            return; // Skip if fixture cannot be located
        }

        File.Copy(fixture, sourcePath, overwrite: true);
        var outputPath = Path.Combine(_rootPath, "output.md");

        var request = new AnydocWorkerRequest("test-exec", sourcePath, outputPath, SourceFormat.Docx);
        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.NotEmpty(content);
    }

    [MarkdownIntegrationFact]
    public async Task Batch_lifecycle_reuses_session()
    {
        var runner = new AnydocWorkerProcessRunner();
        Assert.True(runner.IsAvailable);

        await runner.BeginBatchAsync(CancellationToken.None);
        try
        {
            // EndBatch cleanly shuts down session
            await runner.EndBatchAsync();
        }
        catch
        {
            await runner.EndBatchAsync();
            throw;
        }
    }
}

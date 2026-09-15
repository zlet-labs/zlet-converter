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

    [Fact]
    public async Task Handshake_protocol_version_mismatch_fails_with_incompatible_error()
    {
        var runner = new AnydocWorkerProcessRunner(new AnydocWorkerOptions
        {
            WorkerExecutablePath = MockWorkerExePath(),
            WorkerArguments = "wrong-protocol-version"
        });

        var request = new AnydocWorkerRequest("test-ver", "source.docx", "output.md", SourceFormat.Docx);
        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("anydoc_version_incompatible", result.ErrorCode);
        Assert.Contains("protocol version mismatch", result.ErrorMessage);
    }

    [Fact]
    public async Task Handshake_anydoc_version_mismatch_fails_with_incompatible_error()
    {
        var runner = new AnydocWorkerProcessRunner(new AnydocWorkerOptions
        {
            WorkerExecutablePath = MockWorkerExePath(),
            WorkerArguments = "wrong-anydoc-version"
        });

        var request = new AnydocWorkerRequest("test-ver", "source.docx", "output.md", SourceFormat.Docx);
        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("anydoc_version_incompatible", result.ErrorCode);
        Assert.Contains("anydoc version mismatch", result.ErrorMessage);
    }

    [Fact]
    public async Task Handshake_anydoc_revision_mismatch_fails_with_incompatible_error()
    {
        var runner = new AnydocWorkerProcessRunner(new AnydocWorkerOptions
        {
            WorkerExecutablePath = MockWorkerExePath(),
            WorkerArguments = "wrong-revision"
        });

        var request = new AnydocWorkerRequest("test-ver", "source.docx", "output.md", SourceFormat.Docx);
        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("anydoc_version_incompatible", result.ErrorCode);
        Assert.Contains("anydoc revision mismatch", result.ErrorMessage);
    }

    [Fact]
    public async Task Handshake_timeout_kills_process_and_returns_timed_out_result()
    {
        var runner = new AnydocWorkerProcessRunner(new AnydocWorkerOptions
        {
            WorkerExecutablePath = MockWorkerExePath(),
            WorkerArguments = "hang"
        });

        var request = new AnydocWorkerRequest("test-timeout", "source.docx", "output.md", SourceFormat.Docx);
        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("anydoc_worker_start_failure", result.ErrorCode);
        Assert.True(result.TimedOut);
    }

    /// <summary>
    /// Returns the path to the pre-built mock anydoc worker executable.
    /// The exe and its companion runtime files are built from
    /// tests/Zlet.FolderConverter.MockAnydocWorker and copied to the
    /// mock-workers/ subdirectory of the test output by the CopyMockAnydocWorker MSBuild target.
    /// </summary>
    private static string MockWorkerExePath()
    {
        var exeName = "zlet-mock-anydoc-worker.exe";

        // Primary location: mock-workers/ subdirectory alongside the test assembly.
        // The CopyMockAnydocWorker MSBuild target puts all required runtime files there.
        var mockWorkersDir = Path.Combine(AppContext.BaseDirectory, "mock-workers", exeName);
        if (File.Exists(mockWorkersDir))
        {
            return mockWorkersDir;
        }

        // Fallback: the mock project's own build output (works when running from IDE
        // without the test project's copy target having run yet).
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            foreach (var config in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    dir.FullName,
                    "tests",
                    "Zlet.FolderConverter.MockAnydocWorker",
                    "bin",
                    config,
                    "net8.0-windows",
                    exeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException(
            $"Mock anydoc worker exe not found. Build the solution first. Expected: {mockWorkersDir}");
    }
}

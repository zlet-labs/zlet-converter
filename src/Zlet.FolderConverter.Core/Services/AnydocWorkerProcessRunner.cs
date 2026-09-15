using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.Core.Services;

public sealed record AnydocWorkerOptions
{
    public string? WorkerExecutablePath { get; init; }
    /// <summary>Extra arguments appended to the worker executable command line. Used in tests to select mock behaviour.</summary>
    public string? WorkerArguments { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(2);
}

internal sealed class AnydocVersionIncompatibleException(string message) : Exception(message);

public sealed class StderrBuffer(int maxCapacity = 32 * 1024)
{
    private readonly StringBuilder _buffer = new();
    private readonly object _lock = new();

    public void Append(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_lock)
        {
            _buffer.AppendLine(text);
            if (_buffer.Length > maxCapacity)
            {
                _buffer.Remove(0, _buffer.Length - maxCapacity);
            }
        }
    }

    public string GetContent()
    {
        lock (_lock)
        {
            return _buffer.ToString();
        }
    }
}

public sealed class AnydocWorkerProcessRunner : IAnydocWorkerRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AnydocWorkerOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string? _resolvedWorkerPath;
    private WorkerSession? _session;
    private bool _batchActive;

    public AnydocWorkerProcessRunner(AnydocWorkerOptions? options = null)
    {
        _options = options ?? new AnydocWorkerOptions();
        _resolvedWorkerPath = ResolveWorkerPath(_options.WorkerExecutablePath);
    }

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_resolvedWorkerPath)
        && File.Exists(_resolvedWorkerPath);

    public string AvailabilityMessage => IsAvailable
        ? "Компонент Markdown доступен."
        : "Компонент Markdown недоступен (исполняемый файл zlet-anydoc-worker.exe не найден).";

    public async Task BeginBatchAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _batchActive = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EndBatchAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _batchActive = false;
            await ShutdownSessionAsync(force: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AnydocWorkerExecutionResult> RunAsync(
        AnydocWorkerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsAvailable)
        {
            return new(false, "anydoc_worker_missing", AvailabilityMessage);
        }

        await _gate.WaitAsync(cancellationToken);
        var closeAfterRequest = !_batchActive;
        try
        {
            if (_session is not null && _session.Process.HasExited)
            {
                await ShutdownSessionAsync(force: true);
            }

            if (_session is null)
            {
                try
                {
                    _session = await StartSessionAsync(cancellationToken);
                }
                catch (AnydocVersionIncompatibleException ex)
                {
                    return new(false, "anydoc_version_incompatible", ex.Message);
                }
                catch (TimeoutException ex)
                {
                    return new(false, "anydoc_worker_start_failure", ex.Message, TimedOut: true);
                }
                catch (Exception ex)
                {
                    return new(false, "anydoc_worker_start_failure", $"Не удалось запустить процесс Markdown: {ex.Message}");
                }
            }

            return await ExecuteAsync(_session, request, cancellationToken);
        }
        finally
        {
            if (closeAfterRequest)
            {
                await ShutdownSessionAsync(force: false);
            }
            _gate.Release();
        }
    }

    private async Task<WorkerSession> StartSessionAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _resolvedWorkerPath!,
            Arguments = _options.WorkerArguments ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch Markdown worker process.");

        var stderrBuffer = new StderrBuffer(32 * 1024);
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderrBuffer.Append(e.Data);
            }
        };
        process.BeginErrorReadLine();

        using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeCts.CancelAfter(TimeSpan.FromSeconds(5));

        string? readyLine;
        try
        {
            readyLine = await process.StandardOutput.ReadLineAsync(handshakeCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            throw new TimeoutException("Markdown worker handshake timed out.");
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        if (string.IsNullOrWhiteSpace(readyLine))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("Markdown worker failed to report ready: empty response.");
        }

        try
        {
            var handshake = JsonSerializer.Deserialize<AnydocHandshakeResponse>(readyLine, JsonOptions);
            if (handshake is null || !handshake.Ready)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new InvalidOperationException("Markdown worker reported not ready.");
            }

            if (handshake.Version != "1.0")
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new AnydocVersionIncompatibleException($"Markdown worker protocol version mismatch: {handshake.Version} (expected 1.0)");
            }

            if (handshake.AnydocVersion != "0.2.4")
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new AnydocVersionIncompatibleException($"Markdown worker anydoc version mismatch: {handshake.AnydocVersion} (expected 0.2.4)");
            }

            if (handshake.AnydocRevision != "42bf1c5ecdde9eb0d96d6bd75a9e6698cf93b14c")
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new AnydocVersionIncompatibleException($"Markdown worker anydoc revision mismatch: {handshake.AnydocRevision} (expected 42bf1c5ecdde9eb0d96d6bd75a9e6698cf93b14c)");
            }
        }
        catch (JsonException ex)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException($"Markdown worker invalid handshake: {ex.Message}");
        }

        return new WorkerSession(process, stderrBuffer);
    }

    private async Task<AnydocWorkerExecutionResult> ExecuteAsync(
        WorkerSession session,
        AnydocWorkerRequest request,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(_options.Timeout);

        try
        {
            var json = JsonSerializer.Serialize(new
            {
                id = request.Id,
                sourcePath = request.SourcePath,
                outputPath = request.OutputPath,
                sourceFormat = request.SourceFormat.ToString().ToLowerInvariant(),
                assetDir = request.AssetDir
            }, JsonOptions);

            await session.Process.StandardInput.WriteLineAsync(json.AsMemory(), linkedCts.Token);
            await session.Process.StandardInput.FlushAsync(linkedCts.Token);

            var responseLine = await session.Process.StandardOutput.ReadLineAsync(linkedCts.Token);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                await ShutdownSessionAsync(force: true);
                return new(false, "anydoc_worker_missing_response", "Процесс Markdown завершился без ответа.");
            }

            var response = JsonSerializer.Deserialize<AnydocWorkerResponse>(responseLine, JsonOptions);
            if (response is null)
            {
                return new(false, "anydoc_protocol_error", "Некорректный ответ процесса Markdown.");
            }

            return new(
                Success: response.Success,
                ErrorCode: response.ErrorCode,
                ErrorMessage: response.ErrorMessage,
                HasExtractedText: response.HasExtractedText);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ShutdownSessionAsync(force: true);
            throw;
        }
        catch (OperationCanceledException)
        {
            await ShutdownSessionAsync(force: true);
            return new(false, "anydoc_worker_timeout", "Преобразование превысило допустимое время.", TimedOut: true);
        }
        catch (Exception ex)
        {
            await ShutdownSessionAsync(force: true);
            return new(false, "anydoc_worker_failure", ex.Message);
        }
    }

    private async Task ShutdownSessionAsync(bool force)
    {
        if (_session is null) return;
        var session = _session;
        _session = null;

        try
        {
            if (!session.Process.HasExited)
            {
                if (!force)
                {
                    try
                    {
                        session.Process.StandardInput.Close();
                        using var timeoutCts = new CancellationTokenSource(_options.ShutdownTimeout);
                        await session.Process.WaitForExitAsync(timeoutCts.Token);
                    }
                    catch
                    {
                        force = true;
                    }
                }

                if (force && !session.Process.HasExited)
                {
                    session.Process.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
        }
        finally
        {
            session.Dispose();
        }
    }

    public static string? ResolveWorkerPath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : null;

        var env = Environment.GetEnvironmentVariable("ZLET_ANYDOC_WORKER_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return Path.GetFullPath(env);

        var directCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "zlet-anydoc-worker.exe"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "zlet-anydoc-worker.exe"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "markdown", "zlet-anydoc-worker.exe"),
        };

        foreach (var c in directCandidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var releaseTarget = Path.Combine(dir.FullName, "src", "Zlet.FolderConverter.AnydocWorker", "target", "release", "zlet-anydoc-worker.exe");
            if (File.Exists(releaseTarget)) return Path.GetFullPath(releaseTarget);

            var debugTarget = Path.Combine(dir.FullName, "src", "Zlet.FolderConverter.AnydocWorker", "target", "debug", "zlet-anydoc-worker.exe");
            if (File.Exists(debugTarget)) return Path.GetFullPath(debugTarget);
        }

        return null;
    }

    private sealed class WorkerSession(Process process, StderrBuffer stderr) : IDisposable
    {
        public Process Process { get; } = process;
        public StderrBuffer Stderr { get; } = stderr;

        public void Dispose()
        {
            Process.Dispose();
        }
    }
}

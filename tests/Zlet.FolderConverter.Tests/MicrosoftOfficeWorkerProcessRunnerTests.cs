using System.Diagnostics;
using System.Text.Json;
using Zlet.FolderConverter.Core.Models;
using Zlet.FolderConverter.Core.Services;

namespace Zlet.FolderConverter.Tests;

public sealed class MicrosoftOfficeWorkerProcessRunnerTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _workerPath = Path.Combine(
        Path.GetTempPath(),
        $"zlet-worker-placeholder-{Guid.NewGuid():N}.exe");

    public MicrosoftOfficeWorkerProcessRunnerTests() =>
        File.WriteAllText(_workerPath, "placeholder");

    [Fact]
    public async Task Timeout_terminates_worker_without_unowned_office_process()
    {
        var process = new ControlledWorkerProcess(new DeferredTextReader());
        var officeTerminator = new RecordingOfficeTerminator();
        var runner = CreateRunner(process, officeTerminator);

        var result = await runner.RunAsync(
                Request(OfficeApplicationKind.Word),
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.TimedOut);
        Assert.Equal("worker_timeout", result.ErrorCode);
        Assert.True(process.KillCalled);
        Assert.False(officeTerminator.WasCalled);
    }

    [Fact]
    public async Task Timeout_reads_late_stdout_and_terminates_owned_powerpoint()
    {
        var ownership = new OfficeProcessOwnership(
            4242,
            638900000000000000);
        var process = new ControlledWorkerProcess(
            new DeferredTextReader(StartedLine(ownership, owned: true)));
        var officeTerminator = new RecordingOfficeTerminator();
        var runner = CreateRunner(process, officeTerminator);

        var result = await runner.RunAsync(
            Request(OfficeApplicationKind.PowerPoint),
            CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.True(process.KillCalled);
        Assert.Equal(OfficeApplicationKind.PowerPoint, officeTerminator.Application);
        Assert.Equal(ownership, officeTerminator.Ownership);
    }

    [Fact]
    public async Task Timeout_never_terminates_existing_user_powerpoint()
    {
        var process = new ControlledWorkerProcess(
            new DeferredTextReader(
                StartedLine(
                    new OfficeProcessOwnership(4242, 638900000000000000),
                    owned: false)));
        var officeTerminator = new RecordingOfficeTerminator();
        var runner = CreateRunner(process, officeTerminator);

        var result = await runner.RunAsync(
            Request(OfficeApplicationKind.PowerPoint),
            CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.True(process.KillCalled);
        Assert.False(officeTerminator.WasCalled);
    }

    [Fact]
    public async Task Cancellation_before_started_terminates_worker_without_hanging()
    {
        var process = new ControlledWorkerProcess(new DeferredTextReader());
        var runner = CreateRunner(
            process,
            new RecordingOfficeTerminator(),
            timeout: TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(30));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(
                Request(OfficeApplicationKind.Excel),
                cancellation.Token));

        Assert.True(process.KillCalled);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Cancellation_during_slow_activation_kills_worker_then_drains_owned_started()
    {
        var ownership = new OfficeProcessOwnership(
            4242,
            638900000000000000);
        using var cancellation = new CancellationTokenSource();
        var process = new OwnershipDuringWriteProcess(
            StartedLine(ownership, owned: true),
            cancellation);
        var terminator = new RecordingOfficeTerminator();
        var runner = CreateRunner(
            new FakeLauncher(process),
            terminator,
            timeout: TimeSpan.FromSeconds(10));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(
                Request(OfficeApplicationKind.Word),
                cancellation.Token));

        Assert.True(process.KillCalled);
        Assert.Equal(OfficeApplicationKind.Word, terminator.Application);
        Assert.Equal(ownership, terminator.Ownership);
    }

    [Fact]
    public async Task Cancellation_never_terminates_started_but_unowned_user_office()
    {
        var ownership = new OfficeProcessOwnership(
            4242,
            638900000000000000);
        using var cancellation = new CancellationTokenSource();
        var process = new OwnershipDuringWriteProcess(
            StartedLine(ownership, owned: false),
            cancellation);
        var terminator = new RecordingOfficeTerminator();
        var runner = CreateRunner(
            new FakeLauncher(process),
            terminator,
            timeout: TimeSpan.FromSeconds(10));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(
                Request(OfficeApplicationKind.Word),
                cancellation.Token));

        Assert.True(process.KillCalled);
        Assert.False(terminator.WasCalled);
    }

    [Fact]
    public async Task Batch_reuses_one_worker_for_multiple_sequential_requests()
    {
        var process = new ScriptedWorkerProcess(
            StartedLine(new OfficeProcessOwnership(4242, 638900000000000000), owned: false),
            ResultLine(success: true),
            ResultLine(success: true));
        var launcher = new SequenceLauncher(process);
        var runner = CreateRunner(launcher, new RecordingOfficeTerminator());

        await runner.BeginBatchAsync(CancellationToken.None);
        var first = await runner.RunAsync(Request(OfficeApplicationKind.Word), CancellationToken.None);
        var second = await runner.RunAsync(Request(OfficeApplicationKind.Word), CancellationToken.None);
        await runner.EndBatchAsync();

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(1, launcher.StartCount);
        Assert.Equal(2, process.RequestCount);
        Assert.True(process.InputClosed);
        Assert.False(process.KillCalled);
    }

    [Fact]
    public async Task File_failure_does_not_discard_usable_session()
    {
        var process = new ScriptedWorkerProcess(
            ResultLine(success: false, errorCode: "office_document_failure"),
            ResultLine(success: true));
        var launcher = new SequenceLauncher(process);
        var runner = CreateRunner(launcher, new RecordingOfficeTerminator());

        await runner.BeginBatchAsync(CancellationToken.None);
        var first = await runner.RunAsync(Request(OfficeApplicationKind.Word), CancellationToken.None);
        var second = await runner.RunAsync(Request(OfficeApplicationKind.Word), CancellationToken.None);
        await runner.EndBatchAsync();

        Assert.False(first.Success);
        Assert.True(second.Success);
        Assert.Equal(1, launcher.StartCount);
        Assert.False(process.KillCalled);
    }

    [Fact]
    public async Task Invalid_session_restarts_once_for_next_file_without_retry_loop()
    {
        var invalid = new ScriptedWorkerProcess(
            ResultLine(
                success: false,
                errorCode: "office_com_failure",
                sessionInvalid: true));
        var recovered = new ScriptedWorkerProcess(ResultLine(success: true));
        var launcher = new SequenceLauncher(invalid, recovered);
        var runner = CreateRunner(launcher, new RecordingOfficeTerminator());

        await runner.BeginBatchAsync(CancellationToken.None);
        var first = await runner.RunAsync(Request(OfficeApplicationKind.Word), CancellationToken.None);

        Assert.False(first.Success);
        Assert.True(first.SessionInvalid);
        Assert.Equal(1, launcher.StartCount);
        Assert.True(invalid.KillCalled);

        var second = await runner.RunAsync(Request(OfficeApplicationKind.Word), CancellationToken.None);
        await runner.EndBatchAsync();

        Assert.True(second.Success);
        Assert.Equal(2, launcher.StartCount);
        Assert.False(recovered.KillCalled);
    }

    [Fact]
    public async Task Abandoned_powerpoint_ownership_is_never_terminated()
    {
        var ownership = new OfficeProcessOwnership(
            4242,
            638900000000000000);
        var process = new ScriptedWorkerProcess(
            StartedLine(ownership, owned: true),
            ResultLine(
                success: false,
                errorCode: "powerpoint_session_ownership_lost",
                sessionInvalid: true,
                abandonOwnership: true));
        var terminator = new RecordingOfficeTerminator();
        var runner = CreateRunner(
            new SequenceLauncher(process),
            terminator);

        await runner.BeginBatchAsync(CancellationToken.None);
        var result = await runner.RunAsync(
            Request(OfficeApplicationKind.PowerPoint),
            CancellationToken.None);
        await runner.EndBatchAsync();

        Assert.False(result.Success);
        Assert.True(process.KillCalled);
        Assert.False(terminator.WasCalled);
    }

    [Fact]
    public async Task Second_request_without_stdout_does_not_inherit_first_output()
    {
        var process = new ScriptedWorkerProcess(ResultLine(success: true));
        var runner = CreateRunner(
            new SequenceLauncher(process),
            new RecordingOfficeTerminator());

        await runner.BeginBatchAsync(CancellationToken.None);
        var first = await runner.RunAsync(Request(OfficeApplicationKind.Word), CancellationToken.None);
        var second = await runner.RunAsync(Request(OfficeApplicationKind.Word), CancellationToken.None);
        await runner.EndBatchAsync();

        Assert.True(first.HasStandardOutput);
        Assert.False(second.HasStandardOutput);
        Assert.Equal("worker_result_missing", second.ErrorCode);
    }

    [Fact]
    public async Task Standard_error_is_attributed_only_to_second_request()
    {
        var error = new DeferredDiagnosticReader("second request error");
        var output = new CoordinatedResultReader(
            ResultLine(success: true),
            ResultLine(success: true),
            error.LineConsumed);
        var process = new ScriptedWorkerProcess(
            output,
            error,
            requestCount =>
            {
                if (requestCount == 2)
                {
                    error.ReleaseLine();
                }
            });
        var runner = CreateRunner(
            new SequenceLauncher(process),
            new RecordingOfficeTerminator());

        await runner.BeginBatchAsync(CancellationToken.None);
        var first = await runner.RunAsync(Request(OfficeApplicationKind.Word), CancellationToken.None);
        var second = await runner.RunAsync(Request(OfficeApplicationKind.Word), CancellationToken.None);
        await runner.EndBatchAsync();

        Assert.False(first.HasStandardError);
        Assert.True(second.HasStandardError);
    }

    [Fact]
    public async Task Second_request_timeout_does_not_inherit_first_diagnostics()
    {
        var output = new FirstResultThenBlockReader(ResultLine(success: true));
        var process = new ScriptedWorkerProcess(
            output,
            new StringReader("first request error"));
        var runner = CreateRunner(
            new SequenceLauncher(process),
            new RecordingOfficeTerminator(),
            timeout: TimeSpan.FromMilliseconds(30));

        await runner.BeginBatchAsync(CancellationToken.None);
        var first = await runner.RunAsync(Request(OfficeApplicationKind.Word), CancellationToken.None);
        var second = await runner.RunAsync(Request(OfficeApplicationKind.Word), CancellationToken.None);
        await runner.EndBatchAsync();

        Assert.True(first.HasStandardOutput);
        Assert.True(first.HasStandardError);
        Assert.True(second.TimedOut);
        Assert.False(second.HasStandardOutput);
        Assert.False(second.HasStandardError);
    }

    [Fact]
    public async Task Switching_office_application_closes_previous_session_before_starting_next()
    {
        var word = new ScriptedWorkerProcess(ResultLine(success: true));
        var excel = new ScriptedWorkerProcess(ResultLine(success: true));
        var launcher = new SequenceLauncher(word, excel);
        var runner = CreateRunner(launcher, new RecordingOfficeTerminator());

        await runner.BeginBatchAsync(CancellationToken.None);
        var wordResult = await runner.RunAsync(
            Request(OfficeApplicationKind.Word),
            CancellationToken.None);
        var excelResult = await runner.RunAsync(
            Request(OfficeApplicationKind.Excel),
            CancellationToken.None);
        await runner.EndBatchAsync();

        Assert.True(wordResult.Success);
        Assert.True(excelResult.Success);
        Assert.False(launcher.StartedWhilePreviousActive);
        Assert.Equal(2, launcher.StartCount);
    }

    [Fact]
    public void Terminator_does_not_kill_unknown_pid()
    {
        var terminator = new OwnedOfficeProcessTerminator(
            new FakeProcessLookup(null));

        var terminated = terminator.TryTerminate(
            OfficeApplicationKind.PowerPoint,
            new OfficeProcessOwnership(9999, 638900000000000000));

        Assert.False(terminated);
    }

    [Fact]
    public void Terminator_does_not_kill_reused_pid_with_new_start_time()
    {
        var process = new FakeProcessHandle(
            "POWERPNT",
            638900000000000001);
        var terminator = new OwnedOfficeProcessTerminator(
            new FakeProcessLookup(process));

        var terminated = terminator.TryTerminate(
            OfficeApplicationKind.PowerPoint,
            new OfficeProcessOwnership(4242, 638900000000000000));

        Assert.False(terminated);
        Assert.False(process.KillCalled);
    }

    [Fact]
    public void Terminator_does_not_kill_pid_with_unexpected_process_name()
    {
        var process = new FakeProcessHandle(
            "OTHER",
            638900000000000000);
        var terminator = new OwnedOfficeProcessTerminator(
            new FakeProcessLookup(process));

        var terminated = terminator.TryTerminate(
            OfficeApplicationKind.PowerPoint,
            new OfficeProcessOwnership(4242, 638900000000000000));

        Assert.False(terminated);
        Assert.False(process.KillCalled);
    }

    [Fact]
    public void Terminator_kills_only_exact_process_identity()
    {
        var process = new FakeProcessHandle(
            "POWERPNT",
            638900000000000000);
        var terminator = new OwnedOfficeProcessTerminator(
            new FakeProcessLookup(process));

        var terminated = terminator.TryTerminate(
            OfficeApplicationKind.PowerPoint,
            new OfficeProcessOwnership(4242, 638900000000000000));

        Assert.True(terminated);
        Assert.True(process.KillCalled);
    }

    public void Dispose()
    {
        if (File.Exists(_workerPath))
        {
            File.Delete(_workerPath);
        }
    }

    private MicrosoftOfficeWorkerProcessRunner CreateRunner(
        IOfficeWorkerProcess process,
        IOwnedOfficeProcessTerminator terminator,
        TimeSpan? timeout = null) =>
        new(
            new MicrosoftOfficeWorkerOptions
            {
                WorkerExecutablePath = _workerPath,
                Timeout = timeout ?? TimeSpan.FromMilliseconds(30),
                ShutdownTimeout = TimeSpan.FromMilliseconds(250)
            },
            new FakeLauncher(process),
            terminator);

    private MicrosoftOfficeWorkerProcessRunner CreateRunner(
        IOfficeWorkerProcessLauncher launcher,
        IOwnedOfficeProcessTerminator terminator,
        TimeSpan? timeout = null) =>
        new(
            new MicrosoftOfficeWorkerOptions
            {
                WorkerExecutablePath = _workerPath,
                Timeout = timeout ?? TimeSpan.FromSeconds(1),
                ShutdownTimeout = TimeSpan.FromMilliseconds(250)
            },
            launcher,
            terminator);

    private static OfficeWorkerRequest Request(OfficeApplicationKind application) =>
        new(
            application,
            application == OfficeApplicationKind.Word
                ? @"C:\source.doc"
                : application == OfficeApplicationKind.Excel
                    ? @"C:\source.xls"
                    : @"C:\source.ppt",
            application == OfficeApplicationKind.Word
                ? @"C:\result.docx"
                : application == OfficeApplicationKind.Excel
                    ? @"C:\result.xlsx"
                    : @"C:\result.pptx");

    private static string StartedLine(
        OfficeProcessOwnership ownership,
        bool owned) =>
        JsonSerializer.Serialize(
            new OfficeWorkerMessage(
                OfficeWorkerMessageType.Started,
                OfficeProcessId: ownership.ProcessId,
                OfficeProcessStartTimeUtcTicks: ownership.StartTimeUtcTicks,
                OfficeProcessOwned: owned),
            JsonOptions);

    private static string ResultLine(
        bool success,
        string errorCode = "",
        bool sessionInvalid = false,
        bool abandonOwnership = false) =>
        JsonSerializer.Serialize(
            new OfficeWorkerMessage(
                OfficeWorkerMessageType.Result,
                success,
                errorCode,
                SessionInvalid: sessionInvalid,
                AbandonOfficeProcessOwnership: abandonOwnership),
            JsonOptions);

    private sealed class FakeLauncher(IOfficeWorkerProcess process)
        : IOfficeWorkerProcessLauncher
    {
        public IOfficeWorkerProcess Start(string executablePath) => process;
    }

    private sealed class SequenceLauncher(params ScriptedWorkerProcess[] processes)
        : IOfficeWorkerProcessLauncher
    {
        private int _index;
        public int StartCount { get; private set; }
        public bool StartedWhilePreviousActive { get; private set; }

        public IOfficeWorkerProcess Start(string executablePath)
        {
            if (_index > 0 && processes[_index - 1].IsActive)
            {
                StartedWhilePreviousActive = true;
            }

            StartCount++;
            return processes[_index++];
        }
    }

    private sealed class ScriptedWorkerProcess : IOfficeWorkerProcess
    {
        private readonly TaskCompletionSource _exit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TrackingWriter _input;

        public ScriptedWorkerProcess(params string[] lines)
            : this(
                new StringReader(string.Join(Environment.NewLine, lines)),
                new StringReader(string.Empty))
        {
        }

        public ScriptedWorkerProcess(
            TextReader standardOutput,
            TextReader standardError,
            Action<int>? requestWritten = null)
        {
            StandardOutput = standardOutput;
            StandardError = standardError;
            _input = new TrackingWriter(
                () =>
                {
                    ReleaseReaders();
                    _exit.TrySetResult();
                },
                requestWritten);
        }

        public TextWriter StandardInput => _input;
        public TextReader StandardOutput { get; }
        public TextReader StandardError { get; }
        public int ExitCode => 0;
        public bool KillCalled { get; private set; }
        public bool InputClosed => _input.IsClosed;
        public int RequestCount => _input.RequestCount;
        public bool IsActive => !_exit.Task.IsCompleted;

        public Task WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task;

        public void Kill()
        {
            KillCalled = true;
            ReleaseReaders();
            _exit.TrySetResult();
        }

        public void Dispose()
        {
        }

        private void ReleaseReaders()
        {
            (StandardOutput as IReleaseOnShutdown)?.Release();
            (StandardError as IReleaseOnShutdown)?.Release();
        }

        private sealed class TrackingWriter(
            Action onClose,
            Action<int>? requestWritten) : StringWriter
        {
            public bool IsClosed { get; private set; }
            public int RequestCount => ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Length;

            public override void Close()
            {
                IsClosed = true;
                onClose();
                base.Close();
            }

            public override async Task WriteLineAsync(
                ReadOnlyMemory<char> buffer,
                CancellationToken cancellationToken = default)
            {
                await base.WriteLineAsync(buffer, cancellationToken);
                requestWritten?.Invoke(RequestCount);
            }
        }
    }

    private interface IReleaseOnShutdown
    {
        void Release();
    }

    private sealed class FirstResultThenBlockReader(string firstLine)
        : TextReader, IReleaseOnShutdown
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;

        public override async Task<string?> ReadLineAsync()
        {
            if (Interlocked.Increment(ref _readCount) == 1)
            {
                return firstLine;
            }

            await _release.Task;
            return null;
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class DeferredDiagnosticReader(string line)
        : TextReader, IReleaseOnShutdown
    {
        private readonly TaskCompletionSource _lineRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _shutdown =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _lineConsumed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _lineRead;

        public Task LineConsumed => _lineConsumed.Task;

        public void ReleaseLine() => _lineRelease.TrySetResult();

        public override async Task<string?> ReadLineAsync()
        {
            if (!_lineRead)
            {
                await _lineRelease.Task;
                _lineRead = true;
                _lineConsumed.TrySetResult();
                return line;
            }

            await _shutdown.Task;
            return null;
        }

        public void Release() => _shutdown.TrySetResult();
    }

    private sealed class CoordinatedResultReader(
        string firstLine,
        string secondLine,
        Task secondRelease) : TextReader, IReleaseOnShutdown
    {
        private readonly TaskCompletionSource _shutdown =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;

        public override async Task<string?> ReadLineAsync()
        {
            var readCount = Interlocked.Increment(ref _readCount);
            if (readCount == 1)
            {
                return firstLine;
            }

            if (readCount == 2)
            {
                await secondRelease;
                await Task.Delay(10);
                return secondLine;
            }

            await _shutdown.Task;
            return null;
        }

        public void Release() => _shutdown.TrySetResult();
    }

    private sealed class OwnershipDuringWriteProcess(
        string startedLine,
        CancellationTokenSource cancellation) : IOfficeWorkerProcess
    {
        private readonly DeferredTextReader _output = new(startedLine);
        private readonly TaskCompletionSource _exit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TextWriter StandardInput { get; } =
            new CancellingWriter(cancellation);
        public TextReader StandardOutput => _output;
        public TextReader StandardError { get; } = new StringReader(string.Empty);
        public int ExitCode => 0;
        public bool KillCalled { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task;

        public void Kill()
        {
            KillCalled = true;
            _output.Release();
            _exit.TrySetResult();
        }

        public void Dispose()
        {
        }

        private sealed class CancellingWriter(
            CancellationTokenSource cancellation) : StringWriter
        {
            public override Task WriteLineAsync(
                ReadOnlyMemory<char> buffer,
                CancellationToken cancellationToken = default)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
        }
    }

    private sealed class ControlledWorkerProcess(
        DeferredTextReader standardOutput)
        : IOfficeWorkerProcess
    {
        private readonly TaskCompletionSource _exit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TextWriter StandardInput { get; } = new StringWriter();
        public TextReader StandardOutput => standardOutput;
        public TextReader StandardError { get; } = new StringReader(string.Empty);
        public int ExitCode => 0;
        public bool KillCalled { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task;

        public void Kill()
        {
            KillCalled = true;
            standardOutput.Release();
            _exit.TrySetResult();
        }

        public void Dispose()
        {
        }
    }

    private sealed class DeferredTextReader(string? line = null) : TextReader
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _read;

        public void Release() => _release.TrySetResult();

        public override async Task<string?> ReadLineAsync()
        {
            await _release.Task;
            if (_read)
            {
                return null;
            }

            _read = true;
            return line;
        }
    }

    private sealed class RecordingOfficeTerminator : IOwnedOfficeProcessTerminator
    {
        public bool WasCalled => Ownership is not null;
        public OfficeApplicationKind? Application { get; private set; }
        public OfficeProcessOwnership? Ownership { get; private set; }

        public bool TryTerminate(
            OfficeApplicationKind application,
            OfficeProcessOwnership ownership)
        {
            Application = application;
            Ownership = ownership;
            return true;
        }
    }

    private sealed class FakeProcessLookup(IOfficeProcessHandle? process)
        : IOfficeProcessLookup
    {
        public IOfficeProcessHandle? TryGet(int processId) => process;
    }

    private sealed class FakeProcessHandle(
        string processName,
        long startTimeUtcTicks)
        : IOfficeProcessHandle
    {
        public string ProcessName => processName;
        public long StartTimeUtcTicks => startTimeUtcTicks;
        public bool KillCalled { get; private set; }

        public void Kill() => KillCalled = true;

        public void Dispose()
        {
        }
    }
}

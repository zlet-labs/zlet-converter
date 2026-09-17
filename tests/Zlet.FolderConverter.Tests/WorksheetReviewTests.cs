using System.Text;
using System.IO.Compression;
using Zlet.FolderConverter.App;
using Zlet.FolderConverter.App.Localization;
using Zlet.FolderConverter.App.ViewModels;
using Zlet.FolderConverter.Core.Models;
using Zlet.FolderConverter.Core.Services;

namespace Zlet.FolderConverter.Tests;

public sealed class WorksheetReviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zl056-review", Guid.NewGuid().ToString("N"));
    private readonly RuleSet _rules = RuleSet.CreateDefault().WithRule(SourceFormat.Xlsx, ConversionTarget.Csv);
    public WorksheetReviewTests() => Directory.CreateDirectory(_root);
    private string Output => Path.Combine(_root, "output");
    private ScannedFile Workbook()
    {
        var path = Path.Combine(_root, "book.xlsx");
        if (!File.Exists(path)) File.WriteAllText(path, "fixture");
        return new(path, "book.xlsx", SourceFormat.Xlsx);
    }
    private ScanResult Scan() => new(_root, [Workbook()], []);
    private static ExcelWorkbookInspectionResult Success(string name = "Data") =>
        new(true, [new(name, 1, WorksheetVisibility.Visible, false)]);
    private static DefaultConversionAdapterResolver Resolver() => new([new AvailableAdapter()]);

    [Fact]
    public async Task Async_inspection_returns_to_calling_synchronization_context_and_propagates_token()
    {
        using var cancellation = new CancellationTokenSource();
        var gate = new TaskCompletionSource<ExcelWorkbookInspectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        var inspector = new Inspector(async token => { received = token; return await gate.Task; });
        var planner = new ConversionPlanner(Resolver(), inspector);
        var scan = Scan();
        var returned = new TaskCompletionSource<Task<IReadOnlyList<PlannedOperation>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new RecordingContext();
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try { returned.SetResult(planner.CreatePlanAsync(scan, _root, Output, _rules, cancellation.Token)); }
            catch (Exception error) { returned.SetException(error); }
        }) { IsBackground = true };
        thread.Start();
        try
        {
            var planning = await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(planning.IsCompleted);
            Assert.Equal(cancellation.Token, received);
            gate.SetResult(Success());
            Assert.Single(await planning.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(context.Posts > 0);
        }
        finally { gate.TrySetResult(Success()); }
    }

    [Fact]
    public async Task Failed_inspection_retries_success_is_cached_and_metadata_change_invalidates()
    {
        var inspector = new Inspector(_ => Task.FromResult<ExcelWorkbookInspectionResult>(Success()));
        inspector.Next = _ => Task.FromResult(new ExcelWorkbookInspectionResult(false, [], "worker_timeout"));
        var planner = new ConversionPlanner(Resolver(), inspector);
        var scan = Scan();
        Assert.Equal(OperationStatus.Failed, Assert.Single(await planner.CreatePlanAsync(scan, _root, Output, _rules, default)).Status);
        inspector.Next = _ => Task.FromResult(Success());
        Assert.Equal(OperationStatus.Ready, Assert.Single(await planner.CreatePlanAsync(scan, _root, Output, _rules, default)).Status);
        await planner.CreatePlanAsync(scan, _root, Output, _rules, default);
        Assert.Equal(2, inspector.Calls);
        File.AppendAllText(scan.Files[0].SourcePath, "changed length");
        await planner.CreatePlanAsync(scan, _root, Output, _rules, default);
        Assert.Equal(3, inspector.Calls);
        File.SetLastWriteTimeUtc(scan.Files[0].SourcePath, DateTime.UtcNow.AddMinutes(1));
        await planner.CreatePlanAsync(scan, _root, Output, _rules, default);
        Assert.Equal(4, inspector.Calls);
    }

    [Fact]
    public async Task Cancellation_is_not_cached_and_pre_cancelled_request_never_starts_inspection()
    {
        var inspector = new Inspector(async token => { await Task.Delay(Timeout.Infinite, token); return Success(); });
        var planner = new ConversionPlanner(Resolver(), inspector);
        var scan = Scan();
        using var cts = new CancellationTokenSource();
        var planning = planner.CreatePlanAsync(scan, _root, Output, _rules, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => planning);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => planner.CreatePlanAsync(scan, _root, Output, _rules, cts.Token));
        Assert.Equal(1, inspector.Calls);
        inspector.Next = _ => Task.FromResult(Success());
        await planner.CreatePlanAsync(scan, _root, Output, _rules, default);
        Assert.Equal(2, inspector.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_planning_cannot_replace_new_rule_or_new_scan(bool rescan)
    {
        var gate = new TaskCompletionSource<ExcelWorkbookInspectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        var inspector = new Inspector(token => { received = token; return gate.Task; }); // deliberately ignores cancellation
        var vm = new MainWindowViewModel(new Scanner(Scan()), new ConversionPlanner(Resolver(), inspector))
            { SelectedFolder = _root, OutputPath = Output };
        await vm.ScanAsync();
        var rule = Assert.Single(vm.FormatRules);
        rule.SelectedTarget = rule.Targets.Single(t => t.Target == ConversionTarget.Csv);
        var obsolete = vm.PreviewPlanningTask;
        Assert.True(vm.IsPlanning);
        Assert.False(vm.CanConvert);
        if (rescan) await vm.ScanAsync();
        else rule.SelectedTarget = rule.Targets.Single(t => t.Target == ConversionTarget.Copy);
        Assert.True(received.IsCancellationRequested);
        gate.SetResult(Success("Stale"));
        await obsolete;
        Assert.Equal(rescan ? ConversionTarget.Markdown : ConversionTarget.Copy, Assert.Single(vm.Operations).Operation.Target);
        Assert.False(vm.IsPlanning);
    }

    [Fact]
    public async Task Explicit_ui_cancellation_preserves_preview_and_reaches_inspector()
    {
        var gate = new TaskCompletionSource<ExcelWorkbookInspectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        var inspector = new Inspector(token => { received = token; return gate.Task; });
        var vm = new MainWindowViewModel(new Scanner(Scan()), new ConversionPlanner(Resolver(), inspector))
            { SelectedFolder = _root, OutputPath = Output };
        await vm.ScanAsync();
        var previous = vm.Operations.ToArray();
        var rule = Assert.Single(vm.FormatRules);
        rule.SelectedTarget = rule.Targets.Single(t => t.Target == ConversionTarget.Csv);
        var old = vm.PreviewPlanningTask;
        using var cts = new CancellationTokenSource();
        var request = vm.RebuildPreviewAsync(cts.Token);
        cts.Cancel();
        Assert.True(received.IsCancellationRequested);
        gate.SetResult(Success());
        await Task.WhenAll(old, request);
        Assert.Equal(previous, vm.Operations);
    }

    [Fact]
    public async Task Non_excel_sync_and_async_planning_need_no_inspection()
    {
        var path = Path.Combine(_root, "data.json");
        File.WriteAllText(path, "{}");
        var inspector = new Inspector(_ => throw new InvalidOperationException());
        var planner = new ConversionPlanner(Resolver(), inspector);
        var scan = new ScanResult(_root, [new(path, "data.json", SourceFormat.Json)], []);
        Assert.Equal(planner.CreatePlan(scan, _root, Output, _rules), await planner.CreatePlanAsync(scan, _root, Output, _rules, default));
        Assert.Equal(0, inspector.Calls);
    }

    [Theory]
    [InlineData(SourceFormat.Csv, ".csv")]
    [InlineData(SourceFormat.Tsv, ".tsv")]
    public async Task Empty_delimited_copy_preserves_hash_and_publishes_to_folder_and_zip(SourceFormat format, string extension)
    {
        var source = Path.Combine(_root, "empty" + extension);
        File.WriteAllBytes(source, []);
        var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source));
        var operation = new PlannedOperation(source, Path.GetFileName(source), format, ConversionTarget.Copy, extension,
            Path.Combine(Output, "empty" + extension), true, OperationStatus.Ready, "", Output, _root);
        var adapter = new SafeFileCopyAdapter(new OutputResultValidator());
        var result = await adapter.ConvertAsync(operation, default);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Equal(0, new FileInfo(operation.TargetPath).Length);
        Assert.Equal(before, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)));
        Assert.Equal(before, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(operation.TargetPath)));
        Assert.Equal(OperationStatus.Conflict, (await adapter.ConvertAsync(operation, default)).Status);
        var zip = Path.Combine(_root, "empty.zip");
        Assert.True((await new ResultZipPublisher().PublishAsync(Output, zip, new(1, 0, 0, 0, 0, 0, [result]), default)).Created);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(0, Assert.Single(archive.Entries).Length);
    }

    [Fact]
    public async Task Empty_generated_doc_output_is_still_rejected()
    {
        var path = Path.Combine(_root, "source.doc"); File.WriteAllText(path, "source");
        var operation = new PlannedOperation(path, "source.doc", SourceFormat.Doc, ConversionTarget.Docx, ".docx",
            Path.Combine(Output, "source.docx"), true, OperationStatus.Ready, "", Output, _root);
        var result = await new SafeFileOperationExecutor(new OutputResultValidator()).ExecuteAsync(operation, ConversionTarget.Docx,
            (output, _) => { File.WriteAllBytes(output, []); return Task.FromResult(new TemporaryOutputProductionResult(true)); }, "", null, default);
        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.False(File.Exists(operation.TargetPath));
    }

    [Theory]
    [InlineData(".csv")]
    [InlineData(".tsv")]
    public void Long_names_and_collision_suffixes_fit_component_and_staging_limits(string extension)
    {
        var book = new string('b', 250);
        var sheets = new[] { new string('s', 240) + "a", new string('s', 240) + "b", "CON", "NUL.txt" };
        var names = WorksheetOutputNameBuilder.Build(book, sheets, extension);
        Assert.Equal(names, WorksheetOutputNameBuilder.Build(book, sheets, extension));
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.EndsWith("-2" + extension, names[1]);
        Assert.All(names, name => { Assert.True(name.Length <= 200); Assert.True(name.Length + 38 <= 255); Assert.EndsWith(extension, name); });
        Assert.Contains("___CON", names[2]);
        Assert.Contains("___NUL.txt", names[3]);
        var crossWorkbook = WorksheetOutputNameBuilder.WithCollisionSuffix(names[0], 123456);
        Assert.True(crossWorkbook.Length <= 200);
        Assert.EndsWith("-123456" + extension, crossWorkbook);
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        public int Posts;
        public override void Post(SendOrPostCallback callback, object? state)
        { Interlocked.Increment(ref Posts); ThreadPool.QueueUserWorkItem(_ => callback(state)); }
    }

    [Fact]
    public async Task Zip_publication_failure_with_successful_outputs_cannot_create_report_only_result()
    {
        using var processor = new PublishingProcessor(lockOutput: true);
        var vm = ZipViewModel(processor);
        await vm.ScanAsync();
        var staging = vm.Operations[0].Operation.OutputRootPath;
        try
        {
            await vm.ConvertAsync();
            Assert.Equal(1, vm.FinalSucceeded);
            Assert.True(vm.ZipPublicationFailed);
            Assert.False(File.Exists(vm.OutputPath));
            Assert.False(vm.CanOpenResult);
            Assert.Contains("ZIP publication failed", vm.ReportStatusText);
            Assert.True(vm.HasErrors);
        }
        finally
        {
            processor.Dispose();
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    [Fact]
    public async Task Published_zip_keeps_outputs_and_report_append_failure_preserves_archive()
    {
        using var processor = new PublishingProcessor(lockOutput: false);
        var vm = ZipViewModel(processor);
        await vm.ScanAsync();
        await vm.ConvertAsync();
        Assert.True(vm.ZipPublishedByThisRun);
        Assert.True(vm.CanOpenResult);
        using (var archive = ZipFile.OpenRead(vm.OutputPath))
        {
            Assert.Contains(archive.Entries, entry => entry.FullName == "book.md");
            Assert.Contains(archive.Entries, entry => entry.FullName == "ZletConverter-report.txt");
        }
        var bytes = File.ReadAllBytes(vm.OutputPath);
        using (var locked = new FileStream(vm.OutputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await ConversionReportWriter.WriteAsync(vm, DateTimeOffset.Now, DateTimeOffset.Now);
        Assert.Equal(bytes, File.ReadAllBytes(vm.OutputPath));
        Assert.Contains("Could not save", vm.ReportStatusText);
        Assert.True(vm.CanOpenResult);
    }

    [Theory]
    [InlineData("ru-RU")]
    [InlineData("en-US")]
    public async Task Report_operation_statuses_are_localized(string language)
    {
        var l = LocalizationService.CreateStandalone(language);
        var vm = new MainWindowViewModel(new Scanner(Scan()), new ConversionPlanner(Resolver()), localization: l)
            { SelectedFolder = _root, OutputPath = Output };
        await vm.ScanAsync();
        var basis = vm.Operations[0].Operation;
        vm.Operations.Clear();
        var statuses = new[] { OperationStatus.Succeeded, OperationStatus.Succeeded, OperationStatus.Skipped,
            OperationStatus.Ready, OperationStatus.EngineUnavailable, OperationStatus.Conflict, OperationStatus.Failed,
            OperationStatus.Cancelled, OperationStatus.NotProcessed };
        for (var i = 0; i < statuses.Length; i++)
            vm.Operations.Add(new OperationRowViewModel(basis with { Status = statuses[i],
                Target = i == 1 ? ConversionTarget.Copy : ConversionTarget.Csv }, isNotSelected: i == 3, localization: l));
        var report = ConversionReportWriter.BuildReport(vm, DateTimeOffset.Now, DateTimeOffset.Now);
        var expected = language == "ru-RU"
            ? new[] { "ПРЕОБРАЗОВАНО", "СКОПИРОВАНО", "ПРОПУЩЕНО", "НЕ ВЫБРАНО", "НЕДОСТУПНО", "КОНФЛИКТ", "ОШИБКА", "ОТМЕНЕНО", "НЕ ОБРАБОТАНО" }
            : new[] { "CONVERTED", "COPIED", "SKIPPED", "NOT SELECTED", "UNAVAILABLE", "CONFLICT", "FAILED", "CANCELLED", "NOT PROCESSED" };
        Assert.All(expected, status => Assert.Contains("[" + status + "]", report));
    }

    private MainWindowViewModel ZipViewModel(IConversionProcessor processor) => new(new Scanner(Scan()),
        new ConversionPlanner(Resolver()), processor, localization: LocalizationService.CreateStandalone("en-US"))
        { SelectedFolder = _root, SelectedOutputMode = OutputMode.Zip, OutputPath = Path.Combine(_root, "result.zip") };

    private sealed class PublishingProcessor(bool lockOutput) : IConversionProcessor, IDisposable
    {
        private FileStream? _locked;
        public Task<ConversionSummary> ProcessAsync(IReadOnlyList<PlannedOperation> operations, IProgress<ConversionProgress>? progress, CancellationToken token)
        {
            var operation = operations[0];
            Directory.CreateDirectory(Path.GetDirectoryName(operation.TargetPath)!);
            File.WriteAllText(operation.TargetPath, "completed output");
            if (lockOutput) _locked = new FileStream(operation.TargetPath, FileMode.Open, FileAccess.Read, FileShare.None);
            return Task.FromResult(new ConversionSummary(1, 0, 0, 0, 0, 0, [new(operation, OperationStatus.Succeeded, "")]));
        }
        public void Dispose() { _locked?.Dispose(); _locked = null; }
    }
    private sealed class Inspector(Func<CancellationToken, Task<ExcelWorkbookInspectionResult>> next) : IExcelWorkbookInspector
    {
        public Func<CancellationToken, Task<ExcelWorkbookInspectionResult>> Next { get; set; } = next;
        public int Calls;
        public bool IsAvailable => true;
        public Task<ExcelWorkbookInspectionResult> InspectAsync(string path, CancellationToken token) { Calls++; return Next(token); }
    }
    private sealed class Scanner(ScanResult scan) : IFolderScanner
    { public Task<ScanResult> ScanAsync(string root, bool nested, CancellationToken token) => Task.FromResult(scan); }
    private sealed class AvailableAdapter : IConversionAdapter
    {
        public bool IsAvailable => true;
        public string AvailabilityMessage => "";
        public bool CanConvert(SourceFormat source, ConversionTarget target) => true;
        public Task<ConversionResult> ConvertAsync(PlannedOperation operation, CancellationToken token) => Task.FromResult(new ConversionResult(operation, OperationStatus.Succeeded, ""));
    }
    public void Dispose() => Directory.Delete(_root, true);
}

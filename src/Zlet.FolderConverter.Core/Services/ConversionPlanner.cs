using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.Core.Services;

public sealed class ConversionPlanner : IConversionPlanner
{
    private readonly IConversionAdapterResolver _adapterResolver;
    private readonly IExcelWorkbookInspector _excelInspector;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ExcelWorkbookInspectionResult> _worksheetCache =
        new(StringComparer.OrdinalIgnoreCase);

    public ConversionPlanner(
        IConversionAdapterResolver adapterResolver,
        IExcelWorkbookInspector? excelInspector = null)
    {
        _adapterResolver = adapterResolver;
        _excelInspector = excelInspector ?? new MicrosoftExcelWorkbookInspector(
            new MicrosoftOfficeCapabilityDetector(),
            new MicrosoftOfficeWorkerProcessRunner());
    }

    public IReadOnlyList<PlannedOperation> CreatePlan(
        ScanResult scanResult,
        string rootPath,
        RuleSet ruleSet) =>
        CreatePlan(scanResult, rootPath, Path.Combine(Path.GetFullPath(rootPath), "_converted"), ruleSet);

    public async Task<IReadOnlyList<PlannedOperation>> CreatePlanAsync(
        ScanResult scanResult, string sourceRootPath, string outputRootPath,
        RuleSet ruleSet, CancellationToken cancellationToken)
    {
        var files = new List<ScannedFile>(scanResult.Files.Count);
        foreach (var file in scanResult.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inspect = file.Format is SourceFormat.Xls or SourceFormat.Xlsx
                && ruleSet.GetRule(file.Format).Target is ConversionTarget.Csv or ConversionTarget.Tsv
                && OutputPathGuard.IsSafeSourcePath(file.SourcePath, sourceRootPath, file.RelativePath);
            files.Add(inspect ? await EnsureWorksheetMetadataAsync(file, cancellationToken).ConfigureAwait(false) : file);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return CreatePlan(scanResult with { Files = files }, sourceRootPath, outputRootPath, ruleSet);
    }

    public IReadOnlyList<PlannedOperation> CreatePlan(
        ScanResult scanResult,
        string sourceRootPath,
        string outputRootPath,
        RuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(scanResult);
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRootPath);

        var fullRootPath = Path.GetFullPath(sourceRootPath);
        var targetRootPath = Path.GetFullPath(outputRootPath);

        var operations = scanResult.Files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .SelectMany(file => CreateOperations(
                file,
                fullRootPath,
                targetRootPath,
                ruleSet.GetRule(file.Format)))
            .ToArray();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < operations.Length; index++)
        {
            var operation = operations[index];
            if (string.IsNullOrEmpty(operation.TargetPath)) continue;
            var path = operation.TargetPath;
            var suffix = 2;
            while (!used.Add(path))
            {
                path = Path.Combine(
                    Path.GetDirectoryName(operation.TargetPath)!,
                    WorksheetOutputNameBuilder.WithCollisionSuffix(Path.GetFileName(operation.TargetPath), suffix++));
            }
            if (path == operation.TargetPath) continue;
            var conflict = File.Exists(path) || Directory.Exists(path);
            var status = operation.Status;
            var message = operation.Message;
            if (status == OperationStatus.Conflict && !conflict)
            {
                if (operation.IsWorksheetOperation)
                {
                    status = operation.WorksheetIsEmpty ? OperationStatus.Skipped
                        : operation.AdapterAvailable ? OperationStatus.Ready : OperationStatus.EngineUnavailable;
                    message = operation.WorksheetIsEmpty ? "worksheet_empty"
                        : operation.AdapterAvailable ? "Готово к преобразованию." : "Преобразование недоступно.";
                }
                else
                {
                    status = operation.AdapterAvailable ? OperationStatus.Ready : OperationStatus.EngineUnavailable;
                    message = operation.AdapterAvailable ? "Готово к преобразованию." : "Преобразование недоступно.";
                }
            }
            operations[index] = operation with
            {
                TargetPath = path,
                ResultRelativePath = Path.GetRelativePath(targetRootPath, path),
                Status = conflict ? OperationStatus.Conflict : status,
                Message = conflict ? "Файл результата уже существует." : message,
                DefaultSelected = !conflict && status == OperationStatus.Ready
                    && (!operation.IsWorksheetOperation || operation.WorksheetVisibility == WorksheetVisibility.Visible)
            };
        }
        return operations;
    }

    private IEnumerable<PlannedOperation> CreateOperations(
        ScannedFile file,
        string sourceRootPath,
        string targetRootPath,
        ConversionRule rule)
    {
        if (rule.Target is ConversionTarget.Csv or ConversionTarget.Tsv
            && file.Format is SourceFormat.Xls or SourceFormat.Xlsx)
        {
            return CreateWorksheetOperations(file, sourceRootPath, targetRootPath, rule);
        }

        return [CreateOperation(file, sourceRootPath, targetRootPath, rule)];
    }

    private IReadOnlyList<PlannedOperation> CreateWorksheetOperations(
        ScannedFile file,
        string sourceRootPath,
        string targetRootPath,
        ConversionRule rule)
    {
        if (!OutputPathGuard.IsSafeSourcePath(file.SourcePath, sourceRootPath, file.RelativePath))
            return [Create(file, rule.Target, rule.Target.ToExtension(), string.Empty, false,
                OperationStatus.Failed, "Исходный файл небезопасен или находится вне выбранной папки.", targetRootPath, sourceRootPath)];
        if (file.Worksheets is null && string.IsNullOrEmpty(file.WorksheetInspectionErrorCode) && _excelInspector.IsAvailable)
            throw new InvalidOperationException("Worksheet discovery requires CreatePlanAsync.");
        var targetExtension = rule.Target.ToExtension();
        if (!string.IsNullOrWhiteSpace(file.WorksheetInspectionErrorCode))
        {
            return
            [
                CreateWorksheet(
                    file,
                    rule.Target,
                    targetExtension,
                    string.Empty,
                    false,
                    OperationStatus.Failed,
                    "worksheet_inspection_failure",
                    targetRootPath,
                    sourceRootPath,
                    worksheetName: string.Empty,
                    WorksheetVisibility.Visible,
                    worksheetIsEmpty: false,
                    defaultSelected: false,
                    resultRelativePath: string.Empty)
            ];
        }

        if (file.Worksheets is null)
        {
            return [CreateOperation(file, sourceRootPath, targetRootPath, rule)];
        }

        if (file.Worksheets.Count == 0)
        {
            return
            [
                CreateWorksheet(
                    file,
                    rule.Target,
                    targetExtension,
                    string.Empty,
                    false,
                    OperationStatus.Skipped,
                    "worksheet_none",
                    targetRootPath,
                    sourceRootPath,
                    worksheetName: string.Empty,
                    WorksheetVisibility.Visible,
                    worksheetIsEmpty: true,
                    defaultSelected: false,
                    resultRelativePath: string.Empty)
            ];
        }

        var workbookBaseName = Path.GetFileNameWithoutExtension(file.RelativePath);
        var outputNames = WorksheetOutputNameBuilder.Build(
            workbookBaseName,
            file.Worksheets.Select(sheet => sheet.Name),
            targetExtension);
        var relativeDirectory = Path.GetDirectoryName(file.RelativePath) ?? string.Empty;
        var adapter = _adapterResolver.Resolve(file.Format, rule.Target);
        var adapterAvailable = adapter?.IsAvailable == true;
        var result = new List<PlannedOperation>(file.Worksheets.Count);

        for (var index = 0; index < file.Worksheets.Count; index++)
        {
            var sheet = file.Worksheets[index];
            var relativeOutput = string.IsNullOrWhiteSpace(relativeDirectory)
                ? outputNames[index]
                : Path.Combine(relativeDirectory, outputNames[index]);
            var targetPath = Path.GetFullPath(Path.Combine(targetRootPath, relativeOutput));

            if (!OutputPathGuard.IsSafeTargetPath(targetPath, targetRootPath))
            {
                result.Add(CreateWorksheet(
                    file,
                    rule.Target,
                    targetExtension,
                    string.Empty,
                    false,
                    OperationStatus.Failed,
                    "Недопустимый путь результата.",
                    targetRootPath,
                    sourceRootPath,
                    sheet.Name,
                    sheet.Visibility,
                    sheet.IsEmpty,
                    defaultSelected: false,
                    relativeOutput));
                continue;
            }

            if (File.Exists(targetPath) || Directory.Exists(targetPath))
            {
                result.Add(CreateWorksheet(
                    file,
                    rule.Target,
                    targetExtension,
                    targetPath,
                    adapterAvailable,
                    OperationStatus.Conflict,
                    "Файл результата уже существует.",
                    targetRootPath,
                    sourceRootPath,
                    sheet.Name,
                    sheet.Visibility,
                    sheet.IsEmpty,
                    defaultSelected: false,
                    relativeOutput));
                continue;
            }

            if (sheet.IsEmpty)
            {
                result.Add(CreateWorksheet(
                    file,
                    rule.Target,
                    targetExtension,
                    targetPath,
                    adapterAvailable,
                    OperationStatus.Skipped,
                    "worksheet_empty",
                    targetRootPath,
                    sourceRootPath,
                    sheet.Name,
                    sheet.Visibility,
                    worksheetIsEmpty: true,
                    defaultSelected: false,
                    relativeOutput));
                continue;
            }

            if (adapter is null)
            {
                result.Add(CreateWorksheet(
                    file,
                    rule.Target,
                    targetExtension,
                    targetPath,
                    false,
                    OperationStatus.Unsupported,
                    "Выбранное преобразование не поддерживается.",
                    targetRootPath,
                    sourceRootPath,
                    sheet.Name,
                    sheet.Visibility,
                    worksheetIsEmpty: false,
                    defaultSelected: false,
                    relativeOutput));
                continue;
            }

            if (!adapterAvailable)
            {
                result.Add(CreateWorksheet(
                    file,
                    rule.Target,
                    targetExtension,
                    targetPath,
                    false,
                    OperationStatus.EngineUnavailable,
                    adapter.AvailabilityMessage,
                    targetRootPath,
                    sourceRootPath,
                    sheet.Name,
                    sheet.Visibility,
                    worksheetIsEmpty: false,
                    defaultSelected: false,
                    relativeOutput));
                continue;
            }

            var hidden = sheet.Visibility != WorksheetVisibility.Visible;
            result.Add(CreateWorksheet(
                file,
                rule.Target,
                targetExtension,
                targetPath,
                true,
                OperationStatus.Ready,
                hidden
                    ? "worksheet_hidden"
                    : "Готово к преобразованию.",
                targetRootPath,
                sourceRootPath,
                sheet.Name,
                sheet.Visibility,
                worksheetIsEmpty: false,
                defaultSelected: !hidden,
                relativeOutput));
        }

        return result;
    }

    private async Task<ScannedFile> EnsureWorksheetMetadataAsync(ScannedFile file, CancellationToken cancellationToken)
    {
        if (file.Worksheets is not null
            || !string.IsNullOrWhiteSpace(file.WorksheetInspectionErrorCode)
            || !_excelInspector.IsAvailable)
        {
            return file;
        }

        var info = new FileInfo(file.SourcePath);
        var cacheKey = $"{file.SourcePath}\0{info.Length}\0{info.LastWriteTimeUtc.Ticks}";
        if (!_worksheetCache.TryGetValue(cacheKey, out var inspection))
        {
            try
            {
                inspection = await _excelInspector.InspectAsync(file.SourcePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or InvalidOperationException)
            {
                inspection = new ExcelWorkbookInspectionResult(
                    false,
                    [],
                    "worksheet_inspection_failure");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (inspection.Success) _worksheetCache[cacheKey] = inspection;
        }

        return inspection.Success
            ? file with { Worksheets = inspection.Worksheets }
            : file with { WorksheetInspectionErrorCode = string.IsNullOrEmpty(inspection.ErrorCode) ? "worksheet_inspection_failure" : inspection.ErrorCode };
    }

    private PlannedOperation CreateOperation(
        ScannedFile file,
        string sourceRootPath,
        string targetRootPath,
        ConversionRule rule)
    {
        if (rule.Target == ConversionTarget.Skip)
        {
            return Create(
                file,
                rule.Target,
                string.Empty,
                string.Empty,
                false,
                OperationStatus.Skipped,
                "Файл не будет изменён.",
                targetRootPath,
                sourceRootPath);
        }

        if (!FormatCapabilityCatalog.Get(file.Format).Supports(rule.Target))
        {
            return Create(
                file,
                rule.Target,
                rule.Target.ToExtension(),
                string.Empty,
                false,
                OperationStatus.Unsupported,
                "Выбранное преобразование не поддерживается.",
                targetRootPath,
                sourceRootPath);
        }

        var targetExtension = rule.Target == ConversionTarget.Copy
            ? Path.GetExtension(file.RelativePath)
            : rule.Target.ToExtension();
        if (!OutputPathGuard.TryBuildTargetPath(
                sourceRootPath,
                targetRootPath,
                file.SourcePath,
                file.RelativePath,
                targetExtension,
                out var targetPath))
        {
            return Create(
                file,
                rule.Target,
                targetExtension,
                string.Empty,
                false,
                OperationStatus.Failed,
                "Недопустимый путь результата.",
                targetRootPath,
                sourceRootPath);
        }

        var adapter = _adapterResolver.Resolve(file.Format, rule.Target);
        var adapterAvailable = adapter?.IsAvailable == true;

        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            return Create(
                file,
                rule.Target,
                targetExtension,
                targetPath,
                adapterAvailable,
                OperationStatus.Conflict,
                "Файл результата уже существует.",
                targetRootPath,
                sourceRootPath);
        }

        if (adapter is null)
        {
            return Create(
                file,
                rule.Target,
                targetExtension,
                targetPath,
                false,
                OperationStatus.Unsupported,
                "Выбранное преобразование не поддерживается.",
                targetRootPath,
                sourceRootPath);
        }

        if (!adapterAvailable)
        {
            return Create(
                file,
                rule.Target,
                targetExtension,
                targetPath,
                false,
                OperationStatus.EngineUnavailable,
                adapter.AvailabilityMessage,
                targetRootPath,
                sourceRootPath);
        }

        return Create(
            file,
            rule.Target,
            targetExtension,
            targetPath,
            true,
            OperationStatus.Ready,
            rule.Target == ConversionTarget.Copy
                ? "Будет скопирован без изменений."
                : "Готово к преобразованию.",
            targetRootPath,
            sourceRootPath);
    }

    private static PlannedOperation Create(
        ScannedFile file,
        ConversionTarget target,
        string targetExtension,
        string targetPath,
        bool adapterAvailable,
        OperationStatus status,
        string message,
        string outputRootPath,
        string sourceRootPath) =>
        new(
            file.SourcePath,
            file.RelativePath,
            file.Format,
            target,
            targetExtension,
            targetPath,
            adapterAvailable,
            status,
            message,
            outputRootPath,
            sourceRootPath,
            file.SizeBytes);

    private static PlannedOperation CreateWorksheet(
        ScannedFile file,
        ConversionTarget target,
        string targetExtension,
        string targetPath,
        bool adapterAvailable,
        OperationStatus status,
        string message,
        string outputRootPath,
        string sourceRootPath,
        string worksheetName,
        WorksheetVisibility worksheetVisibility,
        bool worksheetIsEmpty,
        bool defaultSelected,
        string resultRelativePath) =>
        new(
            file.SourcePath,
            file.RelativePath,
            file.Format,
            target,
            targetExtension,
            targetPath,
            adapterAvailable,
            status,
            message,
            outputRootPath,
            sourceRootPath,
            file.SizeBytes,
            worksheetName,
            worksheetVisibility,
            worksheetIsEmpty,
            defaultSelected,
            resultRelativePath);
}

using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.Core.Services;

public sealed class ConversionProcessor : IConversionProcessor
{
    private readonly IConversionAdapterResolver _adapterResolver;
    private readonly IConversionQualityEvaluator? _qualityEvaluator;

    public ConversionProcessor(
        IConversionAdapterResolver adapterResolver,
        IConversionQualityEvaluator? qualityEvaluator = null)
    {
        _adapterResolver = adapterResolver ?? throw new ArgumentNullException(nameof(adapterResolver));
        _qualityEvaluator = qualityEvaluator;
    }
    public async Task<ConversionSummary> ProcessAsync(
        IReadOnlyList<PlannedOperation> operations,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var results = new List<ConversionResult>(operations.Count);
        var readyTotal = operations.Count(operation => operation.Status == OperationStatus.Ready);
        var completedReady = 0;
        var batchLifecycle = _adapterResolver as IConversionBatchLifecycle;

        if (batchLifecycle is not null)
        {
            await batchLifecycle.BeginBatchAsync(cancellationToken);
        }

        try
        {
            foreach (var operation in operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operation.Status != OperationStatus.Ready)
                {
                    results.Add(new ConversionResult(operation, operation.Status, operation.Message));
                    continue;
                }

                progress?.Report(new ConversionProgress(
                    completedReady,
                    readyTotal,
                    operation.RelativePath,
                    OperationStatus.Converting,
                    OperationPercent: null, WorksheetName: operation.WorksheetName));
                int? operationPercent = null;

                ConversionResult result;
                var adapter = _adapterResolver.Resolve(operation.SourceFormat, operation.Target);
                if (adapter?.IsAvailable != true)
                {
                    result = new ConversionResult(
                        operation,
                        adapter is null
                            ? OperationStatus.Unsupported
                            : OperationStatus.EngineUnavailable,
                        adapter?.AvailabilityMessage ?? "Преобразование недоступно.");
                }
                else
                {
                    try
                    {
                        var stageProgress = new InlineProgress<int>(percent =>
                        {
                            var next = Math.Clamp(percent, operationPercent ?? 0, 99);
                            if (operationPercent.HasValue && next == operationPercent.Value)
                                return;

                            operationPercent = next;
                            progress?.Report(new ConversionProgress(
                                completedReady,
                                readyTotal,
                                operation.RelativePath,
                                OperationStatus.Converting,
                                OperationPercent: operationPercent, WorksheetName: operation.WorksheetName));
                        });
                        result = await adapter.ConvertAsync(
                            operation,
                            stageProgress,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        var cancelled = new ConversionResult(
                            operation,
                            OperationStatus.Cancelled,
                            "Отменено пользователем.");
                        progress?.Report(new ConversionProgress(
                            completedReady,
                            readyTotal,
                            operation.RelativePath,
                            OperationStatus.Cancelled,
                            cancelled,
                            operationPercent, operation.WorksheetName));
                        throw;
                    }
                    catch
                    {
                        result = new ConversionResult(
                            operation,
                            OperationStatus.Failed,
                            "Не удалось обработать файл.",
                            new ConversionDiagnostic("unexpected_adapter_failure"));
                    }
                }

                if (result.Status == OperationStatus.Succeeded
                    && result.Operation.Target == ConversionTarget.Markdown
                    && _qualityEvaluator is not null)
                {
                    try
                    {
                        var quality = await _qualityEvaluator.EvaluateAsync(
                            result.Operation,
                            cancellationToken).ConfigureAwait(false);
                        result = result with { Quality = quality };
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        result = result with
                        {
                            Quality = EmbeddedConversionQualityEvaluator.InternalError(
                                "NOT_EVALUATED_PRODUCT_INTEGRATION")
                        };
                    }
                }

                results.Add(result);
                completedReady++;
                progress?.Report(new ConversionProgress(
                    completedReady,
                    readyTotal,
                    operation.RelativePath,
                    result.Status,
                    result,
                    result.Status == OperationStatus.Succeeded ? 100 : operationPercent, operation.WorksheetName));
            }
        }
        finally
        {
            if (batchLifecycle is not null)
            {
                await batchLifecycle.EndBatchAsync();
            }
        }

        return new ConversionSummary(
            results.Count(result => result.Status == OperationStatus.Succeeded),
            results.Count(result => result.Status == OperationStatus.Conflict),
            results.Count(result => result.Status == OperationStatus.Failed),
            results.Count(result => result.Status == OperationStatus.Skipped),
            results.Count(result => result.Status == OperationStatus.EngineUnavailable),
            results.Count(result => result.Status == OperationStatus.Unsupported),
            results);
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}

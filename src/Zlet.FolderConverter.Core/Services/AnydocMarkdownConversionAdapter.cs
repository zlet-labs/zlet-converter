using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.Core.Services;

public sealed class AnydocMarkdownConversionAdapter : IConversionAdapter
{
    private readonly IAnydocWorkerRunner _workerRunner;
    private readonly SafeFileOperationExecutor _executor;

    public AnydocMarkdownConversionAdapter(
        IAnydocWorkerRunner workerRunner,
        IOutputResultValidator validator,
        string? temporaryRoot = null)
    {
        _workerRunner = workerRunner;
        _executor = new SafeFileOperationExecutor(validator, temporaryRoot);
    }

    public bool IsAvailable => _workerRunner.IsAvailable;

    public string AvailabilityMessage => _workerRunner.AvailabilityMessage;

    public bool CanConvert(SourceFormat sourceFormat, ConversionTarget target) =>
        target == ConversionTarget.Markdown
        && sourceFormat is SourceFormat.Doc
            or SourceFormat.Docx
            or SourceFormat.Xls
            or SourceFormat.Xlsx
            or SourceFormat.Ppt
            or SourceFormat.Pptx
            or SourceFormat.Pdf;

    public Task<ConversionResult> ConvertAsync(
        PlannedOperation operation,
        CancellationToken cancellationToken) =>
        ConvertAsync(operation, progress: null, cancellationToken);

    public Task<ConversionResult> ConvertAsync(
        PlannedOperation operation,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        if (!CanConvert(operation.SourceFormat, operation.Target))
        {
            return Task.FromResult(new ConversionResult(
                operation,
                OperationStatus.Unsupported,
                "Выбранное преобразование не поддерживается.",
                new ConversionDiagnostic("markdown_mapping_unsupported")));
        }

        if (!IsAvailable)
        {
            return Task.FromResult(new ConversionResult(
                operation,
                OperationStatus.EngineUnavailable,
                AvailabilityMessage,
                new ConversionDiagnostic("anydoc_worker_missing")));
        }

        return _executor.ExecuteAsync(
            operation,
            operation.Target,
            async (temporaryOutput, token) =>
            {
                var assetDir = $"{Path.GetFileNameWithoutExtension(operation.TargetPath)}_assets";
                var request = new AnydocWorkerRequest(
                    Guid.NewGuid().ToString("N"),
                    operation.SourcePath,
                    temporaryOutput,
                    operation.SourceFormat,
                    AssetDir: assetDir);

                var workerResult = await _workerRunner.RunAsync(request, token);
                return workerResult.Success
                    ? new TemporaryOutputProductionResult(true)
                    : new TemporaryOutputProductionResult(
                        false,
                        workerResult.ErrorCode,
                        ToUserMessage(workerResult),
                        workerResult.TimedOut,
                        workerResult.ExitCode,
                        workerResult.HasStandardOutput,
                        workerResult.HasStandardError);
            },
            "Преобразовано.",
            progress,
            cancellationToken);
    }

    private static string ToUserMessage(AnydocWorkerExecutionResult result) =>
        result.ErrorCode switch
        {
            "pdf_specialist_required" =>
                "PDF не содержит извлекаемого текста (возможно, отсканированный документ). Требуется оптическое распознавание текста (OCR).",
            "document_encrypted" =>
                "Документ зашифрован или защищен паролем.",
            "resource_limit" =>
                "Документ превысил допустимые лимиты ресурсов при обработке.",
            "malformed_document" =>
                "Структура документа повреждена или некорректна.",
            "unsupported_format" =>
                "Формат документа не поддерживается для преобразования в Markdown.",
            "anydoc_worker_timeout" when result.TimedOut =>
                "Преобразование превысило допустимое время.",
            "anydoc_worker_missing" =>
                "Компонент Markdown недоступен.",
            "anydoc_worker_start_failure" =>
                "Не удалось запустить процесс Markdown.",
            "anydoc_version_incompatible" =>
                "Версия компонента Markdown несовместима с приложением.",
            "anydoc_worker_missing_response" =>
                "Процесс Markdown завершился без ответа.",
            "anydoc_protocol_error" =>
                "Ошибка протокола взаимодействия с компонентом Markdown.",
            "anydoc_worker_failure" =>
                "Процесс Markdown сообщил о внутренней ошибке.",
            "read_error" =>
                "Не удалось прочитать исходный документ.",
            "write_error" =>
                "Не удалось записать файл результата Markdown.",
            "asset_export_error" =>
                "Не удалось извлечь встроенные изображения документа.",
            "source_not_found" =>
                "Исходный документ не найден.",
            "conversion_failed" =>
                "Не удалось преобразовать документ в Markdown.",
            _ => "Не удалось преобразовать документ в Markdown."
        };
}

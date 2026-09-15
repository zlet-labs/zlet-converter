using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.App.Localization;

public static class OperationMessageLocalizer
{
    public static bool IsKnownErrorCode(string code) => ErrorCodeKeys.ContainsKey(code);

    public static string ForReport(PlannedOperation operation, string? errorCode, LocalizationService localization)
    {
        if (KnownMessageKeys.TryGetValue(operation.Message, out var key)
            || ErrorCodeKeys.TryGetValue(operation.Message, out key)) return localization.Get(key);
        if (errorCode is not null && ErrorCodeKeys.TryGetValue(errorCode, out key)) return localization.Get(key);
        return localization.Get(operation.Status switch
        {
            OperationStatus.Failed => "OperationProcessingFailed",
            OperationStatus.Conflict => "OperationTargetExists",
            OperationStatus.Unsupported => "OperationUnsupported",
            OperationStatus.EngineUnavailable => "OperationUnavailable",
            OperationStatus.Skipped => "OperationSkipped",
            OperationStatus.Cancelled => "OperationCancelled",
            OperationStatus.NotProcessed => "OperationNotProcessed",
            OperationStatus.Succeeded when operation.Target == ConversionTarget.Copy => "StatusCopied",
            OperationStatus.Succeeded => "StatusConverted",
            _ => "StatusNotSelected"
        });
    }
    private static readonly IReadOnlyDictionary<string, string> ErrorCodeKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["legacy_ppt_table_semantics_partial"] = "LegacyPptTableSemanticsPartial",
            ["worksheet_inspection_failure"] = "WorksheetInspectionFailed",
            ["worksheet_none"] = "WorksheetNone",
            ["worksheet_empty"] = "WorksheetEmpty",
            ["worksheet_hidden"] = "WorksheetHidden",
            ["excel_sheet_operation_failure"] = "OperationOfficeFailure",
            ["unsafe_source"] = "OperationUnsafeSource",
            ["unsafe_target"] = "OperationInvalidTarget",
            ["target_directory_missing"] = "OperationInvalidTarget",
            ["unsafe_target_after_create"] = "OperationInvalidTarget",
            ["target_conflict"] = "OperationTargetExists",
            ["source_unreadable"] = "OperationSourceUnreadable",
            ["output_missing"] = "OperationOutputMissing",
            ["output_empty"] = "OperationOutputInvalid",
            ["output_extension_invalid"] = "OperationOutputExtensionInvalid",
            ["ooxml_structure_invalid"] = "OperationOutputInvalid",
            ["pdf_signature_invalid"] = "OperationOutputInvalid",
            ["unsupported_output_validation"] = "OperationOutputInvalid",
            ["output_unreadable"] = "OperationOutputInvalid",
            ["source_changed"] = "OperationSourceChanged",
            ["io_failure"] = "OperationProcessingFailed",
            ["unexpected_adapter_failure"] = "OperationProcessingFailed",
            ["invalid_json"] = "OperationInvalidJson",
            ["copy_mapping_unsupported"] = "OperationCopyUnsupported",
            ["copy_integrity_mismatch"] = "OperationOutputInvalid",
            ["office_mapping_unsupported"] = "OperationUnsupported",
            ["office_application_missing"] = "OperationOfficeMissing",
            ["worker_missing"] = "OperationOfficeComponentUnavailable",
            ["worker_start_failure"] = "OperationOfficeComponentUnavailable",
            ["worker_timeout"] = "OperationTimeout",
            ["worker_protocol_failure"] = "OperationOfficeFailure",
            ["worker_protocol_invalid"] = "OperationOfficeFailure",
            ["worker_result_missing"] = "OperationOfficeFailure",
            ["powerpoint_already_running"] = "OperationPowerPointRunning",
            ["powerpoint_session_ownership_lost"] = "OperationPowerPointProtected",
            ["office_com_failure"] = "OperationOfficeFailure",
            ["scanned_pdf_unsupported"] = "ScannedPdfUnsupported",
            ["pdf_specialist_required"] = "ScannedPdfUnsupported",
            ["docling_worker_missing"] = "DoclingComponentUnavailable",
            ["anydoc_worker_missing"] = "DoclingComponentUnavailable",
            ["docling_version_incompatible"] = "DoclingVersionIncompatible",
            ["anydoc_version_incompatible"] = "DoclingVersionIncompatible",
            ["docling_worker_timeout"] = "OperationTimeout",
            ["anydoc_worker_timeout"] = "OperationTimeout",
            ["docling_conversion_failed"] = "DoclingConversionFailed",
            ["anydoc_worker_failure"] = "DoclingConversionFailed",
            ["output_invalid_utf8"] = "OperationOutputInvalid",
            ["markdown_mapping_unsupported"] = "OperationUnsupported",
            ["intermediate_output_missing"] = "OperationOutputMissing",
            ["intermediate_file_invalid"] = "OperationOutputInvalid",
            ["document_encrypted"] = "DocumentEncrypted",
            ["resource_limit"] = "DocumentResourceLimit",
            ["malformed_document"] = "MalformedDocument",
            ["read_error"] = "DocumentReadError",
            ["asset_export_error"] = "AssetExportError",
            ["anydoc_worker_start_failure"] = "AnydocWorkerStartFailure",
            ["anydoc_protocol_error"] = "AnydocProtocolError",
            ["anydoc_worker_missing_response"] = "AnydocWorkerMissingResponse",
            ["text_encoding_unsupported"] = "TextEncodingUnsupported",
            ["companion_assets_invalid"] = "CompanionAssetsInvalid",
            ["unsupported_format"] = "DocumentUnsupportedFormat",
            ["source_not_found"] = "DocumentSourceNotFound",
            ["write_error"] = "DocumentWriteError",
            ["conversion_failed"] = "DoclingConversionFailed",
            ["invalid_request"] = "AnydocProtocolError"
        };

    private static readonly IReadOnlyDictionary<string, string> KnownMessageKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Преобразовано с ограничением: в старых PPT структура таблиц может быть упрощена."] = "LegacyPptTableSemanticsPartial",
            ["Converted with a limitation: table structure in legacy PPT files may be simplified."] = "LegacyPptTableSemanticsPartial",
            ["Файл не будет изменён."] = "OperationSkipped",
            ["Выбранное преобразование не поддерживается."] = "OperationUnsupported",
            ["Недопустимый путь результата."] = "OperationInvalidTarget",
            ["Файл результата уже существует."] = "OperationTargetExists",
            ["Будет скопирован без изменений."] = "OperationReadyCopy",
            ["Готово к преобразованию."] = "OperationReady",
            ["Преобразование недоступно."] = "OperationUnavailable",
            ["Отменено пользователем."] = "OperationCancelled",
            ["Не удалось обработать файл."] = "OperationProcessingFailed",
            ["Копирование этого формата не поддерживается."] = "OperationCopyUnsupported",
            ["Исходный файл небезопасен или находится вне выбранной папки."] = "OperationUnsafeSource",
            ["Не удалось открыть исходный файл."] = "OperationSourceUnreadable",
            ["Приложение не создало ожидаемый результат."] = "OperationOutputMissing",
            ["Расширение результата не прошло проверку."] = "OperationOutputExtensionInvalid",
            ["Формат результата не прошёл проверку."] = "OperationOutputInvalid",
            ["Исходный файл изменился во время обработки."] = "OperationSourceChanged",
            ["Преобразование превысило допустимое время."] = "OperationTimeout",
            ["Не удалось преобразовать файл в Microsoft Office."] = "OperationOfficeFailure",
            ["Компонент преобразования Microsoft Office недоступен."] = "OperationOfficeComponentUnavailable",
            ["PowerPoint уже запущен. Закройте его и повторите преобразование."] = "OperationPowerPointRunning",
            ["PowerPoint не запустился."] = "OperationPowerPointStartFailed",
            ["PowerPoint не запустился. Откройте PowerPoint вручную и повторите."] = "OperationPowerPointStartFailedAdvice",
            ["PDF не содержит извлекаемого текста (возможно, отсканированный документ). Оптическое распознавание текста (OCR) не поддерживается."] = "ScannedPdfUnsupported",
            ["PDF не содержит извлекаемого текста (возможно, отсканированный документ). Требуется оптическое распознавание текста (OCR)."] = "ScannedPdfUnsupported",
            ["Компонент Markdown недоступен."] = "DoclingComponentUnavailable",
            ["Компонент Markdown недоступен (исполняемый файл zlet-anydoc-worker.exe не найден)."] = "DoclingComponentUnavailable",
            ["Компонент преобразования Docling недоступен."] = "DoclingComponentUnavailable",
            ["Версия компонента Markdown несовместима с текущим приложением."] = "DoclingVersionIncompatible",
            ["Версия компонента Markdown несовместима с приложением."] = "DoclingVersionIncompatible",
            ["Не удалось преобразовать документ в Markdown."] = "DoclingConversionFailed",
            ["Документ защищён паролем или зашифрован."] = "DocumentEncrypted",
            ["Документ зашифрован или защищен паролем."] = "DocumentEncrypted",
            ["Документ зашифрован или защищён паролем."] = "DocumentEncrypted",
            ["The document is password-protected or encrypted."] = "DocumentEncrypted",
            ["Превышен лимит ресурсов при обработке документа."] = "DocumentResourceLimit",
            ["Документ превысил допустимые лимиты ресурсов при обработке."] = "DocumentResourceLimit",
            ["Resource limit exceeded while processing document."] = "DocumentResourceLimit",
            ["Структура документа повреждена или некорректна."] = "MalformedDocument",
            ["Document structure is corrupted or invalid."] = "MalformedDocument",
            ["Не удалось прочитать содержимое документа."] = "DocumentReadError",
            ["Не удалось прочитать исходный документ."] = "DocumentReadError",
            ["Failed to read document contents."] = "DocumentReadError",
            ["Не удалось экспортировать встроенные ресурсы документа."] = "AssetExportError",
            ["Не удалось извлечь встроенные изображения документа."] = "AssetExportError",
            ["Failed to export embedded document assets."] = "AssetExportError",
            ["Не удалось запустить процесс Markdown."] = "AnydocWorkerStartFailure",
            ["Failed to launch Markdown worker process."] = "AnydocWorkerStartFailure",
            ["Некорректный ответ процесса Markdown."] = "AnydocProtocolError",
            ["Ошибка протокола взаимодействия с компонентом Markdown."] = "AnydocProtocolError",
            ["Invalid response from Markdown worker process."] = "AnydocProtocolError",
            ["Процесс Markdown завершился без ответа."] = "AnydocWorkerMissingResponse",
            ["Markdown worker process terminated without response."] = "AnydocWorkerMissingResponse",
            ["Процесс Markdown сообщил о внутренней ошибке."] = "DoclingConversionFailed",
            ["Кодировка текстового файла не поддерживается."] = "TextEncodingUnsupported",
            ["Text file encoding is not supported."] = "TextEncodingUnsupported",
            ["Формат ресурсов результата не прошёл проверку."] = "CompanionAssetsInvalid",
            ["Result companion assets failed validation."] = "CompanionAssetsInvalid",
            ["Формат документа не поддерживается для преобразования в Markdown."] = "DocumentUnsupportedFormat",
            ["Document format is not supported for Markdown conversion."] = "DocumentUnsupportedFormat",
            ["Исходный документ не найден."] = "DocumentSourceNotFound",
            ["Source document was not found."] = "DocumentSourceNotFound",
            ["Не удалось записать файл результата Markdown."] = "DocumentWriteError",
            ["Failed to write Markdown result file."] = "DocumentWriteError"
        };

    public static string Localize(
        OperationStatus status,
        ConversionTarget target,
        string? message,
        string? errorCode = null,
        LocalizationService? localization = null)
    {
        localization ??= LocalizationService.Current;
        if (message is not null && ErrorCodeKeys.TryGetValue(message, out var codeKey)) return localization.Get(codeKey);
        if (!string.IsNullOrWhiteSpace(message) && KnownMessageKeys.TryGetValue(message, out var messageKey))
            return localization.Get(messageKey);
        if (!string.IsNullOrWhiteSpace(errorCode) && ErrorCodeKeys.TryGetValue(errorCode, out var errorKey))
            return localization.Get(errorKey);

        if (string.IsNullOrWhiteSpace(message))
        {
            return status switch
            {
                OperationStatus.Ready when target == ConversionTarget.Copy => localization.Get("OperationReadyCopy"),
                OperationStatus.Ready => localization.Get("OperationReady"),
                OperationStatus.Converting => localization.Get("OperationExecuting"),
                OperationStatus.Skipped => localization.Get("OperationSkipped"),
                OperationStatus.Cancelled => localization.Get("OperationCancelled"),
                OperationStatus.NotProcessed => localization.Get("OperationNotProcessed"),
                _ => string.Empty
            };
        }

        // Unknown diagnostics remain verbatim so troubleshooting meaning is never hidden.
        return message;
    }
}

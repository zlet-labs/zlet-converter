using System.Text;
using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.Core.Services;

public sealed class TxtMarkdownConversionAdapter : IConversionAdapter
{
    private readonly SafeFileOperationExecutor _executor;

    public TxtMarkdownConversionAdapter(
        IOutputResultValidator validator,
        string? temporaryRoot = null)
    {
        _executor = new SafeFileOperationExecutor(validator, temporaryRoot);
    }

    public bool IsAvailable => true;

    public string AvailabilityMessage => "Преобразование текста доступно.";

    public bool CanConvert(SourceFormat sourceFormat, ConversionTarget target) =>
        target == ConversionTarget.Markdown && sourceFormat == SourceFormat.Txt;

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

        return _executor.ExecuteAsync(
            operation,
            operation.Target,
            async (temporaryOutput, token) =>
            {
                var (success, text) = await TryReadTextAsync(operation.SourcePath, token);
                if (!success)
                {
                    return new TemporaryOutputProductionResult(
                        false,
                        ErrorCode: "text_encoding_unsupported",
                        UserMessage: "Кодировка текстового файла не поддерживается.");
                }
                var normalized = text!.Replace("\r\n", "\n").Replace('\r', '\n');
                await File.WriteAllTextAsync(temporaryOutput, normalized, new UTF8Encoding(false), token);
                return new TemporaryOutputProductionResult(true);
            },
            "Преобразовано.",
            progress,
            cancellationToken);
    }

    private static async Task<(bool Success, string? Text)> TryReadTextAsync(string path, CancellationToken token)
    {
        var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new StreamReader(stream, utf8Strict, detectEncodingFromByteOrderMarks: true);
            var text = await reader.ReadToEndAsync(token);
            return (true, text);
        }
        catch (DecoderFallbackException)
        {
            return (false, null);
        }
    }
}

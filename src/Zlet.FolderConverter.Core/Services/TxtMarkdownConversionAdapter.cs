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
                var text = await ReadTextAsync(operation.SourcePath, token);
                var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
                await File.WriteAllTextAsync(temporaryOutput, normalized, new UTF8Encoding(false), token);
                return new TemporaryOutputProductionResult(true);
            },
            "Преобразовано.",
            progress,
            cancellationToken);
    }

    private static async Task<string> ReadTextAsync(string path, CancellationToken token)
    {
        var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new StreamReader(stream, utf8Strict, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(token);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var win1251 = Encoding.GetEncoding(1251);
            using var stream = File.OpenRead(path);
            using var reader = new StreamReader(stream, win1251, detectEncodingFromByteOrderMarks: false);
            return await reader.ReadToEndAsync(token);
        }
    }
}

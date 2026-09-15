using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.Core.Services;

public sealed class DefaultConversionAdapterResolver
    : IConversionAdapterResolver, IConversionBatchLifecycle
{
    private readonly IReadOnlyList<IConversionAdapter> _adapters;
    private readonly IMicrosoftOfficeWorkerRunner? _officeWorkerRunner;
    private readonly IAnydocWorkerRunner? _anydocWorkerRunner;

    public DefaultConversionAdapterResolver()
        : this(
            new MicrosoftOfficeCapabilityDetector(),
            new MicrosoftOfficeWorkerProcessRunner(),
            new AnydocWorkerProcessRunner())
    {
    }

    public DefaultConversionAdapterResolver(
        IMicrosoftOfficeCapabilityDetector capabilityDetector,
        IMicrosoftOfficeWorkerRunner workerRunner)
        : this(capabilityDetector, workerRunner, new AnydocWorkerProcessRunner())
    {
    }

    public DefaultConversionAdapterResolver(
        IMicrosoftOfficeCapabilityDetector capabilityDetector,
        IMicrosoftOfficeWorkerRunner officeWorkerRunner,
        IAnydocWorkerRunner anydocWorkerRunner)
        : this(CreateDefaultAdapters(capabilityDetector, officeWorkerRunner, anydocWorkerRunner))
    {
        _officeWorkerRunner = officeWorkerRunner;
        _anydocWorkerRunner = anydocWorkerRunner;
    }

    public DefaultConversionAdapterResolver(IEnumerable<IConversionAdapter> adapters)
    {
        _adapters = adapters.ToArray();
    }

    public IConversionAdapter? Resolve(SourceFormat sourceFormat, ConversionTarget target) =>
        _adapters.FirstOrDefault(adapter => adapter.CanConvert(sourceFormat, target));

    async Task IConversionBatchLifecycle.BeginBatchAsync(CancellationToken cancellationToken)
    {
        if (_officeWorkerRunner is not null)
        {
            await _officeWorkerRunner.BeginBatchAsync(cancellationToken);
        }
        if (_anydocWorkerRunner is not null)
        {
            await _anydocWorkerRunner.BeginBatchAsync(cancellationToken);
        }
    }

    async Task IConversionBatchLifecycle.EndBatchAsync()
    {
        try
        {
            if (_officeWorkerRunner is not null)
            {
                await _officeWorkerRunner.EndBatchAsync();
            }
        }
        finally
        {
            if (_anydocWorkerRunner is not null)
            {
                await _anydocWorkerRunner.EndBatchAsync();
            }
        }
    }

    private static IConversionAdapter[] CreateDefaultAdapters(
        IMicrosoftOfficeCapabilityDetector capabilityDetector,
        IMicrosoftOfficeWorkerRunner officeWorkerRunner,
        IAnydocWorkerRunner anydocWorkerRunner)
    {
        var validator = new OutputResultValidator();
        return
        [
            new JsonConversionAdapter(validator),
            new SafeFileCopyAdapter(validator),
            new MicrosoftOfficeConversionAdapter(
                OfficeApplicationKind.Word,
                capabilityDetector,
                officeWorkerRunner,
                validator,
                temporaryRoot: null),
            new MicrosoftOfficeConversionAdapter(
                OfficeApplicationKind.Excel,
                capabilityDetector,
                officeWorkerRunner,
                validator,
                temporaryRoot: null),
            new MicrosoftOfficeConversionAdapter(
                OfficeApplicationKind.PowerPoint,
                capabilityDetector,
                officeWorkerRunner,
                validator,
                temporaryRoot: null),
            new TxtMarkdownConversionAdapter(validator),
            new AnydocMarkdownConversionAdapter(
                anydocWorkerRunner,
                validator,
                temporaryRoot: null)
        ];
    }
}

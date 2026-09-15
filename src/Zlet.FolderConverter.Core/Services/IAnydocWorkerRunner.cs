using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.Core.Services;

public interface IAnydocWorkerRunner
{
    bool IsAvailable { get; }

    string AvailabilityMessage { get; }

    Task BeginBatchAsync(CancellationToken cancellationToken);

    Task EndBatchAsync();

    Task<AnydocWorkerExecutionResult> RunAsync(
        AnydocWorkerRequest request,
        CancellationToken cancellationToken);
}

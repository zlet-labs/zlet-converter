using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.Core.Services;

public interface IConversionQualityEvaluator
{
    Task<ConversionQualityAssessment> EvaluateAsync(
        PlannedOperation operation,
        CancellationToken cancellationToken);
}

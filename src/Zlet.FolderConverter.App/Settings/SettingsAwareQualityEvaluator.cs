using Zlet.FolderConverter.Core.Models;
using Zlet.FolderConverter.Core.Services;

namespace Zlet.FolderConverter.App.Settings;

public sealed class SettingsAwareQualityEvaluator : IConversionQualityEvaluator
{
    private readonly IConversionQualityEvaluator _inner;

    public SettingsAwareQualityEvaluator(IConversionQualityEvaluator? inner = null) =>
        _inner = inner ?? new EmbeddedConversionQualityEvaluator();

    public Task<ConversionQualityAssessment> EvaluateAsync(PlannedOperation operation, CancellationToken cancellationToken)
    {
        if (QualityPolicyRuntime.Current.Enabled)
            return _inner.EvaluateAsync(operation, cancellationToken);

        return Task.FromResult(new ConversionQualityAssessment(
            EmbeddedConversionQualityEvaluator.QualificationProfileId,
            "NOT_EVALUATED",
            "DISABLED_BY_USER",
            "NOT_EVALUATED",
            "NOT_EVALUATED",
            "NOT_EVALUATED",
            null, [], []));
    }
}

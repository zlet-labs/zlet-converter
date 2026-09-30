using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.App.Settings;

public static class QualityPolicyRuntime
{
    private static QualityCheckPolicy _current = new AppSettingsStore().LoadQualityCheckPolicy();
    public static QualityCheckPolicy Current => _current;
    public static void Apply(QualityCheckPolicy policy) => _current = policy.Normalize();
    public static void Reload() => _current = new AppSettingsStore().LoadQualityCheckPolicy();
}

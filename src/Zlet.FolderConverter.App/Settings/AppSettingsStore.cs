using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using Zlet.FolderConverter.App.Localization;
using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.App.Settings;

public sealed record SettingsSaveResult(bool Success, string? ErrorCode = null)
{
    public static SettingsSaveResult Saved { get; } = new(true);
}

public sealed class AppSettingsStore
{
    public AppSettingsStore(string? path = null) => SettingsPath = path ?? DefaultPath;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Zlet Labs", "Zlet Converter", "settings.json");

    public string SettingsPath { get; }

    public SettingsSaveResult TryReset(bool confirmed)
    {
        if (!confirmed) return new(false, "reset_cancelled");
        try
        {
            // Delete only this store's file, including corrupt JSON; never traverse directories.
            File.Delete(SettingsPath);
            return SettingsSaveResult.Saved;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                        or ArgumentException or NotSupportedException)
        {
            return new(false, "settings_reset_failed");
        }
    }

    public string? LoadLanguage()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var root = JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject;
            var language = root?["language"]?.GetValue<string>();
            return AppLanguage.IsSupported(language) ? AppLanguage.Normalize(language!) : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public SettingsSaveResult TrySaveLanguage(string language)
    {
        if (!AppLanguage.IsSupported(language)) return new(false, "invalid_language");
        JsonObject root;
        try
        {
            if (File.Exists(SettingsPath))
            {
                if (JsonNode.Parse(File.ReadAllText(SettingsPath)) is not JsonObject existingRoot)
                    return new(false, "settings_corrupted");
                root = existingRoot;
            }
            else
            {
                root = new JsonObject();
            }
            root["language"] = AppLanguage.Normalize(language);
            var directory = Path.GetDirectoryName(SettingsPath);
            if (string.IsNullOrWhiteSpace(directory)) return new(false, "invalid_settings_path");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $"settings.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporaryPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporaryPath, SettingsPath, true);
                return SettingsSaveResult.Saved;
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (JsonException)
        {
            return new(false, "settings_corrupted");
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or ArgumentException
                                           or NotSupportedException)
        {
            return new(false, "settings_write_failed");
        }
    }

    public QualityCheckPolicy LoadQualityCheckPolicy()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return QualityCheckPolicy.Default;
            var root = JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject;
            var q = root?["qualityCheck"]?.Deserialize<QualityCheckPolicy>();
            return (q ?? QualityCheckPolicy.Default).Normalize();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { return QualityCheckPolicy.Default; }
    }

    public SettingsSaveResult TrySaveQualityCheckPolicy(QualityCheckPolicy policy)
    {
        try
        {
            var root = File.Exists(SettingsPath)
                ? JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject ?? new JsonObject()
                : new JsonObject();
            root["qualityCheck"] = JsonSerializer.SerializeToNode(policy.Normalize());
            var directory = Path.GetDirectoryName(SettingsPath);
            if (string.IsNullOrWhiteSpace(directory)) return new(false, "invalid_settings_path");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $"settings.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporaryPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporaryPath, SettingsPath, true);
                return SettingsSaveResult.Saved;
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new(false, "settings_write_failed"); }
    }

}

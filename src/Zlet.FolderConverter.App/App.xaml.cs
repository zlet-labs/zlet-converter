using System.Windows;
using Zlet.FolderConverter.App.Localization;
using Zlet.FolderConverter.App.Settings;

namespace Zlet.FolderConverter.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (HeadlessBatchCommand.IsRequested(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (!HeadlessBatchCommand.TryParse(e.Args, out var command, out _)
                || command is null)
            {
                Shutdown(HeadlessBatchCommand.ExitUsage);
                return;
            }

            try
            {
                var exitCode = Task.Run(() =>
                        HeadlessBatchRunner.CreateDefault().RunAsync(command, CancellationToken.None))
                    .GetAwaiter()
                    .GetResult();
                Shutdown(exitCode);
            }
            catch (HeadlessBatchConfigurationException)
            {
                Shutdown(HeadlessBatchCommand.ExitUsage);
            }
            catch (OperationCanceledException)
            {
                Shutdown(HeadlessBatchCommand.ExitCancelled);
            }
            catch
            {
                Shutdown(HeadlessBatchCommand.ExitFatal);
            }
            return;
        }

        var settings = new AppSettingsStore();
        var saved = settings.LoadLanguage();
        var explicitLanguage = ReadArgument(e.Args, "--language=");
        var bootstrapLanguage = ReadArgument(e.Args, "--bootstrap-language=");

        if (bootstrapLanguage is not null)
        {
            var result = saved is null && AppLanguage.IsSupported(bootstrapLanguage)
                ? settings.TrySaveLanguage(bootstrapLanguage)
                : SettingsSaveResult.Saved;
            Shutdown(result.Success ? 0 : 1);
            return;
        }

        var decision = StartupLanguageResolver.Resolve(explicitLanguage, saved);
        var language = decision.Language;
        var saveResult = SettingsSaveResult.Saved;
        if (decision.ChooserRequired)
        {
            var chooser = new LanguageChooserWindow();
            if (chooser.ShowDialog() != true || chooser.SelectedLanguage is null) { Shutdown(); return; }
            language = chooser.SelectedLanguage;
            saveResult = settings.TrySaveLanguage(language);
        }
        else if (decision.PersistExplicit) saveResult = settings.TrySaveLanguage(language!);

        LocalizationService.Current.Apply(language!);
        var mainWindow = new MainWindow();
        if (!saveResult.Success) mainWindow.ShowSettingsSaveFailure();
        MainWindow = mainWindow;
        MainWindow.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    internal static string? ReadArgument(IEnumerable<string> args, string prefix) =>
        args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
}

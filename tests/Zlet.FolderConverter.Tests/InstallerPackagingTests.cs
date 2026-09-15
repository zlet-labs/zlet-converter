namespace Zlet.FolderConverter.Tests;

public sealed class InstallerPackagingTests
{
    [Fact]
    public void Installer_is_win_x64_per_user_and_preserves_upgrade_identity()
    {
        var root = FindRepositoryRoot();
        var definitionPath = Path.Combine(root, "installer", "ZletBatchConverter.iss");
        var buildScriptPath = Path.Combine(root, "scripts", "build-installer.ps1");

        Assert.True(File.Exists(definitionPath));
        Assert.True(File.Exists(buildScriptPath));

        var definition = File.ReadAllText(definitionPath);
        var buildScript = File.ReadAllText(buildScriptPath);

        Assert.Contains("#define AppName \"Zlet Converter\"", definition);
        Assert.Contains("#define AppUrl \"https://github.com/zlet-labs/zlet-converter\"", definition);
        Assert.Contains("AppId={{B124EC99-C473-496E-B293-3FCA72E7CACD}", definition);
        Assert.Contains("ArchitecturesAllowed=x64compatible", definition);
        Assert.Contains("ArchitecturesInstallIn64BitMode=x64compatible", definition);
        Assert.Contains("PrivilegesRequired=lowest", definition);
        Assert.Contains(@"DefaultDirName={localappdata}\Programs\Zlet Converter", definition);
        Assert.Contains("Name: \"desktopicon\"", definition);
        Assert.Contains(@"{autoprograms}\Zlet Converter", definition);
        Assert.Contains("UninstallDisplayIcon={app}\\{#AppExeName}", definition);
        Assert.Contains("ZletConverter-v", definition);

        Assert.Contains(@"{app}\ZletBatchConverter.exe", definition);
        Assert.Contains(@"{autoprograms}\Zlet Batch Converter.lnk", definition);
        Assert.Contains(@"{autodesktop}\Zlet Batch Converter.lnk", definition);

        Assert.Contains("Get-ProjectProperty \"ZletProductVersion\"", buildScript);
        Assert.Contains("Get-ProjectProperty \"ZletPortableRuntimeIdentifier\"", buildScript);
        Assert.Contains("Get-ProjectProperty \"ZletExecutableName\"", buildScript);
        Assert.Contains("publish-portable.ps1", buildScript);
        Assert.Contains("ISCC.exe", buildScript);
        Assert.DoesNotContain("<ZletProductVersion>", definition);
    }

    [Fact]
    public void Packaging_scripts_require_anydoc_worker_executable()
    {
        var root = FindRepositoryRoot();
        var publishPortable = File.ReadAllText(Path.Combine(root, "scripts", "publish-portable.ps1"));
        var buildInstaller = File.ReadAllText(Path.Combine(root, "scripts", "build-installer.ps1"));

        Assert.Contains("zlet-anydoc-worker.exe", publishPortable);
        Assert.Contains("zlet-anydoc-worker.exe", buildInstaller);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FolderConverter.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

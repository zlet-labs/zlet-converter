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

    [Fact]
    public void Rust_notices_are_generated_from_locked_package_specific_sources()
    {
        var root = FindRepositoryRoot();
        var anydocLicense = Path.Combine(root, "licenses", "anydoc-MIT.txt");
        var rustDeps = Path.Combine(root, "licenses", "RUST_DEPENDENCIES.md");
        var cargoAboutConfig = Path.Combine(root, "licenses", "cargo-about.toml");
        var cargoAboutTemplate = Path.Combine(root, "licenses", "cargo-about.hbs");
        var obsoleteAggregate = Path.Combine(root, "licenses", "RUST_THIRD_PARTY_LICENSES.txt");
        var thirdPartyNotices = Path.Combine(root, "THIRD_PARTY_NOTICES.md");
        var publishPortablePath = Path.Combine(root, "scripts", "publish-portable.ps1");
        var ciPath = Path.Combine(root, ".github", "workflows", "ci.yml");

        Assert.True(File.Exists(anydocLicense), "licenses/anydoc-MIT.txt must exist.");
        Assert.True(File.Exists(rustDeps), "licenses/RUST_DEPENDENCIES.md must exist.");
        Assert.True(File.Exists(cargoAboutConfig), "licenses/cargo-about.toml must exist.");
        Assert.True(File.Exists(cargoAboutTemplate), "licenses/cargo-about.hbs must exist.");
        Assert.True(File.Exists(thirdPartyNotices), "THIRD_PARTY_NOTICES.md must exist.");
        Assert.False(File.Exists(obsoleteAggregate), "Generic license-family aggregate must not be shipped as dependency-specific evidence.");

        var licenseContent = File.ReadAllText(anydocLicense);
        Assert.Contains("Sideguide Technologies Inc.", licenseContent);
        Assert.Contains("MIT License", licenseContent);

        var rustDepsContent = File.ReadAllText(rustDeps);
        Assert.Contains("anydoc", rustDepsContent);
        Assert.Contains("42bf1c5ecdde9eb0d96d6bd75a9e6698cf93b14c", rustDepsContent);
        Assert.Contains("quick-xml", rustDepsContent);
        Assert.Contains("lopdf", rustDepsContent);
        Assert.Contains("zip", rustDepsContent);

        var configContent = File.ReadAllText(cargoAboutConfig);
        Assert.Contains("x86_64-pc-windows-msvc", configContent);
        Assert.Contains("ignore-transitive-dependencies = false", configContent);

        var templateContent = File.ReadAllText(cargoAboutTemplate);
        Assert.Contains("cargo-about 0.9.1", templateContent);
        Assert.Contains("{{crate.name}} {{crate.version}}", templateContent);
        Assert.Contains("{{text}}", templateContent);
        Assert.Contains("42bf1c5ecdde9eb0d96d6bd75a9e6698cf93b14c", templateContent);
        Assert.Contains("does not claim to synthesize notices", templateContent);

        var noticesContent = File.ReadAllText(thirdPartyNotices);
        Assert.Contains("anydoc", noticesContent);
        Assert.Contains("42bf1c5ecdde9eb0d96d6bd75a9e6698cf93b14c", noticesContent);
        Assert.Contains("licenses/anydoc-MIT.txt", noticesContent);
        Assert.Contains("licenses/RUST_THIRD_PARTY_NOTICES.txt", noticesContent);
        Assert.Contains("cargo-about 0.9.1", noticesContent);
        Assert.Contains("does not claim legal completeness", noticesContent);

        var publishPortable = File.ReadAllText(publishPortablePath);
        Assert.Contains("$cargoAboutVersion = \"0.9.1\"", publishPortable);
        Assert.Contains("cargo install cargo-about", publishPortable);
        Assert.Contains("cargo about generate", publishPortable);
        Assert.Contains("--locked", publishPortable);
        Assert.Contains("--fail", publishPortable);
        Assert.Contains("RUST_THIRD_PARTY_NOTICES.txt", publishPortable);
        Assert.Contains("anydoc 0.2.4", publishPortable);
        Assert.Contains("quick-xml 0.41.0", publishPortable);
        Assert.Contains("lopdf 0.45.0", publishPortable);
        Assert.Contains("zip 8.6.0", publishPortable);

        var ci = File.ReadAllText(ciPath);
        Assert.Contains("Verify portable package and Rust third-party notices", ci);
        Assert.Contains("scripts/publish-portable.ps1", ci);
    }

    [Fact]
    public void Repository_has_exact_rust_toolchain_pin_to_1_88_0()
    {
        var root = FindRepositoryRoot();
        var toolchainFile = Path.Combine(root, "rust-toolchain.toml");
        Assert.True(File.Exists(toolchainFile), "rust-toolchain.toml must exist.");

        var content = File.ReadAllText(toolchainFile);
        Assert.Contains("channel = \"1.88.0\"", content);
        Assert.Contains("profile = \"minimal\"", content);
    }

    [Fact]
    public void Readme_documents_cargo_build_before_dotnet_build()
    {
        var root = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        Assert.Contains("cargo build --manifest-path src/Zlet.FolderConverter.AnydocWorker/Cargo.toml --release --locked", readme);
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

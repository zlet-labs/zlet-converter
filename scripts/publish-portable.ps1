$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$appProject = Join-Path $repoRoot "src\Zlet.FolderConverter.App\Zlet.FolderConverter.App.csproj"
$workerProject = Join-Path $repoRoot "src\Zlet.FolderConverter.OfficeWorker\Zlet.FolderConverter.OfficeWorker.csproj"
$anydocCargoToml = Join-Path $repoRoot "src\Zlet.FolderConverter.AnydocWorker\Cargo.toml"
$readmePath = Join-Path $repoRoot "README_PORTABLE.txt"
$licensePath = Join-Path $repoRoot "LICENSE"
$noticesPath = Join-Path $repoRoot "THIRD_PARTY_NOTICES.md"
$licensesDirectory = Join-Path $repoRoot "licenses"
$cargoAboutConfig = Join-Path $licensesDirectory "cargo-about.toml"
$cargoAboutTemplate = Join-Path $licensesDirectory "cargo-about.hbs"
$cargoAboutVersion = "0.9.1"

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Get-ProjectProperty([string]$Name) {
    $output = & dotnet msbuild $appProject -nologo "-getProperty:$Name"
    if ($LASTEXITCODE -ne 0) {
        Fail "Unable to read MSBuild property '$Name'."
    }

    $value = $output | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_)
    } | Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($value)) {
        Fail "MSBuild property '$Name' is empty."
    }

    return $value.Trim()
}

function Assert-SafeArtifactPath([string]$Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $expectedRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $repoRoot "artifacts\portable"))
    if (-not $fullPath.StartsWith(
        $expectedRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail "Refusing to modify an unexpected artifact path."
    }
}

function Publish-Project([string]$ProjectPath, [string]$Destination) {
    # The solution-level restore does not restore RID-specific runtime packs.
    # Restore each packaged project for the portable RID before --no-restore publish
    # so packaging remains reproducible even when a newer SDK is also installed.
    & dotnet restore $ProjectPath `
        -r $runtimeIdentifier
    if ($LASTEXITCODE -ne 0) {
        Fail "dotnet restore for portable runtime failed."
    }

    & dotnet publish $ProjectPath `
        -c Release `
        -r $runtimeIdentifier `
        --self-contained true `
        --no-restore `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $Destination
    if ($LASTEXITCODE -ne 0) {
        Fail "dotnet publish failed."
    }
}

function Ensure-CargoAbout {
    if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
        Fail "cargo is required to generate Rust third-party notices."
    }

    $versionOutput = & cargo about --version 2>$null
    if ($LASTEXITCODE -eq 0 -and
        ($versionOutput -join " ") -match "cargo-about\s+$([regex]::Escape($cargoAboutVersion))(\s|$)") {
        return
    }

    & cargo install cargo-about `
        --version $cargoAboutVersion `
        --locked `
        --features cli
    if ($LASTEXITCODE -ne 0) {
        Fail "Unable to install pinned cargo-about $cargoAboutVersion."
    }

    $versionOutput = & cargo about --version 2>$null
    if ($LASTEXITCODE -ne 0 -or
        ($versionOutput -join " ") -notmatch "cargo-about\s+$([regex]::Escape($cargoAboutVersion))(\s|$)") {
        Fail "Pinned cargo-about $cargoAboutVersion is not available after installation."
    }
}

function Generate-RustNotices([string]$OutputPath) {
    Ensure-CargoAbout

    & cargo about generate `
        --manifest-path $anydocCargoToml `
        --config $cargoAboutConfig `
        --locked `
        --fail `
        --output-file $OutputPath `
        $cargoAboutTemplate
    if ($LASTEXITCODE -ne 0) {
        Fail "cargo-about failed to generate Rust third-party notices."
    }

    if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf) -or
        (Get-Item -LiteralPath $OutputPath).Length -le 0) {
        Fail "Rust third-party notice artifact was not created."
    }

    $noticeContent = Get-Content -LiteralPath $OutputPath -Raw
    foreach ($requiredToken in @(
        "anydoc 0.2.4",
        "quick-xml 0.41.0",
        "lopdf 0.45.0",
        "zip 8.6.0",
        "42bf1c5ecdde9eb0d96d6bd75a9e6698cf93b14c"
    )) {
        if ($noticeContent.IndexOf(
                $requiredToken,
                [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            Fail "Rust third-party notices are missing required dependency evidence: $requiredToken"
        }
    }
}

$runtimeIdentifier = Get-ProjectProperty "ZletPortableRuntimeIdentifier"
$packageName = Get-ProjectProperty "ZletPortablePackageName"
$executableName = Get-ProjectProperty "ZletExecutableName"
$portableRoot = Join-Path $repoRoot "artifacts\portable\$runtimeIdentifier"
$appFolder = Join-Path $portableRoot $packageName

$sdks = & dotnet --list-sdks
if ($LASTEXITCODE -ne 0 -or -not ($sdks -match "^8\.")) {
    Fail ".NET 8 SDK is required to publish the portable package."
}

foreach ($requiredPath in @(
    $appProject,
    $workerProject,
    $anydocCargoToml,
    $readmePath,
    $licensePath,
    $noticesPath,
    $licensesDirectory,
    $cargoAboutConfig,
    $cargoAboutTemplate
)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        Fail "Required packaging input is missing."
    }
}

Assert-SafeArtifactPath $portableRoot
if (Test-Path -LiteralPath $portableRoot) {
    Remove-Item -LiteralPath $portableRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $appFolder | Out-Null

Publish-Project $appProject $appFolder
Publish-Project $workerProject $appFolder

$anydocWorkerRelease = Join-Path $repoRoot "src\Zlet.FolderConverter.AnydocWorker\target\release\zlet-anydoc-worker.exe"
if (-not (Test-Path -LiteralPath $anydocWorkerRelease -PathType Leaf)) {
    if (Get-Command cargo -ErrorAction SilentlyContinue) {
        & cargo build --manifest-path $anydocCargoToml --release --locked
        if ($LASTEXITCODE -ne 0) {
            Fail "Anydoc worker cargo release build failed."
        }
    }
}
if (-not (Test-Path -LiteralPath $anydocWorkerRelease -PathType Leaf)) {
    Fail "Required packaging input is missing: zlet-anydoc-worker.exe (build with cargo build --release --locked)."
}
Copy-Item -LiteralPath $anydocWorkerRelease `
    -Destination (Join-Path $appFolder "zlet-anydoc-worker.exe") -Force

Copy-Item -LiteralPath $readmePath `
    -Destination (Join-Path $appFolder "README_PORTABLE.txt") -Force
Copy-Item -LiteralPath $licensePath `
    -Destination (Join-Path $appFolder "LICENSE.txt") -Force
Copy-Item -LiteralPath $noticesPath `
    -Destination (Join-Path $appFolder "THIRD_PARTY_NOTICES.md") -Force
$packagedLicenses = Join-Path $appFolder "licenses"
New-Item -ItemType Directory -Force -Path $packagedLicenses | Out-Null
Copy-Item -Path (Join-Path $licensesDirectory "*") `
    -Destination $packagedLicenses -Recurse -Force

$generatedRustNotices = Join-Path $packagedLicenses "RUST_THIRD_PARTY_NOTICES.txt"
Generate-RustNotices $generatedRustNotices

$requiredOutputs = @(
    (Join-Path $appFolder "$executableName.exe"),
    (Join-Path $appFolder "Zlet.FolderConverter.OfficeWorker.exe"),
    (Join-Path $appFolder "zlet-anydoc-worker.exe"),
    (Join-Path $appFolder "README_PORTABLE.txt"),
    (Join-Path $appFolder "LICENSE.txt"),
    (Join-Path $appFolder "THIRD_PARTY_NOTICES.md"),
    $generatedRustNotices
)
foreach ($requiredOutput in $requiredOutputs) {
    if (-not (Test-Path -LiteralPath $requiredOutput -PathType Leaf)) {
        Fail "Portable package validation failed: a required output is missing."
    }
}

$forbiddenDirectories = Get-ChildItem -LiteralPath $appFolder -Recurse -Directory |
    Where-Object {
        $_.Name -in @(
            "bin",
            "obj",
            "fixtures",
            "test-fixtures",
            "tests",
            "python",
            "java"
        )
    }
if ($forbiddenDirectories) {
    Fail "Portable package validation failed: a forbidden directory is present."
}

$forbiddenFiles = Get-ChildItem -LiteralPath $appFolder -Recurse -File |
    Where-Object {
        $_.Extension -in @(
            ".cs",
            ".csproj",
            ".sln",
            ".pdb",
            ".pfx",
            ".pem",
            ".key"
        ) -or
        $_.Name -match "\.(user|suo)$" -or
        $_.Name -match "^(python|java)(\.|$)" -or
        $_.Name -match "\.local\.json$"
    }
if ($forbiddenFiles) {
    Fail "Portable package validation failed: source, test, local, or secret-bearing files are present."
}

$ownedTextFiles = @(
    (Join-Path $appFolder "README_PORTABLE.txt"),
    (Join-Path $appFolder "THIRD_PARTY_NOTICES.md"),
    (Join-Path $appFolder "licenses\README.md")
)
foreach ($textFile in $ownedTextFiles) {
    if (Test-Path -LiteralPath $textFile) {
        $content = Get-Content -Raw -LiteralPath $textFile
        if ($content.IndexOf(
                $repoRoot,
                [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            Fail "Portable package validation failed: a local absolute path was recorded."
        }
    }
}

$zipPath = Join-Path $portableRoot "$packageName.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $appFolder,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $true
)
if (-not (Test-Path -LiteralPath $zipPath) -or
    (Get-Item -LiteralPath $zipPath).Length -le 0) {
    Fail "Portable ZIP was not created."
}

$unpackedBytes = (
    Get-ChildItem -LiteralPath $appFolder -Recurse -File |
    Measure-Object -Property Length -Sum
).Sum
$zipBytes = (Get-Item -LiteralPath $zipPath).Length

Write-Output "Portable ZIP created:"
Write-Output (Resolve-Path -LiteralPath $zipPath).Path
Write-Output "Portable ZIP bytes: $zipBytes"
Write-Output "Unpacked folder bytes: $unpackedBytes"

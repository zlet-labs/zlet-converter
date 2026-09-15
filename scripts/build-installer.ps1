$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$appProject = Join-Path $repoRoot "src\Zlet.FolderConverter.App\Zlet.FolderConverter.App.csproj"
$portableScript = Join-Path $PSScriptRoot "publish-portable.ps1"
$definitionPath = Join-Path $repoRoot "installer\ZletBatchConverter.iss"
$iconPath = Join-Path $repoRoot "src\Zlet.FolderConverter.App\Assets\ZletBatchConverter.ico"

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

function Find-InnoSetupCompiler {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    Fail "Inno Setup 6 was not found. Install it with: winget install --id JRSoftware.InnoSetup --exact"
}

foreach ($requiredPath in @($appProject, $portableScript, $definitionPath, $iconPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        Fail "Required installer input is missing: $requiredPath"
    }
}

$productVersion = Get-ProjectProperty "ZletProductVersion"
$runtimeIdentifier = Get-ProjectProperty "ZletPortableRuntimeIdentifier"
$packageName = Get-ProjectProperty "ZletPortablePackageName"
$executableName = Get-ProjectProperty "ZletExecutableName"
if ($runtimeIdentifier -ne "win-x64") {
    Fail "The installer currently supports only win-x64."
}

& $portableScript
if ($LASTEXITCODE -ne 0) {
    Fail "Portable package build failed."
}

$sourceDirectory = Join-Path $repoRoot "artifacts\portable\$runtimeIdentifier\$packageName"
$installerDirectory = Join-Path $repoRoot "artifacts\installer\$runtimeIdentifier"
$setupBaseName = "$executableName-v$productVersion-Setup-$runtimeIdentifier"
$setupPath = Join-Path $installerDirectory "$setupBaseName.exe"
if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
    Fail "Portable source directory was not created."
}

$requiredBinaries = @(
    (Join-Path $sourceDirectory "$executableName.exe"),
    (Join-Path $sourceDirectory "Zlet.FolderConverter.OfficeWorker.exe"),
    (Join-Path $sourceDirectory "zlet-anydoc-worker.exe")
)
foreach ($binary in $requiredBinaries) {
    if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) {
        Fail "Installer packaging validation failed: required binary is missing from staging directory: $binary"
    }
}

if (Test-Path -LiteralPath $installerDirectory) {
    Remove-Item -LiteralPath $installerDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null

$isccPath = Find-InnoSetupCompiler
& $isccPath "/DAppVersion=$productVersion" `
    "/DRuntimeIdentifier=$runtimeIdentifier" `
    "/DPackageName=$packageName" `
    "/DAppExeName=$executableName.exe" `
    "/DSourceDir=$sourceDirectory" `
    "/DOutputDir=$installerDirectory" `
    "/DIconPath=$iconPath" `
    $definitionPath
if ($LASTEXITCODE -ne 0) {
    Fail "Inno Setup compilation failed."
}

if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf) -or
    (Get-Item -LiteralPath $setupPath).Length -le 0) {
    Fail "Installer output was not created."
}

$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash
$signature = Get-AuthenticodeSignature -LiteralPath $setupPath
Write-Output "Installer created:"
Write-Output (Resolve-Path -LiteralPath $setupPath).Path
Write-Output "Installer bytes: $((Get-Item -LiteralPath $setupPath).Length)"
Write-Output "Installer SHA-256: $hash"
Write-Output "Authenticode status: $($signature.Status)"

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),

    [string]$EvidencePath = (Join-Path (Get-Location) "zlet-acceptance-evidence"),

    [string]$CommitSha = "unknown",

    [string]$TestSetPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot "PackagedAcceptanceMapping.psm1") -Force

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-BinarySourceSignature([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 4) {
        if ($bytes[0] -eq 0x25 -and $bytes[1] -eq 0x50 -and $bytes[2] -eq 0x44 -and $bytes[3] -eq 0x46) { return $true } # %PDF
        if ($bytes[0] -eq 0x50 -and $bytes[1] -eq 0x4B) { return $true } # OOXML ZIP
    }
    return $false
}

if ($env:OS -ne "Windows_NT") {
    throw "Packaged acceptance must run on Windows."
}

$packageRoot = (Resolve-Path -LiteralPath $PackagePath).Path
$repoRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$exe = Join-Path $packageRoot "ZletConverter.exe"
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "ZletConverter.exe was not found in package path: $packageRoot"
}

$supportedExtensions = @(".pdf", ".docx", ".pptx", ".xlsx", ".txt", ".doc", ".xls", ".ppt")
$acceptedManifestRoles = @("smoke_supported_format", "controlled_parity", "real_world_quality")

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$runRoot = Join-Path $EvidencePath $timestamp
$sourceRoot = Join-Path $runRoot "source"
$outputRoot = Join-Path $runRoot "output"
$reportPath = Join-Path $runRoot "conversion-report.json"
New-Item -ItemType Directory -Force -Path $sourceRoot, $outputRoot | Out-Null

$fixtureEvidence = @()
$recursive = $false
$testSetMode = "built_in"

if (-not [string]::IsNullOrWhiteSpace($TestSetPath)) {
    $testSetRoot = (Resolve-Path -LiteralPath $TestSetPath).Path
    $manifestPath = Join-Path $testSetRoot "manifest.json"
    $selectedFiles = @()

    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
        foreach ($entry in @($manifest.files)) {
            if ($acceptedManifestRoles -notcontains [string]$entry.role) { continue }

            $relativePath = ([string]$entry.path).Replace("/", [System.IO.Path]::DirectorySeparatorChar)
            $extension = [System.IO.Path]::GetExtension($relativePath).ToLowerInvariant()
            if ($supportedExtensions -notcontains $extension) { continue }

            $source = Join-Path $testSetRoot $relativePath
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                throw "Manifest-selected test document is missing: $relativePath"
            }

            $actualHash = Get-Sha256 $source
            if ($entry.sha256 -and $actualHash -ne ([string]$entry.sha256).ToLowerInvariant()) {
                throw "Test document hash does not match manifest: $relativePath"
            }

            $selectedFiles += [ordered]@{
                Source = $source
                RelativePath = $relativePath
                Format = $extension.TrimStart(".").ToUpperInvariant()
                Role = [string]$entry.role
                Sha256 = $actualHash
            }
        }
        $testSetMode = "external_manifest"
    }
    else {
        foreach ($source in Get-ChildItem -LiteralPath $testSetRoot -File -Recurse) {
            $extension = $source.Extension.ToLowerInvariant()
            if ($supportedExtensions -notcontains $extension) { continue }

            $relativePath = [System.IO.Path]::GetRelativePath($testSetRoot, $source.FullName)
            $selectedFiles += [ordered]@{
                Source = $source.FullName
                RelativePath = $relativePath
                Format = $extension.TrimStart(".").ToUpperInvariant()
                Role = "extension_selected"
                Sha256 = Get-Sha256 $source.FullName
            }
        }
        $testSetMode = "external_extension_filter"
    }

    if ($selectedFiles.Count -eq 0) {
        throw "No supported document inputs were selected from external test set: $testSetRoot"
    }

    foreach ($selected in $selectedFiles) {
        $destination = Join-Path $sourceRoot $selected.RelativePath
        $destinationDirectory = Split-Path -Parent $destination
        if ($destinationDirectory) { New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null }
        Copy-Item -LiteralPath $selected.Source -Destination $destination

        $fixtureEvidence += [ordered]@{
            format = $selected.Format
            file = $selected.RelativePath
            role = $selected.Role
            sha256 = $selected.Sha256
        }
    }
    $recursive = $true
}
else {
    $fixtureMap = @(
        @{ Format = "DOCX"; Source = "evaluation\fixtures\F08_structured.docx"; Name = "acceptance.docx" },
        @{ Format = "PDF";  Source = "evaluation\fixtures\F01_simple_text.pdf"; Name = "acceptance.pdf" },
        @{ Format = "PPTX"; Source = "evaluation\fixtures\F09_slides.pptx"; Name = "acceptance.pptx" },
        @{ Format = "XLSX"; Source = "evaluation\fixtures\F10_sheets.xlsx"; Name = "acceptance.xlsx" }
    )

    foreach ($fixture in $fixtureMap) {
        $source = Join-Path $repoRoot $fixture.Source
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Required public fixture is missing: $source"
        }
        $destination = Join-Path $sourceRoot $fixture.Name
        Copy-Item -LiteralPath $source -Destination $destination
        $fixtureEvidence += [ordered]@{
            format = $fixture.Format
            file = $fixture.Name
            role = "built_in"
            sha256 = Get-Sha256 $destination
        }
    }

    $txtPath = Join-Path $sourceRoot "acceptance.txt"
    [System.IO.File]::WriteAllText($txtPath, "# Zlet packaged acceptance`r`n`r`nDeterministic local TXT fixture.`r`n", [System.Text.UTF8Encoding]::new($false))
    $fixtureEvidence += [ordered]@{ format = "TXT"; file = "acceptance.txt"; role = "built_in"; sha256 = Get-Sha256 $txtPath }
}

$os = Get-CimInstance Win32_OperatingSystem
$appVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
$startedUtc = [DateTime]::UtcNow.ToString("o")

$processArgs = @(
    "batch",
    "--source", ('"{0}"' -f $sourceRoot),
    "--destination", ('"{0}"' -f $outputRoot),
    "--target", "markdown",
    "--recursive", ($(if ($recursive) { "true" } else { "false" })),
    "--report-json", ('"{0}"' -f $reportPath)
)
$process = Start-Process -FilePath $exe -ArgumentList $processArgs -Wait -PassThru -NoNewWindow
$exitCode = $process.ExitCode

$converterReport = $null
$reportLoadError = $null
if (Test-Path -LiteralPath $reportPath -PathType Leaf) {
    try { $converterReport = Get-Content -LiteralPath $reportPath -Raw -Encoding utf8 | ConvertFrom-Json }
    catch { $reportLoadError = "conversion-report.json could not be parsed: $($_.Exception.Message)" }
}
else {
    $reportLoadError = "conversion-report.json was not created"
}

$results = @()
$overallPass = $true
foreach ($fixture in $fixtureEvidence) {
    $reasons = @()
    $resolved = $null
    $markdown = $null
    $reportedOutput = $null
    $reportedOutputSha256 = $null

    if ($reportLoadError) {
        $reasons += $reportLoadError
    }
    else {
        try {
            $resolved = Resolve-ZletReportedArtifact -Report $converterReport -SourceRelativePath ([string]$fixture.file)
        }
        catch {
            $reasons += $_.Exception.Message
        }
    }

    if ($resolved) {
        $item = $resolved.Item
        if ([string]$item.status -ne "Succeeded") {
            $reasons += "converter status is $([string]$item.status)"
        }

        if ($resolved.Artifact) {
            $reportedOutput = [string]$resolved.Artifact.relativePath
            $reportedOutputSha256 = ([string]$resolved.Artifact.sha256).ToLowerInvariant()
            $candidate = [System.IO.Path]::GetFullPath((Join-Path $outputRoot $reportedOutput))
            $outputPrefix = [System.IO.Path]::GetFullPath($outputRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
            if (-not $candidate.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                $reasons += "reported output path escapes output root"
            }
            else {
                $markdown = $candidate
            }
        }
        else {
            $reasons += "conversion report has no primary artifact for source"
        }
    }

    if ($markdown -and -not (Test-Path -LiteralPath $markdown -PathType Leaf)) {
        $reasons += "reported Markdown output is missing"
    }
    elseif ($markdown) {
        $outputItem = Get-Item -LiteralPath $markdown
        if ($outputItem.Length -eq 0) { $reasons += "empty Markdown output" }
        if (Test-BinarySourceSignature $markdown) { $reasons += "output still has PDF/OOXML binary signature" }

        try {
            $text = [System.IO.File]::ReadAllText($markdown, [System.Text.UTF8Encoding]::new($false, $true))
            if ([string]::IsNullOrWhiteSpace($text)) { $reasons += "Markdown contains no text" }
        }
        catch {
            $reasons += "output is not valid UTF-8 text"
        }

        $actualOutputSha256 = Get-Sha256 $markdown
        if ($reportedOutputSha256 -and $actualOutputSha256 -ne $reportedOutputSha256) {
            $reasons += "output SHA-256 does not match conversion report"
        }
        if ($fixture.format -ne "TXT" -and $fixture.sha256 -eq $actualOutputSha256) {
            $reasons += "output is byte-identical to binary source (Copy regression)"
        }
    }

    $pass = ($reasons.Count -eq 0)
    if (-not $pass) { $overallPass = $false }
    $results += [ordered]@{
        format = $fixture.format
        source = $fixture.file
        sourceSha256 = $fixture.sha256
        output = $reportedOutput
        outputSha256 = if ($markdown -and (Test-Path -LiteralPath $markdown -PathType Leaf)) { Get-Sha256 $markdown } else { $null }
        status = if ($pass) { "PASS" } else { "FAIL" }
        reasons = $reasons
    }
}

if ($exitCode -ne 0) { $overallPass = $false }
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { $overallPass = $false }

$evidence = [ordered]@{
    schema = "zlet-converter-packaged-acceptance/v1"
    startedUtc = $startedUtc
    completedUtc = [DateTime]::UtcNow.ToString("o")
    product = "Zlet Converter"
    appVersion = $appVersion
    commitSha = $CommitSha
    packageExeSha256 = Get-Sha256 $exe
    environment = [ordered]@{
        osCaption = $os.Caption
        osVersion = $os.Version
        osBuildNumber = $os.BuildNumber
        architecture = $env:PROCESSOR_ARCHITECTURE
        powershell = $PSVersionTable.PSVersion.ToString()
    }
    testSetMode = $testSetMode
    invocation = ("ZletConverter.exe batch --target markdown --recursive {0}" -f ($(if ($recursive) { "true" } else { "false" })))
    converterExitCode = $exitCode
    converterReport = if (Test-Path -LiteralPath $reportPath) { "conversion-report.json" } else { $null }
    fixtures = $fixtureEvidence
    routes = $results
    summary = [ordered]@{
        passed = @($results | Where-Object status -eq "PASS").Count
        failed = @($results | Where-Object status -eq "FAIL").Count
        total = $results.Count
        status = if ($overallPass) { "PASS" } else { "FAIL" }
    }
}

$acceptanceReport = Join-Path $runRoot "acceptance-report.json"
$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $acceptanceReport -Encoding utf8

Write-Host ""
Write-Host ("Packaged acceptance: {0} {1}/{2}" -f $evidence.summary.status, $evidence.summary.passed, $evidence.summary.total)
foreach ($result in $results) {
    Write-Host ("{0,-4} {1}" -f $result.format, $result.status)
    foreach ($reason in $result.reasons) { Write-Host ("     - {0}" -f $reason) }
}
Write-Host "Evidence: $runRoot"

if (-not $overallPass) { exit 1 }
exit 0

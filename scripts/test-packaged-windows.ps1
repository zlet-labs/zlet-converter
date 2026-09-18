[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),

    [string]$EvidencePath = (Join-Path (Get-Location) "zlet-acceptance-evidence"),

    [string]$CommitSha = "unknown"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

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

$fixtureMap = @(
    @{ Format = "DOCX"; Source = "evaluation\fixtures\F08_structured.docx"; Name = "acceptance.docx" },
    @{ Format = "PDF";  Source = "evaluation\fixtures\F01_simple_text.pdf"; Name = "acceptance.pdf" },
    @{ Format = "PPTX"; Source = "evaluation\fixtures\F09_slides.pptx"; Name = "acceptance.pptx" },
    @{ Format = "XLSX"; Source = "evaluation\fixtures\F10_sheets.xlsx"; Name = "acceptance.xlsx" }
)

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$runRoot = Join-Path $EvidencePath $timestamp
$sourceRoot = Join-Path $runRoot "source"
$outputRoot = Join-Path $runRoot "output"
$reportPath = Join-Path $runRoot "conversion-report.json"
New-Item -ItemType Directory -Force -Path $sourceRoot, $outputRoot | Out-Null

$fixtureEvidence = @()
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
        sha256 = Get-Sha256 $destination
    }
}

$txtPath = Join-Path $sourceRoot "acceptance.txt"
[System.IO.File]::WriteAllText($txtPath, "# Zlet packaged acceptance`r`n`r`nDeterministic local TXT fixture.`r`n", [System.Text.UTF8Encoding]::new($false))
$fixtureEvidence += [ordered]@{ format = "TXT"; file = "acceptance.txt"; sha256 = Get-Sha256 $txtPath }

$os = Get-CimInstance Win32_OperatingSystem
$appVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
$startedUtc = [DateTime]::UtcNow.ToString("o")

$processArgs = @(
    "batch",
    "--source", ('"{0}"' -f $sourceRoot),
    "--destination", ('"{0}"' -f $outputRoot),
    "--target", "markdown",
    "--recursive", "false",
    "--report-json", ('"{0}"' -f $reportPath)
)
$process = Start-Process -FilePath $exe -ArgumentList $processArgs -Wait -PassThru -NoNewWindow
$exitCode = $process.ExitCode

$results = @()
$overallPass = $true
foreach ($fixture in $fixtureEvidence) {
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($fixture.file)
    $markdown = Join-Path $outputRoot ($stem + ".md")
    $reasons = @()

    if (-not (Test-Path -LiteralPath $markdown -PathType Leaf)) {
        $reasons += "missing Markdown output"
    }
    else {
        $item = Get-Item -LiteralPath $markdown
        if ($item.Length -eq 0) { $reasons += "empty Markdown output" }
        if (Test-BinarySourceSignature $markdown) { $reasons += "output still has PDF/OOXML binary signature" }

        try {
            $text = [System.IO.File]::ReadAllText($markdown, [System.Text.UTF8Encoding]::new($false, $true))
            if ([string]::IsNullOrWhiteSpace($text)) { $reasons += "Markdown contains no text" }
        }
        catch {
            $reasons += "output is not valid UTF-8 text"
        }

        if ($fixture.format -ne "TXT") {
            $sourceHash = $fixture.sha256
            $outputHash = Get-Sha256 $markdown
            if ($sourceHash -eq $outputHash) { $reasons += "output is byte-identical to binary source (Copy regression)" }
        }
    }

    $pass = ($reasons.Count -eq 0)
    if (-not $pass) { $overallPass = $false }
    $results += [ordered]@{
        format = $fixture.format
        source = $fixture.file
        sourceSha256 = $fixture.sha256
        output = if (Test-Path -LiteralPath $markdown) { [System.IO.Path]::GetFileName($markdown) } else { $null }
        outputSha256 = if (Test-Path -LiteralPath $markdown) { Get-Sha256 $markdown } else { $null }
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
    invocation = "ZletConverter.exe batch --target markdown --recursive false"
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

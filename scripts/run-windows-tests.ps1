[CmdletBinding()]
param(
    [string]$GitRef = "main",
    [ValidateSet("Full","Acceptance","BuildOnly")]
    [string]$Mode = "Full",
    [string]$PackagePath,
    [string]$TestPackManifestUrl,
    [string]$TestPackCache = (Join-Path $env:LOCALAPPDATA "Zlet Labs\Zlet Converter\test-packs"),
    [string]$EvidenceRoot = (Join-Path (Get-Location) "zlet-runner-evidence")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Logged {
    param([string]$Name, [scriptblock]$Command, [string]$LogDirectory)
    $stdout = Join-Path $LogDirectory "$Name.stdout.log"
    $stderr = Join-Path $LogDirectory "$Name.stderr.log"
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & $Command 1> $stdout 2> $stderr
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($exitCode -ne 0) { throw "$Name failed with exit code $exitCode" }
}

function Get-FileSha256([string]$Path) {
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

$started = (Get-Date).ToUniversalTime()
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$evidence = Join-Path $EvidenceRoot $stamp
$logs = Join-Path $evidence "logs"
New-Item -ItemType Directory -Force -Path $logs | Out-Null

$status = "FAILED"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$originalRef = (& git -C $repoRoot rev-parse HEAD).Trim()
$packRecord = $null
$packageRecord = $null

try {
    Invoke-Logged "git-fetch" { git -C $repoRoot fetch --tags --prune origin } $logs

    # Resolve a branch name against the freshly fetched origin first. This avoids
    # silently accepting a stale local branch such as `main`.
    $remoteRef = "refs/remotes/origin/$GitRef"
    & git -C $repoRoot show-ref --verify --quiet $remoteRef
    if ($LASTEXITCODE -eq 0) {
        $commit = (& git -C $repoRoot rev-parse "$remoteRef^{commit}").Trim()
    }
    else {
        $resolvedCommit = @(& git -C $repoRoot rev-parse "$GitRef^{commit}" 2>$null)
        if ($LASTEXITCODE -ne 0 -or $resolvedCommit.Count -eq 0) { throw "Git ref could not be resolved after fetch: $GitRef" }
        $commit = ([string]$resolvedCommit[0]).Trim()
    }
    Invoke-Logged "git-checkout" { git -C $repoRoot checkout --detach $commit } $logs
    $checkedOutCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
    if ($checkedOutCommit -ne $commit) { throw "Checked-out commit does not match resolved git ref." }

    if ($Mode -ne "Acceptance" -and -not $PackagePath) {
        Invoke-Logged "restore" { dotnet restore (Join-Path $repoRoot "FolderConverter.sln") } $logs
        Invoke-Logged "build" { dotnet build (Join-Path $repoRoot "FolderConverter.sln") -c Release --no-restore } $logs
        Invoke-Logged "tests" { dotnet test (Join-Path $repoRoot "FolderConverter.sln") -c Release --no-build } $logs
        Invoke-Logged "publish-portable" { powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts\publish-portable.ps1") } $logs

        $exe = Get-ChildItem -Path $repoRoot -Filter ZletConverter.exe -Recurse -File |
            Where-Object { $_.FullName -match "artifacts|publish|portable" } |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if (-not $exe) { throw "Portable build completed but ZletConverter.exe was not found." }
        $PackagePath = $exe.Directory.FullName
    }

    if ($PackagePath) {
        $resolvedPackage = (Resolve-Path $PackagePath).Path
        $exePath = Join-Path $resolvedPackage "ZletConverter.exe"
        if (-not (Test-Path $exePath)) { throw "ZletConverter.exe not found in package path: $resolvedPackage" }
        $packageRecord = [ordered]@{
            path = $resolvedPackage
            exeSha256 = Get-FileSha256 $exePath
            fileVersion = (Get-Item $exePath).VersionInfo.FileVersion
        }
    }

    if ($TestPackManifestUrl) {
        New-Item -ItemType Directory -Force -Path $TestPackCache | Out-Null
        $manifestTemp = Join-Path $env:TEMP ("zlet-pack-" + [guid]::NewGuid().ToString("N") + ".json")
        Invoke-WebRequest -UseBasicParsing -Uri $TestPackManifestUrl -OutFile $manifestTemp
        $manifest = Get-Content -Raw -LiteralPath $manifestTemp | ConvertFrom-Json
        foreach ($required in @("id","archiveUrl","sha256")) {
            if (-not $manifest.$required) { throw "Test-pack manifest is missing '$required'." }
        }
        $safeId = ($manifest.id -replace '[^A-Za-z0-9._-]', '_')
        $expected = ([string]$manifest.sha256).ToLowerInvariant()
        if ($expected -notmatch '^[0-9a-f]{64}$') { throw "Test-pack manifest has an invalid SHA-256 value." }
        $packKey = "$safeId-$expected"
        $archive = Join-Path $TestPackCache "$packKey.zip"
        $validCache = Test-Path -LiteralPath $archive -PathType Leaf
        if ($validCache) {
            $cachedHash = Get-FileSha256 $archive
            if ($cachedHash -ne $expected) {
                throw "Immutable cached test-pack hash mismatch for $archive. Expected $expected, got $cachedHash."
            }
        }
        else {
            $part = Join-Path $TestPackCache ("$packKey." + [guid]::NewGuid().ToString("N") + ".part")
            try {
                Invoke-WebRequest -UseBasicParsing -Uri $manifest.archiveUrl -OutFile $part
                $actual = Get-FileSha256 $part
                if ($actual -ne $expected) {
                    throw "Test-pack SHA-256 mismatch. Expected $expected, got $actual."
                }
                Move-Item -LiteralPath $part -Destination $archive
            }
            finally {
                if (Test-Path -LiteralPath $part) { Remove-Item -Force -LiteralPath $part }
            }
        }

        $packExtractRoot = Join-Path $evidence "test-pack"
        Expand-Archive -LiteralPath $archive -DestinationPath $packExtractRoot -Force
        $packEntries = @(Get-ChildItem -LiteralPath $packExtractRoot)
        if ($packEntries.Count -eq 1 -and $packEntries[0].PSIsContainer) {
            $packTestRoot = $packEntries[0].FullName
        }
        else {
            $packTestRoot = $packExtractRoot
        }
        $packRecord = [ordered]@{ id=$manifest.id; archive=$archive; sha256=$expected; cacheHit=$validCache; manifestUrl=$TestPackManifestUrl; extractedPath=$packTestRoot }
        Copy-Item $manifestTemp (Join-Path $evidence "test-pack-manifest.json")
        Remove-Item -Force $manifestTemp
    }

    if ($Mode -ne "BuildOnly") {
        if (-not $PackagePath) { throw "PackagePath is required for acceptance." }
        Invoke-Logged "packaged-acceptance" {
            powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts\test-packaged-windows.ps1") -PackagePath $PackagePath -CommitSha $commit -EvidencePath (Join-Path $evidence "packaged-acceptance")
        } $logs

        if ($packRecord) {
            Invoke-Logged "test-pack-acceptance" {
                powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts\test-packaged-windows.ps1") -PackagePath $PackagePath -CommitSha $commit -TestSetPath $packTestRoot -EvidencePath (Join-Path $evidence "test-pack-acceptance")
            } $logs
        }
    }

    $status = "PASS"
}
catch {
    $_ | Out-String | Set-Content -Encoding UTF8 (Join-Path $logs "runner-error.log")
    throw
}
finally {
    $finished = (Get-Date).ToUniversalTime()
    $commitNow = try { (& git -C $repoRoot rev-parse HEAD).Trim() } catch { $null }
    $report = [ordered]@{
        schema = "zlet-converter-windows-runner/v1"
        status = $status
        mode = $Mode
        requestedGitRef = $GitRef
        commit = $commitNow
        startedUtc = $started.ToString("o")
        finishedUtc = $finished.ToString("o")
        environment = [ordered]@{
            os = [Environment]::OSVersion.VersionString
            architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            powershell = $PSVersionTable.PSVersion.ToString()
            machine = $env:COMPUTERNAME
        }
        package = $packageRecord
        testPack = $packRecord
        evidencePath = $evidence
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $evidence "runner-report.json")
    $zip = "$evidence.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path (Join-Path $evidence "*") -DestinationPath $zip -Force

    try {
        if ($originalRef) { git -C $repoRoot checkout --detach $originalRef | Out-Null }
    } catch {}
}

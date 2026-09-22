Set-StrictMode -Version Latest

function ConvertTo-ZletRelativeKey([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    return ($Path.Replace('\\', '/').TrimStart('/')).ToLowerInvariant()
}

function Resolve-ZletReportedArtifact {
    param(
        [Parameter(Mandatory = $true)] [object]$Report,
        [Parameter(Mandatory = $true)] [string]$SourceRelativePath
    )

    $sourceKey = ConvertTo-ZletRelativeKey $SourceRelativePath
    $matches = @($Report.items | Where-Object {
        (ConvertTo-ZletRelativeKey ([string]$_.sourceRelativePath)) -eq $sourceKey
    })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one conversion-report item for '$SourceRelativePath'; found $($matches.Count)."
    }

    $item = $matches[0]
    $resultPath = [string]$item.resultRelativePath
    if ([string]::IsNullOrWhiteSpace($resultPath)) {
        return [pscustomobject]@{ Item = $item; Artifact = $null }
    }

    $resultKey = ConvertTo-ZletRelativeKey $resultPath
    $artifacts = @($item.artifacts | Where-Object {
        ([string]$_.kind) -eq 'primary' -and
        (ConvertTo-ZletRelativeKey ([string]$_.relativePath)) -eq $resultKey
    })
    if ($artifacts.Count -ne 1) {
        throw "Expected exactly one primary artifact matching resultRelativePath '$resultPath' for '$SourceRelativePath'; found $($artifacts.Count)."
    }

    return [pscustomobject]@{ Item = $item; Artifact = $artifacts[0] }
}

Export-ModuleMember -Function Resolve-ZletReportedArtifact

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'PackagedAcceptanceMapping.psm1') -Force

$report = [pscustomobject]@{
    items = @(
        [pscustomobject]@{
            sourceRelativePath='foo.docx'; status='Succeeded'; resultRelativePath='foo.md'
            artifacts=@([pscustomobject]@{kind='primary';relativePath='foo.md';sha256='aaa'})
        },
        [pscustomobject]@{
            sourceRelativePath='foo.pdf'; status='Succeeded'; resultRelativePath='foo-2.md'
            artifacts=@([pscustomobject]@{kind='primary';relativePath='foo-2.md';sha256='bbb'})
        },
        [pscustomobject]@{
            sourceRelativePath='nested\\foo.xlsx'; status='Succeeded'; resultRelativePath='nested\\foo-3.md'
            artifacts=@([pscustomobject]@{kind='primary';relativePath='nested\\foo-3.md';sha256='ccc'})
        }
    )
}

$cases = @(
    @{ source='foo.docx'; path='foo.md'; sha='aaa' },
    @{ source='foo.pdf'; path='foo-2.md'; sha='bbb' },
    @{ source='nested/foo.xlsx'; path='nested\\foo-3.md'; sha='ccc' }
)

foreach ($case in $cases) {
    $resolved = Resolve-ZletReportedArtifact -Report $report -SourceRelativePath $case.source
    if ([string]$resolved.Artifact.relativePath -ne $case.path) { throw "Wrong artifact for $($case.source)." }
    if ([string]$resolved.Artifact.sha256 -ne $case.sha) { throw "Wrong artifact hash for $($case.source)." }
}
if ((Resolve-ZletReportedArtifact -Report $report -SourceRelativePath 'foo.docx').Artifact.relativePath -eq
    (Resolve-ZletReportedArtifact -Report $report -SourceRelativePath 'foo.pdf').Artifact.relativePath) {
    throw 'Same-stem sources were incorrectly mapped to one artifact.'
}
Write-Host 'Packaged acceptance mapping regression: PASS'

param(
    [string]$BannerlordRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$OutputRoot = (Join-Path $PSScriptRoot '../artifacts/installed-validation'),
    [switch]$PublishFidelity
)
$ErrorActionPreference = 'Stop'
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$data = Join-Path $repoRoot 'test-data/visual-regression'
$cli = Join-Path $repoRoot 'src/Bannerlord.XmlPreview.VisualRegression.Cli/bin/Debug/net10.0-windows/Bannerlord.XmlPreview.VisualRegression.Cli.exe'
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$priorRoot = $env:BANNERLORD_ROOT
$priorArtifacts = $env:BANNERLORD_M3_ARTIFACTS
try {
    $env:BANNERLORD_ROOT = $BannerlordRoot
    $env:BANNERLORD_M3_ARTIFACTS = Join-Path $OutputRoot 'installed'
    & dotnet test (Join-Path $repoRoot 'Bannerlord.XmlPreview.sln') --no-restore -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Unit, authoring, image, installed acceptance or portable tests failed.' }
    $reports = @()
    foreach ($id in @('m3-conversation', 'm3-banner-builder', 'm3-land-management')) {
        $metadata = if ($id -eq 'm3-land-management') { 'm3-mod-baselines' } else { 'm3-native-baselines' }
        $caseOutput = Join-Path $OutputRoot $id
        & $cli compare --scenario (Join-Path $data "m3-scenarios/$id.json") --metadata (Join-Path $data $metadata) `
            --bannerlord-root $BannerlordRoot --output $caseOutput --mode StructureOnly
        if ($LASTEXITCODE -ne 0) { throw "Installed structural acceptance failed for $id. Artifacts retained in $caseOutput." }
        $report = Get-Content (Join-Path $caseOutput 'report.json') -Raw | ConvertFrom-Json
        if ($report.Outcome -ne 'Passed') { throw "Installed validation requires Passed for $id, received $($report.Outcome)." }
        $reports += [ordered]@{ Case=$id; Outcome=$report.Outcome; Pixels=$report.Checks.Pixels }
    }
    $rows = @(
        [ordered]@{Family='ImageWidget';Status='Verified';Scope='Static sprite and brush images, sizing, visibility, stretch, tint and opacity';Limit='Native in-game pixels are not certified.'},
        [ordered]@{Family='ImageIdentifierWidget';Status='Verified';Scope='Explicit Preview.Sprite or scenario image data, provider identity/arguments, null and HideWhenNull';Limit='Other runtime providers remain visible Partial until deterministic data is supplied.'},
        [ordered]@{Family='MaskedTextureWidget';Status='Verified';Scope='Native five-mask fixture; alpha and single-channel masks, dimensions, tint and missing-input checks';Limit='Static WPF compositing; runtime provider limitations still apply.'},
        [ordered]@{Family='BannerTableauWidget';Status='Placeholder';Scope='Explicit runtime-only placeholder and Partial';Limit='Runtime tableau rendering is deferred.'},
        [ordered]@{Family='Code Widget';Status='Placeholder';Scope='Labeled placeholder and Partial';Limit='Mod executable code is never loaded.'}
    )
    [ordered]@{SchemaVersion=1;Validation='All solution tests and three installed structural comparisons passed';Screens=$reports;Families=$rows} |
        ConvertTo-Json -Depth 10 | Set-Content (Join-Path $OutputRoot 'fidelity.json') -Encoding utf8
    $markdown = @('# M3 image fidelity', '', 'Generated only after unit, rendered-pixel, installed asset and structural assertions pass. In-game screenshot comparison remains optional.', '', '| Family | Status | Verified scope | Limit |', '| --- | --- | --- | --- |')
    foreach ($row in $rows) { $markdown += "| $($row.Family) | $($row.Status) | $($row.Scope) | $($row.Limit) |" }
    $markdown | Set-Content (Join-Path $OutputRoot 'fidelity.md') -Encoding utf8
    if ($PublishFidelity) {
        $destination = Join-Path $repoRoot 'docs/fidelity'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $OutputRoot 'fidelity.json') -Destination (Join-Path $destination 'images.json')
        Copy-Item -LiteralPath (Join-Path $OutputRoot 'fidelity.md') -Destination (Join-Path $destination 'images.md')
    }
    Write-Output "Installed validation and fidelity evidence: $OutputRoot"
} finally {
    $env:BANNERLORD_ROOT = $priorRoot
    $env:BANNERLORD_M3_ARTIFACTS = $priorArtifacts
}

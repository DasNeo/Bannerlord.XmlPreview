param(
    [string]$BannerlordRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$ComparisonPngRoot,
    [string]$OutputRoot = (Join-Path $PSScriptRoot '../artifacts/m3-spike/results')
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ComparisonPngRoot) -or !(Test-Path -LiteralPath $ComparisonPngRoot -PathType Container)) {
    throw 'Supply -ComparisonPngRoot with an existing external PNG comparison directory. Production and required validation do not need comparison PNGs.'
}
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$readerRoot = Join-Path $repoRoot 'artifacts/m3-spike/GauntletUI-LSP/tpac-tool'
$readerCommit = '6b115ff3a6cebdbe25af424eb13926078ed22b2d'
if (!(Test-Path -LiteralPath $readerRoot)) {
    New-Item -ItemType Directory -Path (Split-Path $readerRoot) -Force | Out-Null
    & git clone https://github.com/kerema14/TpacTool.git $readerRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not fetch the evaluation reader.' }
    & git -C $readerRoot checkout --detach $readerCommit
    if ($LASTEXITCODE -ne 0) { throw 'Could not select the pinned evaluation reader.' }
}
$actualCommit = & git -C $readerRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $readerCommit) {
    throw "Reader must be at $readerCommit. Existing checkout was preserved."
}
& dotnet run --project (Join-Path $PSScriptRoot 'TpacSpike/TpacSpike.csproj') -c Release -- `
    (Join-Path $BannerlordRoot 'Modules/Native') $ComparisonPngRoot $OutputRoot
exit $LASTEXITCODE

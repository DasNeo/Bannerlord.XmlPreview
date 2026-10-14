param(
    [string]$BannerlordRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$OutputRoot = (Join-Path $PSScriptRoot '../artifacts/m4-validation')
)
$ErrorActionPreference = 'Stop'
# The M3 runner executes the complete solution (including M4 authoring tests),
# then compares the three installed structural targets against accepted metadata.
& (Join-Path $PSScriptRoot 'run_m3_validation.ps1') -BannerlordRoot $BannerlordRoot -OutputRoot $OutputRoot
Write-Output "M4 automated validation passed. Manual WPF/external-editor smoke acceptance is recorded separately in docs/reports/m4-implementation.md."

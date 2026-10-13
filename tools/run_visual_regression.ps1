param(
    [ValidateSet('Full', 'StructureOnly')][string]$Mode = 'StructureOnly',
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$BannerlordRoot,
    [string]$ReferencePackageRoot,
    [string]$MetadataRoot,
    [string]$OutputRoot = (Join-Path $env:TEMP ('Bannerlord.VisualRegression-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [switch]$Benchmark,
    [ValidateRange(0, 3600)][int]$PerCaseTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$cases = @('standard-window', 'standard-scrollable-panel', 'hint-tooltip', 'crosshair',
    'standard-dropdown', 'option-item', 'options', 'conversation', 'single-query-popup')
$cli = Join-Path $RepoRoot 'Bannerlord.XmlPreview.VisualRegression.Cli/bin/Debug/net10.0-windows/Bannerlord.XmlPreview.VisualRegression.Cli.exe'
if (!(Test-Path -LiteralPath $cli)) { throw 'Build the solution before running Visual Regression.' }
$data = Join-Path $RepoRoot 'test-data/visual-regression'
$portable = [string]::IsNullOrWhiteSpace($BannerlordRoot)
if ($portable -and $Mode -eq 'Full') { throw 'Full requires -BannerlordRoot; portable metadata fixtures cannot compare pixels.' }
$scenarioDirectory = if ($portable) { Join-Path $data 'm3-portable-scenarios' } else { Join-Path $data 'scenarios' }
if ([string]::IsNullOrWhiteSpace($MetadataRoot)) {
    $MetadataRoot = if ($portable) { Join-Path $data 'm3-portable-baselines' } else { Join-Path $data 'native-baselines' }
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$summary = [System.Collections.Generic.List[string]]::new()
$summary.Add('# Visual Regression suite')
$summary.Add('')
$summary.Add("Mode: $Mode; portable metadata fixtures: $portable; benchmark: $Benchmark")
$summary.Add('')
$failed = $false
foreach ($case in $cases) {
    # Optional Full comparison checks the three screens visually; required validation uses StructureOnly for all nine.
    $caseMode = if ($Mode -eq 'Full' -and $case -in @('options', 'conversation', 'single-query-popup')) { 'Full' } else { 'StructureOnly' }
    $caseOutput = Join-Path $OutputRoot $case
    New-Item -ItemType Directory -Path $caseOutput -Force | Out-Null
    $command = if ($Benchmark) { 'benchmark' } else { 'compare' }
    # ProcessStartInfo.Arguments is a Windows command line; quote every path value.
    $arguments = @($command, '--scenario', ('"' + (Join-Path $scenarioDirectory ($case + '.json')) + '"'),
        '--metadata', ('"' + $MetadataRoot + '"'), '--output', ('"' + $caseOutput + '"'), '--mode', $caseMode)
    if ($portable) { $arguments += @('--corpus', ('"' + (Join-Path $data 'corpus') + '"')) }
    else { $arguments += @('--bannerlord-root', ('"' + $BannerlordRoot + '"')) }
    if (![string]::IsNullOrWhiteSpace($ReferencePackageRoot)) { $arguments += @('--package', ('"' + $ReferencePackageRoot + '"')) }
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $cli
    $startInfo.Arguments = $arguments -join ' '
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $completed = $process.WaitForExit($PerCaseTimeoutSeconds * 1000)
    if (!$completed) {
        if (!$process.HasExited) { $process.Kill() }
        $summary.Add("- ${case}: TimedOut (nonzero); mode: $caseMode; artifacts retained.")
        $failed = $true
        continue
    }
    $process.WaitForExit()
    $stdout.Result | Set-Content -LiteralPath (Join-Path $caseOutput 'stdout.txt') -Encoding UTF8
    $stderr.Result | Set-Content -LiteralPath (Join-Path $caseOutput 'stderr.txt') -Encoding UTF8
    $reportPath = if ($Benchmark) { Join-Path $caseOutput 'iteration-5/report.json' } else { Join-Path $caseOutput 'report.json' }
    if (Test-Path -LiteralPath $reportPath) {
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $summary.Add("- ${case}: $($report.Outcome); mode: $caseMode; pixels: $($report.Checks.Pixels); exit: $($process.ExitCode)")
        if ($report.Outcome -ne 'Passed') { $failed = $true }
    } else {
        $summary.Add("- ${case}: no report; exit: $($process.ExitCode); stderr retained.")
        $failed = $true
    }
    if ($process.ExitCode -ne 0) { $failed = $true }
    $process.Dispose()
}
$summary | Set-Content -LiteralPath (Join-Path $OutputRoot 'summary.md') -Encoding UTF8
Write-Output ('Visual Regression evidence: ' + (Join-Path $OutputRoot 'summary.md'))
if ($failed) { exit 1 }
exit 0

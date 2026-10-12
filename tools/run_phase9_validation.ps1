param(
    [string[]]$Fixture = @(
        "standard-window",
        "standard-background",
        "standard-scrollable-panel",
        "land-management",
        "tournament-scrollable-panel",
        "options-top-panel"
    ),
    [switch]$Build,
    [int]$PerFixtureTimeoutSeconds = 30,
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$BannerlordRoot = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord",
    [string]$OutputRoot = (Join-Path (Resolve-Path $PSScriptRoot).Path ("..\\artifacts\\phase9-validation\\" + (Get-Date -Format "yyyyMMdd-HHmmss")))
)

$fixtures = [ordered]@{
    "standard-window" = @{
        Path = Join-Path $BannerlordRoot "Modules\Native\GUI\Prefabs\Standard\Standard.Window.xml"
        Focus = "Root structure and root measurement."
    }
    "standard-background" = @{
        Path = Join-Path $BannerlordRoot "Modules\Native\GUI\Prefabs\Standard\Standard.Background.xml"
        Focus = "A simple container case for brush and sprite drift."
    }
    "standard-scrollable-panel" = @{
        Path = Join-Path $BannerlordRoot "Modules\Native\GUI\Prefabs\Standard\Standard.ScrollablePanel.xml"
        Focus = "ClipRect, InnerPanel, and Scrollbar references."
    }
    "land-management" = @{
        Path = Join-Path $RepoRoot "fixtures\AgricultureEstate\GUI\Prefabs\LandManagement.xml"
        Focus = "A combined drift test for lists, buttons, and text."
    }
    "tournament-scrollable-panel" = @{
        Path = Join-Path $BannerlordRoot "Modules\SandBox\GUI\Prefabs\Tournament\Tournament.xml"
        Focus = "Placeholder-based child insertion for includes."
    }
    "options-top-panel" = @{
        Path = Join-Path $BannerlordRoot "Modules\Native\GUI\Prefabs\Options\SPOptions\Options.xml"
        Focus = "Root fallback for includes without LogicalChildrenLocation."
    }
}

function Get-LineValue {
    param(
        [string]$Content,
        [string]$Label
    )

    $pattern = "(?m)^" + [regex]::Escape($Label) + ":\s*(.+)$"
    $match = [regex]::Match($Content, $pattern)
    if ($match.Success) {
        return $match.Groups[1].Value.Trim()
    }

    return $null
}

function Get-LineValueOrDefault {
    param(
        [string]$Content,
        [string]$Label,
        [string]$DefaultValue
    )

    $value = Get-LineValue -Content $Content -Label $Label
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }

    return $value
}

function Get-WarningCount {
    param([string]$Content)

    $match = [regex]::Match($Content, "(?smi)^Warnings\s*\r?\n(?<body>.+)$")
    if (-not $match.Success) {
        return 0
    }

    $body = $match.Groups["body"].Value.Trim()
    if ([string]::IsNullOrWhiteSpace($body) -or $body -like "None.*") {
        return 0
    }

    return ($body -split "\r?\n").Count
}

function Add-ProgressSummaryLines {
    param(
        [System.Collections.ArrayList]$SummaryLines,
        [string]$Content
    )

    $stage = Get-LineValue -Content $Content -Label "Stage"
    if (-not [string]::IsNullOrWhiteSpace($stage)) {
        [void]$SummaryLines.Add("  LastStage: $stage")
    }

    $loadDurationMs = Get-LineValue -Content $Content -Label "LoadDurationMs"
    if (-not [string]::IsNullOrWhiteSpace($loadDurationMs)) {
        [void]$SummaryLines.Add("  LoadDurationMs: $loadDurationMs")
    }

    $renderDurationMs = Get-LineValue -Content $Content -Label "RenderDurationMs"
    if (-not [string]::IsNullOrWhiteSpace($renderDurationMs)) {
        [void]$SummaryLines.Add("  RenderDurationMs: $renderDurationMs")
    }
}

if ($Build) {
    dotnet build (Join-Path $RepoRoot "Bannerlord.XmlPreview.sln") --no-restore
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$executablePath = Join-Path $RepoRoot "bin\Debug\net10.0-windows\Bannerlord.XmlPreview.exe"
if (-not (Test-Path -LiteralPath $executablePath)) {
    Write-Error "Could not find '$executablePath'. Build the solution first or pass -Build."
    exit 1
}

$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null

$summaryLines = [System.Collections.ArrayList]@(
    "# Phase 9 Validation Summary",
    "",
    "Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz")",
    "BannerlordRoot: $BannerlordRoot",
    "Technical verification only: yes",
    "Manual visual verification performed: no",
    "",
    "## Fixtures"
)
$hasFailures = $false

foreach ($fixtureId in $Fixture) {
    if (-not $fixtures.Contains($fixtureId)) {
        $hasFailures = $true
        [void]$summaryLines.Add("- $fixtureId")
        [void]$summaryLines.Add("  Status: UnknownFixture")
        [void]$summaryLines.Add("  Diagnostics: not created")
        continue
    }

    $fixtureConfig = $fixtures[$fixtureId]
    if (-not (Test-Path -LiteralPath $fixtureConfig.Path)) {
        $hasFailures = $true
        [void]$summaryLines.Add("- $fixtureId")
        [void]$summaryLines.Add("  Path: $($fixtureConfig.Path)")
        [void]$summaryLines.Add("  Focus: $($fixtureConfig.Focus)")
        [void]$summaryLines.Add("  Status: MissingFixture")
        [void]$summaryLines.Add("  Diagnostics: not created")
        continue
    }

    $diagnosticsPath = Join-Path $resolvedOutputRoot "$fixtureId.diagnostics.txt"
    $arguments = @(
        "--bannerlord-root `"$BannerlordRoot`"",
        "--prefab `"$($fixtureConfig.Path)`"",
        "--diagnostics-out `"$diagnosticsPath`"",
        "--exit-after-load"
    )
    $startedAt = Get-Date
    $process = Start-Process -FilePath $executablePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $completed = $process.WaitForExit($PerFixtureTimeoutSeconds * 1000)
    $durationSeconds = [math]::Round(((Get-Date) - $startedAt).TotalSeconds, 2)

    if (-not $completed) {
        $hasFailures = $true
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        [void]$summaryLines.Add("- $fixtureId")
        [void]$summaryLines.Add("  Path: $($fixtureConfig.Path)")
        [void]$summaryLines.Add("  Focus: $($fixtureConfig.Focus)")
        [void]$summaryLines.Add("  Status: TimedOut")
        [void]$summaryLines.Add("  DurationSeconds: $durationSeconds")
        if (Test-Path -LiteralPath $diagnosticsPath) {
            $partialContent = Get-Content -Path $diagnosticsPath -Raw
            Add-ProgressSummaryLines -SummaryLines $summaryLines -Content $partialContent
            [void]$summaryLines.Add("  Diagnostics: $(Split-Path -Leaf $diagnosticsPath)")
        }
        else {
            [void]$summaryLines.Add("  Diagnostics: not created")
        }
        continue
    }

    if ($process.ExitCode -ne 0) {
        $hasFailures = $true
        [void]$summaryLines.Add("- $fixtureId")
        [void]$summaryLines.Add("  Path: $($fixtureConfig.Path)")
        [void]$summaryLines.Add("  Focus: $($fixtureConfig.Focus)")
        [void]$summaryLines.Add("  Status: Failed")
        [void]$summaryLines.Add("  ExitCode: $($process.ExitCode)")
        [void]$summaryLines.Add("  DurationSeconds: $durationSeconds")
        if (Test-Path -LiteralPath $diagnosticsPath) {
            $failedContent = Get-Content -Path $diagnosticsPath -Raw
            Add-ProgressSummaryLines -SummaryLines $summaryLines -Content $failedContent
            [void]$summaryLines.Add("  Diagnostics: $(Split-Path -Leaf $diagnosticsPath)")
        }
        else {
            [void]$summaryLines.Add("  Diagnostics: not created")
        }
        continue
    }

    if (-not (Test-Path -LiteralPath $diagnosticsPath)) {
        $hasFailures = $true
        [void]$summaryLines.Add("- $fixtureId")
        [void]$summaryLines.Add("  Path: $($fixtureConfig.Path)")
        [void]$summaryLines.Add("  Focus: $($fixtureConfig.Focus)")
        [void]$summaryLines.Add("  Status: MissingDiagnostics")
        [void]$summaryLines.Add("  DurationSeconds: $durationSeconds")
        [void]$summaryLines.Add("  Diagnostics: not created")
        continue
    }

    $content = Get-Content -Path $diagnosticsPath -Raw
    $completionStatus = Get-LineValue -Content $content -Label "CompletionStatus"
    $reportedStatus = switch ($completionStatus) {
        "Complete" { "Completed" }
        "Partial" {
            $hasFailures = $true
            "Partial"
        }
        "Failed" {
            $hasFailures = $true
            "Failed"
        }
        default {
            $hasFailures = $true
            "InvalidCompletionStatus"
        }
    }
    [void]$summaryLines.Add("- $fixtureId")
    [void]$summaryLines.Add("  Path: $($fixtureConfig.Path)")
    [void]$summaryLines.Add("  Focus: $($fixtureConfig.Focus)")
    [void]$summaryLines.Add("  Status: $reportedStatus")
    [void]$summaryLines.Add("  CompletionStatus: $(Get-LineValueOrDefault -Content $content -Label "CompletionStatus" -DefaultValue "Missing")")
    [void]$summaryLines.Add("  TruncationReason: $(Get-LineValueOrDefault -Content $content -Label "TruncationReason" -DefaultValue "None")")
    [void]$summaryLines.Add("  DurationSeconds: $durationSeconds")
    Add-ProgressSummaryLines -SummaryLines $summaryLines -Content $content
    [void]$summaryLines.Add("  RootNode: $(Get-LineValue -Content $content -Label "RootNode")")
    [void]$summaryLines.Add("  Nodes: $(Get-LineValueOrDefault -Content $content -Label "Nodes" -DefaultValue "0")")
    [void]$summaryLines.Add("  Bindings: $(Get-LineValueOrDefault -Content $content -Label "Bindings" -DefaultValue "0")")
    [void]$summaryLines.Add("  ResolvedRefs: $(Get-LineValueOrDefault -Content $content -Label "Resolved" -DefaultValue "0")")
    [void]$summaryLines.Add("  UnresolvedRefs: $(Get-LineValueOrDefault -Content $content -Label "Unresolved" -DefaultValue "0")")
    [void]$summaryLines.Add("  MeasureFallbacks: $(Get-LineValueOrDefault -Content $content -Label "MeasureFallbacks" -DefaultValue "0")")
    [void]$summaryLines.Add("  WarningCount: $(Get-WarningCount -Content $content)")
    [void]$summaryLines.Add("  Diagnostics: $(Split-Path -Leaf $diagnosticsPath)")
}

$summaryPath = Join-Path $resolvedOutputRoot "summary.md"
[System.IO.File]::WriteAllText(
    $summaryPath,
    ($summaryLines -join [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Phase 9 validation output written to:"
Write-Host "  $resolvedOutputRoot"
Write-Host "Summary:"
Write-Host "  $summaryPath"

if ($hasFailures) {
    exit 1
}

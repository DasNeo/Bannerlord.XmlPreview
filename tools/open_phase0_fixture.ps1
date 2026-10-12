param(
    [string]$Fixture,
    [switch]$List,
    [switch]$PrintOnly,
    [switch]$Build,
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$BannerlordRoot = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
)

$fixtures = [ordered]@{
    "standard-window" = @{
        Path = Join-Path $BannerlordRoot "Modules\Native\GUI\Prefabs\Standard\Standard.Window.xml"
        Checks = @(
            "Window is preserved as the visual root.",
            "The root hint and root measurement are visible in Diagnostics."
        )
    }
    "standard-background" = @{
        Path = Join-Path $BannerlordRoot "Modules\Native\GUI\Prefabs\Standard\Standard.Background.xml"
        Checks = @(
            "Brush and sprite resolution remains inspectable.",
            "A simple container case for detecting early layout regressions."
        )
    }
    "standard-scrollable-panel" = @{
        Path = Join-Path $BannerlordRoot "Modules\Native\GUI\Prefabs\Standard\Standard.ScrollablePanel.xml"
        Checks = @(
            "ClipRect, InnerPanel, and Scrollbar references are resolved.",
            "The layout section shows sizes and Canvas positions of the key elements."
        )
    }
    "land-management" = @{
        Path = Join-Path $RepoRoot "fixtures\AgricultureEstate\GUI\Prefabs\LandManagement.xml"
        Checks = @(
            "ListPanel and Button drift remains easy to spot.",
            "A useful reference case for button cropping and text centering."
        )
    }
    "tournament-scrollable-panel" = @{
        Path = Join-Path $BannerlordRoot "Modules\SandBox\GUI\Prefabs\Tournament\Tournament.xml"
        Checks = @(
            "Placeholder case: supplied children are inserted at the LogicalChildrenLocation of the expanded Standard.ScrollablePanel.",
            "The ClipRect, InnerPanel, and Scrollbar structure remains inspectable after include insertion."
        )
    }
    "options-top-panel" = @{
        Path = Join-Path $BannerlordRoot "Modules\Native\GUI\Prefabs\Options\SPOptions\Options.xml"
        Checks = @(
            "Root fallback case: supplied children are inserted at the expanded root of Standard.TopPanel.",
            "A missing placeholder no longer produces a separate warning for this case."
        )
    }
}

function Show-FixtureList {
    foreach ($entry in $fixtures.GetEnumerator()) {
        Write-Host "$($entry.Key)`n  $($entry.Value.Path)"
    }
}

if ($List -or [string]::IsNullOrWhiteSpace($Fixture)) {
    Show-FixtureList
    return
}

if (-not $fixtures.Contains($Fixture)) {
    Write-Error "Unknown fixture '$Fixture'. Use -List to see the available ids."
    exit 1
}

$selected = $fixtures[$Fixture]

if ($PrintOnly) {
    Write-Host "Fixture: $Fixture"
    Write-Host "Path: $($selected.Path)"
    Write-Host "Checks:"
    foreach ($check in $selected.Checks) {
        Write-Host "  - $check"
    }

    return
}

if ($Build) {
    dotnet build (Join-Path $RepoRoot "Bannerlord.XmlPreview.sln") --no-restore
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$executablePath = Join-Path $RepoRoot "bin\Debug\net10.0-windows\Bannerlord.XmlPreview.exe"
if (-not (Test-Path -LiteralPath $executablePath)) {
    Write-Error "Could not find '$executablePath'. Run with -Build first or build the solution manually."
    exit 1
}

if (-not (Test-Path -LiteralPath $selected.Path)) {
    Write-Error "The fixture prefab was not found at '$($selected.Path)'."
    exit 1
}

$arguments = @(
    "--bannerlord-root `"$BannerlordRoot`"",
    "--prefab `"$($selected.Path)`""
)

Start-Process -FilePath $executablePath -ArgumentList $arguments

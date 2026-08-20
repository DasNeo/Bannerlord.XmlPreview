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
            "Window bleibt als visueller Root erhalten.",
            "Root-Hinweis und Root-Messung sind in den Diagnostics sichtbar."
        )
    }
    "standard-background" = @{
        Path = Join-Path $BannerlordRoot "Modules\Native\GUI\Prefabs\Standard\Standard.Background.xml"
        Checks = @(
            "Brush- und Sprite-Aufloesung bleibt nachvollziehbar.",
            "Einfacher Container-Fall fuer fruehe Layout-Regressionen."
        )
    }
    "standard-scrollable-panel" = @{
        Path = Join-Path $BannerlordRoot "Modules\Native\GUI\Prefabs\Standard\Standard.ScrollablePanel.xml"
        Checks = @(
            "ClipRect-, InnerPanel- und Scrollbar-Referenzen werden aufgeloest.",
            "Layout-Section zeigt Groessen und Canvas-Positionen der Kernelemente."
        )
    }
    "land-management" = @{
        Path = Join-Path $BannerlordRoot "Modules\AgricultureEstate\GUI\Prefabs\LandManagement.xml"
        Checks = @(
            "ListPanel-/Button-Drift bleibt schnell sichtbar.",
            "Guter Referenzfall fuer Button-Cropping und Textzentrierung."
        )
    }
    "tournament-scrollable-panel" = @{
        Path = Join-Path $BannerlordRoot "Modules\SandBox\GUI\Prefabs\Tournament\Tournament.xml"
        Checks = @(
            "Placeholder-Fall: uebergebene Children landen an der LogicalChildrenLocation des expandierten Standard.ScrollablePanel.",
            "ClipRect-, InnerPanel- und Scrollbar-Struktur bleibt trotz Include-Insertion nachvollziehbar."
        )
    }
    "options-top-panel" = @{
        Path = Join-Path $BannerlordRoot "Modules\Native\GUI\Prefabs\Options\SPOptions\Options.xml"
        Checks = @(
            "Root-Fallback-Fall: uebergebene Children landen am expandierten Root von Standard.TopPanel.",
            "Fehlender Placeholder erzeugt dafuer keine eigene Warning mehr."
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

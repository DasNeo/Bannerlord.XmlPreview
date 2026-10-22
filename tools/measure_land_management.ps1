param(
    [string]$BannerlordRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$OutputRoot = (Join-Path $PSScriptRoot '../artifacts/issue-1/reproduce'),
    [ValidateRange(5, 100)][int]$Processes = 5,
    [switch]$ReadyToRun
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
Push-Location $repo
$priorGame = $env:BANNERLORD_ROOT
$priorOutput = $env:BANNERLORD_LOADING_ARTIFACTS
try {
    dotnet build Bannerlord.XmlPreview.sln -c Release --no-restore -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $env:BANNERLORD_ROOT = $BannerlordRoot
    $env:BANNERLORD_LOADING_ARTIFACTS = Join-Path $OutputRoot 'warm'
    foreach ($iteration in 1..$Processes) {
        dotnet test tests/Bannerlord.XmlPreview.VisualRegression.Tests -c Release --no-build --no-restore --filter FullyQualifiedName~LandManagementLoadMeasurements
        if ($LASTEXITCODE -ne 0) { throw 'Warm measurements failed.' }
    }
    $app = Join-Path $repo 'src/Bannerlord.XmlPreview/bin/Release/net10.0-windows/Bannerlord.XmlPreview.exe'
    if ($ReadyToRun) {
        $publication = Join-Path $OutputRoot 'published'
        dotnet publish src/Bannerlord.XmlPreview/Bannerlord.XmlPreview.csproj -p:PublishProfile=LoadPerformance -o $publication -m:1
        if ($LASTEXITCODE -ne 0) { throw 'ReadyToRun publication failed.' }
        $app = Join-Path $publication 'Bannerlord.XmlPreview.exe'
    }
    # Warm measurements above use the regular test host. Only these independent
    # WPF app measurements use the publication when -ReadyToRun is specified.
    foreach ($variant in @('fixture', 'installed')) {
        $prefab = if ($variant -eq 'fixture') { Join-Path $repo 'fixtures/AgricultureEstate/GUI/Prefabs/LandManagement.xml' }
            else { Join-Path $BannerlordRoot 'Modules/AgricultureEstate/GUI/Prefabs/LandManagement.xml' }
        foreach ($iteration in 1..$Processes) {
            $output = Join-Path $OutputRoot "$variant-$iteration.txt"
            $process = Start-Process -FilePath $app -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
                '--prefab', ('"' + $prefab + '"'), '--bannerlord-root', ('"' + $BannerlordRoot + '"'),
                '--diagnostics-out', ('"' + $output + '"'), '--exit-after-load')
            if ($process.ExitCode -ne 0) { throw "WPF measurement failed: $output" }
        }
    }
} finally {
    $env:BANNERLORD_ROOT = $priorGame
    $env:BANNERLORD_LOADING_ARTIFACTS = $priorOutput
    Pop-Location
}

# Capture workflow

This is the shared capture reference for the `bannerlord-ui` skill and the human [UI creation guide](https://github.com/DasNeo/Bannerlord.XmlPreview/blob/master/docs/guides/create-ui-with-ai.md). Paths to assets below are relative to this file, so the skill remains usable when copied outside the preview repository.

## Locate the renderer

Use Windows x64 and an installed Bannerlord game. Documented game validation covers v1.4.7; other versions may differ. From [GitHub Releases](https://github.com/DasNeo/Bannerlord.XmlPreview/releases), extract a package containing `Bannerlord.XmlPreview.VisualRegression.Cli.exe`. Prefer self-contained for newcomers; framework-dependent requires .NET 10 Desktop Runtime x64. Keep the extracted files together. Check the actual executable with `--help`; older releases may lack it. The desktop executable opens prefabs but does not export PNG captures.

For a source checkout with .NET 10 SDK, build from the repository root:

```powershell
dotnet build src/Bannerlord.XmlPreview.VisualRegression.Cli -c Debug -m:1
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
$cli = Join-Path (Get-Location) 'src/Bannerlord.XmlPreview.VisualRegression.Cli/bin/Debug/net10.0-windows/Bannerlord.XmlPreview.VisualRegression.Cli.exe'
```

## Prepare a screen and scenario

Keep editable source in the user's mod project. For installed-game capture, copy the working prefab into that user's installed module, e.g. `Modules/MyMod/GUI/Prefabs/MyScreen.xml`. The module must have a valid `SubModule.xml`. For a new project, create a separate module with a unique module Id, Name, and Version, dependency on Native, and an empty SubModules collection when it has no executable code. This is a preview scaffold, not a complete gameplay integration. Read installed module conventions rather than adding unrelated game dependencies.

Copy [scenario-template.json](../assets/scenario-template.json) into the mod project and adapt:

- `PrefabIdentity`: installation-relative path using forward slashes, not an absolute source path. Match the actual installed module and filename.
- `Id`, `Scene`, `DataState`: describe this screen and data state.
- `CaptureProfile`: requested dimensions and a descriptive Id. Supported settings are UI scale 1, English, sRGB, FullFrame, and an RGB background. Omitted settings use these defaults.
- `Bindings`: match XML names without the leading `@`. Values are strings, including `"true"` and `"false"`.
- `Lists` and `WidgetStates`: use exact expanded widget paths from the widget tree or `structure.json`, not guessed XML paths. Each list item needs a unique stable `__key` string.

For example, these collections populate a list and disable a button; replace the selectors and data fields with those of the actual screen:

```json
{
  "Lists": {
    "Window > ListPanel[Id=Workshops]": [
      { "__key": "workshop-1", "Name": "Smithy" }
    ]
  },
  "WidgetStates": {
    "Window > ButtonWidget[Id=Upgrade]": { "IsEnabled": "false" }
  }
}
```

For list syntax, the release/source `docs/images/previews/scenarios/escape-menu.json` is a populated example. When it is unavailable, consult the [source example](https://github.com/DasNeo/Bannerlord.XmlPreview/blob/master/docs/images/previews/scenarios/escape-menu.json). Separate scenarios can represent empty/populated lists, long text, selection, disabled states, and target resolutions as needed.

## Capture

Set these paths to the user's actual locations. `$moduleRoot` is the installed module that owns the prefab; use Native for an initial read-only EscapeMenu capture. For that first check, `$scenarioPath` can point to `docs/images/previews/scenarios/escape-menu.json` in the extracted release or source checkout.

```powershell
$gameRoot = 'C:/path/to/Mount & Blade II Bannerlord'
$cli = 'C:/path/to/extracted-preview/Bannerlord.XmlPreview.VisualRegression.Cli.exe'
$moduleRoot = Join-Path $gameRoot 'Modules/MyMod'
$scenarioPath = 'C:/path/to/mod-project/scenarios/my-screen-initial.json'
$outputRoot = 'C:/path/to/mod-project/artifacts/ai-ui'
$nativeVersion = ([xml](Get-Content -LiteralPath (Join-Path $gameRoot 'Modules/Native/SubModule.xml') -Raw)).Module.Version.value
$moduleVersion = ([xml](Get-Content -LiteralPath (Join-Path $moduleRoot 'SubModule.xml') -Raw)).Module.Version.value
$run = Join-Path $outputRoot ([guid]::NewGuid().ToString('N'))

& $cli capture `
  --scenario $scenarioPath `
  --metadata (Join-Path $run 'metadata') `
  --output $run `
  --bannerlord-root $gameRoot `
  --game-version $nativeVersion `
  --module-version $moduleVersion `
  --mode Full

$captureExitCode = $LASTEXITCODE
Write-Output "Capture exit code: $captureExitCode"
Write-Output "Review artifacts in: $run"
```

Use a new output directory for every capture. The fresh metadata path has no `manifest.json`, so the explicit game/module versions apply. An existing manifest takes precedence over version arguments. Check that both version values came from the appropriate installed modules.

`capture --mode Full` produces image and structural artifacts without requiring a Reference Screenshot or comparing against one. `StructureOnly` omits the image and cannot support visual review. `compare` checks established expectations; `accept` changes baselines and is not part of routine design capture.

## Inspect and diagnose

Exit code 0 means capture completed, not that the design is correct. Exit code 1 needs investigation and can mean Partial capture; a PNG may still exist. Read `capture.json` for completion status and Diagnostics before making claims about the result. If capture failed before writing that file, inspect stderr and the supplied paths/metadata.

Open `actual.png` with the agent's image viewer and assess the requested layout. Read `structure.json` for the expanded widget hierarchy, `layout.json` for measured bounds, and `behavior-trace.json` when behavior needs investigation. A visible screenshot or successful process alone does not establish fidelity. Identify placeholders and unresolved warnings, and state what must be verified in game.

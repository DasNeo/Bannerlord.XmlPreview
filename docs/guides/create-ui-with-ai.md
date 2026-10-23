# Create Bannerlord UIs with an AI coding agent

Bannerlord XML Preview lets an AI coding agent see the UI it is building. You describe a screen, the agent writes Gauntlet XML, the preview tool renders it to a PNG, and the agent inspects that image before making the next change. You can review the same images and steer the design throughout the process.

This works best with an agent that can edit local files, run commands on Windows, and open local images. A text-only assistant can help write XML, but it needs you to provide visual feedback.

## Let your AI agent guide you

If you are unfamiliar with mod projects, XML, or command-line tools, paste the following prompt into your AI coding agent. It asks the agent to work through this guide with you and perform the setup it can handle. You do not need to understand every section before starting.

```text
Help me create a Bannerlord UI using Bannerlord.XmlPreview. I am new to
this workflow, so guide me from setup to a first rendered preview.

Read this guide before starting:
https://github.com/DasNeo/Bannerlord.XmlPreview/blob/master/docs/guides/create-ui-with-ai.md
If you cannot access it, ask me to provide the guide.

Start by asking what screen I want to build and whether I already have
a mod project. Ask only the questions needed for the next step, using
plain language. Explain terms such as prefab, binding, and scenario
briefly when we first need them.

Check which tools and files you can access. Locate my Bannerlord
installation, preview release package, and mod project where possible;
ask me for paths you cannot find. If something is missing, help me set
it up using the guide. Prefer the self-contained release for a first
preview. If I have no mod project, help me create a minimal working
module for the preview.

Perform the file edits and commands you can run yourself. For steps
only I can do, give me specific instructions and wait for the result.
Keep Native files unchanged and work in my own mod files.

Start with a simple version of my screen and sample data. Render it
with the CLI, read capture.json, and inspect actual.png. Show me the
image and ask for design feedback before expanding the screen. If
you cannot run commands or view images, explain what I need to run
or share so we can continue.

As we refine the design, check the requested states and resolutions.
Explain incomplete captures and preview limitations. Distinguish
sample data from working game integration, and finish by explaining
what still needs to be connected and tested in Bannerlord.
```

The remaining sections are the detailed reference for you and your agent. The first goal is a rendered image you can discuss together; a working screen in the game also needs data and interaction code.

## What you need

- Windows x64 and a local Bannerlord installation for game assets.
- An AI coding agent with terminal access and image viewing.
- A mod project or working copy where the agent can create and edit UI files.
- A Bannerlord.XmlPreview release package containing `Bannerlord.XmlPreview.VisualRegression.Cli.exe`. Choose the self-contained package to get its runtime included, or install .NET 10 Desktop Runtime x64 for the framework-dependent package.

**Both release packages include the rendering CLI alongside the desktop application.** Extract the whole ZIP and keep the files together. Run `Bannerlord.XmlPreview.VisualRegression.Cli.exe` for automatic PNG capture. The desktop application's `--prefab` and `--diagnostics-out` options can open a prefab and export Diagnostics, but do not export a preview PNG. Older releases may lack the rendering CLI; use a release that includes it or build from source as described below.

The documented game validation covers Bannerlord v1.4.7. Other versions may produce different results.

## 1. Give the agent a design brief

Describe the screen's purpose, contents, layout, and expected states. Include a sketch or example image if you have one. Identify the game installation and the mod files the agent may edit.

For example:

> Create a Bannerlord Gauntlet UI for managing a settlement's workshops. Use the game's existing visual style: a centered panel, a title at the top, a scrollable workshop list on the left, details on the right, and Upgrade and Close buttons at the bottom. Include workshop name, level, income, and upgrade cost. Preview an empty list, three workshops, a selected workshop with a long name, and an upgrade the player cannot afford. Start at 1920 x 1080 and also check 1280 x 720. Work in my mod's prefab files and show me rendered images as you iterate.

Tell the agent whether you want just the visual prefab or also the game-side code that opens the screen and supplies its data. Preview data can make a screen look populated before that integration exists.

## 2. Set up image capture

You can ask your agent to do this setup. Extract a release ZIP and open PowerShell in the extracted application folder. Confirm the CLI starts:

```powershell
.\Bannerlord.XmlPreview.VisualRegression.Cli.exe --help
```

Try a capture of the included EscapeMenu example before generating your own UI. Run these commands from the extracted application folder, changing the game path as needed:

```powershell
$game = 'C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord'
$cli = Join-Path (Get-Location) 'Bannerlord.XmlPreview.VisualRegression.Cli.exe'
$nativeVersion = ([xml](Get-Content -LiteralPath "$game/Modules/Native/SubModule.xml" -Raw)).Module.Version.value
$run = Join-Path 'artifacts/ai-ui' ([guid]::NewGuid().ToString('N'))

& $cli capture `
  --scenario docs/images/previews/scenarios/escape-menu.json `
  --metadata artifacts/ai-ui/metadata `
  --output $run `
  --bannerlord-root $game `
  --game-version $nativeVersion `
  --module-version $nativeVersion `
  --mode Full

$captureExitCode = $LASTEXITCODE
Write-Output "Capture exit code: $captureExitCode"
Write-Output "Review artifacts in: $run"
```

Use an unused metadata path with no `manifest.json` for this example. The explicit version arguments provide the metadata; an existing manifest takes precedence over those arguments. Each run needs a new output directory.

Ask the agent to open `$run/actual.png` using its image-viewing tool. Also have it read `capture.json`, which records completion status and Diagnostics. Exit code 0 means the capture completed; exit code 1 requires investigation and can indicate a Partial capture. Some incomplete runs still produce images, so the existence of a PNG alone is not enough.

Here, `capture --mode Full` requests pixels as well as structural artifacts. It does not require an in-game Reference Screenshot or compare against one. Use `capture` for the design loop; `compare` is for checking against previously established expectations.

### Alternative: build from source

Developers can use the .NET 10 SDK to build from the repository root:

```powershell
dotnet build src/Bannerlord.XmlPreview.VisualRegression.Cli -c Debug -m:1
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
$cli = Join-Path (Get-Location) 'src/Bannerlord.XmlPreview.VisualRegression.Cli/bin/Debug/net10.0-windows/Bannerlord.XmlPreview.VisualRegression.Cli.exe'
```

Run the capture example from the repository root with this `$cli` value. The sample scenario has the same relative path in the source checkout and release ZIP.

## 3. Point the preview at your UI

The agent needs two inputs: your prefab XML and a **Preview Scenario**, a JSON file containing the resolution, sample data, and widget states used for that render.

For the installed-game capture workflow, put the working prefab inside your own installed mod module, such as `Modules/MyMod/GUI/Prefabs/WorkshopManager.xml`. The module must be discoverable and have a valid `SubModule.xml`. Keep the editable source in your mod project and copy changes into that module as needed. Leave Native files unchanged.

Have the agent create a scenario like this, replacing the module and prefab names with yours:

```json
{
  "SchemaVersion": 1,
  "Id": "workshop-manager-populated",
  "PrefabIdentity": "modules/mymod/gui/prefabs/workshopmanager.xml",
  "CaptureProfile": {
    "Id": "workshop-1920x1080",
    "Width": 1920,
    "Height": 1080
  },
  "Scene": "Workshop management screen",
  "DataState": "Initial layout with sample title",
  "Bindings": {
    "Title": "Workshop Management"
  },
  "Lists": {},
  "WidgetStates": {}
}
```

`Title` is only an example: bindings must match the names used by your XML. The prefab identity is relative to the Bannerlord installation, uses forward slashes, and is not an absolute file path. Use the installed mod's actual version from its `SubModule.xml` for `--module-version`; keep `--game-version` tied to the game's Native version.

Use the same capture command with your scenario path and a fresh output directory. The agent should populate additional bindings and template lists as your design develops. List and widget-state selectors must match expanded widget paths exactly; use the widget tree or captured structure to find them. Each list entry needs a unique, stable `__key` value. The [EscapeMenu scenario](../images/previews/scenarios/escape-menu.json) provides a populated-list example.

Keep separate scenarios for empty, populated, long-text, selected, and disabled states, plus the resolutions you want to check. Use Capture Profiles with UI scale 1, English, sRGB, and FullFrame; those are the currently accepted settings.

## 4. Ask the agent to render, inspect, and revise

After giving it your design brief and file paths, paste this instruction:

```text
Use Bannerlord.XmlPreview's rendering CLI to develop this UI iteratively.

Read docs/guides/create-ui-with-ai.md in the extracted preview package
or source repository for setup.
Work in my mod's UI files and keep Native files unchanged. Reuse existing
Gauntlet widgets, brushes, and sprites where appropriate.

Create Preview Scenarios for the requested data states and resolutions.
After each meaningful XML change, run capture with --mode Full into a new
output directory. Read capture.json and open actual.png with your image
viewer. Check clipping, overlap, alignment, spacing, text readability,
list overflow, and button states. Use structure.json and layout.json to
investigate problems that the image alone does not explain.

Fix the problems you find, then capture and inspect again. Keep the
scenario data fixed when comparing a layout change. Explain any Partial
results, placeholders, or unresolved Diagnostics. If a preview limitation
prevents verification, identify it and the in-game check needed.

Show me the final images, changed prefab files, scenarios, and remaining
game-side integration tasks. Distinguish preview sample data from working
game bindings and commands. Finish when all requested states and resolutions
have been reviewed and each remaining issue is explicitly identified.
```

You can then give feedback against a specific image: “Keep the left list width, increase the details panel padding, and move the cost above the Upgrade button.” Saving each attempt in a separate directory lets you compare designs and return to an earlier version.

The CLI produces these useful files:

| File | What it tells you |
| --- | --- |
| `actual.png` | The rendered UI for visual review. |
| `capture.json` | The scenario identity, completion status, Diagnostics, and artifact hashes. |
| `structure.json` | The expanded Render Structure. |
| `layout.json` | Measured Key Bounds for investigating layout. |
| `behavior-trace.json` | Widget Behavior information for deeper investigation. |

## 5. Review the UI in game

Treat the preview as a way to shorten the design loop. The final check is still the screen running in Bannerlord with its real data and interactions.

Have the agent explain which ViewModel properties, list items, and commands the XML expects. Connect those to your mod, then test opening and closing the screen, selection, scrolling, button actions, disabled states, long text, and your target resolutions. Verify that actions change the intended game state.

Custom executable Code Widgets appear as labeled placeholders because the preview does not execute third-party mod code. Portraits, banners, and scene-dependent images may also remain Partial without suitable scenario inputs. Resolve those checks in game. A successful capture does not establish pixel-perfect agreement with Bannerlord or prove that game-side actions work.

Once you have a reviewed design, you can add regression comparisons to catch unintended changes. See the [Visual Regression workflow](https://github.com/DasNeo/Bannerlord.XmlPreview/blob/main/test-data/visual-regression/README.md) for Structure Only checks and optional calibrated in-game image comparisons. Baseline Acceptance is a separate, explicit step; routine design captures do not update accepted expectations.

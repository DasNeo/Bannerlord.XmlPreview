# Create Bannerlord UIs with an AI coding agent

Bannerlord XML Preview lets an AI coding agent see the UI it is building. You describe a screen, the agent writes Gauntlet XML, the preview tool renders it to a PNG, and the agent inspects that image before making the next change. You can review the same images and steer the design throughout the process.

This works best with an agent that can edit local files, run commands on Windows, and open local images. A text-only assistant can help write XML, but it needs you to provide visual feedback.

## Optional: install the Bannerlord UI skill

The reusable [bannerlord-ui skill](../../skills/bannerlord-ui/SKILL.md) teaches a compatible coding agent this workflow, including setup, prefab editing, rendered image inspection, and the final integration handoff. It uses the same [capture reference](../../skills/bannerlord-ui/references/capture-workflow.md) as this guide. The skill contains instructions and a scenario template; it requires the preview tool and a local game installation separately.

For Codex, copy the whole `skills/bannerlord-ui` folder from this repository or a release that includes it into your mod project's `.agents/skills/` directory. In PowerShell, from the repository root or extracted release folder:

```powershell
$modProject = 'C:/path/to/your-mod-project'
$skillDestination = Join-Path $modProject '.agents/skills/bannerlord-ui'
if (Test-Path -LiteralPath $skillDestination) { throw 'A skill already exists here; review it before updating.' }
New-Item -ItemType Directory -Path (Split-Path $skillDestination) -Force | Out-Null
Copy-Item -LiteralPath ./skills/bannerlord-ui -Destination $skillDestination -Recurse
```

For personal installation across projects in Codex, use `Join-Path $env:USERPROFILE '.agents/skills/bannerlord-ui'` as the destination instead. Other agents may use different skill locations; follow their installation instructions and copy the complete folder, including `references/` and `assets/`.

Open your mod project in a new agent session, then ask:

> Use $bannerlord-ui to help me build a workshop management screen. I am new to Gauntlet UI work; guide me through setup and show me a first rendered preview.

You can also ask it to edit an existing prefab or inspect a specific resolution. Compatible agents may select the skill automatically when its description matches the task. You can use the copyable prompt below without installing a skill.

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

You can ask your agent to do this setup. Follow the shared [capture workflow](../../skills/bannerlord-ui/references/capture-workflow.md#locate-the-renderer) for release and source-build options, then use its [capture command](../../skills/bannerlord-ui/references/capture-workflow.md#capture).

For a first check, capture the included `docs/images/previews/scenarios/escape-menu.json` and use Native as the owning module. Ask the agent to read `capture.json` and open `actual.png`. The capture reference explains exit codes, fresh output directories, version metadata, and the difference between capture and comparison.

## 3. Point the preview at your UI

The renderer needs your prefab XML and a **Preview Scenario**: a JSON file defining the resolution, sample data, and widget states for that render.

Follow [Prepare a screen and scenario](../../skills/bannerlord-ui/references/capture-workflow.md#prepare-a-screen-and-scenario) and adapt the [scenario template](../../skills/bannerlord-ui/assets/scenario-template.json) in your mod project. Keep editable source there and copy working prefabs into your own installed module for capture. Leave Native files unchanged.

Bindings must match the names in your XML. List and widget-state selectors must match expanded widget paths; the capture reference explains how to find them. Add states and resolutions as the design develops, rather than trying to model every possible state before the first preview.

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

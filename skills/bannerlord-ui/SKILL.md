---
name: bannerlord-ui
description: "Create, edit, or visually check Bannerlord Gauntlet XML UIs using Bannerlord.XmlPreview captures. Use for mod UI layout work, preview data, or guided setup of this rendering workflow."
---

# Bannerlord UI

Build the requested Gauntlet UI through actual rendered images, using the user's mod files and installed game assets. The rendering CLI is a separate local prerequisite, not included in this skill.

## Establish the task

Determine whether the user needs a new screen, an edit, or a visual check. Use supplied paths and requirements; ask only for missing information needed to proceed. For newcomers, explain unfamiliar terms as they arise and reach a simple first preview before expanding the design. Experienced users can proceed directly to their requested change or check.

Locate the user's mod project, Bannerlord installation, and `Bannerlord.XmlPreview.VisualRegression.Cli.exe`. Read [capture-workflow.md](references/capture-workflow.md) before first setup or capture; it contains the release/source options, scenario rules, exact command, and artifact interpretation. Reuse a working setup on later iterations.

Use an agent with Windows command execution, file editing, and image viewing. If a capability is unavailable, give the user the specific command or image to provide and identify which verification remains outstanding.

## Author and iterate

- Work in the user's own module. Keep Native and unrelated mods unchanged. If no project exists, establish a separate minimal module as described in the capture reference. Preserve existing source edits when copying into an installed working module.
- Reuse installed Gauntlet widgets, brushes, sprites, and prefab patterns. Clarify whether the request includes game-side integration; preview data alone does not implement it.
- Adapt [the scenario template](assets/scenario-template.json) for a new screen, or reuse the user's existing Preview Scenario. Create the states and resolutions relevant to the request, keeping sample data fixed when assessing a layout change.
- After each meaningful layout change, capture into a fresh directory. Read `capture.json` and open `actual.png` with an image-viewing tool. Check clipping, overlap, alignment, spacing, text readability, list overflow, and requested widget states. Use `structure.json` and `layout.json` to investigate problems the image cannot explain.
- Fix observed problems and capture again. If a preview limitation prevents further verification, report the limitation and the in-game check needed instead of repeatedly changing otherwise plausible XML. Present a first design to a newcomer for feedback; apply already specified changes without adding a mandatory approval at every iteration.

## Finish with evidence

For every requested state and resolution, provide the reviewed image and capture status, or the specific reason it could not be checked. Summarize changed prefabs, scenario paths, unresolved Diagnostics, and remaining ViewModel bindings, commands, or game-side tasks. A completed capture is not proof of in-game fidelity or working interactions.

Code Widgets remain placeholders because the preview does not execute third-party mod code. Portraits, banners, and runtime images may remain Partial. Keep these limitations visible in the handoff. Use `capture` for design iteration; baseline acceptance is separate work requiring an explicit request.

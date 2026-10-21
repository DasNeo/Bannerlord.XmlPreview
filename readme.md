# Bannerlord.XmlPreview

Preview and inspect Mount & Blade II: Bannerlord Gauntlet XML prefabs without launching the game. Open a prefab, explore its widget tree, adjust preview data and resolution, and investigate layout, asset, binding, and reference issues.

## Download and install

You need Windows x64 and a local Bannerlord installation for game assets. Game assets are not included. Validation currently covers Bannerlord v1.4.7, with AgricultureEstate v1.4.7.3 coverage; other versions may differ.

Download a ZIP from [GitHub Releases](https://github.com/DasNeo/Bannerlord.XmlPreview/releases):

| Package | Choose this when |
| --- | --- |
| `win-x64-self-contained.zip` | You want the included .NET desktop runtime. |
| `win-x64-framework-dependent.zip` | You already have the .NET 10 Desktop Runtime x64 installed and want a smaller download. |

Extract the entire ZIP to a folder and run `Bannerlord.XmlPreview.exe`. Each ZIP has a matching SHA-256 checksum file.

## Open your first preview

1. Start `Bannerlord.XmlPreview.exe`.
2. In the **Bannerlord** field, enter your game installation directory, or use **Browse**. Select **Apply**.
3. Select **Open Prefab** and choose a Gauntlet XML prefab from the game or a mod. For example, open `Modules\Native\GUI\Prefabs\Standard\Standard.Window.xml` inside your Bannerlord installation.
4. Explore the preview and widget tree to inspect layout and Diagnostics. Change the **Preview** width and height to check another resolution.

Assets load directly from installed module TPAC packages. Verified editable mod packages can use their RuntimeDataCache companion; manual DDS export is not required.

## Work on a prefab

Edit the XML in your preferred editor. The app watches loaded dependencies; use **Reload** to request a fresh preview. If loading fails, the **Last Good Preview** remains visible and is marked as stale. Check Diagnostics for the failure before relying on that view.

Use **Scenario** controls to create, save, clone, rename, or delete a Preview Scenario. A scenario preserves preview data, widget states, and resolution so you can return to the same setup. Bindings and list contents are editable in the app. Save changes before switching scenarios.

Scenarios are stored in `%LOCALAPPDATA%\Bannerlord.XmlPreview\Scenarios`. Use **Edit Scenario** to edit JSON for widget states, dynamic image masks, and reference capture settings. To enable editor navigation, set `BANNERLORD_PREVIEW_EDITOR` to an editor executable that accepts `--goto file:line:column`; otherwise source actions copy the location.

## Compare with a reference

**Compare Reference** opens an image comparison for the current scenario, with reference, actual, side-by-side, overlay, and diff views. Shared zoom and pan help inspect differences. Its uncalibrated default does not certify in-game fidelity.

**Open Comparison Report** opens saved Visual Regression artifacts for the active saved scenario. A Full run includes pixel comparison; a Structure Only report records pixels as `NotRun` and has no image comparison views. See the [Visual Regression workflow](https://github.com/DasNeo/Bannerlord.XmlPreview/blob/main/test-data/visual-regression/README.md) for preparing reference packages and running comparisons.

## Understand preview limits

The preview helps investigate prefabs, but a successful structural check does not establish pixel-perfect agreement with the game. Check the [image fidelity matrix](docs/fidelity/images.md) for verified behavior and known limitations.

- Mod-specific executable Code Widgets appear as labeled placeholders. The app does not execute their code.
- Images that require live game state need explicit scenario data. Unavailable runtime content remains visibly `Partial`.
- Missing assets or references appear in Diagnostics. Confirm the installation path and relevant module assets when a preview is incomplete.

When reporting a problem, include the game and mod versions, prefab path, Preview Scenario, Diagnostics, and expected behavior. [Report an issue](https://github.com/DasNeo/Bannerlord.XmlPreview/issues).

## Command line

From the extracted application folder:

```powershell
.\Bannerlord.XmlPreview.exe `
  --bannerlord-root "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord" `
  --prefab "C:\path\to\YourPrefab.xml"
```

| Argument | Purpose |
| --- | --- |
| `--bannerlord-root <path>` | Set the Bannerlord installation directory. |
| `--prefab <path>` | Load a prefab directly. |
| `--diagnostics-out <path>` | Write a Diagnostics snapshot. |
| `--exit-after-load` | Exit after the technical validation run. |

## Development

For building, testing, architecture, and release instructions, see [Contributing](https://github.com/DasNeo/Bannerlord.XmlPreview/blob/main/CONTRIBUTING.md).

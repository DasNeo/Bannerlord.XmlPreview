# README preview captures

Captured on 2026-10-22 with the production loader, asset catalog, and renderer through the Visual Regression CLI. These are direct renderer PNGs, with no image retouching. Each is a full 1920 × 1080 frame, UI scale 1, English, sRGB, composited onto black. No in-game reference has been supplied or accepted.

`manifest.json` records prefab and PNG hashes, versions, and file locations. `scenarios/` preserves the exact Preview Scenarios. `captures/` preserves the CLI capture metadata and Diagnostics, including scenario and output hashes. Structural artifacts and exploratory candidates remain in the ignored `artifacts/readme/` directory.

| Example | Source and preview state | Capture result |
| --- | --- | --- |
| Land Management | Installed `Modules/AgricultureEstate/GUI/Prefabs/LandManagement.xml`, AgricultureEstate v1.4.7.3. Uses explicit estate labels, values, descriptions, costs, and static sprites. This is the installed prefab, not the older preserved repository fixture. | `Complete`, no Diagnostics. Completion does not establish in-game fidelity. |
| Tournament leaderboard | `Modules/Native/GUI/Prefabs/GameMenu/GameMenuTournamentLeaderboard.xml`, Native v1.4.7. Three illustrative heroes with 24, 18, and 12 victories; third row highlighted as the player. Runtime portrait/banner images are absent and their widgets hidden by the scenario. | `Partial`: missing image identities are still reported for each row. The absent horizontal scrollbar has an explicit scenario explanation. |
| Escape menu | `Modules/Native/GUI/Prefabs/EscapeMenu.xml`, Native v1.4.7. Eight menu entries and a single fixed game tip with tip navigation disabled. | `Complete`, no Diagnostics. The backdrop's `AlphaFactor=0.4` applies independently of the menu and tip content. |

The EscapeMenu capture was regenerated for issue #8. Inspection of the installed v1.4.7 `TaleWorlds.GauntletUI.dll` confirms that `Widget.OnRender` multiplies the sprite material's alpha by the widget's `AlphaFactor` and `ContextAlpha`; `Widget.Render` then renders children independently without propagating the parent's `AlphaFactor`. Pixel regression cases verify backdrop translucency, explicit child alpha, and brush-layer alpha. No in-game pixel comparison has been performed.

Town Management, general Options, and Campaign Options were also inspected. Their current captures contained incomplete controls, placeholders, or empty sections, so the simpler EscapeMenu was selected for the third README example. These choices describe visual review, not a certification of Correctly Rendered Prefabs.

## Reproduce

From the repository root, build the capture CLI:

```powershell
dotnet build src/Bannerlord.XmlPreview.VisualRegression.Cli/Bannerlord.XmlPreview.VisualRegression.Cli.csproj -m:1
$game = 'C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord'
$cli = 'src/Bannerlord.XmlPreview.VisualRegression.Cli/bin/Debug/net10.0-windows/Bannerlord.XmlPreview.VisualRegression.Cli.dll'
foreach ($id in 'land-management', 'tournament-leaderboard', 'escape-menu') {
    $moduleVersion = if ($id -eq 'land-management') { 'v1.4.7.3' } else { 'v1.4.7' }
    dotnet $cli capture --scenario "docs/images/previews/scenarios/$id.json" `
        --metadata artifacts/readme/no-reference-metadata `
        --game-version v1.4.7 --module-version $moduleVersion `
        --output "artifacts/readme/reproduced-$id" --bannerlord-root $game --mode Full
}
```

Use fresh output directories. The metadata path above intentionally has no reference manifest. `Full` requests PNG capture here; no comparison or baseline acceptance runs. The leaderboard returns exit code 1 for its documented `Partial` status while retaining its PNG and Diagnostics. Inspect those results before replacing the checked-in examples.

## Add the in-game screenshots

Capture the same screens at 1920 × 1080, UI scale 1, in English, keeping the full frame. Record game/mod versions, visible data, selections, and scroll position. Supply the original PNGs; the preview scenarios can then be adjusted to match the real values. The leaderboard's portraits and banners and the live scene backgrounds will still require separate consideration.

Place the supplied images beside these previews using `land-management-ingame.png`, `tournament-leaderboard-ingame.png`, and `escape-menu-ingame.png`, then replace the README's pending cells with links. Preserve any remaining visible differences in the captions. These documentation examples do not automatically become Visual Regression baselines.

# M4 Steps to Repeat (StR)

Use this procedure to test the authoring loop. Record each case as Passed, Failed or NotRun, with the observed result. M4 was accepted on 2026-10-21 using combined automated, installed structural and live smoke evidence; see [M4 completion](../reports/m4-completion.md). Acceptance does not turn unexecuted checks into Passed results.

Recorded execution: [2026-10-21 retest results](../reports/m4-retest-2026-10-21.md), including automated totals, exercised WPF cases and remaining NotRun cases.

## 1. Build and run automated acceptance

From the repository root in PowerShell:

```powershell
dotnet restore ./Bannerlord.XmlPreview.sln
dotnet build ./Bannerlord.XmlPreview.sln --no-restore -m:1
./tools/run_m4_validation.ps1
```

If Bannerlord is installed elsewhere, pass `-BannerlordRoot 'D:\path\to\Bannerlord'`. The installed checks require Native and AgricultureEstate assets. Expected: the solution tests and all three installed Structure Only comparisons pass; optional in-game Reference Package coverage may be skipped. The structural reports must show pixels `NotRun`. Retain `artifacts/m4-validation/` and the test output.

For a focused portable M4 run without installed-asset acceptance:

```powershell
dotnet test ./Bannerlord.XmlPreview.sln --no-restore -m:1 --filter 'FullyQualifiedName~AuthoringTests|FullyQualifiedName~DependencyWatcherTests|FullyQualifiedName~ScenarioRepositoryTests|FullyQualifiedName~ImageAssetTests.DependencyInvalidationReloadsMetadataAndImagePixels'
```

Expected: scenario persistence/rendering, dependency batches, Last Good Preview recovery, cancellation, source navigation, asset invalidation and comparison-window tests pass. This focused run does not replace installed acceptance or manual visual review.

## 2. Create a disposable manual fixture

Run from the repository root. Use a fresh directory and fresh scenario IDs for each run; do not overwrite scenarios from a previous test.

```powershell
$repoRoot = (Get-Location).Path
$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$smokeRoot = Join-Path $repoRoot "artifacts/m4-manual/$runId"
New-Item -ItemType Directory -Path $smokeRoot | Out-Null
@'
<Prefab>
  <Window>
    <Widget Id="smoke-root" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="640" SuggestedHeight="360" Color="#264461FF">
      <Children>
        <TextWidget Id="greeting" Text="@Greeting" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="300" SuggestedHeight="40" />
        <M4Smoke.Part />
        <ListPanel Id="rows" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="300" SuggestedHeight="120" MarginTop="100">
          <ItemTemplate>
            <TextWidget Id="row" Text="@Name" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="200" SuggestedHeight="30" />
          </ItemTemplate>
        </ListPanel>
      </Children>
    </Widget>
  </Window>
</Prefab>
'@ | Set-Content -LiteralPath (Join-Path $smokeRoot 'M4Smoke.xml') -Encoding utf8
@'
<Prefab>
  <Window>
    <Widget Id="include-panel" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="300" SuggestedHeight="40" MarginTop="50">
      <Children>
        <TextWidget Id="include-label" Text="Include version 1" WidthSizePolicy="StretchToParent" HeightSizePolicy="StretchToParent" />
      </Children>
    </Widget>
  </Window>
</Prefab>
'@ | Set-Content -LiteralPath (Join-Path $smokeRoot 'M4Smoke.Part.xml') -Encoding utf8
```

Optionally set `$env:BANNERLORD_PREVIEW_EDITOR` to the full path of an editor executable accepting `--goto file:line:column`, before starting the app. Without it, source actions copy the location to the clipboard; paste into a text editor to check it. Use **Copy Location** to test the explicit clipboard action too.

Start the app from the same shell:

```powershell
& ./bin/Debug/net10.0-windows/Bannerlord.XmlPreview.exe --prefab (Join-Path $smokeRoot 'M4Smoke.xml')
```

Expected: status becomes `Loaded M4Smoke.xml`, the Node Tree includes `include-panel`, and the preview is not stale. Set the Preview width/height controls to `640` and `360`, then click **Apply Size**. Expand **Diagnostics**, **Bindings** and **Mocks** as needed. Record the initial reload generation and any Diagnostics before editing.

## 3. Persist and switch Preview Scenarios

1. Enter `m4-smoke-<runId>-a` in the identity field beside the Scenario selector, replacing `<runId>` with the run ID above. Click **Create** and wait for loading to finish.
2. Change the `Greeting` binding to `Scenario A`; leave the field so the edit commits. In Mocks, edit the row's `Name`, add a second row, and give it a different name. Wait for the preview to update.
3. Check that Scenario Status shows **Unsaved changes**. Click **Save**; it should show **Saved**. Inspect `%LOCALAPPDATA%/Bannerlord.XmlPreview/Scenarios/<scenario-id>.json`: bindings and list rows must match the UI, and each row must have a unique `__key`.
4. Enter `m4-smoke-<runId>-b` in the identity field and click **Clone**. Change `Greeting` to `Scenario B`, set the resolution to `800 × 450`, click **Apply Size**, wait, then **Save**.
5. Switch between A and B. Expected: each scenario restores its binding, list values and resolution; Diagnostics identify the active scenario and schema version. Try switching while a binding is dirty: switching must be blocked with a save message. Save, then retry.
6. Close and reopen the app with the same prefab. Select A, then B. Expected: persisted values survive restart. The selector need not automatically restore the last selected scenario.
7. With B saved, enter a third fresh identity and click **Rename**. Expected: the new identity is selectable and its JSON exists; the old B filename is gone. Optionally click **Delete** for this disposable renamed scenario. Expected: its file/selector entry disappears and the prefab returns to the unsaved interactive preview. Select saved A for the remaining cases. For M4 acceptance, the passing automated scenario-repository lifecycle test is accepted deletion evidence; a separate live Delete-button check is not required. Record which evidence was used without claiming unexercised UI behavior passed.

## 4. Apply external scenario values and reject invalid documents

1. With A saved and active, click **Edit Scenario**. Open the copied path manually if no editor adapter is configured. Add the following entry to `WidgetStates`, preserving all other fields:

   ```json
   "Window > Widget[Id=smoke-root] > TextWidget[Id=greeting]": {
     "IsVisible": "false"
   }
   ```

2. Save the JSON. Expected: an automatic reload hides the greeting, keeps the list/include, and lists the scenario file in changed dependencies. Set `IsVisible` to `true` and save; the greeting returns.
3. Temporarily set `SchemaVersion` to `999`, or introduce an unknown top-level JSON field. Expected: the app reports a scenario error, retains the previous preview/tree, marks it stale, and keeps the last valid active scenario. Restore schema 1/remove the unknown field and save. Expected: automatic recovery clears the stale state.
4. Make an unsaved binding edit in the app, then change the scenario JSON externally. Expected: a conflict is reported rather than silently discarding dirty UI values. Save the intended UI values before continuing; this intentionally replaces the external edit in this disposable scenario.

## 5. Reload root/includes and retain the Last Good Preview

Perform each save separately, waiting for a successful reload between saves.

| Action in the external editor | Expected result |
| --- | --- |
| Change the root widget's `Color` and save `M4Smoke.xml` once. | Preview color changes. Diagnostics name the root as a changed dependency and the reload generation advances once for the debounced save. |
| Change `Include version 1` to `Include version 2` and save `M4Smoke.Part.xml`. | Included text changes without opening the root again. Diagnostics identify the include path; one debounced save causes one reload. |
| Use the editor's atomic-save mode, if available, and save the include again. | Rename/replace saves also reload and recover without manual intervention. |
| Remove the include's closing `</Widget>` and save. | Error Diagnostics appear; the previous preview and Node Tree remain visible and status says the Last Good Preview is stale. A failed load must not publish an empty/new tree. |
| Click the XML error's source button. | The include opens at the parser's error line, or its `file:line:column` is copied. |
| Restore the closing tag and save. | The next dependency change recovers automatically; the stale marker clears. |
| Make a dirty `Greeting` edit, then change/save the include. | The dirty binding survives the dependency reload; it stays marked unsaved until **Save** is clicked. |

If an editor emits multiple saves or autosaves, record that behavior before interpreting the generation count. The deterministic debounce and cancellation tests in step 1 isolate this from editor timing.

## 6. Navigate from the tree and inspect Behavior Trace

1. Double-click `include-panel` in the Node Tree. Expected: `M4Smoke.Part.xml` opens at line 3, or that location is copied. Its tooltip should show the same source file/line/column.
2. Select `smoke-root`, then use **Open Source** and **Copy Location**. Expected: both resolve to `M4Smoke.xml`, line 3.
3. Click **Behavior Trace**. Expected: a separate read-only trace view opens. Trace entries identify Widget IDs/rules and include source locations where mapping is available. Close it before continuing.

## 7. Optional copied-game brush and image mutation checks

These checks provide additional integration confidence. For M4 acceptance, the user accepted passing installed Structure Only comparisons together with the automated invalidation tests and live generated brush/RDC checks. Repeating mutations with copied installed-game assets is optional and does not block milestone completion.

Use a **disposable copy of a Bannerlord installation with Native and AgricultureEstate**, preserving module paths, rather than altering the live game files. Select this copy in the app's Bannerlord controls and apply it. Open `Modules/AgricultureEstate/GUI/Prefabs/LandManagement.xml` from the copy.

1. Pick a brush used by the selected widget from its debug information. In the copied `GUI/Brushes` definition, change a visible layer color and save a valid XML document. Expected: automatic reload, the brush path in changed dependencies, and the changed brush appearance. Restore it and check recovery.
2. Identify a resolved image's package/payload path from its debug information or installed image artifacts under `artifacts/m4-validation/installed/`. Using a compatible asset tool, replace the copied TPAC/RDC pair with a valid version having a visible pixel change while retaining the sprite identity and valid metadata. Expected: automatic reload and changed pixels, without restarting the app. Restore the original pair afterward. Do not test this by changing arbitrary binary bytes.
3. If these optional checks are skipped or the copied installation/valid replacement asset is unavailable, record **NotRun** and the reason. The focused `DependencyInvalidationReloadsMetadataAndImagePixels` test checks changed metadata dimensions and independently expected changed pixel channels using generated assets. The accepted live generated-asset checks are separate evidence; neither claims a copied-game mutation run occurred.

## 8. Check comparison modes, masks and difference navigation

Return to `M4Smoke.xml` using the original installation setting and select saved A. Wait until it is not stale. For a reproducible synthetic image input, create a white PNG at A's current Capture Profile dimensions. This is a synthetic comparison input, not an in-game Reference Screenshot:

```powershell
Add-Type -AssemblyName System.Drawing
# Use A's current saved Width/Height if changed from 640 × 360.
$image = [System.Drawing.Bitmap]::new(640, 360)
$drawing = [System.Drawing.Graphics]::FromImage($image)
try {
    $drawing.Clear([System.Drawing.Color]::White)
    $image.Save((Join-Path $smokeRoot 'synthetic-white.png'), [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $drawing.Dispose()
    $image.Dispose()
}
```

1. Click **Compare Reference** and select `synthetic-white.png`. Expected: a comparison window opens with side-by-side images; the synthetic white input differs from the colored actual preview.
2. Select **Reference**, **Actual**, **Overlay** and **Diff**, then return to **Side-by-side**. Expected: all five modes work. Change zoom and pan the shared viewport; both images use the same scale/pan.
3. Check metadata: active scenario, Capture Profile/dimensions, mask count, Pixel Tolerance and comparison summary are shown. The interactive exact default is labeled uncalibrated, with structural comparison `NotRun`; a matching image must not be presented as a complete Full regression pass.
4. Select a listed Key Bounds difference. Expected: its source widget is selected in the main Node Tree when a mapping exists.
5. Close the comparison window. Add `{"Name":"dynamic-test","Reason":"Manual mask check","X":0,"Y":0,"Width":20,"Height":20}` to the scenario's `Masks` array in the external editor and save. Reopen **Compare Reference** with the same PNG. Expected: mask count increases, and the diff shows the masked area distinctly. Remove the test mask and save afterward.
6. With malformed XML/stale preview, **Compare Reference** must refuse to compare it as the current preview. Repair the XML before continuing.

For **Open Comparison Report**, select a saved, unmodified scenario for which a Full Visual Regression run and its `capture.json`, images and `report.json` already exist. Open that report. Expected: the recorded Full mode/checks/tolerance and actual/diff/overlay artifacts are displayed. A report for another scenario is rejected; a Structure Only report is rejected as an image view because pixels are `NotRun`. If no matching Full artifacts/optional Reference Package are configured, mark the manual Full-report case **NotRun**; the automated artifact/window tests remain available. Do not use Baseline Acceptance to manufacture a passing result for this smoke test.

## 9. Check cancellation and record results

The focused automated test `NewDependencyChangeCancelsSlowObsoleteReload` deliberately blocks a reload, triggers a newer dependency change, and checks that only the newer result publishes. Its result must pass. During manual testing, rapid saves may be coalesced before any load begins; an unchanged cancellation count alone is not a failure. If requests overlap on a larger prefab, check the cancelled-reload count in Diagnostics and verify that the latest content wins and the UI recovers.

Record date, commit/working-tree state, runtime, game/module versions, editor and atomic-save/autosave settings, fixture paths, scenario IDs and artifact directory. Use this result sheet:

| Case | Result | Evidence / actual result |
| --- | --- | --- |
| Automated suite and installed Structure Only targets | NotRun | |
| Scenario lifecycle, saved values and restart | NotRun | |
| External states/schema validation and dirty conflict | NotRun | |
| Root/include reload, malformed XML and recovery | NotRun | |
| Diagnostic/tree source navigation and Behavior Trace | NotRun | |
| Optional brush reload in copied game installation | NotRun | |
| Optional changed image pixels in copied game installation | NotRun | |
| Five comparison modes, zoom/pan, masks and mapped differences | NotRun | |
| Full report artifact opening and mismatched-report rejection | NotRun | |
| Obsolete-load cancellation | NotRun | |

Retain evidence before removing the disposable fixture/scenarios/copy. Record whether acceptance uses live or automated evidence for each required behavior. Scenario deletion and deterministic obsolete-load cancellation may use their passing automated tests. Copied-game mutation checks and Full/Reference Package checks are optional; document every NotRun case and its reason without reporting it as passed.

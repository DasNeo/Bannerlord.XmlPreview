# Tournament Leaderboard regression (#6)

The installed v1.4.7 Native prefab is exercised by
`TournamentLeaderboardTests.PopulatedAndEmptyLeaderboardPreserveColumnsAndConfineScrollbarPainting`.
Set `BANNERLORD_ROOT` to the game directory and run:

```powershell
dotnet test tests/Bannerlord.XmlPreview.VisualRegression.Tests --filter FullyQualifiedName~TournamentLeaderboard
```

The Preview Scenario uses 1920 x 1080, UI scale 1, English, sRGB,
with title `Leaderboard`, rank header `#`, Caladog (1, 24, champion), Rhagaea (2, 18), and Ironwood
(3, 12, main hero), then repeats with no entries. The horizontal
scrollbar is explicitly absent because this viewport scrolls vertically.

## Image inputs and fidelity

The layout scenario explicitly defines null portrait/banner behavior:
the identities and providers are empty and each image widget has
`HideWhenNull=true` through WidgetStates. These widgets collapse without
missing-image markers. This verifies null handling; it supplies no
portrait or clan-banner fidelity evidence. A separate render removes
those overrides and verifies that all six missing image inputs have red
notices and still report Partial. Its optional capture is saved as
`leaderboard-3-missing-images.png`.

The test checks local normal/player visibility, IntText, usable name
widths and column positions, full-width alternating bands and opacity,
the plaque's image flip, font identity, and descendant painting within
the outer scrollbar. Two portable cases check both player states and
exact cell positions without an installation. An atlas test checks
coverage, glyph advances and kerning.

Set `BANNERLORD_M3_ARTIFACTS` to a directory to save populated/empty PNGs
and layout diagnostics. Generated artifacts are optional review evidence,
never accepted automatically.

## Renderer corrections

- IsHidden collapses a widget and removes its flow allocation.
- Text widgets resolve IntText/FloatText when Text is absent.
- WPF stack Measure/Arrange includes margins exactly once.
- Stack children use the same Behavior Graph materialization as canvas children.
- Sibling-index visibility watches the expanded list instance index.
- LauncherGUI sprite metadata cannot overwrite game GUI sprites.
- Static sprite flipping affects the image, leaving child text upright.
- Scrollbar handle/viewport ratios never exceed one.
- Game FNT advances, kerning and distance-field atlases drive bitmap text.
  Brush font-color overrides and outline metadata are honored.

## Review limits

The [README comparison](../../readme.md#preview-examples) includes an in-game
screenshot alongside an illustrative preview. The original defect discussion
is in [issue #6](https://github.com/DasNeo/Bannerlord.XmlPreview/issues/6).
No calibrated pixel comparison has been performed.
Bitmap distance-field edge smoothing and outline amounts are preview
approximations; this work does not certify in-game pixel fidelity or
runtime image fidelity.

The visibility and margin corrections changed six portable structural
snapshots: options, single-query-popup, crosshair, standard-dropdown,
conversation, and option-item. Following review, the separate CLI `accept`
command recorded them in `test-data/visual-regression/issue-6-portable-baselines`.
Its README describes the reviewed differences. CI uses that package;
the original M3 metadata remains unchanged. Comparison evidence is under
`artifacts/issue-6/ci-review`.

# Issue #11 installed Native structural expectations

Accepted with explicit user approval on 2026-10-22 through the CLI `accept`
command. Native and game release: v1.4.7. Capture Profile:
1920 x 1080, UI scale 1, English, sRGB, full frame.

Only one Key Bounds entry differs from `m3-native-baselines`:
`PersuationExtension` in `m3-conversation` changes from
`(X=1720, Y=970, Width=150, Height=110)` to
`(X=1770, Y=970, Width=100, Height=110)`.
Its right edge stays at 1870. The scoped fixture retains the persuasion
image's children, including an empty progress list and its wrapper's
50-pixel left and right margins. The stack-panel margin correction in
commit `0ce5fb3` counts WPF DesiredSize margins once. The old expectation
predates that correction; restoring the extra width would reintroduce
incorrect margin accounting.

All other conversation Key Bounds, Render Structure, Widget IDs, scenario
inputs and Expected Warnings are unchanged. The banner-builder entry and
snapshots are carried forward unchanged. The M3 source package is immutable.

Review evidence: `artifacts/issue-11-before/report.json` and
`artifacts/issue-11-candidate/{capture,structure,layout}.json`, with an
optional preview at `artifacts/issue-11-candidate/actual.png`. The preview
shows the scoped image fixture, not a full conversation screen or an
in-game Reference Screenshot. Structure Only acceptance does not use it as
a pixel reference. Pixels remain NotRun.

`CoverChildrenStackCountsEmptyPersuasionMarginsOnce` independently checks
the empty content's 100-pixel margin span and 50-pixel placement.
The installed leaderboard test checks the band sprite's alpha of 0.1
and its container opacity of 1, preserving independent child opacity.
No renderer behavior or tolerance was changed for this issue.

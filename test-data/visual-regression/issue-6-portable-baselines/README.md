# Issue #6 portable structural expectations

Accepted on 2026-10-22 with the CLI `accept` command after reviewing the
six changed cases against the M3 package and their source attributes.
The unchanged Preview Scenarios remain in `m3-portable-scenarios`.
Earlier baseline packages remain immutable.

| Case | Reviewed correction |
|---|---|
| crosshair | The reload list's five-pixel top margin is applied once, moving its clip Y from 10 to 5 and filler Y from 12 to 7. |
| standard-dropdown | The ScrollingRichTextWidget variant with resolved IsHidden=true now reports Resolved.IsVisible=false. Key Bounds are unchanged. |
| option-item | The same hidden dropdown variant collapses. The forty-pixel option margin is applied once, moving option X from 580 to 540 and its descendants with it. |
| options | Embedded option-item changes propagate through the screen. Stack margins and alignment are applied once; zero-sized navigation helpers receive WPF's aligned slots rather than the previous origin. The largest delta (631 pixels) belongs to those helpers. |
| conversation | Stack margins change content positions and the CoverChildren height (232 to 212). The bottom-aligned container moves from Y=844 to Y=864; its twenty-pixel bottom margin is no longer counted twice. |
| single-query-popup | Stack content bounds omit duplicated margins: width 582 to 512, height 643 to 633. Dependent centered content and scrollbar positions follow the corrected bounds. |

Widget identities, counts, input scenarios and Expected Warnings are unchanged.
Only the resolved hidden-text property changes in Render Structure.
standard-window, standard-scrollable-panel and hint-tooltip are copied
byte-for-byte from M3.

Review evidence was generated under `artifacts/issue-6/ci-review`, including
per-field old/new bounds, relevant source margins/alignment, and comparison
reports. Sequential acceptance stages are under
`artifacts/issue-6/ci-acceptance`.

These are Structure Only expectations using explicit portable asset fixtures.
Pixel comparison is NotRun; they do not certify Native image pixels or
in-game pixel fidelity. CI tests and `tools/run_visual_regression.ps1` use
this package by default. Compare and validation commands never accept or
overwrite expectations.

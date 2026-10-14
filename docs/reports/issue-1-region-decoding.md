# LandManagement region decoding investigation

Measured 2026-10-14 on validation-host, Windows, .NET 10.0.5, Release configuration. Issue: https://github.com/DasNeo/Bannerlord.XmlPreview/issues/1.

Direct sprite-region decoding substantially improves first load, but **does not meet a maximum of 1000 ms combined load and render**. Warm reloads are comfortably below that target. The implementation was subsequently committed in `1e6d19e`; issue #1 remains open. See the [further metadata and first-render investigation](issue-1-metadata-and-first-render.md) for the next optimization and optional ReadyToRun publication.

## Fresh WPF processes

Each sample launches the actual app with `--exit-after-load --diagnostics-out`. Process startup is excluded and the OS file cache is uncontrolled. No extracted PNG override or `res/` directory is used. Default preview data, 1920 × 1080 root layout; preserved fixture has 80 nodes and installed prefab has 178.

| Variant | Load median | Render median | Combined samples (ms) | Combined median | Maximum / nearest-rank p95 |
| --- | ---: | ---: | --- | ---: | ---: |
| Preserved | 727 ms | 405 ms | 1326, 1151, 1129, 1128, 1131 | 1131 ms | 1326 ms |
| Installed | 605 ms | 473 ms | 1065, 1076, 1070, 1085, 1081 | 1076 ms | 1085 ms |

The preserved median needs another 131 ms (11.6%) removed; its slowest sample needs 326 ms (24.6%). Installed median needs another 76 ms (7.1%). This does not establish a hard upper bound across machines or disk-cache states. The preserved result remains 346 ms above the historical 785 ms figure, which is not a controlled same-session baseline.

Raw phase evidence: `artifacts/issue-1/wpf-after/*.txt`. Representative preserved phases: asset warm-up 574 ms, constants/metadata 97 ms, widget properties 35 ms, root materialization 255 ms, arrangement 6 ms. Other render work includes first-use planning, JIT and snapshot generation; these timings overlap their parent totals.

## Same-session comparison and warm reloads

A dedicated STA integration benchmark constructs production loader/renderer/catalog instances and measures one initial load followed by five warm loads for each variant. Five test processes were run before and after. Every process measures preserved first and installed second with separate catalogs; the installed initial result therefore benefits from already initialized JIT/WPF state. Do not compare those installed figures directly to fresh WPF app timings. Allocation figures cover each measured load/render; peak working set is cumulative for the test host, not retained cache memory. Timing scopes are enabled for both versions.

| Variant/state | Before median / p95 | Region median / p95 | Before / region median allocations |
| --- | ---: | ---: | ---: |
| Preserved initial (5) | 1900 / 6050 ms | 1103 / 2336 ms | 1355 / 249 MiB |
| Preserved warm (25) | 66 / 77 ms | 61 / 72 ms | 14 / 14 MiB |
| Installed initial (5) | 1238 / 1263 ms | 652 / 5982 ms | 1484 / 254 MiB |
| Installed warm (25) | 90 / 111 ms | 91 / 113 ms | 22 / 22 MiB |

Initial medians improve by 42% and 47%. Large outliers remain, including a 5982 ms installed sample after the change; a maximum-one-second claim is unsupported. Test-host peak working set across both variants/reloads fell from approximately 1233 MiB to 440 MiB. Raw samples, allocations, peak memory and phase diagnostics: `artifacts/issue-1/before`, `after`. A separate instrumented sample is in `artifacts/issue-1/instrumented`.

## Implementation and instrumentation

`TpacTextureReader.DecodeRegion` copies only the compressed blocks intersecting a sprite, uses the existing format decoder and premultiplication, and trims the aligned edges. R8 uses row spans. Full-texture requests remain supported. Dimensions, bounds, formats and truncated input are validated.

`TpacAtlasStore` retains a 64 MiB LRU of loaded BC/R8 payloads after package LZ4 decompression, alongside the existing 64 MiB frozen sprite and 128 MiB whole-atlas LRUs. Maximum retained buffers across these caches are 256 MiB; active images and transient buffers are additional. One lock coordinates payload loading, decoding, eviction and invalidation. Oversized entries are used without retention. Package metadata remains lazy.

Instrumented preserved load: 19 region decodes, 8 payload loads, 4,504,816 block-aligned pixels, zero retained full-atlas bytes, 17,915,804 retained sprite bytes, 58,982,400 retained payload bytes. Installed: 30 region decodes, 9 payload loads, 4,493,536 block-aligned pixels, zero retained full-atlas bytes. `FullAtlasPixelCount` reports the hypothetical sum of full-atlas areas for those requests (202,899,456 and 311,951,360); it is not the previous implementation's actual decoded area, because that implementation reused atlases.

## Validation and next step

193 solution tests passed; one optional in-game screenshot test skipped. Deterministic tests compare region output pixel-for-pixel with full decode/crop for BC7, DXT1, DXT5, BC4 and R8, including randomized alpha, unaligned origins, non-block-sized texture edges and full-image requests. Cache tests verify shared concurrent regions, payload reuse, eviction, invalidation and zero-capacity operation. After the final atlas-edge adjustment, all seven focused equivalence/cache/measurement cases passed again.

All three installed Structure Only comparisons passed: conversation, banner builder, LandManagement. Pixels are explicitly NotRun for these comparisons. No mod executable code is loaded. Structural evidence: `artifacts/issue-1/validation`.

The next focused experiment should measure how much warm-up is spent on unused brush states and how much fresh render time is JIT. A versioned automatic sprite disk cache or precompiled publication could then be measured if needed. Narrower warm-up must still resolve every image actually used by the preview. Serial BC decoding was trialed at 1151 ms combined preserved load and provided no improvement; it was reverted. No additional cache or warm-up policy is included here.

Reproduce with `tools/measure_land_management.ps1`; it builds Release, records five initial-plus-five-warm samples per variant, then five independent WPF app samples for each variant. Local raw artifacts are not committed.

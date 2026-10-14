# LandManagement metadata and first-render optimization

Further work for issue #1 on `feature/load-performance-improvement`, measured 2026-10-14. The previous region-decoding implementation is already on master. This change reduces metadata discovery work and adds an optional precompiled publication. The user accepted the measured performance and withdrew the one-second target on 2026-10-14. The target comparisons below document the investigation; they no longer block completion. Independent WPF launches supply timings; allocations and peak memory come from the integration test host, as described below. This evidence and its limitations are accepted for completion.

## Changes

- Discover the module's RDC companion path once per TPAC package, rather than once per texture lacking embedded pixels. A temporary profile found 3,257 discovery calls while reading 152 packages for the preserved fixture. Missing sprite lookup still checks the complete module scope; module precedence and diagnostics are preserved.
- Consume small reserved fields sequentially instead of seeking for each field. Skip unused source strings without allocating and decoding them. Large ranges still use bounded seeks; invalid metadata cannot rewind the parser.
- Analyze alpha bounds directly from Pbgra32 sprites. Unpremultiplying RGB is unnecessary because the algorithm reads only alpha. Other pixel formats retain the existing conversion path.
- Record package-metadata, payload-load and per-format region-decode phases in existing diagnostics. Temporary image-identity and per-companion trace hooks were removed.
- Add the opt-in `LoadPerformance` win-x64, framework-dependent ReadyToRun publish profile and a `-ReadyToRun` benchmark switch. Normal Release builds retain their usual publication behavior. The publication requires the matching .NET desktop runtime; it is not self-contained.

No disk cache, extracted assets, warm-up pruning, mod executable code, Behavior Graph cache or changes to cache memory limits are introduced.

## Fresh WPF process measurements

Windows validation host, .NET runtime 10.0.5, SDK 10.0.201, Release. Five independent fresh WPF processes per variant per configuration. Times exclude process startup; OS file cache and background machine activity are uncontrolled. Default preview data, 1920 × 1080 root layout; preserved fixture 80 nodes, installed prefab 178 nodes. This session's baseline is slower than the earlier region-decoding report, so compare within this session.

| Configuration | Variant | Load median | Render median | Combined median | Maximum / nearest-rank p95 |
| --- | --- | ---: | ---: | ---: | ---: |
| Before, regular Release | Preserved | 1065 ms | 621 ms | 1672 ms | 1883 ms |
| After, regular Release | Preserved | 771 ms | 613 ms | 1393 ms | 1413 ms |
| After, ReadyToRun publication | Preserved | 571 ms | 535 ms | 1119 ms | 1157 ms |
| Before, regular Release | Installed | 758 ms | 715 ms | 1473 ms | 1608 ms |
| After, regular Release | Installed | 713 ms | 744 ms | 1445 ms | 1477 ms |
| After, ReadyToRun publication | Installed | 527 ms | 642 ms | 1169 ms | 1218 ms |

Combined samples, in run order:

| Configuration | Preserved (ms) | Installed (ms) |
| --- | --- | --- |
| Before | 1883, 1723, 1590, 1672, 1601 | 1608, 1473, 1444, 1494, 1455 |
| After regular Release | 1393, 1359, 1365, 1413, 1393 | 1445, 1472, 1393, 1439, 1477 |
| After ReadyToRun | 1157, 1126, 1085, 1106, 1119 | 1161, 1153, 1210, 1169, 1218 |

The normal preserved median improves 16.7%; the installed median changes only 1.9%, which is too small to claim a reliable gain independently of run variation. The optimized ReadyToRun publication improves medians 33.1% and 20.6% relative to this session's regular baseline. Publication and parser gains are separate effects; these numbers do not attribute the entire improvement to metadata parsing. Median phase values need not sum to the combined median.

Both published medians remain above target by 119 ms and 169 ms, respectively; worst observed samples remain above by 157 ms and 218 ms. No hard maximum or performance gate is claimed.

## Profile and warm reload evidence

Initial instrumented metadata profiles took roughly 500–650 ms across 152 packages; a narrower trace attributed about 304 ms to repeated companion discovery. After the changes, representative normal metadata time was 202 ms, with BC7 regions 255 ms, DXT5 regions 87 ms and payload loading 39 ms. The representative ReadyToRun sample measured metadata 156 ms, BC7 138 ms, DXT5 88 ms and payload loading 31 ms. These are individual diagnostic runs, not isolated microbenchmark medians; decode work spans both load and render and overlaps parent phases.

Image-identity tracing showed that most warm-up sprites are subsequently used. No warm-up policy was changed. ReadyToRun's improvement is consistent with reduced first-use compilation work, but this investigation does not isolate all runtime/JIT overhead.

The existing STA production integration benchmark also ran in five test processes before and after, each measuring one initial load and five warm reloads per variant. Installed runs second with a separate catalog but initialized JIT/WPF, so its initial figure is not a fresh app process measurement. The `-ReadyToRun` switch affects only fresh app measurements; its warm measurements still use the regular test host.

| Variant/state | Before median / p95 | After regular median / p95 | Median allocated memory before / after |
| --- | ---: | ---: | ---: |
| Preserved initial (5) | 1852 / 3089 ms | 1363 / 1429 ms | 249.7 / 242.7 MiB |
| Preserved warm (25) | 87 / 142 ms | 97 / 139 ms | 14 / 14 MiB |
| Installed initial (5) | 1038 / 1381 ms | 761 / 1152 ms | 254.1 / 251.1 MiB |
| Installed warm (25) | 184 / 260 ms | 174 / 203 ms | 22.2 / 22.2 MiB |

Cumulative test-host peak working set across both variants/reloads was approximately 409 MiB before and 411 MiB after. These figures do not measure retained cache memory. No consistent warm-reload improvement is claimed.

## Validation and reproduction

198 solution tests passed; one optional in-game screenshot test skipped. New deterministic cases verify consecutive texture records across short/long reserved metadata, reject undersized metadata, and check identical selection/content bounds for Bgra32, Pbgra32 and Rgba64 sprites with transparent and partially transparent pixels. Existing companion identity/checksum/range rejection, region-equivalence and cache-bound tests pass.

All three installed Structure Only comparisons passed: conversation, banner builder and LandManagement. Pixel comparisons are explicitly NotRun. The benchmark script syntax and publish profile XML were checked; the profile was published and exercised in all ten fresh-process measurements above.

Reproduce normal Release:

```powershell
./tools/measure_land_management.ps1 -OutputRoot '<external-evidence-directory>/release'
```

Reproduce precompiled publication:

```powershell
./tools/measure_land_management.ps1 -OutputRoot '<external-evidence-directory>/readytorun' -ReadyToRun
```

Publish separately with `dotnet publish Bannerlord.XmlPreview.csproj -p:PublishProfile=LoadPerformance -o '<output-directory>'`.

Local raw evidence is outside the repository at `C:/Users/andre/Documents/ChatGPT/bannerlord-xml-preview/issue-1-further/`: `before`, `final-release`, `final-readytorun`, and `validation`. Earlier trial folders are exploratory evidence, not the final sample sets above. Raw files and publications are not committed.

Optional future work is to profile first-render materialization and consider a versioned automatic sprite disk cache if another first-use reduction is required. Neither is required to complete issue #1 at the accepted performance. Any such cache must separately report cache-population and cache-hit launches; it cannot establish a one-second maximum on first population without measurement.

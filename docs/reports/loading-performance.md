# WPF loading performance investigation

The subsequent [issue #1 region-decoding investigation](issue-1-region-decoding.md) records the 2026-10-14 implementation, validation and remaining gap to a one-second combined load/render target.

Measured on 2026-10-13, validation-host, using the WPF app's `--exit-after-load` diagnostics path. Each screen measurement starts a fresh process; the OS file cache is uncontrolled. Times exclude process startup.

| Screen / version | Load | Render | Combined |
| --- | ---: | ---: | ---: |
| Preserved LandManagement, pre-M3 historical report | 347 ms | 438 ms | 785 ms |
| Preserved LandManagement, M3 before fix | 2301 ms | 2051 ms | 4352 ms |
| Preserved LandManagement, after fix | 1907 ms | 594 ms | 2501 ms |
| Preserved LandManagement, second fresh process after fix | 1910 ms | 602 ms | 2512 ms |
| Installed LandManagement, after fix (178 nodes) | 1667 ms | 654 ms | 2321 ms |
| Standard.Window, before fix | 812 ms | 597 ms | 1409 ms |
| Standard.Window, after fix | 772 ms | 376 ms | 1148 ms |

The same preserved LandManagement fixture has 80 nodes throughout. Before the fix, awaiting asset warm-up took 2115 ms and rendering the root node took 1860 ms. XML expansion took 5 ms, property resolution 47 ms and arrangement 10 ms. The behavior graph took 41 ms. Asset handling accounts for the regression.

A temporary decode trace reproduced 17 atlas decodes for 9 distinct atlases. LandManagement exceeds the shared 128 MiB atlas cache: several individual atlases occupy 64 MiB each. Warm-up visited sprites in mixed atlas order and discarded the independent crops it created. Evicted atlases therefore had to be decoded again, both during warm-up and during rendering. BC7 decodes in this trace took 276–455 ms each; DXT atlas decodes took roughly 120–187 ms. Separately, loading, warm-up and rendering created catalogs that each parsed the same sprite/brush XML metadata.

The fix retains independent frozen crops in a separate 64 MiB LRU, groups warm-up requests by atlas and shares metadata between catalogs with the same installation snapshot and module owner. The loader also reuses its already scoped catalog for warm-up. Metadata is refreshed when the installation index is invalidated, and module precedence remains isolated. Retained atlas and crop caches are bounded to 192 MiB combined; this does not bound total process memory or images held by the current visual tree.

Fresh-process load plus render improves by about 42%, with render time improving by about 71%. First-use decoding of compressed game textures remains slower than loading the former extracted PNG assets; these results do not restore pre-M3 cold-load time. No `res/` dependency is reintroduced.

Validation: 153 tests passed, one optional in-game screenshot test skipped, and all three installed structural comparisons passed. New deterministic tests verify crop reuse after atlas eviction, the separate crop memory bound, shared metadata across prefab scopes, module isolation and metadata refresh after index invalidation.

Evidence: `artifacts/loading-investigation/before`, `traced`, `decodes-before.txt`, `after`, `after-repeat`, `installed-after.diagnostics.txt`, and `validation`. The temporary trace hook was removed from production code.

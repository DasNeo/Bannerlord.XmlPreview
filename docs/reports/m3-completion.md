# M3 completion: image and asset fidelity

Completed and validated on 2026-10-12 against Bannerlord Native v1.4.7 and AgricultureEstate v1.4.7.3. The [image fidelity matrix](../fidelity/images.md) and [machine-readable matrix](../fidelity/images.json) describe verified scope and runtime limitations. Structural success does not certify matching in-game pixels.

## Implementation

- Structured image resolution distinguishes Resolved, Missing, RuntimeOnly and Invalid, retaining requested provider/identity/arguments, module, source package, actual payload path, texture format, dimensions, sprite crop, attempted sources and Diagnostics. Invalid outcomes have machine-readable failure kinds for metadata, formats, absent pixels, payload corruption, crop bounds and unsafe override identities.
- The installation index discovers module declarations, `AssetPackages`, editable `Assets` packages and sprite metadata. Lookup reverses deterministic dependency load order: the owning/explicitly selected module overrides dependencies. Direct, transitive, duplicate-name, unrelated-module isolation, missing-module, cycle and malformed-metadata cases have tests. An unavailable dependency directory is recorded explicitly in resolution Diagnostics and cannot supply assets. No third-party DLL is loaded for assets or providers.
- Native textures load directly from TPAC, with lazy metadata and requested-payload LZ4 decompression. Managed decoding covers BC7, DXT1, DXT5, BC4 and R8_UNORM. Frozen Pbgra32 bitmaps preserve crop coordinates and nine-region metadata. A shared 128 MiB atlas LRU coordinates requests and serializes expensive decoding. Independent sprite crops have a separate 64 MiB LRU so warm-up results survive atlas eviction without pinning atlases. Module-scoped metadata is shared across loading and rendering. See the [WPF loading investigation](loading-performance.md) for the subsequent performance correction. A single decoded texture is limited to 128 MiB; package payload fields are bounded and range checked.
- Production no longer scans extracted DDS/PNG directories or substitutes basename/suffix matches. The catalog has an explicitly selected PNG override constructor for controlled cases. The compact texture-only TPAC reader adapts TpacTool's field layout; managed BCnEncoder.NET 2.3.0 and K4os.Compression.LZ4 1.3.8 are pinned production dependencies. Required notices are retained and copied into build/publish output; see [third-party notices](../../THIRD-PARTY-NOTICES.md).
- ImageWidget, ImageIdentifierWidget and MaskedTextureWidget now emit declarative image blueprints from Bannerlord Behavior rules. Core owns WPF sizing, stretch, tint, opacity, masks, null handling and placeholders. Alpha and single-channel coverage masks have independently expected pixel tests. Missing sources/masks produce labeled Missing placeholders and Partial; runtime-only images produce purple placeholders and Partial. Invalid deterministic data fails the isolated render, preserving the Last Good Preview and its stale marker.
- Provider data is scenario-local. Preview.Sprite resolves an exact identity; Preview.ImageSprite explicitly supplies scenario image data while retaining original provider identity and arguments. Providers without deterministic data and BannerTableau remain runtime-only Partial. Mod executable providers are never invoked.
- Persistent scenarios, exact Diagnostics expectations, image status/dimension snapshots, explicit fixture scope, three installed structural comparisons, nine portable cases and generated Fidelity Status are available. `tools/run_m3_validation.ps1 -PublishFidelity` publishes Verified rows only after required tests and all three structural comparisons pass.

## Installed acceptance and deliberate changes

1. SPConversation verifies static brush images, persuasion visibility/alpha, five fixed provider instances and deterministic Banner icon 101. Hidden brush layers are explicitly valid empty content. Image null/HideWhenNull behavior is independently verified with controlled fixtures.
2. BannerBuilder verifies all five MaskedTextureWidget cases, mask dimensions and static image assets. SandBox is an explicit data scope because Kingdom.TornBanner.Big is defined in that module while the prefab lives in Native. The required structural slice excludes BannerTableau; a separate installed assertion preserves its RuntimeOnly/Partial outcome.
3. The preserved full Land Management tree renders without a Code Widget. All five `ae_*` sprites resolve from mod data. `clan_frame_small` is genuinely absent from the available data scope and remains an explicitly diagnosed missing asset, without a replacement image. The popup width stays 1500; its height is deliberately corrected from the old 1000 fallback to the declared SandBox brush metric of 960. Independent assertions verify the brush metric, loaded constant and arranged bounds. The earlier candidate and the reviewed correction remain in ignored artifacts.

M1 and M2 baselines remain immutable. New expectations were accepted through the existing explicit CLI acceptance operation. The nine portable widget counts are unchanged; eight layouts remain byte-identical to M2. Options changes one Filler X coordinate after removing the ImageWidget frame approximation; its dimensions and slider/handle bounds remain unchanged. Five cases gain image result properties; Conversation additionally uses explicit controlled provider metadata and the declared persuasion brush alpha. See the [portable package review](../../test-data/visual-regression/m3-portable-baselines/README.md).

## Plan adjustments supported by installed evidence

AgricultureEstate ships an editable 552-byte TPAC with texture metadata and a pixel companion in RuntimeDataCache, rather than a published atlas payload inside AssetPackages. The reader supports the observed RDC0 version-0 single-texture record, validates asset/type identities, source checksum, size and offset, and records the RDC payload path explicitly. This loads actual mod pixels directly from its package data without requiring any extracted image. Controlled fixtures reject stale/mismatched/corrupt companions. Other RDC record layouts fail explicitly; support is not inferred beyond the verified layout.

The installation lacks the declared Bannerlord.Harmony module data directory, although the mod carries its runtime library. Data lookup reports that unavailable dependency and searches the available declared data closure. It neither executes the bundled library nor invents assets for it. A missing owner, malformed declaration or dependency cycle is Invalid.

Native image-family scope intentionally isolates the requested fidelity from unrelated screen behaviors. It is explicitly recorded in each scenario and never prunes normal app renders. Full Land Management remains the preserved combined geometry fixture. No regenerated preview PNG has been accepted as an in-game screenshot reference.

## Verification

- `dotnet build Bannerlord.XmlPreview.sln --no-restore -m:1`: zero warnings/errors.
- `powershell -ExecutionPolicy Bypass -File tools/run_m3_validation.ps1 -PublishFidelity`: exit 0; 151 tests passed (143 VisualRegression, 8 host tests); one optional Full group skipped.
- All nine portable Native cases passed StructureOnly through the real renderer.
- Installed SPConversation, BannerBuilder and Land Management structural comparisons passed Structure, Key Bounds and exact Diagnostics; pixels were NotRun.
- Focused assertions verify all five decoder formats, premultiplied alpha, crop bounds, freeze semantics, concurrent reuse/eviction, structured failure kinds, RDC identity/checksum, module precedence, provider inputs, stretch, tint, opacity, nulls, masks and unavailable/Code/Runtime placeholders.
- Required asset tests use the production catalog without a PNG override. Native package paths and AgricultureEstate's TPAC/RDC paths are asserted explicitly.
- Core source remains free of concrete Bannerlord image/pilot widget names and DemoData references. `git diff --check` passed. Dependency notices are present in build output.

Evidence: `artifacts/m3-validation` contains three comparison reports, installed image assertions, production performance measurements and generated fidelity reports. Reviewed candidate/acceptance evidence remains under `artifacts/m3-structural-reviewed`, `artifacts/m3-mod-metrics-reviewed`, and `artifacts/m3-portable-candidates`.

## Performance and limits

The [initial spike](m3-progress.md) records first and repeated decodes and extracted-PNG consistency. Production measurements on validation-host, .NET 10.0.5, use first payload/decode/crop in process and five warm crops of retained atlases; the OS cache is uncontrolled. Exact samples and allocation/working-set measurements are retained in `artifacts/m3-validation/installed/production-performance.json`. At the time of these measurements, retained atlas cache memory was bounded at 128 MiB and the test proved one decode per retained atlas. The subsequent performance correction adds a separate 64 MiB crop cache. Process peak includes the entire test host, parallel fixtures and GC retention; it is not an individual atlas's retained footprint.

| Atlas | Dimensions | First payload/decode/crop | Warm crop median | First-load allocation | Retained cache after sample |
| --- | --- | --- | --- | --- | --- |
| Native BC7 ui_mplobby_1 | 4096 x 4096 | 620.05 ms | 0.0036 ms | 261.00 MiB | 64 MiB |
| Native DXT5 ui_group1_3 | 4096 x 2048 | 106.89 ms | 0.0055 ms | 133.15 MiB | 96 MiB |
| AgricultureEstate DXT5 atlas | 512 x 512 | 3.86 ms | 0.0055 ms | 4.21 MiB | 97 MiB |

The combined test host recorded a 1193.7 MiB working-set high-water mark, reflecting all test activity rather than the bounded cache alone. Warm samples measure a 4 x 4 cropped bitmap; they do not include a complete screen render.

Verified means these explicit static preview expectations passed. Arbitrary runtime portraits, provider logic, 3D banner tableau rendering, game screenshots and interactive editing are not certified. Unsupported texture formats/RDC layouts, corrupt payloads, invalid crop bounds and genuinely missing data remain explicit outcomes. Asset dependency watching and richer persistent authoring controls belong to M4.

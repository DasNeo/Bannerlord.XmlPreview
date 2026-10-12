# M3 Implementation Plan: Image and Asset Fidelity

## Objective

Render static, provider-backed, masked, Native, and mod-provided image data honestly. Eliminate silent image substitution and distinguish resolved, missing, runtime-only, and invalid assets.

Load Gauntlet sprite atlases directly from the installed game's TPAC packages, eliminating manual TPAC-to-DDS export and PNG cropping as production prerequisites. Keep controlled image fixtures for portable tests.

## Prerequisite

M2 is complete. Image widget semantics are implemented as Bannerlord Widget Behavior rules, not new Core name checks.

Direct TPAC loading is M3 work and does not expand M2. Start M3 with the decoding spike below, then complete structured resolution, module precedence, and production TPAC loading before implementing the image widget families.

## Target module shape

Deepen the existing asset-resolution path behind `IAssetCatalog` or a replacement interface with one structured result model. Callers must not coordinate manifest lookup, module precedence, file loading, placeholder policy, and Diagnostics themselves.

Production and test adapters justify the asset seam:

- Bannerlord installation adapter resolves real module data.
- in-memory fixture adapter supplies deterministic test images and failure cases.

## Work package 3.1: Define structured image resolution

Define an image-resolution result containing:

- requested identity and provider information
- owning module
- resolved source path or runtime-only reason
- TPAC package path, texture identity, and sprite crop bounds when the source is an atlas
- dimensions and format metadata
- status: resolved, missing, runtime-only, or invalid
- Diagnostics entries

Rules:

- missing and runtime-only are different outcomes
- no caller receives an unexplained fallback image
- Runtime-only Placeholder is visible and produces `Partial`
- invalid data fails when it prevents deterministic interpretation
- resolution results are suitable for Fidelity Status reporting

## Work package 3.2: Implement module-aware asset precedence

Extend `BannerlordInstallationIndex` and `BannerlordAssetCatalog`:

1. identify the prefab's owning module
2. read declared module dependencies in deterministic order
3. resolve the owning module before or according to the verified Bannerlord precedence rule
4. include Native dependencies where declared
5. record every attempted resolution source in debug Diagnostics
6. distinguish true absence from fallback-name coincidence

Add tests with synthetic module graphs for direct dependency, transitive dependency, duplicate asset name, missing dependency, and cycle or malformed metadata.

## Work package 3.3: Load sprite images directly from TPAC

Begin with a focused decoding spike against the installed game:

1. Decode representative BC7 and DXT5 atlases and crop sprites using SpriteData XML coordinates.
2. Compare dimensions, orientation, color channels, and transparent edges against the existing extracted PNGs. This verifies decoding consistency, not in-game rendering fidelity.
3. Record cold-load time, warm-load time, and peak memory before choosing the decoder and cache strategy.

Use [TpacTool](https://github.com/szszss/TpacTool) as a package-reader candidate and [GauntletUI-LSP](https://github.com/kerema14/GauntletUI-LSP) and its TpacTool fork as integration references. Inspect decoder coverage, dependencies, licenses, and compatibility with the application's target framework before adopting or adapting code; the dependency choice remains evidence-driven.

Production integration:

1. Keep TPAC parsing and texture decoding behind the asset module in the Bannerlord adapter.
2. Extend installation indexing to discover module AssetPackages and apply the precedence from work package 3.2 to texture lookup.
3. Retain sprite-part SheetX, SheetY, width, and height in the asset snapshot; resolve the category and sheet ID to the atlas texture identity.
4. Load package metadata lazily, decompress only requested texture payloads, decode pixels, and crop sprites in memory without requiring intermediate DDS or PNG files.
5. Cover the formats found in the installed Native UI package: BC7, DXT1, DXT5, BC4, and R8_UNORM. Report unsupported formats, absent pixel data, corrupt payloads, and invalid crop bounds explicitly.
6. Produce frozen WPF bitmaps with the existing premultiplied-alpha semantics. Preserve sprite dimensions and nine-region metadata.
7. Decode each requested atlas once per cache lifetime, coordinate concurrent requests, and bound decoded-atlas memory. Perform expensive loading during background asset warm-up.
8. Report package, texture, crop, and failure information through structured resolution and Diagnostics rather than requiring an image file path.
9. Keep controlled image fixtures and explicitly selected PNG overrides; remove manual extraction as a production setup requirement and document the resulting setup.

Verify direct loading with representative Native sprites and the AgricultureEstate mod sprites. Existing extracted PNGs may serve as migration comparison inputs but must not be required for the installed-game acceptance runs.

## Work package 3.4: Implement `ImageWidget` behavior

1. Map image properties into declarative Behavior Contributions.
2. Resolve static image and brush-backed content through the asset module.
3. Preserve sizing, stretch, visibility, and opacity semantics required by fixtures.
4. Emit explicit Diagnostics for missing data.
5. Add focused unit fixtures and an M3 Visual Regression case.

## Work package 3.5: Implement `ImageIdentifierWidget` behavior

1. Define the provider input contract for `AdditionalArgs`, `ImageId`, and `TextureProviderName`.
2. Supply provider values through the Preview Scenario, never global state.
3. Return a real image when the provider adapter can resolve deterministic data.
4. Return Runtime-only Placeholder and `Partial` when the game runtime is required.
5. Implement and test null and `HideWhenNull` behavior.

Do not load mod assemblies to invoke image providers.

## Work package 3.6: Implement `MaskedTextureWidget` behavior

1. Express mask image, source texture, color, and layout intent through Behavior Contributions.
2. Perform WPF-specific masking only inside Core materialization.
3. Preserve deterministic output at the Capture Profile resolution.
4. Add focused tests for missing mask, missing source, dimensions, and color changes.

## Work package 3.7: Make placeholders explicit

Create visually distinct placeholders for:

- Code Widget
- runtime-only image
- missing asset
- invalid asset

Each placeholder includes machine-readable status and debug metadata. A placeholder must never resemble a successfully resolved production asset.

## Work package 3.8: Build acceptance scenarios

### `SPConversation.xml`

Create a persistent scenario with fixed values for `IsPersuading`, `AdditionalArgs`, `Id`, and `TextureProviderName`. Verify:

- ordinary `ImageWidget` content
- provider-backed `ImageIdentifierWidget`
- deterministic banner identity
- `HideWhenNull`
- scoped assertions that are not obscured by unrelated specialized widgets

### `BannerBuilderScreen.xml`

Create a scenario with fixed banner code, colors, provider values, and selection state. Verify:

- five Masked Texture cases
- static Image Widgets
- mask geometry and color
- explicit runtime-only `Partial` for `BannerTableauWidget`

Tableau fidelity remains outside M3.

### `fixtures/AgricultureEstate/GUI/Prefabs/LandManagement.xml`

Use the preserved strong mod baseline from the repository fixture, with assets resolved from the installed game. Verify:

- `ae_gardens`, `ae_granary`, `ae_storehouse`, `ae_irrigation`, and `ae_patrols`
- Native brush dependencies
- exact classification of `clan_frame_small` as resolved or genuinely missing
- no regression in existing geometry

## Work package 3.9: Publish Fidelity Status

Generate a machine-readable and Markdown matrix for image widget families:

- `Verified`
- `Implemented`
- `Partial`
- `Placeholder`
- `Unknown`

Derive `Verified` from explicit behavior, asset-resolution, dimensions and mask assertions together with structural regression. Identify optional visual evidence separately; structural success does not certify in-game pixel fidelity. Do not infer verification from class presence or reflection coverage.

## Test strategy

- in-memory asset adapter tests every structured result
- module precedence tests use synthetic module graphs
- controlled texture inputs verify decoder output, alpha handling, and crop bounds for the supported formats
- installed-game acceptance resolves Native and AgricultureEstate sprites directly from TPAC without extracted DDS or PNG prerequisites
- negative TPAC cases distinguish missing textures, absent pixel data, unsupported formats, corrupt payloads, and invalid bounds
- decoding spike records cold/warm loading and peak memory; portable CI retains controlled image fixtures without requiring a game installation
- Behavior tests cover all three image families
- Structure Only regression covers the three accepted screens; focused asset and mask assertions use controlled inputs without in-game screenshots
- negative cases verify missing, runtime-only, invalid, and Code Widget placeholders
- no test executes a mod assembly

## Exit checklist

- [ ] All three image widget families have real Behavior rules.
- [ ] Module dependency order controls data-asset lookup.
- [ ] Native and AgricultureEstate sprites load directly from TPAC without manual DDS export or PNG cropping.
- [ ] Supported texture formats, crop bounds, alpha handling, and explicit TPAC failure outcomes are verified.
- [ ] Cold/warm loading and peak memory are recorded, and decoded-atlas caching is bounded.
- [ ] Silent fallback images are eliminated.
- [ ] Runtime-only content is visible and `Partial`.
- [ ] Both Native image screens pass their scoped structural, asset-resolution, dimensions and mask assertions.
- [ ] Land Management passes without a Code Widget.
- [ ] Explicit Code Widget case returns the expected `Partial`.
- [ ] Image-family Fidelity Status is published.

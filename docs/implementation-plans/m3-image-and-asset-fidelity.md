# M3 Implementation Plan: Image and Asset Fidelity

## Objective

Render static, provider-backed, masked, Native, and mod-provided image data honestly. Eliminate silent image substitution and distinguish resolved, missing, runtime-only, and invalid assets.

## Prerequisite

M2 is complete. Image widget semantics are implemented as Bannerlord Widget Behavior rules, not new Core name checks.

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

## Work package 3.3: Implement `ImageWidget` behavior

1. Map image properties into declarative Behavior Contributions.
2. Resolve static image and brush-backed content through the asset module.
3. Preserve sizing, stretch, visibility, and opacity semantics required by fixtures.
4. Emit explicit Diagnostics for missing data.
5. Add focused unit fixtures and an M3 Visual Regression case.

## Work package 3.4: Implement `ImageIdentifierWidget` behavior

1. Define the provider input contract for `AdditionalArgs`, `ImageId`, and `TextureProviderName`.
2. Supply provider values through the Preview Scenario, never global state.
3. Return a real image when the provider adapter can resolve deterministic data.
4. Return Runtime-only Placeholder and `Partial` when the game runtime is required.
5. Implement and test null and `HideWhenNull` behavior.

Do not load mod assemblies to invoke image providers.

## Work package 3.5: Implement `MaskedTextureWidget` behavior

1. Express mask image, source texture, color, and layout intent through Behavior Contributions.
2. Perform WPF-specific masking only inside Core materialization.
3. Preserve deterministic output at the Capture Profile resolution.
4. Add focused tests for missing mask, missing source, dimensions, and color changes.

## Work package 3.6: Make placeholders explicit

Create visually distinct placeholders for:

- Code Widget
- runtime-only image
- missing asset
- invalid asset

Each placeholder includes machine-readable status and debug metadata. A placeholder must never resemble a successfully resolved production asset.

## Work package 3.7: Build acceptance scenarios

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

## Work package 3.8: Publish Fidelity Status

Generate a machine-readable and Markdown matrix for image widget families:

- `Verified`
- `Implemented`
- `Partial`
- `Placeholder`
- `Unknown`

Derive `Verified` only from a passing Full comparison. Do not infer it from class presence or reflection coverage.

## Test strategy

- in-memory asset adapter tests every structured result
- module precedence tests use synthetic module graphs
- Behavior tests cover all three image families
- Visual Regression covers the three accepted screens
- negative cases verify missing, runtime-only, invalid, and Code Widget placeholders
- no test executes a mod assembly

## Exit checklist

- [ ] All three image widget families have real Behavior rules.
- [ ] Module dependency order controls data-asset lookup.
- [ ] Silent fallback images are eliminated.
- [ ] Runtime-only content is visible and `Partial`.
- [ ] Both Native image screens pass their scoped Full assertions.
- [ ] Land Management passes without a Code Widget.
- [ ] Explicit Code Widget case returns the expected `Partial`.
- [ ] Image-family Fidelity Status is published.

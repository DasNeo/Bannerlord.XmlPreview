# M3 installed image scenarios

Run `tools/run_installed_validation.ps1` with the current Bannerlord installation. No extracted DDS, cropped PNG directory or Reference Package is required. `AssetModule` selects the data dependency closure; the default is the prefab's owning module.

- `m3-conversation`: scoped Native ImageWidget and ImageIdentifierWidget XML, including persuasion visibility and explicit fixed provider inputs.
- `m3-banner-builder`: all five native MaskedTextureWidget instances plus static images. SandBox is the explicit active data module because `Kingdom.TornBanner.Big` is defined in SandBox while the prefab lives in Native. `BannerTableauWidget` is tested separately as runtime-only Partial and excluded from this required Complete structural slice.
- `m3-land-management`: the full dashboard fixture copied from the installed mod on 2026-10-22, including Native brushes, frames, icons, and mod sprites. Explicit bindings provide upgrade descriptions, costs, build times, off switches, and empty project progress. No warnings are expected. Missing declared executable dependency directories are retained in asset-resolution traces; their assemblies are not loaded.

`ScopeWidgetTypes` is an explicit regression fixture selection, not a production render pruning policy. The scoped Native roots preserve each selected XML node's properties, children, constants and original Widget ID path. They verify image dimensions and mask data independently of unrelated specialized screen behavior. The full mod fixture tree is compared. Exact expected Diagnostics are recorded in the scenarios; a new warning fails comparison.

Preview.Sprite is a deterministic provider that resolves ImageId as an exact sprite/texture identity. AdditionalArgs remains attached to the request for inspection. `Preview.ImageSprite` supplies explicit scenario image data while retaining the original provider identity and arguments. Other providers return RuntimeOnly when deterministic data is unavailable; no mod assembly is invoked.

Immutable accepted expectations are split between `m3-native-baselines` (Native v1.4.7) and `m3-mod-baselines` (AgricultureEstate v1.4.7.3). Their capture profile is 1920 x 1080, English, sRGB, UI scale 1. Pixels are NotRun in required structural comparison; controlled image tests separately verify compositing pixels.

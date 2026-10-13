# M3 image fidelity

Generated only after unit, rendered-pixel, installed asset and structural assertions pass. In-game screenshot comparison remains optional.

| Family | Status | Verified scope | Limit |
| --- | --- | --- | --- |
| ImageWidget | Verified | Static sprite and brush images, sizing, visibility, stretch, tint and opacity | Native in-game pixels are not certified. |
| ImageIdentifierWidget | Verified | Explicit Preview.Sprite or scenario image data, provider identity/arguments, null and HideWhenNull | Other runtime providers remain visible Partial until deterministic data is supplied. |
| MaskedTextureWidget | Verified | Native five-mask fixture; alpha and single-channel masks, dimensions, tint and missing-input checks | Static WPF compositing; runtime provider limitations still apply. |
| BannerTableauWidget | Placeholder | Explicit runtime-only placeholder and Partial | Runtime tableau rendering is deferred. |
| Code Widget | Placeholder | Labeled placeholder and Partial | Mod executable code is never loaded. |

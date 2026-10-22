# Product direction

Priorities, in order:

1. Renderer fidelity.
2. Analysis and Diagnostics.
3. Authoring support.

When these conflict, the earlier goal wins. Core stays generic; Bannerlord-specific behavior belongs in the adapter. Mods supply XML and data assets, but executable mod widgets are represented by labeled placeholders. See the [architecture decisions](adr/).

## Current baseline

M0–M4 are complete: load coordination, Visual Regression, the Behavior Graph, TPAC/RDC image loading, and the external-editor authoring loop. The [original milestone plans](implementation-plans/README.md) retain the implementation rationale and acceptance criteria; they are historical specifications, not an open backlog.

Validation covers Bannerlord v1.4.7 and AgricultureEstate v1.4.7.3. Structural checks and focused behavior/asset tests establish the tested scope. They do not certify in-game pixel fidelity. Current image-family coverage and limitations are in the [fidelity matrix](fidelity/images.md).

## Further work

Track new defects and specifications in [GitHub Issues](https://github.com/DasNeo/Bannerlord.XmlPreview/issues). Prioritize concrete differences between the preview and the game, backed by a reproducible prefab and Preview Scenario. New behavior should include regression coverage and explicit Diagnostics for remaining limitations.

Runtime tableau rendering and providers that require live game state remain incomplete. Calibrated in-game pixel comparisons are optional; their workflow is documented in [Visual Regression](../test-data/visual-regression/README.md). No performance target is currently a release gate; measure bottlenecks before optimizing, and never hide omitted widgets or assets to meet a timing target.

## Release expectations

- Identify builds as Preview quality and publish the tested Fidelity Status.
- Keep game asset packages out of release archives; load assets from the user's installation.
- Keep optional calibrated Reference Packages separate from the application.
- When the game version changes, refresh versioned scenarios and asset metadata deliberately and rerun validation. Create a new immutable Reference Package if optional screenshot comparisons are used.

Build, validation, and release commands live in the [development guide](development/README.md).

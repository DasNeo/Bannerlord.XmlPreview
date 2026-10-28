# Visibility fallback and deferred hidden subtrees

The preview keeps widgets without an `IsVisible` attribute visible. If an `IsVisible` value or binding cannot resolve to a Boolean, it falls back to false. Explicit and resolved Boolean values are preserved. Missing bindings remain visible in the diagnostics; the preview does not invent initialized game data for conditional slots.

Interactive previews defer expensive visual construction for hidden subtrees. This applies both to a locally hidden widget and to widgets beneath a hidden ancestor. A collapsed stand-in identifies the deferred branch in the inspector and retains its source location. The document, complete logical Render Structure, expanded list-instance identities and Behavior Graph preflight remain available. Binding/scenario changes trigger a new render, which constructs branches that become visible.

Deferral does not bypass Required Connection validation. The render plan protects selected behavior owners, their references, layout sources/targets and requested Key Bounds. Sources used for dimension copying retain their contents. Controls whose layout behavior can reveal content retain the corresponding content. Hidden Canvas children whose content or assets can affect surrounding geometry also retain normal construction; flow-panel children that do not contribute to visible layout can be deferred.

The diagnostics expose `DeferredHiddenSubtrees` and a `HiddenSubtreesDeferred` section with Widget IDs, paths, instance keys and logical subtree counts. A preview with deferred hidden content has `Partial` validation status because hidden asset validation has not run. This is distinct from budget-based Truncation: deferral does not add a Truncation reason. The stand-in is not reported as a resolved image or as fully materialized behavior.

Strict captures retain full hidden materialization by default. `RegressionRenderOptions.DeferHiddenSubtrees` explicitly opts a caller into deferral. The app enables this flag for interactive scenarios; CLI captures and behavior-trace inspection keep the strict default. Visibility fallback is the same in both modes.

`HiddenSubtreeTests` verifies absent and unresolved visibility, explicit bindings, retained logical nodes and source navigation, strict capture behavior, repeated renders, per-instance visibility, inherited visibility, dimension copying, Canvas geometry, and preflight failure for missing Required Connections. Existing native layout and image/glyph checks remain applicable. No baselines are accepted automatically.

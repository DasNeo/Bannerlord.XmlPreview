# Bannerlord.XmlPreview

Terminology for the faithful preview and analysis of Bannerlord Gauntlet prefabs.

## Language

**Correctly Rendered Prefab**:
A prefab whose Render Structure matches the reference, whose pixel differences remain within the agreed tolerance, and whose Diagnostics contain no unexplained warnings. Differences caused by preview data are explicitly identified.
_Avoid_: Looks correct, sufficiently supported

**Render Structure**:
The Gauntlet widget tree after includes and templates have been expanded, together with the agreed Key Bounds. The WPF Visual Tree is not part of the Render Structure.
_Avoid_: WPF tree, XML structure

**Code Widget**:
A mod-specific widget whose behavior requires custom executable code and is not defined by the standard Gauntlet description alone. The preview represents it with an explicitly labeled placeholder.
_Avoid_: Unknown widget, unsupported widget

**Preview Scenario**:
A named, persistent preview state for a prefab. It defines bindings, list contents, widget states, resolution, and permitted dynamic image regions; every masked region has a name, reason, and bounds.
_Avoid_: Mock, test data

**Reference Screenshot**:
A target view captured manually in the game with a documented game version, module version, resolution, UI scale, language, scene, data state, and crop rule.
_Avoid_: Golden, comparison image

**Pixel Tolerance**:
The calibrated global threshold for maximum color-channel difference and proportion of differing pixels. Fixture-specific deviations are permitted only with a documented reason.
_Avoid_: Looks similar, approximate match

**Key Bounds**:
The layout bounds that a visual test treats as authoritative for named widgets and selected root and layout nodes.
_Avoid_: All WPF bounds, arbitrary bounds

**Partial**:
A test result for recognized content that the preview controls and visibly replaces but does not render faithfully. A Code Widget with a labeled placeholder has this status.
_Avoid_: Passed, Failed, partially passed

**Visual Regression**:
The module that accepts a prefab, a Preview Scenario, and reference metadata, then produces a structured comparison report and reference, actual, diff, and overlay artifacts.
_Avoid_: Screenshot script, pixel test

**Full**:
A Visual Regression run mode in which Render Structure, Key Bounds, and pixel comparison are required.
_Avoid_: Full test, complete test

**Structure Only**:
A Visual Regression run mode in which Render Structure and Key Bounds are required and pixel comparison is visibly reported as `NotRun`.
_Avoid_: Full, pixel test passed

**Runtime-only Placeholder**:
A visible replacement for a dynamic image whose real content is available only during game execution. It produces a `Partial` result and must not appear to be a real asset.
_Avoid_: Fallback image, preview image

**Widget Behavior**:
Bannerlord-specific rendering or interaction behavior that the Bannerlord adapter supplies as a rule and phase-specific contribution to the Behavior Graph.
_Avoid_: Widget hack, widget special case

**Behavior Graph**:
The graph of Widget Behavior rules, five Behavior Phases, and explicit dependencies that is resolved and deterministically sorted for every render run. The generic Core executes the graph without knowing concrete Bannerlord widget names and discards all graph state after the run.
_Avoid_: Behavior list, handler chain

**Behavior Phase**:
One of the five fixed execution stages: `Describe`, `Instantiate`, `Connect`, `PrepareLayout`, and `Inspect`. `Inspect` is read-only after Arrange; adding another phase requires a new project decision.
_Avoid_: Arbitrary hook, WPF lifecycle

**Behavior Contribution**:
The immutable, declarative result produced by a stateless handler for exactly one Behavior Phase. The five types are `DescriptionContribution`, `ElementBlueprint`, `ConnectionContribution`, `LayoutContribution`, and `InspectionContribution`.
_Avoid_: WPF mutation, callback result

**Rule ID**:
The global, stable identity of a Behavior rule in the form `core.<capability>` or `bannerlord.<widget-family>.<purpose>`. Renaming a Rule ID is a Diagnostics change that requires explicit review.
_Avoid_: Handler name, class name

**Widget ID**:
The stable identity of an expanded widget, composed of the canonical prefab path, expanded XML path, and the template or list-instance key from the Preview Scenario. WPF positions and object hashes are not part of this identity.
_Avoid_: WPF ID, object ID

**Required Connection**:
A widget reference whose target must exist for correct behavior. A missing target causes the render run to fail.
_Avoid_: Optional Connection, expected warning

**Optional Connection**:
A widget reference whose target may be absent when the Preview Scenario documents the reason. Its absence still produces a Diagnostic.
_Avoid_: Required Connection, ignored reference

**Behavior Trace**:
An explanation of the matchers, selected and skipped rules, execution order, contributions, and support status for a widget. A summary is always present; the full trace is generated only for debugging, failures, or requested Visual Regression artifacts.
_Avoid_: Log dump, stack trace

**Last Good Preview**:
The most recent successfully rendered preview, which remains visible after a later load failure and is clearly marked as stale.
_Avoid_: Current preview, fallback preview

**Fidelity Status**:
The published maturity of a widget family: `Verified` for a passed Full golden, `Implemented` for behavior without a Full golden, `Partial` for deliberately incomplete fidelity, `Placeholder` for replacement-only rendering, and `Unknown` for an unevaluated family.
_Avoid_: Supported, works

**Expected Warning**:
A Diagnostic documented in the Preview Scenario with its code, widget path, and reason. Any new or changed warning causes the comparison to fail.
_Avoid_: Known warning, ignored warning

**Reference Package**:
An optional, separately distributed package containing Reference Screenshots and their manifest. It is immutable for a specific game version, module version, and Capture Profile; the source repository contains scenarios, metadata, and expected hashes.
_Avoid_: Asset package, baseline folder

**Capture Profile**:
The named combination of resolution, UI scale, language, scene, data state, and crop rule used to capture Reference Screenshots.
_Avoid_: Screenshot settings, environment

**Truncation**:
An incomplete render run in which a configured node or rendering budget omits additional widgets. Such a run has status `Partial` and must not be reported as complete.
_Avoid_: Completed, performance optimization

**Baseline Acceptance**:
The explicit approval of new target artifacts through a separate accept command. Approval displays the reference, actual, diff, overlay, and metrics and never occurs automatically.
_Avoid_: Baseline update, automatic overwrite

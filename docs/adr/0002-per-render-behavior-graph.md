---
status: accepted
---

# Plan declarative Widget Behavior for every render

Core resolves one Behavior Graph per expanded render tree through `BehaviorGraph.Plan`; the graph and its immutable results are discarded after the run, with no graph cache or game-version cache key. The fixed phases are `Describe`, `Instantiate`, `Connect`, `PrepareLayout`, and read-only `Inspect` after Arrange; their contracts are `DescriptionContribution`, `ElementBlueprint`, `ConnectionContribution`, `LayoutContribution`, and `InspectionContribution`.

The Bannerlord adapter supplies stateless handlers, matchers, stable Rule IDs, and trusted game type metadata. Handlers receive immutable XML descriptions, resolved scenario values, and Widget IDs and return declarative contributions. Core validates the complete plan before WPF materialization, owns fallback and generic materialization, and rejects conflicts, cycles, missing Required Connections, and unexplained missing Optional Connections. Direct WPF mutation by handlers, permanent parallel legacy dispatch, and executable mod rules are rejected: they would bypass preflight and weaken determinism and the boundary already recorded in [ADR 0001](0001-do-not-execute-mod-code-widgets.md).

The decision was accepted after `BehaviorRenderTests` passed local visibility, scroll viewport/inner-panel/bar connections, ordered state propagation, and measured dimension copying with padding. Dimension copying used the existing `LayoutContribution` and required no new phase or graph entry point. Graph-interface tests cover sorting, conflict/error handling, append ordering, fallback, cancellation, immutable scenario isolation, and trusted metadata. See [M2 evidence and fidelity limits](../reports/m2-completion.md) for structural acceptance and the distinction between preview verification and optional in-game pixel evidence.

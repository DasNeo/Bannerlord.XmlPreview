# Milestone Implementation Plans

Status: M0 complete and reverified 2026-10-04; M1 implementation complete, milestone acceptance pending; M2-M4 proposed

These plans turn the [development roadmap](../roadmap.md) into executable work packages. They preserve the terminology in [GLOSSARY.md](../../GLOSSARY.md) and the execution boundary in [ADR 0001](../adr/0001-do-not-execute-mod-code-widgets.md).

## Execution order

1. [M0: Stabilize the Current Refactor](./m0-stabilize-current-refactor.md)
2. [M1: Measurable Visual Fidelity](./m1-measurable-visual-fidelity.md)
3. [M2: Behavior Graph and Widget Fidelity](./m2-behavior-graph.md)
4. [M3: Image and Asset Fidelity](./m3-image-and-asset-fidelity.md)
5. [M4: Authoring Loop](./m4-authoring-loop.md)

Each milestone starts only after the previous milestone meets its exit criteria. A later milestone may add a narrow enabling contract earlier only when the current milestone cannot be completed without it. Such an exception must be documented in the current milestone report.

M0 implementation and verification are recorded in [the M0 completion report](../reports/m0-completion.md).

M1's implementation, verification, performance evidence and remaining automated validation acceptance are recorded in [the M1 progress report](../reports/m1-progress.md). Screenshot capture and Full comparison are optional and do not block milestones.

## Rules shared by every milestone

- Preserve unrelated user changes in the working tree.
- Prefer vertical changes that include implementation, diagnostics, fixture coverage, and documentation together.
- Keep Core generic. Bannerlord names and semantics belong in the Bannerlord adapter.
- Treat the module interface as the primary test surface.
- Replace obsolete tests and paths when a new seam supersedes them; do not maintain permanent parallel implementations.
- Add structured outcomes before scripts parse human-readable text.
- Never convert `Partial`, Truncation, missing assets, or missing behavior into `Passed` for convenience.
- Record performance before optimizing it.
- Update the roadmap, Glossary, and relevant milestone report when an accepted decision changes.

## Milestone report format

Each milestone closes with `docs/reports/m<N>-completion.md` containing:

- implemented work packages
- important deviations from the plan
- build and test commands with results
- fixture and Visual Regression results
- performance measurements where applicable
- known limitations and explicitly deferred work
- links to ADRs created during the milestone

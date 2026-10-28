# Presentation structural baselines

Accepted on 2026-10-25 with explicit user approval after review of the
candidate reports and changed bounds. This immutable set starts from
`issue-6-portable-baselines` and updates three Structure Only cases:

- `standard-dropdown`: at most 1.75 px from removing synthetic brush borders.
- `options`: at most 2 px from removing synthetic brush borders.
- `conversation`: 20 px on the banner's X coordinate from honoring its declared
  position offset in a flow layout.

The other six cases retain their previous accepted expectations. Prior sets
are preserved. The explicit `accept` command verified each candidate's
scenario, structure/layout hashes, completion and Diagnostics before producing
this new set. No Reference Screenshots, pixel tolerance or pixel acceptance
are included. The portable Native suite uses this set; historical scenario
hash checks continue to cover earlier accepted sets as well.

The source fix also settles CoverChildren dependencies for centered children.
The intermediate 245 px conversation row-height difference was a renderer bug
that was fixed before approval and is not part of this acceptance.

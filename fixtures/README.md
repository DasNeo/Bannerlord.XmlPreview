# Preserved prefab fixtures

`AgricultureEstate/GUI/Prefabs/LandManagement.xml` preserves the existing Land Management regression baseline after the installed mod prefab changed. Copied byte-for-byte on 2026-10-04 from the supplied `LandManagement.xml` (source last modified 2026-07-30).

Both `tools/open_phase0_fixture.ps1` and `tools/run_phase9_validation.ps1` load this copy for `land-management`, relative to `RepoRoot`. Keep it stable when updating the installed mod. Brushes, sprites, and referenced prefabs still resolve through the Bannerlord installation selected by `BannerlordRoot`; this is a preserved XML baseline, not a self-contained asset bundle.

# Archived investigation tools

These tools are retained for historical decoder comparisons. They are outside the
production solution and required validation. Current asset loading reads installed
TPAC packages directly; DDS export and PNG extraction are not setup requirements.

- `extract_bannerlord_sprites.py`: crops sprites from previously exported DDS
  atlases. Requires Python 3.10 or later and Pillow in a separate environment.
  Run `python tools/archive/extract_bannerlord_sprites.py --help` for arguments.
- `run_tpac_spike.ps1` and `TpacSpike/`: compare the pinned candidate reader with
  separately supplied PNGs. Run `./tools/archive/run_tpac_spike.ps1
  -ComparisonPngRoot 'C:/path/to/comparison-pngs'` from the repository root.
  This downloads evaluation source into `artifacts/m3-spike/` and restores the
  spike's legacy dependencies. These are deliberately retained at their original
  versions for reproducibility, not maintained as current production dependencies.

The old `report_native_widget_coverage.ps1` was retired because its independent
support table no longer represented the Behavior Graph or Fidelity Status. Use
preview Diagnostics and Behavior Traces for individual widgets and the published
[image fidelity matrix](../../docs/fidelity/images.md) for verified image scope.

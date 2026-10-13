# M3 AgricultureEstate expectations

Accepted through the explicit CLI accept operation on 2026-10-12 from the preserved full Land Management fixture. Version: AgricultureEstate v1.4.7.3 on Bannerlord v1.4.7. Sprite data comes from TPAC metadata plus its validated RuntimeDataCache pixel companion, without extracted image prerequisites. The missing clan_frame_small asset is explicitly diagnosed and never substituted.

See `../m3-scenarios/README.md` for asset dependency scope and diagnostics expectations.

The final package includes the reviewed module-metric correction: Encyclopedia.Frame resolves from the declared SandBox dependency, giving popup bounds 1500 x 960 rather than the previous generic height fallback of 1000. Tests independently assert the brush metric, loaded constant and arranged bounds. The earlier candidate remains in `artifacts/m3-module-metrics-before`.

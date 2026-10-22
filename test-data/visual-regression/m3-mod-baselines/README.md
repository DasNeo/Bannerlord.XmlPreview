# M3 AgricultureEstate expectations

Accepted through the explicit CLI accept operation on 2026-10-22 from the updated full Land Management dashboard fixture. Version: AgricultureEstate v1.4.7.3 on Bannerlord v1.4.7. Fixture SHA-256: `f76ad36b175dbb21ce785b49999938d5849145385f243ec09b71094b741c62d7`. Sprite data comes from TPAC metadata plus its validated RuntimeDataCache pixel companion, without extracted image prerequisites. The new layout uses `frame_small_9` and no longer references the missing `clan_frame_small` asset. Explicit scenario bindings produce no warnings.

See `../m3-scenarios/README.md` for asset dependency scope and diagnostics expectations.

Encyclopedia.Frame resolves from the declared SandBox dependency, giving popup bounds 1500 x 960. Tests independently assert the brush metric, loaded constant, and arranged dashboard and panel bounds. Structural comparison checks the full Render Structure and Key Bounds; pixels remain NotRun. The updated candidate and acceptance output are in `artifacts/agriculture-estate-update/` when generated locally.

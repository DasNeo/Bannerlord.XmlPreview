# Prefab fixtures

`AgricultureEstate/GUI/Prefabs/LandManagement.xml` contains the dashboard layout from the installed AgricultureEstate v1.4.7.3 module: holdings, upgrade cards, workforce, stockpile, and project panels. Copied byte-for-byte on 2026-10-22 from `Modules/AgricultureEstate/GUI/Prefabs/LandManagement.xml` (source last modified 2026-10-04). SHA-256: `f76ad36b175dbb21ce785b49999938d5849145385f243ec09b71094b741c62d7`.

## Attribution and permissions

The fixture comes from [Agriculture Estate Updated](https://www.nexusmods.com/mountandblade2bannerlord/mods/4535), maintained by this project's author, DasNeo (DasNeoxx on Nexus Mods). It continues [Agriculture Estate](https://www.nexusmods.com/mountandblade2bannerlord/mods/3088), whose page credits LL Muse as creator and kz14365 as uploader.

As checked on 2026-10-22, both Nexus pages permit uploads to other sites with creator credit, modifications, and asset reuse. Both prohibit use of their assets in mods or files sold for money. These are the source mod's permissions, not an MIT license. The fixture retains those terms and is excluded from this repository's MIT grant; consult the linked pages for the full permissions. Attribution here preserves credit to the original creators and the continuing maintainer.

Both `tools/open_phase0_fixture.ps1` and `tools/run_phase9_validation.ps1` load this copy for `land-management`, relative to `RepoRoot`. Update the M3 scenario and structural expectations deliberately when replacing it. Brushes, sprites, and referenced prefabs still resolve through the Bannerlord installation selected by `BannerlordRoot`; this fixture contains XML only. The README preview already uses this layout; its capture provenance is recorded in `docs/images/previews/manifest.json`.

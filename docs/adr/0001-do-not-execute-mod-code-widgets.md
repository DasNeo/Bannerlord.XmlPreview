---
status: accepted
---

# Do Not Execute Mod Code for Widgets

Bannerlord.XmlPreview does not execute third-party mod code. It reproduces standard Gauntlet behavior faithfully, while recognized Code Widgets render as visible placeholders with `Partial` status because loading third-party assemblies would sacrifice determinism, stability, and a clear preview execution boundary.

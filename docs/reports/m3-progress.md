# M3 progress: direct TPAC decoding spike

Spike recorded 2026-10-12 after the [M2 completion audit](m2-completion.md). This report preserves initial investigation evidence; production implementation and final validation are now recorded in [M3 completion](m3-completion.md).

## Implemented slice

`tools/TpacSpike` is an isolated .NET 10 Windows evaluation executable, outside the production solution. `tools/run_tpac_spike.ps1` fetches the pinned candidate reader into ignored artifacts, checks its commit, and runs it against installed Native data. The host excludes tools and artifacts from SDK source and XAML discovery.

As of 2026-10-13, comparison PNGs must be supplied explicitly with `-ComparisonPngRoot <external-directory>`. The repository no longer retains an extracted-image directory or a default comparison-image location. Missing inputs fail before fetching or building the evaluation reader. Historical measurements below retain the command used at the time.

The spike reads `gauntlet_ui.tpac` metadata lazily, loads requested texture payloads, decodes BGRA pixels in memory, crops with NativeSpriteData coordinates, freezes WPF bitmaps, and compares every RGBA channel with existing extracted PNGs. Invalid crop bounds fail explicitly. It emits cropped PNGs and a JSON report containing texture inventory, dimensions, crop coordinates, timing, allocation, process peak working set and comparison results. Missing comparison inputs and pixel differences return exit 1. Existing PNGs are comparison inputs for this spike, not a proposed production prerequisite.

## Candidate review

- [Upstream TpacTool](https://github.com/szszss/TpacTool), inspected at `b56b77ad273ba67192b1594dbb2eeca8c542b3b7`: MIT package reader with lazy LZ4 payload loading. Its pixel decoder lacks BC7.
- [GauntletUI-LSP](https://github.com/kerema14/GauntletUI-LSP), inspected at `c9479dc5e927f00e482230582d5221ffc7789636`: integration reference for atlas naming and SpriteData crops.
- [Pinned TpacTool fork](https://github.com/kerema14/TpacTool/tree/6b115ff3a6cebdbe25af424eb13926078ed22b2d): adds BC7 via BCnEncoder.NET 2.3.0. Its library sources compile for this net10.0-windows spike. The unused AssetManager is excluded because its Thread.Abort API is unsupported on modern .NET.
- Dependencies inspected: TpacTool MIT; lz4net 1.0.15.93 BSD redistribution terms; JetBrains.Annotations 2021.3.0; [BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET) 2.3.0 package declares MIT OR Unlicense and CommunityToolkit.HighPerformance 8.4.0. Production integration still needs retained notices and a complete transitive license review.

At the spike, no candidate source or dependency had been added to the production adapter. The spike links locally fetched reader sources; upstream code remains in ignored artifacts. The later production reader/decoder decision is documented in the completion report.

## Measurements and verification

Command: `powershell -ExecutionPolicy Bypass -File tools/run_tpac_spike.ps1`.

Machine: validation-host; .NET 10.0.5; Release build. Metadata load: 12.19 ms. First load means first use in this process; the OS disk cache was not flushed. Warm decode is the median of five repeated decodes of retained compressed payloads. Retained-atlas crop measures one in-memory crop and excludes bitmap creation and PNG encoding.

| Format / texture | Dimensions | Payload load | First decode | Warm decode median | Retained crop | Decoded bytes |
| --- | --- | --- | --- | --- | --- | --- |
| BC7 / ui_bannerbuilder_1 | 512 x 128 | 14.07 ms | 21.25 ms | 11.46 ms | 0.136 ms | 0.25 MiB |
| BC7 / ui_mplobby_1 | 4096 x 4096 | 6.30 ms | 654.01 ms | 474.18 ms | 0.021 ms | 64 MiB |
| DXT5 / ui_group1_3 | 4096 x 2048 | 5.30 ms | 192.27 ms | 47.01 ms | 0.014 ms | 32 MiB |

All three selected sprite crops matched the existing PNGs exactly: dimensions, orientation, color and alpha; maximum channel difference and differing-pixel count were zero. This is decoding consistency evidence, not an in-game screenshot comparison. Raw measurements and comparison crop identities are in `artifacts/m3-spike/results/report.json`.

The large BC7 first load allocated about 261 MiB. The combined process peak reached about 840 MiB during repeated large-atlas decodes. This is a process-wide high-water mark including GC retention and all preceding samples, not an individual texture's retained memory. It argues for limiting concurrent decodes as well as retained atlas bytes. The installed UI package contains BC7 (21), DXT1 (59), DXT5 (8), BC4 (1), and R8_UNORM (8), all with pixel payloads.

## Integration recommendations recorded at the spike

Implement structured asset outcomes and synthetic module dependency/precedence tests, then integrate lazy TPAC resolution behind the Bannerlord asset catalog. Use a byte-bounded decoded-atlas cache, coalesce simultaneous requests, and bound expensive concurrent decoding. The initial evidence favors a reader plus managed decoder approach; a production dependency decision is still pending controlled decoder tests and comparison of a smaller maintained reader integration.

The integration work listed at the spike was DXT1/BC4/R8 controlled decoding tests, corrupt/unsupported/absent payload cases, premultiplied-alpha materialization checks, mod package lookup, dependency precedence, production cache tests, and image-family Behavior rules and acceptance scenarios. This work is now verified in the completion report; the spike alone did not certify those exit criteria.

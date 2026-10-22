# Third-party image and package components

The root MIT license covers original project code and documentation. It does not relicense third-party components, game or mod content, or screenshots containing that content. Their existing rights and license terms continue to apply.

The mod prefab fixture at `fixtures/AgricultureEstate/GUI/Prefabs/LandManagement.xml` comes from Agriculture Estate Updated, maintained by DasNeo (DasNeoxx), continuing the original mod credited to LL Muse and uploaded by kz14365. It is excluded from the project's MIT grant and retains the source mod's permissions, including creator attribution for uploads and restrictions on use in sold mods or files. See [fixture attribution and permissions](fixtures/README.md) for both source pages and the permission summary checked on 2026-10-22.

The production TPAC texture field reader in `src/Bannerlord.XmlPreview.Bannerlord/Assets/TpacTextureReader.cs` adapts the binary layout from [szszss/TpacTool](https://github.com/szszss/TpacTool), reviewed at `b56b77ad273ba67192b1594dbb2eeca8c542b3b7`. Copyright 2020-2022 szszss, MIT. The complete notice is retained in `licenses/TpacTool-MIT.txt`. TpacTool's GUI, exporters, mesh readers and assembly manager are not shipped.

Production dependencies:

| Component | Version | License / notice |
| --- | --- | --- |
| [BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET) | 2.3.0 | MIT option, `licenses/BCnEncoder-MIT.txt` |
| [K4os.Compression.LZ4](https://github.com/MiloszKrajewski/K4os.Compression.LZ4) | 1.3.8 | MIT, `licenses/K4os-LZ4-MIT.txt`; underlying LZ4 BSD notice in `licenses/LZ4-BSD.txt` |
| [CommunityToolkit.HighPerformance](https://github.com/CommunityToolkit/dotnet) | 8.4.0, transitive | MIT, `licenses/CommunityToolkit-MIT.txt` and `licenses/CommunityToolkit-ThirdPartyNotices.txt` |

The net10.0 production build selects BCnEncoder's netstandard2.1 assets. Its optional netstandard2.0-only Microsoft.Bcl.Numerics dependency is not selected. Package versions are pinned in the Bannerlord project. The host copies these notices into build and publish output.

The archived `tools/archive/TpacSpike` evaluation additionally links the kerema14/TpacTool fork and uses lz4net and JetBrains.Annotations. Those components stay outside the production solution. See [archived investigation tools](tools/archive/README.md) for their scope and invocation.

Game and mod images remain the property of their producers. The application loads assets from the user's installation; game asset packages are not bundled. The README includes screenshots illustrating the game and preview output.

Projektüberblick
Bannerlord.XmlPreview ist ein WPF-Tool zum Rendern von Bannerlord-Gauntlet-UI-Prefabs aus XML in eine Preview-Oberfläche. Ziel der laufenden Refaktorierung war, den generischen Renderer-Core von Bannerlord-spezifischem Wissen zu trennen.
Solution-Struktur
Es gibt aktuell 4 Projekte:
- App: [Bannerlord.XmlPreview.csproj](C:/Dev/WPF/Bannerlord.XmlPreview/Bannerlord.XmlPreview.csproj)
- Core: [Bannerlord.XmlPreview.Core.csproj](C:/Dev/WPF/Bannerlord.XmlPreview/Bannerlord.XmlPreview.Core/Bannerlord.XmlPreview.Core.csproj)
- Bannerlord-Adapter: [Bannerlord.XmlPreview.Bannerlord.csproj](C:/Dev/WPF/Bannerlord.XmlPreview/Bannerlord.XmlPreview.Bannerlord/Bannerlord.XmlPreview.Bannerlord.csproj)
- DemoData: [Bannerlord.XmlPreview.DemoData.csproj](C:/Dev/WPF/Bannerlord.XmlPreview/Bannerlord.XmlPreview.DemoData/Bannerlord.XmlPreview.DemoData.csproj)
Die Solution ist: [Bannerlord.XmlPreview.sln](C:/Dev/WPF/Bannerlord.XmlPreview/Bannerlord.XmlPreview.sln)
Architektur in einem Satz
Core rendert XML-Knoten generisch und fragt über Interfaces nach externem Wissen; Bannerlord liefert dieses Wissen über Adapter/Provider; die WPF-App verdrahtet alles per DI.
Wichtige Laufzeit-Pipeline
1. XML wird über [PrefabLoader.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Services/PrefabLoader.cs) geladen.
2. Widget-Namen werden über IWidgetNodeFactory in Node-Typen gemappt.
3. Ein PrefabDocument wird erzeugt.
4. [PrefabRenderer.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Services/PrefabRenderer.cs) erstellt den PrefabRenderContext.
5. Die Node-Hierarchie rendert sich rekursiv in WPF-Elemente.
6. Die WPF-App zeigt das Ergebnis im Preview-UI an.
Wichtige Core-Bausteine
- Render-Kontext: [PrefabRenderContext.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Rendering/PrefabRenderContext.cs)
- Dokumentmodell: [PrefabDocument.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Models/PrefabDocument.cs)
- Node-Basis: [PrefabNode.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Models/Nodes/PrefabNode.cs)
- Typische generische Nodes:
  - [WidgetNode.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Models/Nodes/WidgetNode.cs)
  - [ContainerPrefabNode.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Models/Nodes/ContainerPrefabNode.cs)
  - [ButtonWidgetNode.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Models/Nodes/ButtonWidgetNode.cs)
  - [BrushWidgetNode.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Models/Nodes/BrushWidgetNode.cs)
  - [NavigatableListPanelNode.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Models/Nodes/NavigatableListPanelNode.cs)
Wichtige Adapter-Schnittstellen
Diese liegen in Core unter den Abstractions und definieren den Schnitt zwischen generischem Renderer und Bannerlord-Wissen:
- [IWidgetNodeFactory.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Core/Abstractions/IWidgetNodeFactory.cs)
- [IPreviewDataSourceProvider.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Core/Abstractions/IPreviewDataSourceProvider.cs)
- [IRootLayoutHints.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Core/Abstractions/IRootLayoutHints.cs)
- [IBrushMetricsProvider.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Core/Abstractions/IBrushMetricsProvider.cs)
- [IBrushOverlayFeatureHandler.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Core/Abstractions/IBrushOverlayFeatureHandler.cs)
- [IWidgetTextSourceProvider.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Core/Abstractions/IWidgetTextSourceProvider.cs)
- [IWidgetDebugMetadataProvider.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Core/Abstractions/IWidgetDebugMetadataProvider.cs)
- [IAssetCatalog.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Interfaces/IAssetCatalog.cs)
Bannerlord-spezifische Implementierungen
Die Adapter sitzen im Projekt Bannerlord:
- Widget-Mapping: [BannerlordWidgetNodeFactory.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Bannerlord/Adapters/BannerlordWidgetNodeFactory.cs)
- Root-Layout-Hints: [BannerlordRootLayoutHints.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Bannerlord/Adapters/BannerlordRootLayoutHints.cs)
- Brush-Metriken: [BannerlordBrushMetricsProvider.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Bannerlord/Adapters/BannerlordBrushMetricsProvider.cs)
- Brush-Overlay-Sondermodi wie CoverWithTexture: [BannerlordBrushOverlayFeatureHandler.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Bannerlord/Adapters/BannerlordBrushOverlayFeatureHandler.cs)
- Button-Text-Sonderwissen: [BannerlordWidgetTextSourceProvider.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Bannerlord/Adapters/BannerlordWidgetTextSourceProvider.cs)
- Tooltip-/Debug-Schlüssel: [BannerlordWidgetDebugMetadataProvider.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Bannerlord/Adapters/BannerlordWidgetDebugMetadataProvider.cs)
- Preview-DataSource-Sonderfälle: [BannerlordPreviewDataSourceProvider.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Bannerlord/Preview/BannerlordPreviewDataSourceProvider.cs)
- Bannerlord-Asset-Katalog: [BannerlordAssetCatalog.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Services/BannerlordAssetCatalog.cs)
- Bannerlord-spezifisches Escape-Menu-Widget: [EscapeMenuButtonWidgetNode.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Bannerlord/Widgets/EscapeMenuButtonWidgetNode.cs)
Wichtige Refaktorierungs-Ergebnisse
Diese Punkte sind schon umgesetzt:
- Widget-Mapping ist aus PrefabLoader in IWidgetNodeFactory gezogen.
- NavigatableListPanel kennt keine Bannerlord-Demo-Items mehr direkt, sondern fragt IPreviewDataSourceProvider.
- EscapeMenuButtonWidget ist isoliert im Bannerlord-Adapter.
- Root-Größe läuft über IRootLayoutHints.
- Parameter.ButtonText läuft über IWidgetTextSourceProvider.
- Brush-Metriken/Padding laufen über IBrushMetricsProvider.
- Tooltip-/Debug-Schlüssel laufen über IWidgetDebugMetadataProvider.
- CoverWithTexture läuft über IBrushOverlayFeatureHandler.
- IBannerlordAssetCatalog wurde in neutrales IAssetCatalog umbenannt.
- Die wichtigsten Asset-Modelle im Core tragen jetzt generische Namen wie SpriteDefinition, BrushDefinition, BrushLayerDefinition.
DI-Verdrahtung
Die App registriert die Implementierungen in [App.xaml.cs](C:/Dev/WPF/Bannerlord.XmlPreview/App.xaml.cs). Wenn etwas “magisch” zur Laufzeit zusammengebaut wird, ist das meistens dort verdrahtet.
Preview-/Debug-relevante Stellen
- Debug Attached Properties: [PreviewElementDebugProperties.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Rendering/PreviewElementDebugProperties.cs)
- Auswahlrahmen im UI: [SelectionBorderAdorner.cs](C:/Dev/WPF/Bannerlord.XmlPreview/Rendering/SelectionBorderAdorner.cs)
- ViewModel der App: [MainWindowViewModel.cs](C:/Dev/WPF/Bannerlord.XmlPreview/ViewModels/MainWindowViewModel.cs)
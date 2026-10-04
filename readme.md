# Bannerlord.XmlPreview

`Bannerlord.XmlPreview` ist ein WPF-Werkzeug zum Laden, Expandieren und Rendern von Bannerlord-Gauntlet-Prefabs aus XML. Ziel ist eine nachvollziehbare Preview, die Layout-, Asset-, Binding- und Referenzprobleme ohne Spielstart untersuchbar macht.

## Solution

- `Bannerlord.XmlPreview.csproj`: WPF-Host, DI, UI und Startargumente
- `Bannerlord.XmlPreview.Core/`: generische Loader-, Render-, Node- und Diagnostics-Logik
- `Bannerlord.XmlPreview.Bannerlord/`: Bannerlord-spezifische Adapter, Assets und Preview-Daten
- `Bannerlord.XmlPreview.DemoData/`: deterministische Preview-Daten
- `Bannerlord.XmlPreview.Tests/`: automatisierte M0-Vertrags- und Diagnostics-Tests

Der Core bleibt generisch. Bannerlord-Namen und -Semantik gehören in den Adapter. Mod-spezifischer ausführbarer Widget-Code wird nicht geladen; siehe [ADR 0001](docs/adr/0001-do-not-execute-mod-code-widgets.md).

## Build und Tests

```powershell
dotnet restore C:\Dev\WPF\Bannerlord.XmlPreview\Bannerlord.XmlPreview.sln
dotnet build C:\Dev\WPF\Bannerlord.XmlPreview\Bannerlord.XmlPreview.sln --no-restore
dotnet test C:\Dev\WPF\Bannerlord.XmlPreview\Bannerlord.XmlPreview.sln --no-build
```

## App starten

```powershell
C:\Dev\WPF\Bannerlord.XmlPreview\bin\Debug\net10.0-windows\Bannerlord.XmlPreview.exe
```

Unterstützte Argumente:

- `--prefab <path>` lädt direkt ein Prefab.
- `--bannerlord-root <path>` setzt die Bannerlord-Installation.
- `--diagnostics-out <path>` schreibt einen Diagnostics-Snapshot.
- `--exit-after-load` beendet die App nach dem technischen Lauf.

Beispiel:

```powershell
C:\Dev\WPF\Bannerlord.XmlPreview\bin\Debug\net10.0-windows\Bannerlord.XmlPreview.exe `
  --bannerlord-root "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord" `
  --prefab "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Native\GUI\Prefabs\Standard\Standard.Window.xml"
```

## Technische Regression

Phase 9 prüft sechs kuratierte Fixtures. `Complete` ist erfolgreich; Timeout, Prozessfehler, fehlende Diagnostics, `Partial` und `Failed` führen zu einem Gesamt-Exitcode ungleich null. Einzel-Diagnostics und `summary.md` bleiben auch bei Fehlschlägen erhalten.

Das Fixture `land-management` verwendet in beiden Fixture-Skripten die archivierte Version unter [`fixtures/AgricultureEstate/GUI/Prefabs/LandManagement.xml`](fixtures/AgricultureEstate/GUI/Prefabs/LandManagement.xml). Herkunft und Abhängigkeiten sind in der [Fixture-Dokumentation](fixtures/README.md) beschrieben.

```powershell
powershell -ExecutionPolicy Bypass -File C:\Dev\WPF\Bannerlord.XmlPreview\tools\run_phase9_validation.ps1 `
  -PerFixtureTimeoutSeconds 30 `
  -OutputRoot C:\path\to\m0-validation
```

Diagnostics enthalten unter anderem `CompletionStatus`, `TruncationReason`, `RootActual`, `ElementLayout` und Phasen-Timings. Der Lauf ist eine technische Prüfung und keine visuelle Endabnahme.

## Architektur und Planung

- [Glossar](GLOSSARY.md)
- [Roadmap](docs/roadmap.md)
- [Milestone-Pläne](docs/implementation-plans/README.md)
- [M0-Abschlussbericht](docs/reports/m0-completion.md)

Wichtige Einstiegspunkte sind `App.xaml.cs`, `ViewModels/MainWindowViewModel.cs`, `Bannerlord.XmlPreview.Core/Services/PrefabLoader.cs`, `Bannerlord.XmlPreview.Core/Services/PrefabRenderer.cs` und `Bannerlord.XmlPreview.Core/Rendering/PrefabRenderContext.cs`.

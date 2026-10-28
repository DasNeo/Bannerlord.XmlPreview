# Inventory item tuple regression

`Modules/SandBox/GUI/Prefabs/Inventory/InventoryItemTuple.xml` depends on runtime
Widget Behavior as well as XML layout. Opening it alone selects the market side
because `IsPlayerItem` defaults to `false`. To preview the inventory side, include
it from a wrapper with `Parameter.IsPlayerItem="true"`; a binding cannot change a
parameter already resolved by the loader.

The loader preserves prefab-scoped visual definitions and resolves their
parameters and constants independently for each include. Rendering uses the
final static state, including its dimensions, margins and offsets. Transition
animation and game commands are not simulated.

The Bannerlord adapter supplies tuple background and equipment eligibility,
main/name/extension states, extension visibility, and value profit states. A
standalone tuple defaults to battle equipment mode. Set `Preview.EquipmentMode`
on the tuple in a scenario's `WidgetStates` to `0` for civilian, `1` for battle,
or `2` for stealth. This is explicit scenario data, not a live inventory screen
connection.

`BrushListPanel` retains stack layout while painting its brush. Trusted text and
image-identifier descendants retain text/provider rendering. Provider-image
previews hide the loading indicator and show either explicit scenario image
data or a labeled Runtime-only Placeholder. Installed CLI capture forwards the
same trusted type metadata and bitmap fonts as the desktop renderer.

## Verification

Portable tests in `InventoryRenderingTests` check include parameter isolation,
visual-state sizing, selected/collapsed layout, background and price rendering,
profit styles, and explicit versus runtime-only image data. Behavior Graph tests
check missing targets and conflicting writers for targeted descriptions.

With an installed game, run:

```powershell
$env:BANNERLORD_ROOT = 'C:/path/to/Mount & Blade II Bannerlord'
$env:BANNERLORD_INVENTORY_ARTIFACTS = 'C:/path/to/inventory-artifacts'
dotnet test tests/Bannerlord.XmlPreview.VisualRegression.Tests --filter FullyQualifiedName~InventoryRenderingTests
```

The installed case checks market/inventory and selected/collapsed combinations,
including transfer-button placement, background brush, price, thumbnail status,
and Key Bounds. It writes four PNGs and diagnostic/layout JSON files when the
artifact environment variable is set. Wrappers are temporary; shipped game XML
is unchanged.

The installed v1.4.7 tuple still refers to an absent `SliderTextWidget` and an
unresolved `Inventory\Extension\drop_shadow_on_everything` sprite. These are
reported rather than silently replaced. Item thumbnails need game runtime data
or an explicit scenario image and remain `Partial` otherwise. The supplied
in-game crops have no calibrated Capture Profile; these checks do not certify
pixel fidelity against them.

# Safehouse (Unity)

Same project as the Python/Tkinter `safehouse_v0_1` build, moving to Unity because the scope grew
(Gundog Revised-style character/skill system + Tarkov-style items, maps, combat).

## Folder structure

```
Assets/_Project/
    Scripts/
        Core/     Plain C# game logic - no UnityEngine.MonoBehaviour dependency.
                  Combat, expeditions, inventory placement, injury system, abilities. Ported
                  from safehouse/*.py, one file at a time. Testable without a scene.
        Data/     ScriptableObject definitions and data-loading code (the C# equivalent of
                  safehouse/catalog.py, gear.py, maps.py, raids.py: parsing + validation only).
        UI/       MonoBehaviours: screens, controllers, anything that touches a GameObject.
                  Calls into Core; never contains game rules itself.
        Editor/   Editor-only tooling (data import from the Tarkov snapshot, build scripts).
                  Anything in a folder named exactly "Editor" is excluded from player builds.
    Data/         ScriptableObject instances, once any exist. The raw rule data does NOT live
                  here - see Assets/StreamingAssets/data below.
    Prefabs/
    Scenes/
Assets/StreamingAssets/
    data/         The rule data itself: items, traders, maps, gear, containers, combat,
                  raid_nodes, maps_overrides - copied straight from the Python project's data/.
Assets/Tests/
    EditMode/     Unity Test Framework tests for Scripts/Core (the equivalent of tests/*.py).
    PlayMode/     Tests that need a running scene (UI flows, screen wiring).
```

## Where the data lives, and why

`Assets/StreamingAssets/data/*.json` are byte-for-byte copies of the Python project's `data/*.json`.
StreamingAssets ships them next to the built game as plain files you can open and edit, which is
exactly how the Python build works. `Resources/` would bake them into the player and take that away,
and a plain folder under `Assets/` can't be loaded by path at runtime at all.

Only the four hand-tuned files (`containers`, `combat`, `raid_nodes`, `maps_overrides`) are edited
here. `items`, `traders`, `maps` and `gear` stay generated: `tools/import_tarkov.py` in the Python
repo remains the one generator, and its output is copied over. Regenerating in two places would let
the two projects drift apart, and item ids must stay stable or existing saves stop opening.

`Scripts/Data/GameDataLoader.cs` locates, reads and version-checks those documents - the C#
counterpart of the Python `storage.prepare_environment`. It deliberately stops there: turning JSON
into typed rules belongs to each module as it gets ported.

## Tests

```
"C:\Program Files\Unity\Hub\Editor\6000.2.5f1\Editor\Unity.exe" -batchmode -nographics ^
  -projectPath "C:\Users\User\Desktop\Safehouse" -runTests -testPlatform EditMode ^
  -testResults "Logs\editmode-results.xml" -logFile "Logs\editmode.log"
```

Exit code 0 means every test passed; the XML lists them individually.

Tests live in their own top-level `Assets/Tests/`, not under `_Project/`, so a later asmdef split
keeps the test assembly from being pulled into anything that references `_Project` as a whole.
Only content under `Assets/` (or `Packages/`) is part of Unity's asset database at all - a folder
made next to `Assets/` is invisible to the Editor, which is why this isn't `Tests/` at the project root.

## Why this split

Mirrors the Python project's own rule ("규칙 계산은 순수 함수로 두고, 수치는 data/*.json에 둔다"):
**Core never touches UnityEngine**, so it can be unit-tested the same way the Python side is, and
so game rules stay in one place instead of leaking into MonoBehaviours. UI only renders and forwards
input to Core.

## Ported so far

| Python | C# | Notes |
| --- | --- | --- |
| `models.ItemDefinition`, `ItemInstance` | `Core/ItemDefinition.cs`, `Core/ItemInstance.cs` | Validation only; save parsing is not ported yet. |
| `inventory.py` | `Core/Placement.cs` | Every rule, checked against the Python output for the same grids. |
| `catalog.py` | `Data/CatalogLoader.cs` | JSON stays in Data so Core needs no dependencies. |

`Safehouse.Core` is compiled with `noEngineReferences`, so it genuinely cannot call into
UnityEngine - the same separation the Python side gets for free by keeping rules out of `ui/`.

## The GEAR screen (`Scripts/UI`, `UI/`)

A first real screen, built with UI Toolkit (UXML + USS) instead of uGUI/Canvas, matching the visual
language worked out in the design artboard at
https://claude.ai/artifact/Y8ycmAv5F6yfN76Mr4LbDB - open that link and `UI/Theme.uss` side by side
if a colour or spacing value ever needs to change; keep them in sync by eye.

- `UI/GearScreen.uxml` + `UI/Theme.uss` - the screen's layout and styling. LOADOUT and CARRIED show
  one hand-written illustrative loadout (the same simplification the artboard made); only the STASH
  panel is wired to real data.
- `Scripts/UI/GearScreenController.cs` - loads the shipped catalog (`CatalogLoader`), builds a demo
  stash (`Scripts/UI/Sample/SampleStash.cs`) by running real items through `PlacementRules.FirstFit`,
  and renders one cell per placed item. Clicking a cell (`Button.clicked`) updates the detail panel.
  This is the first place `Safehouse.Core`/`Safehouse.Data` actually drive a screen, not just a test.
- `Scripts/UI/Chrome/FacetedPanel.cs`, `GridBackground.cs` - USS has no `clip-path` and no repeating
  gradients, so the artboard's cut-corner panels and the stash's faint grid lines are drawn directly
  with `MeshGenerationContext.painter2D` instead of needing texture assets. `Theme.uss` deliberately
  avoids `box-shadow`/`outline` - support for those varies by Unity version, and an unsupported USS
  property just silently does nothing, which is a worse failure mode than not using it.
- Item icons are placeholder monograms (3 letters from the item's category), not art. The project
  will use real Tarkov icon sprites once those are in place; swapping a monogram `Label` for an
  `Image` is a one-line change per cell, so building throwaway vector icons here would waste effort.
- `Scripts/Editor/GearSceneBuilder.cs` (menu: **Safehouse > Build Gear Scene**) is the generator for
  `UI/GearPanelSettings.asset` and `Scenes/Gear.unity` - the same relationship as
  `tools/import_tarkov.py` and `data/items.json` on the Python side: the generator is what gets
  edited, its output is what gets committed (both files are checked in; don't hand-edit them, fix
  the builder and re-run it instead, safe any time since it overwrites both). Open `Scenes/Gear.unity`
  and press Play to actually see the screen - a batch-mode CLI run can compile and test it but cannot
  render it, so this is the only way to eyeball a visual change.
- `Assets/Tests/PlayMode/GearScreenControllerTests.cs` builds the real UXML + controller in a bare
  `GameObject` (no scene needed) and drives selection through `GearScreenController.SelectItem()`
  rather than simulating a pointer event, for the same coverage with far less machinery.

## Not done yet

- `Scripts/Editor` has no README-documented scope beyond `GearSceneBuilder` yet; add more tooling
  there as it's needed (data import from the Tarkov snapshot, build scripts).
- LOADOUT and CARRIED on the GEAR screen are static mock data, not a real character's loadout -
  that needs the save system (below) ported first.
- Combat, expeditions, gear and the injury system are still Python-only. They wait on the
  Gundog Revised combat/ability rules being settled.
- The save system (`storage.py`, `models.Profile`, `migrations.py`) is not ported. Its shape depends
  on how abilities end up working.
- No real fonts yet (Oswald / JetBrains Mono in the artboard) - the screen currently renders with
  Unity's default UI Toolkit font. Needs the font files added and wired into `Theme.uss`.
- Game art/icons are not bundled here, same reasoning as the Python project's `icon_cache/`
  (Escape from Tarkov assets via tarkov.dev: private use only).

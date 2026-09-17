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

## Not done yet

- `Scripts/UI` and `Scripts/Editor` are still empty and have no asmdef. Add one to each as soon as
  it gets code.
- Combat, expeditions, gear and the injury system are still Python-only. They wait on the
  Gundog Revised combat/ability rules being settled.
- The save system (`storage.py`, `models.Profile`, `migrations.py`) is not ported. Its shape depends
  on how abilities end up working.
- Game art/icons are not bundled here, same reasoning as the Python project's `icon_cache/`
  (Escape from Tarkov assets via tarkov.dev: private use only).

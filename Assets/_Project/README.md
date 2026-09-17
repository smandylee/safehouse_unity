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
    Data/         Actual data assets (imported from data/*.json in the Python project, or
                  ScriptableObject instances built from them).
    Prefabs/
    Scenes/
Assets/Tests/
    EditMode/     Unity Test Framework tests for Scripts/Core (the equivalent of tests/*.py).
    PlayMode/     Tests that need a running scene (UI flows, screen wiring).
```

Tests live in their own top-level `Assets/Tests/`, not under `_Project/`, so a later asmdef split
keeps the test assembly from being pulled into anything that references `_Project` as a whole.
Only content under `Assets/` (or `Packages/`) is part of Unity's asset database at all - a folder
made next to `Assets/` is invisible to the Editor, which is why this isn't `Tests/` at the project root.

## Why this split

Mirrors the Python project's own rule ("규칙 계산은 순수 함수로 두고, 수치는 data/*.json에 둔다"):
**Core never touches UnityEngine**, so it can be unit-tested the same way the Python side is, and
so game rules stay in one place instead of leaking into MonoBehaviours. UI only renders and forwards
input to Core.

## Not done yet

- No asmdef files yet (Core/Data/UI/Tests aren't split into separate assemblies). Add these once
  real code exists, so Tests can reference Core without pulling in UI/Editor code.
- No git repo initialized for this project yet.
- Game art/icons are not bundled here either, same reasoning as the Python project's `icon_cache/`
  (Escape from Tarkov assets via tarkov.dev: private use only).

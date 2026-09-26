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
                  raid_nodes, maps_overrides, hideout - copied straight from or equivalent to
                  the Python project's data/.
Assets/Tests/
    EditMode/     Unity Test Framework tests for Scripts/Core (the equivalent of tests/*.py).
    PlayMode/     Tests that need a running scene (UI flows, screen wiring).
```

## Where the data lives, and why

`Assets/StreamingAssets/data/*.json` are byte-for-byte copies of the Python project's `data/*.json`.
StreamingAssets ships them next to the built game as plain files you can open and edit, which is
exactly how the Python build works. `Resources/` would bake them into the player and take that away,
and a plain folder under `Assets/` can't be loaded by path at runtime at all.

The hand-tuned files (`containers`, `combat`, `raid_nodes`, `maps_overrides`, `hideout`) are edited
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
| `gear.py` (slots, calibers, capacity) | `Core/GearData.cs`, `Data/GearLoader.cs` | Only what the loadout reads; combat-only numbers and enemies wait for the combat port. |
| `session.equip` / `unequip`, `models.EquippedItem` | `Core/Loadout.cs`, `Core/LoadoutRules.cs` | Same rules: old piece returns to the stash, ammo must match the rifle, a refused change alters nothing. |
| `models.Profile`, `TraderState`, `config` constants | `Core/Profile.cs`, `Core/ProfileParts.cs`, `Core/ProfileRules.cs` | Schema 8 = the Python v6 profile plus `carried` (rig and backpack grids) and `personal_room` (level). Trader state is kept and written back untouched. |
| `storage.ProfileRepository`, `migrations.py` | `Data/ProfileRepository.cs`, `Data/ProfileSerializer.cs`, `Data/ProfileMigrations.cs`, `Data/Storage.cs` | Atomic writes, `.bak` of the previous save, restore from backup with the bad file kept in `recovery/`, migrations v1-v7 step for step, plus 7 to 8. |
| `session.InventorySession` (commit/rollback) | `Data/CharacterSession.cs` | A change is saved first and only then becomes the open character; a failed save changes nothing. Also loads and saves the shared account. |
| `traders.py`, `session.buy` / `sell` / `set_standing` | `Core/Traders.cs`, `Core/TradingRules.cs`, `Data/TraderLoader.cs` | Loyalty (needs both spending and standing), per-character stock that restocks in fixed UTC windows, buy/sell as one save, the no-arbitrage check across all traders. The clock is passed in, never read. No barter or quest locks - the Python build has none either. |
|| `account.json`, hideout, personal room | `Core/Account.cs`, `Core/Facility.cs`, `Core/Hideout.cs`, `Core/HideoutDefinition.cs`, `Core/HideoutRules.cs`, `Core/PersonalRoom.cs`, `Data/AccountRepository.cs`, `Data/AccountSerializer.cs`, `Data/HideoutLoader.cs`, `StreamingAssets/data/hideout.json` | Shared hideout with all Tarkov facilities (starting at level 0 except Generator), per-character private room level, facility dependencies/costs/effects in data, and population-dependent generator fuel cost. Stat-boosting facilities (Air Filtering Unit, Library, Shooting Range) are flagged `decorative` and do not alter character stats. Schema 1 account + schema 8 profile. |

`Safehouse.Core` is compiled with `noEngineReferences`, so it genuinely cannot call into
UnityEngine - the same separation the Python side gets for free by keeping rules out of `ui/`.

## The GEAR screen (`Scripts/UI`, `UI/`)

A first real screen, built with UI Toolkit (UXML + USS) instead of uGUI/Canvas, matching the visual
language worked out in the design artboard at
https://claude.ai/artifact/Y8ycmAv5F6yfN76Mr4LbDB - open that link and `UI/Theme.uss` side by side
if a colour or spacing value ever needs to change; keep them in sync by eye.

- `UI/GearScreen.uxml` + `UI/Theme.uss` - the screen's layout and styling. The STASH panel, CARRIED's
  RIG / BACKPACK grids and the seven LOADOUT slot cards are wired to the open character; only the
  body-part health and ability panels are hand-written.
- `Scripts/UI/GearScreenController.cs` - loads the shipped catalog (`CatalogLoader`) and the open character
  (`CharacterSession`; a new data folder starts with the sample one, `Scripts/UI/Sample/`), and renders one
  cell per item it holds. Clicking a cell (`Button.clicked`) updates the detail panel.
  Cells can be dragged to a new spot, in the same grid or into another (stash, rig, backpack): a green/red ghost shows where a drop would land, **R** turns
  the item (while dragging, or in place when just selected), **Esc** cancels a drag, and a refused
  drop or turn snaps back and flashes the cell red. The rule is `PlacementRules.Move` in Core; the
  controller only forwards pointer input to it (`TryMoveItem` / `TryRotateItem`, which tests call
  directly). While dragging inside the stash, holding the pointer near the top or bottom edge of the
  visible area scrolls the long stash automatically. Each change is saved to the open character before
  it shows (see "Saves").
  Dropping a stash / rig / backpack item on its LOADOUT slot wears it (`LoadoutRules.Equip`: the slot
  card turns green or red while hovering, the old piece goes back to the stash); dragging a slot card
  onto a grid takes it off there (`LoadoutRules.Unequip`). Equipping a rig or backpack resizes its
  CARRIED grid from the item's `capacity` (6 cells wide, height rounded up) and moves the old grid's
  contents back to the stash; a swap that would not fit is refused.
  This is the first place `Safehouse.Core`/`Safehouse.Data` actually drive a screen, not just a test.
- `Scripts/UI/Chrome/FacetedPanel.cs`, `GridBackground.cs` - USS has no `clip-path` and no repeating
  gradients, so the artboard's cut-corner panels and the stash's faint grid lines are drawn directly
  with `MeshGenerationContext.painter2D` instead of needing texture assets. `Theme.uss` deliberately
  avoids `box-shadow`/`outline` - support for those varies by Unity version, and an unsupported USS
  property just silently does nothing, which is a worse failure mode than not using it.
- Item icons: an item with an icon in `icon_cache/` shows it in its cell, its slot card and the detail
  panel (`Scripts/UI/IconLibrary.cs`); any other item - or everyone without the folder - keeps the
  placeholder monogram (3 letters from the item's category). The drag copy always stays a monogram so
  the moving preview stands out, as in the Python build. See "Item icons" below.
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

### Item icons (`icon_cache/`, not in git)

The icons are Escape from Tarkov game art (via tarkov.dev), private use only - like the Python
build's, they are **not** part of this repository or of any build, and `icon_cache/` is git-ignored.
Each person puts their own copy in `icon_cache/` next to the `Assets/` folder (next to the `.exe` in a
packaged game). Unity only imports what is under `Assets/`, so the folder is invisible to the Editor.

```
icon_cache/48/<item_id>.png     each exactly the item's footprint at 48 px per cell (a 4x1 rifle is 192x48)
```

That is the same layout the Python project's `tools/import_icons.py` writes, so either run that tool
(it downloads them) and copy its `icon_cache/48/` here, or copy an existing one. Icons are read from
disk the first time an item is drawn and cached; a missing folder or file is not an error. A turned item
draws the same upright PNG spun 90 degrees rather than needing a second file. Do not redistribute them.

#### Text-free art for the LOADOUT slots (`icon_cache/art/`)

Those inventory icons have the item's short name (`LV-119`, `AK-12`) baked into the picture at a size
that suits a grid cell. A slot card shows the same item at a different size each time, so the baked-in
text would come out a different size on every card. The slot cards therefore use a text-free render
instead, at the item's own proportions with no background box, fitted whole into the card:

```
py -3 -m pip install pillow           :: once
py -3 tools/import_art.py             :: downloads ~670 wearable items (weapons, ammo, gear, meds), ~35 MB
```

`tools/import_art.py` reads each item's tarkov.dev id from the Python project's snapshot
(`../safehouse_v0_1/data/source/tarkov_snapshot.json.gz`, or pass `--snapshot`), fetches the 512 px render
and stores it as a 256 px PNG in `icon_cache/art/<item_id>.png`. An item without art there falls back to
its stash icon (stretched to the card), and one without either keeps its placeholder text. Same rules as
above: game art, git-ignored, private use only.

### Fonts (`UI/Fonts/`)

Oswald (labels/headings) and JetBrains Mono (numbers, so digit columns line up regardless of value)
are the two fonts the artboard was designed around. Both are SIL Open Font License - free to bundle
in the built game, unlike the Tarkov icon/text assets in `icon_cache/`. Only the weights `Theme.uss`
actually uses are checked in (Oswald Regular/Medium/SemiBold/Bold, JetBrains Mono Regular/Medium/Bold),
each with its `OFL.txt` alongside it.

UI Toolkit has no `font-weight` property - a distinct weight is a distinct font *file*, picked with
`-unity-font-definition: url(...)` per USS class (see `Theme.uss`'s `.font-oswald-*`/`.font-mono*`
classes and the classes that use them directly, like `.text-heading`). `-unity-font-style: bold`
still appears in a few small spots (the placeholder item-icon monograms, inline ability numbers) -
that only fakes a bold by skewing the regular face, which is fine for tiny accents but not worth a
dedicated class for every single one.

The project's `data/*.json` items and character bios can contain Korean text (character names, bios),
which neither Oswald nor JetBrains Mono can render (they're Latin-only) - **Noto Serif KR** covers
that and is the same license, but isn't wired in yet since nothing on screen needs it today. Bring it
in the same way (copy the weights you need into `UI/Fonts/NotoSerifKR/`, add a `.font-*` class) once
a screen has to render Korean text.

## The TRADERS screen (`UI/TradersScreen.uxml`, `Scripts/UI/TradersScreenController.cs`)

A second UI document on the same panel as GEAR (`GearSceneBuilder` adds both; re-run **Safehouse > Build Gear
Scene** after pulling changes to the UXML or the scene). `ScreenNavigator` shows one and hides the other; the top
bar's GEAR / TRADERS tabs call it. Both screens show the same open character (the GEAR screen holds the
`CharacterSession`), so a purchase made here is on the GEAR screen's stash and money straight away.

Pick a trader (portrait, loyalty level, what the next level needs, the standing adjustment), search and filter the
offers, and BUY; select an item in the stash on the right to see what the trader pays, and SELL (the button asks
once more before selling). Offers the character cannot buy are greyed, and BUY says why. Every trade is built by
`TradingRules` and committed - saved - before the screen changes, so one that cannot be saved does not happen. A
downed character cannot trade, and one away on an expedition cannot either (the Python rules).

Standing is only raised by hand for now ("Standing (GM adjustment)"), because the quests that will raise it do not
exist yet - and the higher loyalty levels need standing, so without it only level 1 is ever open.

Trader portraits are game art like the item icons: `py -3 tools/import_art.py --traders` downloads them into
`icon_cache/traders/` (git-ignored, private use). Without them the trader buttons are text only.

## The HIDEOUT screen (`UI/HideoutScreen.uxml`, `Scripts/UI/HideoutScreenController.cs`)

A third UI document on the same panel as GEAR and TRADERS (`GearSceneBuilder` adds all three; re-run **Safehouse > Build Gear
Scene** after pulling changes to the UXML or the scene). `ScreenNavigator` shows one and hides the others; the top bar's
GEAR / TRADERS / HIDEOUT tabs call it. The HIDEOUT screen reads and writes the shared `Account` (loaded through the
GEAR screen's `CharacterSession`), so a facility upgraded here is upgraded for every character.

Each facility card shows its current level, description, upgrade cost and requirements, and the effects the current
level provides. The UPGRADE button is enabled only when every dependency and cost is satisfied; clicking it upgrades
the facility and saves the account immediately. Facilities whose real Tarkov purpose is to boost character stats
(Air Filtering Unit, Library, Shooting Range) are flagged `decorative` and explicitly do **not** alter character stats -
they can be built for flavour, but the Unity port keeps character stats in the profile only.

The data lives in `StreamingAssets/data/hideout.json` and is loaded by `Data/HideoutLoader.cs`. All Tarkov facilities
are present; a new account starts with the Generator at level 1 and everything else at level 0, matching the Python
build's hideout.

## Saves

The game keeps its **own** data folder, apart from the Python build's, so nothing here can touch a campaign
the Python build owns: `SAFEHOUSE_UNITY_DATA_DIR` if set, otherwise `Application.persistentDataPath/Safehouse`
(on Windows `%USERPROFILE%\AppData\LocalLow\<company>\Safehouse\Safehouse`).

```
account.json  (+ .json.bak)      shared hideout + character order, schema 1
saves/<id>.json  (+ .json.bak)   one file per character, schema 8
recovery/                        a damaged save that was replaced from its backup
.safehouse.lock                  held while the game runs, so two copies cannot write at once
```

**Safehouse > Import Python Characters** copies the Python build's characters (`%LOCALAPPDATA%\Safehouse\saves`,
or `SAFEHOUSE_DATA_DIR`) into it. The Python files are only read; each goes through the migrations (old saves are
schema 5 or earlier) and the same checks as any load, and one that will not pass is listed and left out. Running it
again skips characters already here, so progress made in this game is never overwritten. A brand-new folder starts
with a default account (shared hideout with only the Generator built + empty character order) and a "Sample Operator" (sample stash, rig, backpack and gear) so the screen has something to show.

On the GEAR screen the `<` `>` buttons beside the name switch between saved characters, and the last one opened is
reopened next time. Every move, turn, equip and take-off is saved as it happens; if the save fails the change does
not happen and the reason is shown. A downed character cannot be changed, and one away on an expedition can have
only their gear changed (the Python rules). If the data folder is already in use by another copy of the game, the
screen still opens but warns that nothing will be kept.

The stash is the character's own size (10 x 20 by default, larger after a hideout upgrade) and scrolls.

## Not done yet

- `Scripts/Editor` has no README-documented scope beyond `GearSceneBuilder` yet; add more tooling
  there as it's needed (data import from the Tarkov snapshot, build scripts).
- Combat, expeditions, gear and the injury system are still Python-only. They wait on the
  Gundog Revised combat/ability rules being settled.
- Settings and expeditions are not ported.
- No screen creates or deletes a character (the repository can).
- Hideout effects other than stash size / fuel cost are not wired to combat or expeditions yet; the UI upgrades and
  saves them, but the systems that will read them are still Python-only.
- Korean text (character bios, names) has no font yet - see "Fonts" above.
- Game art/icons are not bundled here, same reasoning as the Python project's `icon_cache/`
  (Escape from Tarkov assets via tarkov.dev: private use only).

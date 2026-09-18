using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Safehouse.Core;
using Safehouse.Data;
using Safehouse.UI.Sample;
using UnityEngine;
using UnityEngine.UIElements;

namespace Safehouse.UI
{
    /// <summary>
    /// Wires the GEAR screen's UXML to real game data. The STASH panel and the RIG / BACKPACK grids in
    /// CARRIED are data-driven: real catalog items placed by the real placement rules, and items can be
    /// dragged between any of the three grids. LOADOUT still shows one illustrative, hand-written
    /// loadout, and the contents of all three grids are sample data (<see cref="SampleStash"/>,
    /// <see cref="SampleLoadout"/>) until Profile/save loading is ported (see the Unity project README).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GearScreenController : MonoBehaviour
    {
        public const string StashGridName = "stash";
        public const string RigGridName = "rig";
        public const string BackpackGridName = "backpack";

        private const float Pitch = 48f;
        private const float DragThreshold = 4f;

        /// <summary>One on-screen grid: the element cells live in, and the rules-side grid behind it.</summary>
        private sealed class GridView
        {
            public string Name;
            public VisualElement Element;
            public StashGrid Grid;
        }

        private Dictionary<string, ItemDefinition> _catalog;
        private VisualElement _root;
        private readonly List<GridView> _views = new List<GridView>();
        private readonly Dictionary<string, Button> _cellsByInstanceId = new Dictionary<string, Button>();
        private string _selectedInstanceId;

        private Label _labelStashCells;
        private Label _labelStashValue;
        private Label _labelCarried;
        private VisualElement _swatchSelected;
        private Label _labelSelectedIcon;
        private Label _labelSelectedName;
        private Label _labelSelectedRarity;
        private Label _labelSelectedSub;
        private Label _labelSelectedValue;
        private Label _labelSelectedWeight;
        private Label _labelSelectedPerCell;

        private GridView StashView => _views[0];

        private void OnEnable()
        {
            _catalog = CatalogLoader.Load();

            _root = GetComponent<UIDocument>().rootVisualElement;

            // The panel scales the 1440x900 design to fit the window but keeps its proportions, so
            // a window of a different aspect ratio leaves a strip of empty space on one axis.
            // Centre the design in it rather than letting the whole strip pile up on one side.
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;

            _views.Clear();
            _views.Add(new GridView
            {
                Name = StashGridName, Element = _root.Q<VisualElement>("stash-grid"),
                Grid = SampleStash.Build(_catalog),
            });
            _views.Add(new GridView
            {
                Name = RigGridName, Element = _root.Q<VisualElement>("rig-grid"),
                Grid = SampleLoadout.BuildRig(_catalog),
            });
            _views.Add(new GridView
            {
                Name = BackpackGridName, Element = _root.Q<VisualElement>("backpack-grid"),
                Grid = SampleLoadout.BuildBackpack(_catalog),
            });

            _labelStashCells = _root.Q<Label>("label-stash-cells");
            _labelStashValue = _root.Q<Label>("label-stash-value");
            _labelCarried = _root.Q<Label>("label-carried");
            _swatchSelected = _root.Q<VisualElement>("swatch-selected");
            _labelSelectedIcon = _root.Q<Label>("label-selected-icon");
            _labelSelectedName = _root.Q<Label>("label-selected-name");
            _labelSelectedRarity = _root.Q<Label>("label-selected-rarity");
            _labelSelectedSub = _root.Q<Label>("label-selected-sub");
            _labelSelectedValue = _root.Q<Label>("label-selected-value");
            _labelSelectedWeight = _root.Q<Label>("label-selected-weight");
            _labelSelectedPerCell = _root.Q<Label>("label-selected-percell");

            BuildCells();
            UpdateHeaders();

            var highestValueId = StashView.Grid.Stash
                .OrderByDescending(instance => _catalog[instance.ItemId].BaseValue)
                .Select(instance => instance.InstanceId)
                .FirstOrDefault();
            if (highestValueId != null)
            {
                SelectItem(highestValueId);
            }
        }

        private void BuildCells()
        {
            _cellsByInstanceId.Clear();

            foreach (var view in _views)
            {
                view.Element.Clear();
                foreach (var instance in view.Grid.Stash)
                {
                    var cell = CreateCell(instance);
                    view.Element.Add(cell);
                    _cellsByInstanceId[instance.InstanceId] = cell;
                }
            }
        }

        private Button CreateCell(ItemInstance instance)
        {
            var item = _catalog[instance.ItemId];
            PlacementRules.Footprint(item, instance.Rotation, out var width, out var height);

            var cell = new Button { name = "cell-" + instance.InstanceId };
            cell.AddToClassList("cell");
            PositionCell(cell, instance.X, instance.Y, width, height);
            cell.style.borderTopColor = cell.style.borderBottomColor =
                cell.style.borderLeftColor = cell.style.borderRightColor = RarityPalette.For(item.Rarity);

            cell.Add(MonogramLabel(item));

            if (width >= 2 || height >= 2)
            {
                var label = new Label(item.Name.ToUpperInvariant());
                label.AddToClassList("cell-label");
                cell.Add(label);
            }

            var instanceId = instance.InstanceId;
            cell.clicked += () => SelectItem(instanceId);
            RegisterDrag(cell, instanceId);
            return cell;
        }

        private static Label MonogramLabel(ItemDefinition item) =>
            new Label(Monogram(item.Category))
            {
                style =
                {
                    alignSelf = Align.Center,
                    color = RarityPalette.For(item.Rarity),
                    unityFontStyleAndWeight = FontStyle.Bold,
                },
            };

        private static void PositionCell(VisualElement cell, int x, int y, int width, int height)
        {
            cell.style.left = x * Pitch + 2;
            cell.style.top = y * Pitch + 2;
            cell.style.width = width * Pitch - 2;
            cell.style.height = height * Pitch - 2;
        }

        private GridView ViewOf(string instanceId) =>
            _views.First(view => view.Grid.Stash.Any(candidate => candidate.InstanceId == instanceId));

        private ItemInstance Find(string instanceId) =>
            ViewOf(instanceId).Grid.Stash.First(candidate => candidate.InstanceId == instanceId);

        private void UpdateHeaders()
        {
            PlacementRules.StashTotals(StashView.Grid, _catalog, out var cells, out _, out var value);
            _labelStashCells.text = $"{cells} / {StashView.Grid.StashWidth * StashView.Grid.StashHeight} CELLS";
            _labelStashValue.text = Spaced(value);

            var carried = 0;
            var capacity = 0;
            foreach (var view in _views.Skip(1))
            {
                PlacementRules.StashTotals(view.Grid, _catalog, out var used, out _, out _);
                carried += used;
                capacity += view.Grid.StashWidth * view.Grid.StashHeight;
            }

            _labelCarried.text = $"{carried} / {capacity}";
        }

        /// <summary>
        /// Moves (and optionally turns) an item within the grid it is already in. Returns why the move
        /// was refused, or null on success - the same contract as <see cref="PlacementRules.PlacementError"/>.
        /// Public so tests can drive a move without simulating a pointer drag.
        /// </summary>
        public string TryMoveItem(string instanceId, int x, int y, int rotation) =>
            TryTransfer(instanceId, ViewOf(instanceId), x, y, rotation);

        /// <summary>Moves an item into the named grid ("stash", "rig" or "backpack") at x,y. Same
        /// contract as <see cref="TryMoveItem"/>; nothing changes when it is refused.</summary>
        public string TryTransferItem(string instanceId, string gridName, int x, int y, int rotation)
        {
            var target = _views.FirstOrDefault(view => view.Name == gridName);
            return target == null
                ? $"Unknown grid: {gridName}."
                : TryTransfer(instanceId, target, x, y, rotation);
        }

        /// <summary>Which grid ("stash", "rig" or "backpack") currently holds this item.</summary>
        public string GridOf(string instanceId) => ViewOf(instanceId).Name;

        private string TryTransfer(string instanceId, GridView target, int x, int y, int rotation)
        {
            var source = ViewOf(instanceId);
            StashGrid newSource;
            StashGrid newTarget;
            try
            {
                (newSource, newTarget) = PlacementRules.Transfer(
                    source.Grid, target.Grid, _catalog, instanceId, x, y, rotation);
            }
            catch (ValidationException error)
            {
                return error.Message;
            }

            source.Grid = newSource;
            target.Grid = newTarget;

            var moved = Find(instanceId);
            PlacementRules.Footprint(_catalog[moved.ItemId], moved.Rotation, out var width, out var height);
            var cell = _cellsByInstanceId[instanceId];
            if (source != target)
            {
                target.Element.Add(cell); // the new list order puts it last, which is also drawn on top
            }

            PositionCell(cell, moved.X, moved.Y, width, height);
            UpdateHeaders();
            if (_selectedInstanceId == instanceId)
            {
                SelectItem(instanceId); // the detail line shows the footprint, which a turn changes
            }

            return null;
        }

        /// <summary>Turns an item 90 degrees where it sits. Refused (returns the reason) if the turned
        /// shape would not fit there; a square item has nothing to turn and is left alone.</summary>
        public string TryRotateItem(string instanceId)
        {
            var instance = Find(instanceId);
            var item = _catalog[instance.ItemId];
            if (item.Width == item.Height)
            {
                return null;
            }

            return TryMoveItem(instanceId, instance.X, instance.Y, instance.Rotation == 0 ? 90 : 0);
        }

        // ---- dragging ----

        private sealed class Drag
        {
            public string InstanceId;
            public int PointerId;
            public bool Active;
            public int Rotation;
            public Vector2 StartPointer;   // panel coordinates
            public Vector2 Pointer;        // panel coordinates, latest
            public Vector2 Grab;           // pointer minus the cell's top-left, panel coordinates
            public float MarginLeft;       // the cell's margin, so the drop lands where the ghost shows
            public float MarginTop;
            public GridView TargetView;    // null while the pointer is over no grid
            public int TargetX;
            public int TargetY;
        }

        private Drag _drag;
        private VisualElement _ghost;
        private VisualElement _proxy;

        private void RegisterDrag(Button cell, string instanceId)
        {
            // TrickleDown: a Button's built-in Clickable stops pointer events from reaching later
            // handlers on the same element, so a normal (bubble-phase) callback would never run.
            cell.RegisterCallback<PointerDownEvent>(evt => OnPointerDown(cell, instanceId, evt), TrickleDown.TrickleDown);
            cell.RegisterCallback<PointerMoveEvent>(evt => OnPointerMove(cell, evt), TrickleDown.TrickleDown);
            cell.RegisterCallback<PointerUpEvent>(evt => OnPointerUp(cell, evt), TrickleDown.TrickleDown);
            cell.RegisterCallback<PointerCaptureOutEvent>(_ => CancelDrag(cell));
            cell.RegisterCallback<KeyDownEvent>(evt => OnKeyDown(cell, instanceId, evt), TrickleDown.TrickleDown);
        }

        private void OnPointerDown(VisualElement cell, string instanceId, PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            SelectItem(instanceId);
            cell.Focus(); // so R / Esc reach this cell

            _drag = new Drag
            {
                InstanceId = instanceId,
                PointerId = evt.pointerId,
                Rotation = Find(instanceId).Rotation,
                StartPointer = evt.position,
                Pointer = evt.position,
                Grab = (Vector2)evt.position - cell.worldBound.min,
                MarginLeft = cell.resolvedStyle.marginLeft,
                MarginTop = cell.resolvedStyle.marginTop,
            };
            cell.CapturePointer(evt.pointerId);
        }

        private void OnPointerMove(VisualElement cell, PointerMoveEvent evt)
        {
            if (_drag == null || _drag.PointerId != evt.pointerId)
            {
                return;
            }

            _drag.Pointer = evt.position;
            if (!_drag.Active)
            {
                if ((_drag.Pointer - _drag.StartPointer).magnitude < DragThreshold)
                {
                    return; // still a click, not a drag
                }

                BeginDrag(cell);
            }

            RefreshDrag();
        }

        private void BeginDrag(VisualElement cell)
        {
            _drag.Active = true;

            // The cell itself stays put (dimmed) and a copy follows the pointer. The copy lives at the
            // top of the screen's tree, so it draws over every panel - a cell dragged inside its own
            // grid would be hidden behind the next panel the moment it crossed the border.
            var item = _catalog[Find(_drag.InstanceId).ItemId];
            _proxy = new VisualElement { pickingMode = PickingMode.Ignore };
            _proxy.AddToClassList("cell");
            _proxy.AddToClassList("cell-proxy");
            _proxy.style.borderTopColor = _proxy.style.borderBottomColor =
                _proxy.style.borderLeftColor = _proxy.style.borderRightColor = RarityPalette.For(item.Rarity);
            _proxy.Add(MonogramLabel(item));
            _root.Add(_proxy);

            _ghost = new VisualElement { pickingMode = PickingMode.Ignore };
            _ghost.AddToClassList("cell-ghost");

            cell.AddToClassList("cell-dragging");
        }

        /// <summary>Moves the copy with the pointer, and shows where a drop would land in green or red.</summary>
        private void RefreshDrag()
        {
            var drag = _drag;
            var instance = Find(drag.InstanceId);
            PlacementRules.Footprint(_catalog[instance.ItemId], drag.Rotation, out var width, out var height);

            var topLeft = drag.Pointer - drag.Grab;
            var local = _root.WorldToLocal(topLeft);
            _proxy.style.left = local.x;
            _proxy.style.top = local.y;
            _proxy.style.width = width * Pitch - 2;
            _proxy.style.height = height * Pitch - 2;

            drag.TargetView = _views.FirstOrDefault(view => view.Element.worldBound.Contains(drag.Pointer));
            if (drag.TargetView == null)
            {
                _ghost.RemoveFromHierarchy();
                return;
            }

            var inGrid = drag.TargetView.Element.WorldToLocal(topLeft);
            drag.TargetX = Mathf.RoundToInt((inGrid.x - drag.MarginLeft - 2) / Pitch);
            drag.TargetY = Mathf.RoundToInt((inGrid.y - drag.MarginTop - 2) / Pitch);

            if (_ghost.parent != drag.TargetView.Element)
            {
                drag.TargetView.Element.Add(_ghost);
            }

            PositionCell(_ghost, drag.TargetX, drag.TargetY, width, height);
            var fits = PlacementRules.CanPlace(drag.TargetView.Grid, _catalog, instance.ItemId,
                drag.TargetX, drag.TargetY, drag.Rotation, instance.InstanceId);
            _ghost.EnableInClassList("cell-ghost-bad", !fits);
        }

        private void OnPointerUp(VisualElement cell, PointerUpEvent evt)
        {
            var drag = _drag;
            if (drag == null || drag.PointerId != evt.pointerId)
            {
                return;
            }

            // Clear first: releasing the capture raises PointerCaptureOut, which must not see a live drag.
            _drag = null;
            if (cell.HasPointerCapture(evt.pointerId))
            {
                cell.ReleasePointer(evt.pointerId);
            }

            if (!drag.Active)
            {
                return;
            }

            EndDrag(cell);
            if (drag.TargetView == null)
            {
                return; // dropped on empty screen: the item simply stays where it was
            }

            if (TryTransfer(drag.InstanceId, drag.TargetView, drag.TargetX, drag.TargetY, drag.Rotation) != null)
            {
                Reject(cell);
            }
        }

        private void CancelDrag(VisualElement cell)
        {
            var drag = _drag;
            _drag = null;
            if (drag != null && drag.Active)
            {
                EndDrag(cell);
            }
        }

        /// <summary>Removes the drag visuals; the cell was never moved, so there is nothing to snap back.</summary>
        private void EndDrag(VisualElement cell)
        {
            _proxy?.RemoveFromHierarchy();
            _proxy = null;
            _ghost?.RemoveFromHierarchy();
            _ghost = null;
            cell.RemoveFromClassList("cell-dragging");
        }

        private static void Reject(VisualElement cell)
        {
            cell.AddToClassList("cell-rejected");
            cell.schedule.Execute(() => cell.RemoveFromClassList("cell-rejected")).StartingIn(350);
        }

        private void OnKeyDown(VisualElement cell, string instanceId, KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.R)
            {
                if (_drag != null && _drag.Active)
                {
                    var item = _catalog[Find(instanceId).ItemId];
                    if (item.Width != item.Height)
                    {
                        _drag.Rotation = _drag.Rotation == 0 ? 90 : 0;
                        // Centre the copy on the pointer: the old grab point may now be off the shape.
                        PlacementRules.Footprint(item, _drag.Rotation, out var width, out var height);
                        _drag.Grab = new Vector2(width * Pitch - 2, height * Pitch - 2) / 2f;
                        RefreshDrag();
                    }
                }
                else if (TryRotateItem(instanceId) != null)
                {
                    Reject(cell);
                }

                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape && _drag != null && _drag.Active)
            {
                var pointerId = _drag.PointerId;
                CancelDrag(cell);
                if (cell.HasPointerCapture(pointerId))
                {
                    cell.ReleasePointer(pointerId);
                }

                evt.StopPropagation();
            }
        }

        /// <summary>Selects an item by instance id, in whichever grid it is. Public so tests can drive
        /// it directly instead of simulating a pointer click on a specific screen position.</summary>
        public void SelectItem(string instanceId)
        {
            if (_selectedInstanceId != null && _cellsByInstanceId.TryGetValue(_selectedInstanceId, out var previous))
            {
                previous.RemoveFromClassList("cell-selected");
            }

            _selectedInstanceId = instanceId;
            if (_cellsByInstanceId.TryGetValue(instanceId, out var current))
            {
                current.AddToClassList("cell-selected");
            }

            var instance = Find(instanceId);
            var item = _catalog[instance.ItemId];
            PlacementRules.Footprint(item, instance.Rotation, out var width, out var height);
            var color = RarityPalette.For(item.Rarity);

            _swatchSelected.style.borderTopColor = _swatchSelected.style.borderBottomColor =
                _swatchSelected.style.borderLeftColor = _swatchSelected.style.borderRightColor = color;
            _labelSelectedIcon.text = Monogram(item.Category);
            _labelSelectedIcon.style.color = color;
            _labelSelectedName.text = item.Name;
            _labelSelectedRarity.text = item.Rarity.ToUpperInvariant();
            _labelSelectedRarity.style.color = color;
            _labelSelectedSub.text = $"{item.Category} · {width} × {height} cells";
            _labelSelectedValue.text = Spaced(item.BaseValue);
            _labelSelectedWeight.text = $"{item.Weight:0.00} kg";
            _labelSelectedPerCell.text = Spaced(item.BaseValue / (width * height));
        }

        private static string Monogram(string category)
        {
            var letters = category.Where(char.IsLetter).Take(3).ToArray();
            return new string(letters).ToUpperInvariant();
        }

        private static string Spaced(double value) =>
            System.Math.Round(value).ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");
    }
}

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
    /// Wires the GEAR screen's UXML to real game data. Only the STASH panel is data-driven right
    /// now - it is the one part of this screen that exercises Safehouse.Core (placement rules) and
    /// Safehouse.Data (the catalog loader) end to end. LOADOUT and CARRIED show one illustrative,
    /// hand-written loadout, the same simplification the artboard mock-up made; wiring them to a
    /// real save happens once Profile/save loading is ported (see the Unity project README).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GearScreenController : MonoBehaviour
    {
        private const float Pitch = 48f;

        private Dictionary<string, ItemDefinition> _catalog;
        private StashGrid _stash;
        private VisualElement _stashGrid;
        private readonly Dictionary<string, Button> _cellsByInstanceId = new Dictionary<string, Button>();
        private string _selectedInstanceId;

        private Label _labelStashCells;
        private Label _labelStashValue;
        private VisualElement _swatchSelected;
        private Label _labelSelectedIcon;
        private Label _labelSelectedName;
        private Label _labelSelectedRarity;
        private Label _labelSelectedSub;
        private Label _labelSelectedValue;
        private Label _labelSelectedWeight;
        private Label _labelSelectedPerCell;

        private void OnEnable()
        {
            _catalog = CatalogLoader.Load();
            _stash = SampleStash.Build(_catalog);

            var root = GetComponent<UIDocument>().rootVisualElement;
            _stashGrid = root.Q<VisualElement>("stash-grid");
            _labelStashCells = root.Q<Label>("label-stash-cells");
            _labelStashValue = root.Q<Label>("label-stash-value");
            _swatchSelected = root.Q<VisualElement>("swatch-selected");
            _labelSelectedIcon = root.Q<Label>("label-selected-icon");
            _labelSelectedName = root.Q<Label>("label-selected-name");
            _labelSelectedRarity = root.Q<Label>("label-selected-rarity");
            _labelSelectedSub = root.Q<Label>("label-selected-sub");
            _labelSelectedValue = root.Q<Label>("label-selected-value");
            _labelSelectedWeight = root.Q<Label>("label-selected-weight");
            _labelSelectedPerCell = root.Q<Label>("label-selected-percell");

            BuildStashCells();
            UpdateStashHeader();

            var highestValueId = _stash.Stash
                .OrderByDescending(instance => _catalog[instance.ItemId].BaseValue)
                .Select(instance => instance.InstanceId)
                .FirstOrDefault();
            if (highestValueId != null)
            {
                SelectItem(highestValueId);
            }
        }

        private void BuildStashCells()
        {
            _stashGrid.Clear();
            _cellsByInstanceId.Clear();

            foreach (var instance in _stash.Stash)
            {
                var item = _catalog[instance.ItemId];
                PlacementRules.Footprint(item, instance.Rotation, out var width, out var height);

                var cell = new Button { name = "cell-" + instance.InstanceId };
                cell.AddToClassList("cell");
                cell.style.left = instance.X * Pitch + 2;
                cell.style.top = instance.Y * Pitch + 2;
                cell.style.width = width * Pitch - 2;
                cell.style.height = height * Pitch - 2;
                cell.style.borderTopColor = cell.style.borderBottomColor =
                    cell.style.borderLeftColor = cell.style.borderRightColor = RarityPalette.For(item.Rarity);

                var monogram = Monogram(item.Category);
                cell.Add(new Label(monogram)
                {
                    style =
                    {
                        alignSelf = Align.Center,
                        color = RarityPalette.For(item.Rarity),
                        unityFontStyleAndWeight = FontStyle.Bold,
                    },
                });

                if (width >= 2 || height >= 2)
                {
                    var label = new Label(item.Name.ToUpperInvariant());
                    label.AddToClassList("cell-label");
                    cell.Add(label);
                }

                var instanceId = instance.InstanceId;
                cell.clicked += () => SelectItem(instanceId);

                _stashGrid.Add(cell);
                _cellsByInstanceId[instance.InstanceId] = cell;
            }
        }

        private void UpdateStashHeader()
        {
            PlacementRules.StashTotals(_stash, _catalog, out var cells, out _, out var value);
            _labelStashCells.text = $"{cells} / {_stash.StashWidth * _stash.StashHeight} CELLS";
            _labelStashValue.text = Spaced(value);
        }

        /// <summary>Selects a stash item by instance id. Public so tests can drive it directly
        /// instead of simulating a pointer click on a specific screen position.</summary>
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

            var instance = _stash.Stash.First(candidate => candidate.InstanceId == instanceId);
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

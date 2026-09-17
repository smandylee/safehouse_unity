using System.Collections.Generic;
using Safehouse.Core;

namespace Safehouse.UI.Sample
{
    /// <summary>
    /// A stash filled with real items from the shipped catalog, placed by the real placement rules.
    /// This exists only for the GEAR screen mock-up - a save file builds a stash from a player's
    /// actual inventory, never from this list.
    /// </summary>
    public static class SampleStash
    {
        // Item ids as tools/import_tarkov.py derives them from each item's real name.
        private static readonly string[] ItemIds =
        {
            "kalashnikov-ak-12-545x39-assault-rifle",
            "graphics-card",
            "car-first-aid-kit",
            "bundle-of-wires",
            "military-power-filter",
            "grizzly-medical-kit",
            "toolset",
            "army-bandage",
            "esmarch-tourniquet",
            "paracord",
            "salewa-first-aid-kit",
            "physical-bitcoin",
            "analgin-painkillers",
            "duct-tape",
            "power-supply-unit",
            "golden-neck-chain",
        };

        public static StashGrid Build(IReadOnlyDictionary<string, ItemDefinition> catalog,
            int width = 10, int height = 12)
        {
            var grid = new StashGrid(width, height);
            var placed = new List<ItemInstance>();

            foreach (var itemId in ItemIds)
            {
                if (!catalog.ContainsKey(itemId))
                {
                    // The bundled catalog is regenerated from tarkov.dev; skip rather than fail if
                    // one of these names ever drops out of a refresh.
                    continue;
                }

                var current = new StashGrid(width, height, placed);
                var spot = PlacementRules.FirstFit(current, catalog, itemId);
                if (spot == null)
                {
                    continue;
                }

                placed.Add(ItemInstance.Create(NewInstanceId(placed.Count), itemId,
                    spot.Value.X, spot.Value.Y, spot.Value.Rotation));
            }

            return new StashGrid(width, height, placed);
        }

        private static string NewInstanceId(int index) => (index + 1).ToString("x32");
    }
}

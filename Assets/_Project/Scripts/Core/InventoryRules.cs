using System;
using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// High-level inventory operations used by crafting and trading: adding or removing items by type, rather than
    /// by exact grid cell. Built on top of the placement rules so items are always placed legally.
    /// </summary>
    public static class InventoryRules
    {
        /// <summary>
        /// Removes <paramref name="count"/> items with <paramref name="itemId"/> from the grid. Returns the new grid.
        /// Throws if there are not enough matching items.
        /// </summary>
        public static StashGrid RemoveItems(StashGrid stash, IReadOnlyDictionary<string, ItemDefinition> catalog,
            string itemId, int count)
        {
            var available = stash.Stash.Count(i => i.ItemId == itemId);
            if (available < count)
            {
                throw new ValidationException($"Need {count} {catalog[itemId].Name}, have {available}.");
            }

            var removed = 0;
            var kept = new List<ItemInstance>();
            foreach (var item in stash.Stash)
            {
                if (removed < count && item.ItemId == itemId)
                {
                    removed++;
                    continue;
                }

                kept.Add(item);
            }

            return stash.With(kept);
        }

        /// <summary>
        /// Adds <paramref name="count"/> new instances of <paramref name="itemId"/> to the grid, placing each one at
        /// the first free spot found. Throws if the grid cannot hold them all.
        /// </summary>
        public static StashGrid AddItems(StashGrid stash, IReadOnlyDictionary<string, ItemDefinition> catalog,
            string itemId, int count, Func<string> newInstanceId = null)
        {
            if (!catalog.TryGetValue(itemId, out var item))
            {
                throw new ValidationException($"Unknown item type: {itemId}.");
            }

            var current = stash;
            for (var i = 0; i < count; i++)
            {
                current = AddItem(current, catalog, item, newInstanceId ?? DefaultNewInstanceId);
            }

            return current;
        }

        private static StashGrid AddItem(StashGrid stash, IReadOnlyDictionary<string, ItemDefinition> catalog,
            ItemDefinition item, Func<string> newInstanceId)
        {
            for (var y = 0; y < stash.StashHeight; y++)
            {
                for (var x = 0; x < stash.StashWidth; x++)
                {
                    if (PlacementRules.CanPlace(stash, catalog, item.ItemId, x, y))
                    {
                        var instance = ItemInstance.Create(newInstanceId(), item.ItemId, x, y);
                        var list = new List<ItemInstance>(stash.Stash) { instance };
                        return stash.With(list);
                    }
                }
            }

            throw new ValidationException($"No space in the stash for {item.Name}.");
        }

        private static string DefaultNewInstanceId() => Guid.NewGuid().ToString("N");
    }
}

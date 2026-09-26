using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>What an equip produced. Each field is a fresh copy; the inputs are untouched.</summary>
    public readonly struct EquipResult
    {
        public EquipResult(Loadout loadout, StashGrid source, StashGrid stash, StashGrid rig, StashGrid backpack)
        {
            Loadout = loadout;
            Source = source;
            Stash = stash;
            Rig = rig;
            Backpack = backpack;
        }

        public Loadout Loadout { get; }

        /// <summary>The grid the item came from. The same object as <see cref="Stash"/> when it came from the stash.</summary>
        public StashGrid Source { get; }

        /// <summary>The stash, which is where whatever was worn before goes back to.</summary>
        public StashGrid Stash { get; }

        /// <summary>The rig grid, resized when a rig was equipped.</summary>
        public StashGrid Rig { get; }

        /// <summary>The backpack grid, resized when a backpack was equipped.</summary>
        public StashGrid Backpack { get; }
    }

    /// <summary>
    /// Wearing and taking off gear. The C# side of Python session.equip/unequip. Like the placement
    /// rules these are pure: a refused change throws and nothing - grid, loadout - has been altered.
    /// </summary>
    public static class LoadoutRules
    {
        /// <summary>
        /// Wears an item taken from <paramref name="source"/> (the stash, or a rig or backpack grid).
        /// Whatever the slot held goes back to <paramref name="stash"/> by first fit, keeping its
        /// instance id. Changing to a weapon of another caliber also takes off the loaded ammunition.
        /// Equipping a rig or backpack resizes that grid to the item's capacity and empties it; the old
        /// grid's contents go back to the stash with the old piece.
        /// </summary>
        public static EquipResult Equip(GearData gear, IReadOnlyDictionary<string, ItemDefinition> catalog,
            Loadout loadout, StashGrid source, StashGrid stash, StashGrid rig, StashGrid backpack, string instanceId)
        {
            var index = PlacementRules.IndexOf(source, instanceId);
            var item = source.Stash[index];
            var name = NameOf(catalog, item.ItemId);

            var slot = gear.SlotOf(item.ItemId);
            if (slot == null)
            {
                throw new ValidationException($"{name} cannot be equipped.");
            }

            var weapon = loadout.Get(LoadoutSlots.Primary);
            if (slot == LoadoutSlots.Ammo && (weapon == null || !gear.Fits(weapon.ItemId, item.ItemId)))
            {
                throw new ValidationException($"{name} does not fit the weapon this character carries.");
            }

            var takenOff = loadout.Items.Where(worn => worn.Slot == slot).ToList();
            var kept = loadout.Items.Where(worn => worn.Slot != slot).ToList();
            if (slot == LoadoutSlots.Primary)
            {
                // Ammunition for the old weapon does not fit the new one.
                var loaded = loadout.Get(LoadoutSlots.Ammo);
                if (loaded != null && !gear.Fits(item.ItemId, loaded.ItemId))
                {
                    takenOff.Add(loaded);
                    kept.Remove(loaded);
                }
            }

            kept.Add(EquippedItem.Create(slot, item.InstanceId, item.ItemId));

            var remaining = new List<ItemInstance>(source.Stash);
            remaining.RemoveAt(index);
            var sourceAfter = source.With(remaining);
            var sameGrid = ReferenceEquals(source, stash);
            var stashAfter = sameGrid ? sourceAfter : stash;

            foreach (var worn in takenOff)
            {
                stashAfter = ReturnToGrid(stashAfter, catalog, worn);
            }

            var rigAfter = rig;
            var backpackAfter = backpack;
            if (slot == LoadoutSlots.Rig)
            {
                foreach (var carried in rig.Stash)
                {
                    stashAfter = ReturnToGrid(stashAfter, catalog, AsEquipped(carried, LoadoutSlots.Rig));
                }

                rigAfter = EmptyGridFor(gear, item.ItemId);
            }
            else if (ReferenceEquals(source, rig))
            {
                rigAfter = sourceAfter;
            }

            if (slot == LoadoutSlots.Backpack)
            {
                foreach (var carried in backpack.Stash)
                {
                    stashAfter = ReturnToGrid(stashAfter, catalog, AsEquipped(carried, LoadoutSlots.Backpack));
                }

                backpackAfter = EmptyGridFor(gear, item.ItemId);
            }
            else if (ReferenceEquals(source, backpack))
            {
                backpackAfter = sourceAfter;
            }

            return new EquipResult(new Loadout(kept), sameGrid ? stashAfter : sourceAfter, stashAfter, rigAfter, backpackAfter);
        }

        /// <summary>The reason <see cref="Equip"/> would refuse, or null when it would succeed.</summary>
        public static string EquipError(GearData gear, IReadOnlyDictionary<string, ItemDefinition> catalog,
            Loadout loadout, StashGrid source, StashGrid stash, StashGrid rig, StashGrid backpack, string instanceId)
        {
            try
            {
                Equip(gear, catalog, loadout, source, stash, rig, backpack, instanceId);
                return null;
            }
            catch (ValidationException error)
            {
                return error.Message;
            }
        }

        /// <summary>
        /// Takes the item in <paramref name="slot"/> off and puts it into <paramref name="target"/> at
        /// x,y. Taking off a weapon also unloads its ammunition, which goes into the same grid by first
        /// fit. Refused, changing nothing, when either does not fit. Taking off a rig or backpack resets
        /// that grid to the default empty size.
        /// </summary>
        public static (Loadout Loadout, StashGrid Target, StashGrid Rig, StashGrid Backpack) Unequip(
            IReadOnlyDictionary<string, ItemDefinition> catalog,
            Loadout loadout, string slot, StashGrid target, StashGrid rig, StashGrid backpack, int x, int y,
            int rotation = 0)
        {
            var worn = loadout.Get(slot)
                ?? throw new ValidationException($"Nothing is equipped in the {slot} slot.");

            var error = PlacementRules.PlacementError(target, catalog, worn.ItemId, x, y, rotation);
            if (error != null)
            {
                throw new ValidationException(error);
            }

            var placed = new List<ItemInstance>(target.Stash)
            {
                ItemInstance.Create(worn.InstanceId, worn.ItemId, x, y, rotation),
            };
            var targetAfter = target.With(placed);

            var removed = new List<string> { slot };
            var loaded = loadout.Get(LoadoutSlots.Ammo);
            if (slot == LoadoutSlots.Primary && loaded != null)
            {
                targetAfter = ReturnToGrid(targetAfter, catalog, loaded);
                removed.Add(LoadoutSlots.Ammo);
            }

            var rigAfter = slot == LoadoutSlots.Rig
                ? new StashGrid(Profile.DefaultRigWidth, Profile.DefaultRigHeight)
                : ReferenceEquals(target, rig) ? targetAfter : rig;
            var backpackAfter = slot == LoadoutSlots.Backpack
                ? new StashGrid(Profile.DefaultBackpackWidth, Profile.DefaultBackpackHeight)
                : ReferenceEquals(target, backpack) ? targetAfter : backpack;

            return (new Loadout(loadout.Items.Where(item => !removed.Contains(item.Slot))), targetAfter, rigAfter,
                backpackAfter);
        }

        private static StashGrid EmptyGridFor(GearData gear, string itemId)
        {
            var stats = gear.Equipment.TryGetValue(itemId, out var found) ? found : null;
            if (stats == null)
            {
                throw new ValidationException($"No equipment data for {itemId}.");
            }

            var (width, height) = stats.GridDimensions();
            return new StashGrid(width, height);
        }

        private static EquippedItem AsEquipped(ItemInstance item, string slot) =>
            EquippedItem.Create(slot, item.InstanceId, item.ItemId);

        private static StashGrid ReturnToGrid(StashGrid grid, IReadOnlyDictionary<string, ItemDefinition> catalog,
            EquippedItem worn)
        {
            var spot = PlacementRules.FirstFit(grid, catalog, worn.ItemId);
            if (spot == null)
            {
                throw new ValidationException(
                    $"No space in the stash for {NameOf(catalog, worn.ItemId)}. Nothing was changed.");
            }

            var next = new List<ItemInstance>(grid.Stash)
            {
                ItemInstance.Create(worn.InstanceId, worn.ItemId, spot.Value.X, spot.Value.Y, spot.Value.Rotation),
            };
            return grid.With(next);
        }

        private static string NameOf(IReadOnlyDictionary<string, ItemDefinition> catalog, string itemId) =>
            catalog.TryGetValue(itemId, out var item) ? item.Name : itemId;
    }
}

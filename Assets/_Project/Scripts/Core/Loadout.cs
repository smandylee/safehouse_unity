using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// An item worn or wielded in one loadout slot. Worn items are not placed in any grid; they keep
    /// the instance id they had, so wearing and un-wearing never mints a second copy.
    /// </summary>
    public sealed class EquippedItem
    {
        private EquippedItem(string slot, string instanceId, string itemId)
        {
            Slot = slot;
            InstanceId = instanceId;
            ItemId = itemId;
        }

        public string Slot { get; }
        public string InstanceId { get; }
        public string ItemId { get; }

        public static EquippedItem Create(string slot, string instanceId, string itemId)
        {
            if (!LoadoutSlots.IsSlot(slot))
            {
                throw new ValidationException($"Unknown loadout slot: '{slot}'.");
            }

            return new EquippedItem(slot,
                Validate.Identifier(instanceId, "instance_id", instance: true),
                Validate.Identifier(itemId, "item_id"));
        }

        public override string ToString() => $"{Slot}: {ItemId}";
    }

    /// <summary>What a character has on: at most one item per slot. Immutable, like the grids.</summary>
    public sealed class Loadout
    {
        public Loadout(IEnumerable<EquippedItem> items = null)
        {
            Items = (items ?? Enumerable.Empty<EquippedItem>()).ToList();
            if (Items.Select(item => item.Slot).Distinct().Count() != Items.Count)
            {
                throw new ValidationException("Loadout uses a slot more than once.");
            }
        }

        public static Loadout Empty { get; } = new Loadout();

        public IReadOnlyList<EquippedItem> Items { get; }

        /// <summary>What is in this slot, or null.</summary>
        public EquippedItem Get(string slot) => Items.FirstOrDefault(item => item.Slot == slot);

        public EquippedItem FindByInstance(string instanceId) =>
            Items.FirstOrDefault(item => item.InstanceId == instanceId);
    }
}

using System.Collections.Generic;
using Safehouse.Core;

namespace Safehouse.UI.Sample
{
    /// <summary>
    /// The rig and backpack shown in the GEAR screen's CARRIED panel, filled with real catalog items
    /// like <see cref="SampleStash"/>. Stand-in data until a character's real loadout can be loaded
    /// from a save; instance ids sit in their own ranges so they never collide with the stash's.
    /// </summary>
    public static class SampleLoadout
    {
        public const int RigWidth = 6;
        public const int RigHeight = 4;
        public const int BackpackWidth = 6;
        public const int BackpackHeight = 8;

        private static readonly string[] RigItemIds =
        {
            "ak-12-545x39-30-round-magazine",
            "ak-12-545x39-30-round-magazine",
            "ak-74-545x39-6l31-60-round-magazine",
            "cat-hemostatic-tourniquet",
            "cat-hemostatic-tourniquet",
        };

        private static readonly string[] BackpackItemIds =
        {
            "item-case",
            "magazine-case",
            "documents-case",
            "dogtag-case",
        };

        // What the character wears: the same gear the artboard's LOADOUT panel showed, as real items.
        private static readonly (string Slot, string ItemId)[] Worn =
        {
            (LoadoutSlots.Primary, "kalashnikov-ak-12-545x39-assault-rifle"),
            (LoadoutSlots.Ammo, "545x39mm-bp-gs"),
            (LoadoutSlots.Helmet, "rys-t-bulletproof-helmet-black"),
            (LoadoutSlots.Armor, "6b45-body-armor-emr"),
            (LoadoutSlots.Rig, "spiritus-systems-lv-119-plate-carrier-black-division-v1"),
            (LoadoutSlots.Backpack, "6sh118-raid-backpack-emr"),
            (LoadoutSlots.Meds, "calok-b-hemostatic-applicator"),
        };

        public static Loadout BuildEquipped(IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            var worn = new List<EquippedItem>();
            foreach (var (slot, itemId) in Worn)
            {
                if (catalog.ContainsKey(itemId))
                {
                    worn.Add(EquippedItem.Create(slot, (3000 + worn.Count).ToString("x32"), itemId));
                }
            }

            return new Loadout(worn);
        }

        public static StashGrid BuildRig(IReadOnlyDictionary<string, ItemDefinition> catalog) =>
            SampleStash.Fill(catalog, RigItemIds, RigWidth, RigHeight, firstInstanceNumber: 1000);

        public static StashGrid BuildBackpack(IReadOnlyDictionary<string, ItemDefinition> catalog) =>
            SampleStash.Fill(catalog, BackpackItemIds, BackpackWidth, BackpackHeight, firstInstanceNumber: 2000);
    }
}

using System.Collections.Generic;

namespace Safehouse.Core
{
    /// <summary>The seven places a character wears or wields something, in the order the screen lists them.</summary>
    public static class LoadoutSlots
    {
        public const string Primary = "primary";
        public const string Ammo = "ammo";
        public const string Armor = "armor";
        public const string Helmet = "helmet";
        public const string Rig = "rig";
        public const string Backpack = "backpack";
        public const string Meds = "meds";

        public static readonly IReadOnlyList<string> All =
            new[] { Primary, Ammo, Armor, Helmet, Rig, Backpack, Meds };

        public static bool IsSlot(string slot) => slot != null && ((IList<string>)All).Contains(slot);
    }

    // Only the numbers the loadout screen and the equip rules read. The Python gear tables carry more
    // (fire rate, penetration, durability, heal amounts, ...); those arrive with the combat and
    // expedition ports that actually use them, so this does not guess at them now.

    public sealed class WeaponStats
    {
        public WeaponStats(string itemId, string caliber, int ergonomics, int recoil)
        {
            ItemId = Validate.Identifier(itemId, "item_id");
            Caliber = Validate.Text(caliber, "caliber", 60);
            Ergonomics = ergonomics;
            Recoil = recoil;
        }

        public string ItemId { get; }
        public string Caliber { get; }
        public int Ergonomics { get; }
        public int Recoil { get; }
    }

    public sealed class AmmoStats
    {
        public AmmoStats(string itemId, string caliber, int damage)
        {
            ItemId = Validate.Identifier(itemId, "item_id");
            Caliber = Validate.Text(caliber, "caliber", 60);
            Damage = damage;
        }

        public string ItemId { get; }
        public string Caliber { get; }
        public int Damage { get; }
    }

    public sealed class EquipmentStats
    {
        public EquipmentStats(string itemId, string slot, int armorClass, int capacity)
        {
            ItemId = Validate.Identifier(itemId, "item_id");
            if (slot != LoadoutSlots.Armor && slot != LoadoutSlots.Helmet
                && slot != LoadoutSlots.Rig && slot != LoadoutSlots.Backpack)
            {
                throw new ValidationException($"{itemId}: {slot} is not an equipment slot.");
            }

            Slot = slot;
            ArmorClass = armorClass;
            Capacity = capacity;
        }

        public string ItemId { get; }
        public string Slot { get; }
        public int ArmorClass { get; }

        /// <summary>Grid cells a rig or backpack adds to what a character can carry.</summary>
        public int Capacity { get; }

        /// <summary>
        /// The internal grid shape for this rig or backpack. Armor and helmets have no grid and return (0, 0).
        /// Width is fixed at 6 columns to match the CARRIED panel; height is the smallest whole number of rows
        /// that holds the capacity.
        /// </summary>
        public (int Width, int Height) GridDimensions()
        {
            if (Capacity <= 0)
            {
                return (0, 0);
            }

            const int width = 6;
            var height = (Capacity + width - 1) / width;
            return (width, height);
        }
    }

    public sealed class MedStats
    {
        public MedStats(string itemId, int uses)
        {
            ItemId = Validate.Identifier(itemId, "item_id");
            Uses = uses;
        }

        public string ItemId { get; }
        public int Uses { get; }
    }

    /// <summary>
    /// Which item can go in which loadout slot, and what each one carries. The C# side of the Python
    /// gear.GearData: lookups only, nothing here rolls dice.
    /// </summary>
    public sealed class GearData
    {
        private readonly Dictionary<string, WeaponStats> _weapons = new Dictionary<string, WeaponStats>();
        private readonly Dictionary<string, AmmoStats> _ammo = new Dictionary<string, AmmoStats>();
        private readonly Dictionary<string, EquipmentStats> _equipment = new Dictionary<string, EquipmentStats>();
        private readonly Dictionary<string, MedStats> _meds = new Dictionary<string, MedStats>();

        public GearData(IEnumerable<WeaponStats> weapons, IEnumerable<AmmoStats> ammo,
            IEnumerable<EquipmentStats> equipment, IEnumerable<MedStats> meds)
        {
            var seen = new HashSet<string>();
            foreach (var row in weapons ?? new WeaponStats[0]) { Add(seen, _weapons, row.ItemId, row); }
            foreach (var row in ammo ?? new AmmoStats[0]) { Add(seen, _ammo, row.ItemId, row); }
            foreach (var row in equipment ?? new EquipmentStats[0]) { Add(seen, _equipment, row.ItemId, row); }
            foreach (var row in meds ?? new MedStats[0]) { Add(seen, _meds, row.ItemId, row); }
        }

        public static GearData Empty { get; } = new GearData(null, null, null, null);

        private static void Add<T>(HashSet<string> seen, Dictionary<string, T> table, string itemId, T row)
        {
            // An item in two tables would have two answers to "which slot?".
            if (!seen.Add(itemId))
            {
                throw new ValidationException($"An item cannot be in two gear tables: {itemId}.");
            }

            table[itemId] = row;
        }

        public IReadOnlyDictionary<string, WeaponStats> Weapons => _weapons;
        public IReadOnlyDictionary<string, AmmoStats> Ammo => _ammo;
        public IReadOnlyDictionary<string, EquipmentStats> Equipment => _equipment;
        public IReadOnlyDictionary<string, MedStats> Meds => _meds;

        /// <summary>Which loadout slot an item belongs in, or null when it cannot be equipped.</summary>
        public string SlotOf(string itemId)
        {
            if (_weapons.ContainsKey(itemId)) { return LoadoutSlots.Primary; }
            if (_ammo.ContainsKey(itemId)) { return LoadoutSlots.Ammo; }
            if (_meds.ContainsKey(itemId)) { return LoadoutSlots.Meds; }
            return _equipment.TryGetValue(itemId, out var gear) ? gear.Slot : null;
        }

        /// <summary>Whether this ammunition fires from this weapon (same caliber).</summary>
        public bool Fits(string weaponId, string ammoId) =>
            _weapons.TryGetValue(weaponId, out var weapon)
            && _ammo.TryGetValue(ammoId, out var round)
            && weapon.Caliber == round.Caliber;
    }
}

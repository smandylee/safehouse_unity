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

    /// <summary>Weapon classes combat.json keys a fight by. The same list as the Python gear.WEAPON_CLASSES.</summary>
    public static class WeaponClasses
    {
        public static readonly IReadOnlyList<string> All = new[]
        {
            "assault-rifle", "carbine", "smg", "shotgun", "marksman", "bolt-action",
            "machine-gun", "pistol", "special",
        };
    }

    public sealed class WeaponStats
    {
        public WeaponStats(string itemId, string caliber, int ergonomics, int recoil,
            int fireRate = 0, string weaponClass = "", int effectiveDistance = 0)
        {
            ItemId = Validate.Identifier(itemId, "item_id");
            Caliber = Validate.Text(caliber, "caliber", 60);
            Ergonomics = ergonomics;
            Recoil = recoil;
            FireRate = fireRate;
            WeaponClass = weaponClass ?? "";
            EffectiveDistance = effectiveDistance;
        }

        public string ItemId { get; }
        public string Caliber { get; }
        public int Ergonomics { get; }
        public int Recoil { get; }
        public int FireRate { get; }
        public string WeaponClass { get; }
        public int EffectiveDistance { get; }
    }

    public sealed class AmmoStats
    {
        public AmmoStats(string itemId, string caliber, int damage,
            int penetration = 0, int armorDamage = 0, int projectiles = 1)
        {
            ItemId = Validate.Identifier(itemId, "item_id");
            Caliber = Validate.Text(caliber, "caliber", 60);
            Damage = damage;
            Penetration = penetration;
            ArmorDamage = armorDamage;
            Projectiles = projectiles < 1 ? 1 : projectiles;
        }

        public string ItemId { get; }
        public string Caliber { get; }
        public int Damage { get; }
        public int Penetration { get; }
        public int ArmorDamage { get; }
        public int Projectiles { get; }

        /// <summary>A shotgun shell counts every pellet.</summary>
        public int ShotDamage => Damage * Projectiles;
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
        public MedStats(string itemId, int uses, int heal = 0,
            bool stopsLightBleed = false, bool stopsHeavyBleed = false, bool treatsFracture = false)
        {
            ItemId = Validate.Identifier(itemId, "item_id");
            Uses = uses;
            Heal = heal;
            StopsLightBleed = stopsLightBleed;
            StopsHeavyBleed = stopsHeavyBleed;
            TreatsFracture = treatsFracture;
        }

        public string ItemId { get; }
        public int Uses { get; }
        public int Heal { get; }
        public bool StopsLightBleed { get; }
        public bool StopsHeavyBleed { get; }
        public bool TreatsFracture { get; }
    }

    /// <summary>An enemy as the game equips it. Mob ids keep the game's own spelling, which is not an item id.</summary>
    public sealed class MobStats
    {
        public MobStats(string mobId, string name, int health, int armorClass, int penetration, int damage, int fireRate)
        {
            MobId = Validate.Text(mobId, "mob_id", 60);
            Name = Validate.Text(name, "Enemy name", 60);
            Health = health;
            ArmorClass = armorClass;
            Penetration = penetration;
            Damage = damage;
            FireRate = fireRate;
        }

        public string MobId { get; }
        public string Name { get; }
        public int Health { get; }
        public int ArmorClass { get; }
        public int Penetration { get; }
        public int Damage { get; }
        public int FireRate { get; }
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
        private readonly Dictionary<string, MobStats> _mobs = new Dictionary<string, MobStats>();

        public GearData(IEnumerable<WeaponStats> weapons, IEnumerable<AmmoStats> ammo,
            IEnumerable<EquipmentStats> equipment, IEnumerable<MedStats> meds, IEnumerable<MobStats> mobs = null)
        {
            var seen = new HashSet<string>();
            foreach (var row in weapons ?? new WeaponStats[0]) { Add(seen, _weapons, row.ItemId, row); }
            foreach (var row in ammo ?? new AmmoStats[0]) { Add(seen, _ammo, row.ItemId, row); }
            foreach (var row in equipment ?? new EquipmentStats[0]) { Add(seen, _equipment, row.ItemId, row); }
            foreach (var row in meds ?? new MedStats[0]) { Add(seen, _meds, row.ItemId, row); }
            foreach (var row in mobs ?? new MobStats[0])
            {
                if (_mobs.ContainsKey(row.MobId))
                {
                    throw new ValidationException($"An enemy appears more than once: {row.MobId}.");
                }

                _mobs[row.MobId] = row;
            }
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
        public IReadOnlyDictionary<string, MobStats> Mobs => _mobs;

        /// <summary>Carrying capacity from the rig and backpack a character wears. Pockets are added by the expedition rules.</summary>
        public int CarryCells(IEnumerable<string> itemIds)
        {
            var cells = 0;
            foreach (var itemId in itemIds)
            {
                if (itemId != null && _equipment.TryGetValue(itemId, out var gear))
                {
                    cells += gear.Capacity;
                }
            }

            return cells;
        }

        /// <summary>Which body parts are actually covered: a helmet protects the head, a vest the torso.</summary>
        public Dictionary<string, int> ArmorClasses(IEnumerable<string> itemIds)
        {
            var classes = new Dictionary<string, int>();
            foreach (var itemId in itemIds)
            {
                if (itemId == null || !_equipment.TryGetValue(itemId, out var equip) || equip.ArmorClass <= 0)
                {
                    continue;
                }

                var parts = equip.Slot == LoadoutSlots.Helmet
                    ? new[] { "head" }
                    : equip.Slot == LoadoutSlots.Armor ? new[] { "thorax", "stomach" } : new string[0];
                foreach (var part in parts)
                {
                    classes[part] = classes.TryGetValue(part, out var current)
                        ? System.Math.Max(current, equip.ArmorClass)
                        : equip.ArmorClass;
                }
            }

            return classes;
        }

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

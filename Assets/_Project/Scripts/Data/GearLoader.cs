using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Safehouse.Core;

namespace Safehouse.Data
{
    /// <summary>
    /// Turns gear.json into <see cref="GearData"/>. The C# side of the Python gear.load_gear: it owns
    /// the JSON shape, while Safehouse.Core only ever sees validated objects. Combat-only tables
    /// (enemies) and the numbers only combat reads are left for the combat port.
    /// </summary>
    public static class GearLoader
    {
        public static GearData Parse(JObject document, IReadOnlyDictionary<string, ItemDefinition> catalog = null)
        {
            var weapons = Table(document, "weapons").Select(row => Read(row, catalog, "Weapon", (id, r) =>
                new WeaponStats(id, r.Value<string>("caliber"),
                    r.Value<int>("ergonomics"), r.Value<int>("recoil")))).ToList();
            var ammo = Table(document, "ammo").Select(row => Read(row, catalog, "Ammo", (id, r) =>
                new AmmoStats(id, r.Value<string>("caliber"), r.Value<int>("damage")))).ToList();
            var equipment = Table(document, "equipment").Select(row => Read(row, catalog, "Equipment", (id, r) =>
                new EquipmentStats(id, r.Value<string>("slot"),
                    r.Value<int>("armor_class"), r.Value<int>("capacity")))).ToList();
            var meds = Table(document, "meds").Select(row => Read(row, catalog, "Med", (id, r) =>
                new MedStats(id, r.Value<int>("uses")))).ToList();

            try
            {
                return new GearData(weapons, ammo, equipment, meds);
            }
            catch (ValidationException error)
            {
                throw new GameDataException(error.Message, error);
            }
        }

        public static GearData Load(IReadOnlyDictionary<string, ItemDefinition> catalog = null) =>
            Parse(GameDataLoader.Load(GameDataFile.Gear), catalog);

        private static IEnumerable<KeyValuePair<string, JToken>> Table(JObject document, string name)
        {
            if (!(document[name] is JObject table))
            {
                throw new GameDataException($"Gear database must contain a {name} table.");
            }

            return table.Properties().Select(property => new KeyValuePair<string, JToken>(property.Name, property.Value));
        }

        private static T Read<T>(KeyValuePair<string, JToken> row, IReadOnlyDictionary<string, ItemDefinition> catalog,
            string label, System.Func<string, JObject, T> build)
        {
            if (!(row.Value is JObject fields))
            {
                throw new GameDataException($"{label} {row.Key} must be an object.");
            }

            // Both files come from one generator run; an id missing from items.json means they drifted.
            if (catalog != null && !catalog.ContainsKey(row.Key))
            {
                throw new GameDataException(
                    $"{label} {row.Key}: unknown item_id. Regenerate items.json and gear.json together.");
            }

            try
            {
                return build(row.Key, fields);
            }
            catch (ValidationException error)
            {
                throw new GameDataException($"{label} {row.Key}: {error.Message}", error);
            }
        }
    }
}

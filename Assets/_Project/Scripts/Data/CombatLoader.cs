using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Safehouse.Core;

namespace Safehouse.Data
{
    /// <summary>Turns combat.json into <see cref="CombatNumbers"/>. The file is hand-tuned; this only checks its shape.</summary>
    public static class CombatLoader
    {
        public static CombatNumbers Parse(JObject document)
        {
            var weapon = Ints(document["weapon"] as JObject, "weapon");
            var engagement = new Dictionary<string, IReadOnlyDictionary<string, int>>();
            var engagementRaw = (JObject)document["engagement"];
            foreach (var range in MapZones.EngagementRanges)
            {
                engagement[range] = Ints((JObject)engagementRaw[range], "engagement " + range);
            }

            var armor = Ints(document["armor"] as JObject, "armor");
            var encountersRaw = (JObject)document["encounters"];
            var encounters = new Dictionary<string, (int Low, int High)>();
            foreach (var zoneId in MapZones.Ids)
            {
                var span = (JArray)encountersRaw[zoneId];
                encounters[zoneId] = (span[0].Value<int>(), span[1].Value<int>());
            }

            var scav = Ints(document["scav"] as JObject, "scav");
            var swing = (JObject)document["swing"];
            var practical = Ints(document["practical_rate"] as JObject, "practical_rate");
            var hitLocation = new Dictionary<string, IReadOnlyDictionary<string, int>>();
            var hitRaw = (JObject)document["hit_location"];
            foreach (var range in MapZones.EngagementRanges)
            {
                hitLocation[range] = Ints((JObject)hitRaw[range], "hit_location " + range);
            }

            var injury = (JObject)document["injury"];
            var chances = new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["heavy_bleed_chance"] = Ints((JObject)injury["heavy_bleed_chance_percent"], "heavy bleed"),
                ["light_bleed_chance"] = Ints((JObject)injury["light_bleed_chance_percent"], "light bleed"),
                ["fracture_chance"] = Ints((JObject)injury["fracture_chance_percent"], "fracture"),
            };
            return new CombatNumbers(weapon, engagement, armor, encounters,
                encountersRaw.Value<int>("night_percent"), encountersRaw.Value<int>("boss_damage_percent"),
                encountersRaw.Value<int>("boss_chance_percent"), scav, document["scav"].Value<string>("name"),
                document["party"].Value<int>("extra_member_percent"), swing.Value<int>("low_percent"),
                swing.Value<int>("high_percent"), Ints(document["unarmed"] as JObject, "unarmed"),
                document["meds"].Value<int>("use_below_percent"), document["fire"].Value<int>("shots_land_percent"),
                document["incoming"].Value<int>("enemy_skill_percent"), practical,
                document["limits"].Value<int>("max_seconds"), hitLocation, chances,
                Ints((JObject)injury["untreated_bleed_damage"], "bleed damage"),
                injury.Value<int>("fracture_firepower_penalty_percent"));
        }

        public static CombatNumbers Load() => Parse(GameDataLoader.Load(GameDataFile.Combat));

        private static Dictionary<string, int> Ints(JObject raw, string label)
        {
            if (raw == null)
            {
                throw new GameDataException($"{label} must be an object.");
            }

            return raw.Properties().Where(property => property.Value.Type == JTokenType.Integer)
                .ToDictionary(property => property.Name, property => property.Value.Value<int>());
        }
    }
}

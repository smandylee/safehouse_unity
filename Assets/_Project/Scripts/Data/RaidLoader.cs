using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Safehouse.Core;

namespace Safehouse.Data
{
    /// <summary>Turns raid_nodes.json into the node kinds and odds a direct-play map is built from.</summary>
    public static class RaidLoader
    {
        public static RaidRules Parse(JObject document)
        {
            var kindsRaw = (JObject)document["kinds"];
            var kinds = new Dictionary<string, NodeKind>();
            foreach (var kindId in RaidKinds.All)
            {
                var raw = (JObject)kindsRaw[kindId];
                var choices = new List<RaidChoice>();
                foreach (var choice in (JArray)raw["choices"] ?? new JArray())
                {
                    var row = (JObject)choice;
                    choices.Add(new RaidChoice(row.Value<string>("label"), row.Value<int>("loot_rolls"),
                        row.Value<int>("enemy_percent"), row.Value<int>("night_enemy_bonus_percent"),
                        row.Value<string>("action")));
                }

                kinds[kindId] = new NodeKind(kindId, ZoneInts((JObject)raw["zone_weight"]),
                    raw.Value<string>("title"), raw.Value<string>("text"), choices);
            }

            var patrol = (JObject)document["patrol_percent"];
            return new RaidRules(ZoneInts((JObject)document["nodes_per_zone"]), ZoneInts(patrol),
                patrol.Value<int>("night_bonus_percent"), kinds);
        }

        public static RaidRules Load() => Parse(GameDataLoader.Load(GameDataFile.RaidNodes));

        private static Dictionary<string, int> ZoneInts(JObject raw)
        {
            return MapZones.Ids.ToDictionary(zoneId => zoneId, zoneId => raw.Value<int>(zoneId));
        }
    }
}

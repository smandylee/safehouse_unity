using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Safehouse.Core;

namespace Safehouse.Data
{
    /// <summary>Turns maps.json and containers.json into the types expeditions search with.</summary>
    public static class MapLoader
    {
        public static Dictionary<string, MapDefinition> ParseMaps(JObject document,
            IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            if (!(document["maps"] is JArray maps) || maps.Count == 0 || maps.Count > 30)
            {
                throw new GameDataException("Map database must list 1 to 30 maps.");
            }

            var result = new Dictionary<string, MapDefinition>();
            foreach (var row in maps)
            {
                var map = ReadMap((JObject)row, catalog);
                if (result.ContainsKey(map.MapId))
                {
                    throw new GameDataException($"Duplicate map_id: {map.MapId}.");
                }

                result[map.MapId] = map;
            }

            return result;
        }

        public static ContainerRules ParseContainers(JObject document, IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            if (!(document["containers"] is JObject containers))
            {
                throw new GameDataException("Container rules must contain a containers object.");
            }

            var categories = new HashSet<string>(catalog.Values.Select(item => item.Category));
            var rarities = new HashSet<string>(catalog.Values.Select(item => item.Rarity));
            var rules = new Dictionary<string, ContainerRule>();
            foreach (var property in containers.Properties())
            {
                rules[property.Name] = ReadRule((JObject)property.Value, "Container " + property.Name, categories);
            }

            return new ContainerRules(
                Weights((JObject)document["rarity_weights"], "Rarity weights", rarities),
                ReadRule((JObject)document["default"], "Default container", categories),
                rules,
                ZonePercents((JObject)document["depth_rarity_percent"], "depth_rarity_percent"));
        }

        public static Dictionary<string, MapDefinition> LoadMaps(IReadOnlyDictionary<string, ItemDefinition> catalog) =>
            ParseMaps(GameDataLoader.Load(GameDataFile.Maps), catalog);

        public static ContainerRules LoadContainers(IReadOnlyDictionary<string, ItemDefinition> catalog) =>
            ParseContainers(GameDataLoader.Load(GameDataFile.Containers), catalog);

        private static MapDefinition ReadMap(JObject raw, IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            var mapId = raw.Value<string>("map_id");
            var engagement = Choice(raw.Value<string>("engagement_range"), "engagement_range", MapZones.EngagementRanges);
            var night = raw["night_zones"] as JArray;
            return new MapDefinition(mapId, raw.Value<string>("name"), raw.Value<int>("raid_minutes"), engagement,
                ReadZones((JArray)raw["zones"], catalog, mapId, engagement),
                night == null ? null : ReadZones(night, catalog, mapId + " night", engagement));
        }

        private static IReadOnlyList<Zone> ReadZones(JArray raw, IReadOnlyDictionary<string, ItemDefinition> catalog,
            string label, string mapRange)
        {
            if (raw == null || raw.Count != MapZones.Ids.Count)
            {
                throw new GameDataException($"{label}: a map needs exactly {MapZones.Ids.Count} zones.");
            }

            var zones = new List<Zone>();
            for (var i = 0; i < MapZones.Ids.Count; i++)
            {
                zones.Add(ReadZone((JObject)raw[i], catalog, label, MapZones.Ids[i], mapRange));
            }

            return zones;
        }

        private static Zone ReadZone(JObject raw, IReadOnlyDictionary<string, ItemDefinition> catalog, string label,
            string expectedId, string mapRange)
        {
            var zoneId = Choice(raw.Value<string>("zone_id"), "zone_id", MapZones.Ids);
            if (zoneId != expectedId)
            {
                throw new GameDataException($"{label}: zones must be outer, center, deep in that order.");
            }

            var known = new HashSet<string>(catalog.Keys);
            var loose = Weights((JObject)raw["loose"], $"{label} {zoneId} loose", known);
            var containersToken = raw["containers"] as JObject;
            var containers = containersToken == null || !containersToken.Properties().Any()
                ? new Dictionary<string, int>()
                : Weights(containersToken, $"{label} {zoneId} containers", null);
            var bosses = new List<BossSpawn>();
            foreach (var boss in (JArray)raw["bosses"] ?? new JArray())
            {
                bosses.Add(ReadBoss((JObject)boss));
            }

            var range = raw.Value<string>("engagement_range") ?? mapRange;
            return new Zone(zoneId, raw.Value<string>("name"), Choice(range, "engagement_range", MapZones.EngagementRanges),
                raw.Value<int>("loose_spots"), loose, containers, bosses);
        }

        private static BossSpawn ReadBoss(JObject raw)
        {
            var escorts = new List<Escort>();
            foreach (var escort in (JArray)raw["escorts"] ?? new JArray())
            {
                var row = (JObject)escort;
                escorts.Add(new Escort(row.Value<string>("mob_id"), row.Value<string>("name"), row.Value<int>("count")));
            }

            if (raw["night_only"] == null || raw["night_only"].Type != JTokenType.Boolean)
            {
                throw new GameDataException($"Boss {raw.Value<string>("mob_id")}: night_only must be true or false.");
            }

            return new BossSpawn(raw.Value<string>("mob_id"), raw.Value<string>("name"), PerMille(raw["chance"]),
                raw.Value<bool>("night_only"), escorts);
        }

        private static ContainerRule ReadRule(JObject raw, string label, HashSet<string> categories)
        {
            var count = (JArray)raw["count"];
            if (count == null || count.Count != 2)
            {
                throw new GameDataException($"{label} count must be [minimum, maximum].");
            }

            return new ContainerRule(count[0].Value<int>(), count[1].Value<int>(),
                Weights((JObject)raw["categories"], label + " categories", categories));
        }

        private static Dictionary<string, int> Weights(JObject raw, string label, HashSet<string> allowed)
        {
            if (raw == null || !raw.Properties().Any())
            {
                throw new GameDataException($"{label} must be an object with at least one entry.");
            }

            var weights = new Dictionary<string, int>();
            foreach (var property in raw.Properties())
            {
                if (allowed != null && !allowed.Contains(property.Name))
                {
                    throw new GameDataException($"{label}: unknown entry {property.Name}.");
                }

                weights[property.Name] = property.Value.Value<int>();
            }

            return weights;
        }

        private static Dictionary<string, int> ZonePercents(JObject raw, string label)
        {
            var result = new Dictionary<string, int>();
            foreach (var zoneId in MapZones.Ids)
            {
                if (raw[zoneId] == null)
                {
                    throw new GameDataException($"{label} needs one entry per zone.");
                }

                result[zoneId] = raw[zoneId].Value<int>();
            }

            return result;
        }

        private static int PerMille(JToken token)
        {
            var value = token.Value<double>();
            if (value < 0 || value > 1)
            {
                throw new GameDataException("A boss chance must be a number from 0 to 1.");
            }

            return (int)Math.Round(value * 1000, MidpointRounding.ToEven);
        }

        private static string Choice(string value, string label, IReadOnlyList<string> allowed)
        {
            if (value == null || !allowed.Contains(value))
            {
                throw new GameDataException($"{label} must be one of {string.Join(", ", allowed)}.");
            }

            return value;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Safehouse.Core;

namespace Safehouse.Data
{
    /// <summary>
    /// Turns locations.json into a <see cref="LocationGraph"/> per map and attaches it to the matching
    /// <see cref="MapDefinition"/>. Unlike every other file under StreamingAssets/data, this one is not a copy
    /// of anything in the Python build: it is Unity-only, hand-authored content, the same way raid_nodes.json's
    /// node kinds already were - a layer of named places (Dorms, Gas Station, ...) on top of the real
    /// outer/center/deep loot data those maps already carry. A map this file says nothing about keeps the plain
    /// outer/center/deep march (<see cref="MapDefinition.Locations"/> stays null).
    /// </summary>
    public static class LocationLoader
    {
        public static Dictionary<string, LocationGraph> Parse(JObject document)
        {
            if (!(document["maps"] is JObject maps))
            {
                throw new GameDataException("Location database must contain a maps object.");
            }

            var result = new Dictionary<string, LocationGraph>();
            foreach (var property in maps.Properties())
            {
                result[property.Name] = ReadGraph((JObject)property.Value, property.Name);
            }

            return result;
        }

        public static Dictionary<string, LocationGraph> Load() => Parse(GameDataLoader.Load(GameDataFile.Locations));

        /// <summary>
        /// Attaches each map's location graph (when it has one) to the loaded maps in place. Separate from
        /// <see cref="MapLoader.LoadMaps"/> because that file is a straight copy of the Python build's and this
        /// one is not - the two stay independently editable and independently loadable.
        /// </summary>
        public static void LoadInto(IDictionary<string, MapDefinition> maps) => Attach(maps, Load());

        /// <summary>The attaching step on its own, for callers (tests, mostly) that already have both parsed.</summary>
        public static void Attach(IDictionary<string, MapDefinition> maps, IReadOnlyDictionary<string, LocationGraph> graphs)
        {
            foreach (var pair in graphs)
            {
                if (!maps.TryGetValue(pair.Key, out var map))
                {
                    throw new GameDataException($"locations.json names unknown map: {pair.Key}.");
                }

                maps[pair.Key] = map.WithLocations(pair.Value);
            }
        }

        private static LocationGraph ReadGraph(JObject raw, string mapId)
        {
            if (!(raw["locations"] is JArray rows) || rows.Count == 0)
            {
                throw new GameDataException($"{mapId}: locations must be a non-empty list.");
            }

            try
            {
                var locations = rows.Select(row => ReadLocation((JObject)row)).ToList();
                return new LocationGraph(raw.Value<string>("entry_location_id"), locations);
            }
            catch (ValidationException error)
            {
                throw new GameDataException($"{mapId}: {error.Message}", error);
            }
        }

        private static MapLocation ReadLocation(JObject raw) =>
            new MapLocation(raw.Value<string>("location_id"), raw.Value<string>("name"),
                raw.Value<string>("zone_id"),
                raw["connects_to"]?.Select(token => token.Value<string>()).ToList() ?? new List<string>());
    }
}

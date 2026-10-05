using System;
using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// Maps, zones and container loot. The C# side of maps.py: the numbers come from data, and a search
    /// is a pure function of a <see cref="PythonRandom"/> so an expedition can be replayed.
    /// </summary>
    public static class MapZones
    {
        public static readonly IReadOnlyList<string> Ids = new[] { "outer", "center", "deep" };
        public static readonly IReadOnlyList<string> EngagementRanges = new[] { "close", "medium", "long" };
    }

    public sealed class Escort
    {
        public Escort(string mobId, string name, int count)
        {
            MobId = Validate.Text(mobId, "escort mob_id", 60);
            Name = Validate.Text(name, "escort name", 60);
            Count = count;
        }

        public string MobId { get; }
        public string Name { get; }
        public int Count { get; }
    }

    /// <summary>Chance is per mille: 600 means 60%, the same unit maps.py keeps after reading a 0..1 JSON number.</summary>
    public sealed class BossSpawn
    {
        public BossSpawn(string mobId, string name, int chancePerMille, bool nightOnly, IEnumerable<Escort> escorts)
        {
            MobId = Validate.Text(mobId, "boss mob_id", 60);
            Name = Validate.Text(name, "boss name", 60);
            ChancePerMille = chancePerMille;
            NightOnly = nightOnly;
            Escorts = (escorts ?? Enumerable.Empty<Escort>()).ToList();
        }

        public string MobId { get; }
        public string Name { get; }
        public int ChancePerMille { get; }
        public bool NightOnly { get; }
        public IReadOnlyList<Escort> Escorts { get; }
    }

    public sealed class Zone
    {
        public Zone(string zoneId, string name, string engagementRange, int looseSpots,
            IReadOnlyDictionary<string, int> loose, IReadOnlyDictionary<string, int> containers,
            IEnumerable<BossSpawn> bosses)
        {
            ZoneId = zoneId;
            Name = name;
            EngagementRange = engagementRange;
            LooseSpots = looseSpots;
            Loose = loose ?? new Dictionary<string, int>();
            Containers = containers ?? new Dictionary<string, int>();
            Bosses = (bosses ?? Enumerable.Empty<BossSpawn>()).ToList();
        }

        public string ZoneId { get; }
        public string Name { get; }
        public string EngagementRange { get; }
        public int LooseSpots { get; }
        public IReadOnlyDictionary<string, int> Loose { get; }
        public IReadOnlyDictionary<string, int> Containers { get; }
        public IReadOnlyList<BossSpawn> Bosses { get; }

        public int ContainerCount
        {
            get
            {
                var total = 0;
                foreach (var count in Containers.Values)
                {
                    total += count;
                }

                return total;
            }
        }

        public IEnumerable<BossSpawn> NightBosses(bool night) =>
            Bosses.Where(boss => night || !boss.NightOnly);
    }

    public sealed class MapDefinition
    {
        public MapDefinition(string mapId, string name, int raidMinutes, string engagementRange,
            IReadOnlyList<Zone> zones, IReadOnlyList<Zone> nightZones = null, LocationGraph locations = null)
        {
            MapId = mapId;
            Name = name;
            RaidMinutes = raidMinutes;
            EngagementRange = engagementRange;
            Zones = zones;
            NightZones = nightZones ?? new Zone[0];
            Locations = locations;
        }

        public string MapId { get; }
        public string Name { get; }
        public int RaidMinutes { get; }
        public string EngagementRange { get; }
        public IReadOnlyList<Zone> Zones { get; }
        public IReadOnlyList<Zone> NightZones { get; }

        /// <summary>
        /// The named places within this map to travel between (Dorms, Gas Station, ...), each tagged with
        /// which of the three zones below backs its loot, pacing and combat numbers. Null for a map with no
        /// authored locations yet, which falls back to the plain outer/center/deep march.
        /// </summary>
        public LocationGraph Locations { get; }

        public MapDefinition WithLocations(LocationGraph locations) =>
            new MapDefinition(MapId, Name, RaidMinutes, EngagementRange, Zones, NightZones, locations);

        public Zone Zone(string zoneId, bool night)
        {
            var zones = night && NightZones.Count > 0 ? NightZones : Zones;
            foreach (var zone in zones)
            {
                if (zone.ZoneId == zoneId)
                {
                    return zone;
                }
            }

            throw new ValidationException($"{Name} has no {zoneId} zone.");
        }
    }

    /// <summary>
    /// One named place within a map (Dorms, Gas Station, ...). Not from the game - invented content, the same
    /// way raid_nodes.json's node kinds are - layered on top of the real outer/center/deep loot data: a
    /// location borrows its zone's loot tables, engagement range and bosses, and only adds a name and a
    /// travel choice. <see cref="ConnectsTo"/> is forward-only, like the node routes within a location: once
    /// the party leaves, that location is behind them.
    /// </summary>
    public sealed class MapLocation
    {
        public MapLocation(string locationId, string name, string zoneId, IReadOnlyList<string> connectsTo)
        {
            LocationId = Validate.Identifier(locationId, "location_id");
            Name = Validate.Text(name, "location name", 60);
            if (!MapZones.Ids.Contains(zoneId))
            {
                throw new ValidationException($"{name}: zone_id must be one of {string.Join(", ", MapZones.Ids)}.");
            }

            ZoneId = zoneId;
            ConnectsTo = (connectsTo ?? Enumerable.Empty<string>()).ToList();
        }

        public string LocationId { get; }
        public string Name { get; }
        public string ZoneId { get; }

        /// <summary>Locations reachable from this one's exit. Empty means this is a dead end: extract only.</summary>
        public IReadOnlyList<string> ConnectsTo { get; }
    }

    /// <summary>A map's named locations and how they connect. One graph per map; <see cref="MapDefinition.Locations"/>.</summary>
    public sealed class LocationGraph
    {
        public LocationGraph(string entryLocationId, IReadOnlyList<MapLocation> locations)
        {
            var list = (locations ?? Enumerable.Empty<MapLocation>()).ToList();
            if (list.Count == 0)
            {
                throw new ValidationException("A location graph needs at least one location.");
            }

            var byId = new Dictionary<string, MapLocation>();
            foreach (var location in list)
            {
                if (!byId.TryAdd(location.LocationId, location))
                {
                    throw new ValidationException($"Duplicate location_id: {location.LocationId}.");
                }
            }

            foreach (var location in list)
            {
                foreach (var target in location.ConnectsTo)
                {
                    if (!byId.ContainsKey(target))
                    {
                        throw new ValidationException(
                            $"{location.Name} connects to unknown location: {target}.");
                    }

                    if (target == location.LocationId)
                    {
                        throw new ValidationException($"{location.Name} cannot connect to itself.");
                    }
                }
            }

            if (!byId.ContainsKey(entryLocationId))
            {
                throw new ValidationException($"entry_location_id {entryLocationId} is not one of this map's locations.");
            }

            CheckNoCycle(list);
            EntryLocationId = entryLocationId;
            Locations = byId;
        }

        /// <summary>
        /// ConnectsTo must only ever lead forward: without this, a future edit that links two locations back
        /// to each other would let a party bounce between them forever, re-rolling a cleared location's loot
        /// each time. Classic three-colour DFS: a location found still "in progress" (gray) when revisited
        /// means the path back to it closes a loop.
        /// </summary>
        private static void CheckNoCycle(IReadOnlyList<MapLocation> list)
        {
            var byId = list.ToDictionary(location => location.LocationId);
            var state = list.ToDictionary(location => location.LocationId, _ => 0); // 0 white, 1 gray, 2 black
            foreach (var location in list)
            {
                if (state[location.LocationId] == 0)
                {
                    Visit(location, byId, state);
                }
            }
        }

        private static void Visit(MapLocation location, IReadOnlyDictionary<string, MapLocation> byId,
            Dictionary<string, int> state)
        {
            state[location.LocationId] = 1;
            foreach (var targetId in location.ConnectsTo)
            {
                if (state[targetId] == 1)
                {
                    throw new ValidationException(
                        $"This location graph has a cycle through {location.Name}. Locations must only lead forward.");
                }

                if (state[targetId] == 0)
                {
                    Visit(byId[targetId], byId, state);
                }
            }

            state[location.LocationId] = 2;
        }

        public string EntryLocationId { get; }
        public IReadOnlyDictionary<string, MapLocation> Locations { get; }

        public MapLocation Get(string locationId) =>
            locationId != null && Locations.TryGetValue(locationId, out var location)
                ? location
                : throw new ValidationException($"Unknown location: {locationId}.");
    }

    public sealed class ContainerRule
    {
        public ContainerRule(int countMin, int countMax, IReadOnlyDictionary<string, int> categories)
        {
            CountMin = countMin;
            CountMax = countMax;
            Categories = categories;
        }

        public int CountMin { get; }
        public int CountMax { get; }
        public IReadOnlyDictionary<string, int> Categories { get; }
    }

    public sealed class ContainerRules
    {
        public ContainerRules(IReadOnlyDictionary<string, int> rarityWeights, ContainerRule fallback,
            IReadOnlyDictionary<string, ContainerRule> containers, IReadOnlyDictionary<string, int> depthRarityPercent)
        {
            RarityWeights = rarityWeights;
            Fallback = fallback;
            Containers = containers;
            DepthRarityPercent = depthRarityPercent;
        }

        public IReadOnlyDictionary<string, int> RarityWeights { get; }
        public ContainerRule Fallback { get; }
        public IReadOnlyDictionary<string, ContainerRule> Containers { get; }
        public IReadOnlyDictionary<string, int> DepthRarityPercent { get; }

        public ContainerRule RuleFor(string containerType) =>
            Containers.TryGetValue(containerType, out var rule) ? rule : Fallback;
    }

    /// <summary>Catalog lookups a loot roll needs, prepared once per expedition instead of per search.</summary>
    public sealed class LootTables
    {
        private readonly IReadOnlyDictionary<string, ItemDefinition> _catalog;
        private readonly Dictionary<string, (List<string> Items, List<double> Weights)> _byCategory =
            new Dictionary<string, (List<string>, List<double>)>();

        public LootTables(IReadOnlyDictionary<string, ItemDefinition> catalog, ContainerRules rules)
        {
            _catalog = catalog;
            Rules = rules;
        }

        public ContainerRules Rules { get; }

        public (IReadOnlyList<string> Items, IReadOnlyList<double> Weights) Category(string category, string zoneId)
        {
            var key = category + "\n" + zoneId;
            if (!_byCategory.TryGetValue(key, out var cached))
            {
                var items = _catalog.Where(pair => pair.Value.Category == category)
                    .Select(pair => pair.Key).OrderBy(id => id, StringComparer.Ordinal).ToList();
                var percent = Rules.DepthRarityPercent[zoneId];
                var exponent = 100.0 / percent;
                var weights = items.Select(itemId =>
                    Math.Pow(Rules.RarityWeights.TryGetValue(_catalog[itemId].Rarity, out var weight) ? weight : 1, exponent))
                    .ToList();
                cached = (items, weights);
                _byCategory[key] = cached;
            }

            return cached;
        }
    }

    public static class LootRolls
    {
        public static List<string> SearchLoose(Zone zone, PythonRandom random)
        {
            if (zone.Loose.Count == 0)
            {
                return new List<string>();
            }

            return new List<string> { PickSorted(random, zone.Loose) };
        }

        public static List<string> SearchContainer(string containerType, LootTables tables, PythonRandom random, string zoneId)
        {
            var rule = tables.Rules.RuleFor(containerType);
            var found = new List<string>();
            var rolls = random.RandInt(rule.CountMin, rule.CountMax);
            for (var i = 0; i < rolls; i++)
            {
                var category = PickSorted(random, rule.Categories);
                var (items, weights) = tables.Category(category, zoneId);
                if (items.Count > 0)
                {
                    found.Add(random.Choices(items, weights));
                }
            }

            return found;
        }

        /// <summary>One search of the zone: either a loose-loot spot or one container.</summary>
        public static List<string> SearchZone(Zone zone, LootTables tables, PythonRandom random)
        {
            var spots = zone.LooseSpots;
            var containers = zone.ContainerCount;
            if (spots + containers == 0)
            {
                return new List<string>();
            }

            if (random.Random() < (double)spots / (spots + containers))
            {
                return SearchLoose(zone, random);
            }

            return SearchContainer(PickSorted(random, zone.Containers), tables, random, zone.ZoneId);
        }

        /// <summary>Python's _pick: keys are sorted, so the roll does not depend on file order.</summary>
        public static string PickSorted(PythonRandom random, IReadOnlyDictionary<string, int> weights)
        {
            var keys = weights.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
            var values = keys.Select(key => (double)weights[key]).ToList();
            return random.Choices(keys, values);
        }

        /// <summary>Route generation picks in dictionary order, which is the JSON order the loader kept.</summary>
        public static string PickInOrder(PythonRandom random, IReadOnlyDictionary<string, int> weights)
        {
            var keys = new List<string>();
            var values = new List<double>();
            foreach (var pair in weights)
            {
                if (pair.Value > 0)
                {
                    keys.Add(pair.Key);
                    values.Add(pair.Value);
                }
            }

            return random.Choices(keys, values);
        }
    }
}

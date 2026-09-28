using System;
using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// Direct-play node maps. A route is a forward-only branching path: once the party leaves a column,
    /// that column is behind them. Item and boss placement still come from the map.
    /// </summary>
    public static class RaidKinds
    {
        public static readonly IReadOnlyList<string> All = new[]
        {
            "start", "container", "loose", "corpse", "noise", "boss_lair", "exit",
        };

        public static readonly IReadOnlyList<string> Placed = new[] { "container", "loose", "corpse", "noise" };
        public const int MinNodes = 3;
        public const int MaxNodes = 40;
    }

    public sealed class RaidChoice
    {
        public RaidChoice(string label, int lootRolls, int enemyPercent, int nightEnemyBonusPercent, string action)
        {
            Label = label;
            LootRolls = lootRolls;
            EnemyPercent = enemyPercent;
            NightEnemyBonusPercent = nightEnemyBonusPercent;
            Action = action;
        }

        public string Label { get; }
        public int LootRolls { get; }
        public int EnemyPercent { get; }
        public int NightEnemyBonusPercent { get; }
        public string Action { get; }
    }

    public sealed class NodeKind
    {
        public NodeKind(string kindId, IReadOnlyDictionary<string, int> zoneWeight, string title, string text,
            IReadOnlyList<RaidChoice> choices)
        {
            KindId = kindId;
            ZoneWeight = zoneWeight;
            Title = title;
            Text = text;
            Choices = choices;
        }

        public string KindId { get; }
        public IReadOnlyDictionary<string, int> ZoneWeight { get; }
        public string Title { get; }
        public string Text { get; }
        public IReadOnlyList<RaidChoice> Choices { get; }
    }

    public sealed class RaidRules
    {
        public RaidRules(IReadOnlyDictionary<string, int> nodesPerZone, IReadOnlyDictionary<string, int> patrolPercent,
            int patrolNightBonusPercent, IReadOnlyDictionary<string, NodeKind> kinds)
        {
            NodesPerZone = nodesPerZone;
            PatrolPercent = patrolPercent;
            PatrolNightBonusPercent = patrolNightBonusPercent;
            Kinds = kinds;
        }

        public IReadOnlyDictionary<string, int> NodesPerZone { get; }
        public IReadOnlyDictionary<string, int> PatrolPercent { get; }
        public int PatrolNightBonusPercent { get; }
        public IReadOnlyDictionary<string, NodeKind> Kinds { get; }
    }

    public sealed class RouteNode
    {
        public RouteNode(string nodeId, string kind, double x, double y, IReadOnlyList<string> neighbors, bool cleared,
            string containerType, string bossMobId)
        {
            NodeId = nodeId;
            Kind = kind;
            X = x;
            Y = y;
            Neighbors = neighbors ?? new string[0];
            Cleared = cleared;
            ContainerType = containerType;
            BossMobId = bossMobId;
        }

        public string NodeId { get; }
        public string Kind { get; }
        public double X { get; }
        public double Y { get; }
        public IReadOnlyList<string> Neighbors { get; }
        public bool Cleared { get; }
        public string ContainerType { get; }
        public string BossMobId { get; }

        public RouteNode With(IReadOnlyList<string> neighbors = null, bool? cleared = null) =>
            new RouteNode(NodeId, Kind, X, Y, neighbors ?? Neighbors, cleared ?? Cleared, ContainerType, BossMobId);
    }

    public sealed class Route
    {
        public Route(string startNodeId, IReadOnlyDictionary<string, RouteNode> nodes, string currentNodeId = null)
        {
            StartNodeId = startNodeId;
            Nodes = new Dictionary<string, RouteNode>(nodes);
            CurrentNodeId = string.IsNullOrEmpty(currentNodeId) ? startNodeId : currentNodeId;
        }

        public string StartNodeId { get; }
        public Dictionary<string, RouteNode> Nodes { get; }
        public string CurrentNodeId { get; }

        public Route With(string currentNodeId = null, IReadOnlyDictionary<string, RouteNode> nodes = null) =>
            new Route(StartNodeId, nodes ?? Nodes, currentNodeId ?? CurrentNodeId);
    }

    public static class RaidMaps
    {
        public static Route Generate(Zone zone, bool night, RaidRules rules, GearData gear, CombatNumbers numbers,
            PythonRandom random)
        {
            var total = Math.Max(RaidKinds.MinNodes, rules.NodesPerZone[zone.ZoneId]);
            var bossMobId = RollBoss(zone, night, gear, numbers, random);
            var reserved = bossMobId != null ? 2 : 1;
            var interiorCount = Math.Max(0, total - 1 - reserved);
            var weights = new Dictionary<string, int>();
            foreach (var kind in RaidKinds.Placed)
            {
                if (kind == "container" && zone.Containers.Count == 0)
                {
                    continue;
                }

                weights[kind] = rules.Kinds[kind].ZoneWeight[zone.ZoneId];
            }

            var columns = new List<List<string>> { new List<string> { "start" } };
            foreach (var size in ColumnSizes(interiorCount, random))
            {
                var column = new List<string>();
                for (var i = 0; i < size; i++)
                {
                    column.Add(LootRolls.PickInOrder(random, weights));
                }

                columns.Add(column);
            }

            if (bossMobId != null)
            {
                columns.Add(new List<string> { "boss_lair" });
            }

            columns.Add(new List<string> { "exit" });

            var nodes = new Dictionary<string, RouteNode>();
            var idsByColumn = new List<List<string>>();
            var counter = 0;
            var span = Math.Max(1, columns.Count - 1);
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                var columnIds = new List<string>();
                var columnKinds = columns[columnIndex];
                for (var row = 0; row < columnKinds.Count; row++)
                {
                    var kind = columnKinds[row];
                    var nodeId = "n" + counter;
                    counter++;
                    var containerType = kind == "container" ? LootRolls.PickInOrder(random, zone.Containers) : null;
                    var y = (row + 1.0) / (columnKinds.Count + 1) + random.Uniform(-0.05, 0.05);
                    y = Math.Min(0.9, Math.Max(0.1, y));
                    nodes[nodeId] = new RouteNode(nodeId, kind,
                        Math.Round((double)columnIndex / span, 4, MidpointRounding.ToEven),
                        Math.Round(y, 4, MidpointRounding.ToEven),
                        new string[0], false, containerType, kind == "boss_lair" ? bossMobId : null);
                    columnIds.Add(nodeId);
                }

                idsByColumn.Add(columnIds);
            }

            var edges = nodes.Keys.ToDictionary(id => id, id => new HashSet<string>());
            for (var columnIndex = 0; columnIndex < idsByColumn.Count - 1; columnIndex++)
            {
                var currentColumn = idsByColumn[columnIndex];
                var nextColumn = idsByColumn[columnIndex + 1];
                for (var row = 0; row < currentColumn.Count; row++)
                {
                    var forwardCount = Math.Min(nextColumn.Count, random.RandInt(1, 2));
                    foreach (var targetRow in NearbyRows(row, currentColumn.Count, nextColumn.Count, random, forwardCount))
                    {
                        edges[currentColumn[row]].Add(nextColumn[targetRow]);
                    }
                }

                var reached = new HashSet<string>();
                foreach (var nodeId in currentColumn)
                {
                    foreach (var target in edges[nodeId])
                    {
                        reached.Add(target);
                    }
                }

                for (var targetRow = 0; targetRow < nextColumn.Count; targetRow++)
                {
                    var target = nextColumn[targetRow];
                    if (reached.Contains(target))
                    {
                        continue;
                    }

                    var sourceRow = NearbyRows(targetRow, nextColumn.Count, currentColumn.Count, random, 1)[0];
                    edges[currentColumn[sourceRow]].Add(target);
                }
            }

            var linked = new Dictionary<string, RouteNode>();
            foreach (var pair in nodes)
            {
                var neighbors = edges[pair.Key].OrderBy(id => id, StringComparer.Ordinal).ToList();
                linked[pair.Key] = pair.Value.With(neighbors: neighbors);
            }

            return new Route(idsByColumn[0][0], linked);
        }

        private static string RollBoss(Zone zone, bool night, GearData gear, CombatNumbers numbers, PythonRandom random)
        {
            foreach (var boss in zone.NightBosses(night))
            {
                var chance = (int)Math.Round(boss.ChancePerMille * (double)numbers.BossChancePercent / CombatRules.Percent,
                    MidpointRounding.ToEven);
                if (random.RandRange(1000) < chance && gear.Mobs.ContainsKey(boss.MobId))
                {
                    return boss.MobId;
                }
            }

            return null;
        }

        private static List<int> ColumnSizes(int count, PythonRandom random)
        {
            var sizes = new List<int>();
            var remaining = count;
            while (remaining > 0)
            {
                var size = Math.Min(remaining, random.RandInt(1, 3));
                sizes.Add(size);
                remaining -= size;
            }

            return sizes;
        }

        private static double ProjectRow(int row, int fromSize, int toSize) =>
            fromSize <= 1 ? (toSize - 1) / 2.0 : row * (toSize - 1.0) / (fromSize - 1);

        private static List<int> NearbyRows(int row, int fromSize, int toSize, PythonRandom random, int want)
        {
            var center = ProjectRow(row, fromSize, toSize);
            var order = new List<(int Row, double Distance, double Tie)>();
            for (var candidate = 0; candidate < toSize; candidate++)
            {
                order.Add((candidate, Math.Abs(candidate - center), random.Random()));
            }

            order.Sort((a, b) =>
            {
                var distance = a.Distance.CompareTo(b.Distance);
                return distance != 0 ? distance : a.Tie.CompareTo(b.Tie);
            });
            return order.Take(want).Select(entry => entry.Row).ToList();
        }
    }
}

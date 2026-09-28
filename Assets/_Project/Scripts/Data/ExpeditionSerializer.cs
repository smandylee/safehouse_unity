using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Safehouse.Core;

namespace Safehouse.Data
{
    /// <summary>Expedition files, schema 2. A schema 1 file is a simulated expedition from before direct play existed.</summary>
    public static class ExpeditionSerializer
    {
        public static byte[] Serialize(Expedition expedition) =>
            new UTF8Encoding(false).GetBytes(ToJson(expedition).ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");

        public static JObject ToJson(Expedition expedition)
        {
            var loot = new JObject();
            foreach (var pair in expedition.Loot)
            {
                loot[pair.Key] = new JArray(pair.Value);
            }

            var words = new JArray();
            foreach (var word in expedition.RngState.Words)
            {
                words.Add(word);
            }

            words.Add(expedition.RngState.Index);
            return new JObject
            {
                ["schema_version"] = Expedition.SchemaVersion,
                ["expedition_id"] = expedition.ExpeditionId,
                ["map_id"] = expedition.MapId,
                ["night"] = expedition.Night,
                ["party"] = new JArray(expedition.Party),
                ["started_at"] = expedition.StartedAt,
                ["zone_id"] = expedition.ZoneId,
                ["zone_started_at"] = expedition.ZoneStartedAt,
                ["status"] = expedition.Status,
                ["outcome"] = expedition.Outcome == null ? JValue.CreateNull() : new JValue(expedition.Outcome),
                ["loot"] = loot,
                ["discarded"] = new JArray(expedition.Discarded),
                ["log"] = new JArray(expedition.Log),
                ["rng_state"] = new JArray { expedition.RngState.Version, words, Gauss(expedition.RngState.GaussNext) },
                ["mode"] = expedition.Mode,
                ["route"] = expedition.Route == null ? JValue.CreateNull() : RouteJson(expedition.Route),
            };
        }

        public static Expedition FromJson(JObject raw)
        {
            var version = raw.Value<int?>("schema_version");
            if (version != 1 && version != Expedition.SchemaVersion)
            {
                throw new ValidationException($"Expedition file must contain schema_version 1 or {Expedition.SchemaVersion}.");
            }

            var legacy = version == 1;
            var party = raw["party"].Select(token => token.Value<string>()).ToList();
            var loot = new Dictionary<string, IReadOnlyList<string>>();
            foreach (var property in ((JObject)raw["loot"]).Properties())
            {
                loot[property.Name] = property.Value.Select(token => token.Value<string>()).ToList();
            }

            var rng = (JArray)raw["rng_state"];
            var sequence = (JArray)rng[1];
            if (sequence.Count != PythonRandom.StateLength + 1)
            {
                throw new ValidationException("Corrupt random-generator state. The expedition file may have been edited by hand.");
            }

            var words = new uint[PythonRandom.StateLength];
            for (var i = 0; i < PythonRandom.StateLength; i++)
            {
                words[i] = unchecked((uint)sequence[i].Value<long>());
            }

            var route = legacy || raw["route"] == null || raw["route"].Type == JTokenType.Null
                ? null
                : ReadRoute((JObject)raw["route"]);
            return new Expedition(
                raw.Value<string>("expedition_id"), raw.Value<string>("map_id"), raw.Value<bool>("night"), party,
                raw.Value<long>("started_at"), raw.Value<string>("zone_id"), raw.Value<long>("zone_started_at"),
                raw.Value<string>("status"), raw["outcome"].Type == JTokenType.Null ? null : raw.Value<string>("outcome"),
                loot, raw["discarded"].Select(token => token.Value<string>()).ToList(),
                raw["log"].Select(token => token.Value<string>()).ToList(),
                new RngState(rng[0].Value<int>(), words, sequence[PythonRandom.StateLength].Value<int>(),
                    rng[2].Type == JTokenType.Null ? (double?)null : rng[2].Value<double>()),
                legacy ? ExpeditionModes.Simulation : raw.Value<string>("mode"), route);
        }

        private static JToken Gauss(double? value) =>
            value.HasValue ? new JValue(value.Value) : JValue.CreateNull();

        private static JObject RouteJson(Route route)
        {
            var nodes = new JObject();
            foreach (var pair in route.Nodes)
            {
                var node = pair.Value;
                nodes[pair.Key] = new JObject
                {
                    ["node_id"] = node.NodeId,
                    ["kind"] = node.Kind,
                    ["x"] = node.X,
                    ["y"] = node.Y,
                    ["neighbors"] = new JArray(node.Neighbors),
                    ["cleared"] = node.Cleared,
                    ["container_type"] = node.ContainerType == null ? JValue.CreateNull() : new JValue(node.ContainerType),
                    ["boss_mob_id"] = node.BossMobId == null ? JValue.CreateNull() : new JValue(node.BossMobId),
                };
            }

            return new JObject
            {
                ["start_node_id"] = route.StartNodeId,
                ["current_node_id"] = route.CurrentNodeId,
                ["nodes"] = nodes,
            };
        }

        private static Route ReadRoute(JObject raw)
        {
            var nodes = new Dictionary<string, RouteNode>();
            foreach (var property in ((JObject)raw["nodes"]).Properties())
            {
                var row = (JObject)property.Value;
                nodes[property.Name] = new RouteNode(row.Value<string>("node_id"), row.Value<string>("kind"),
                    row.Value<double>("x"), row.Value<double>("y"),
                    row["neighbors"].Select(token => token.Value<string>()).ToList(), row.Value<bool>("cleared"),
                    row["container_type"].Type == JTokenType.Null ? null : row.Value<string>("container_type"),
                    row["boss_mob_id"].Type == JTokenType.Null ? null : row.Value<string>("boss_mob_id"));
            }

            return new Route(raw.Value<string>("start_node_id"), nodes, raw.Value<string>("current_node_id"));
        }
    }
}

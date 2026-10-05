using System.Collections.Generic;
using NUnit.Framework;
using Safehouse.Core;
using Safehouse.Data;

namespace Safehouse.Tests
{
    /// <summary>Expedition files: the schema 4 location_id and node_visits fields, and that older files still load.</summary>
    public sealed class ExpeditionSerializerTests
    {
        private static readonly string PartyMember = new string('1', 32);

        private static Expedition Direct(string locationId)
        {
            var route = new Route("n0", new Dictionary<string, RouteNode>
            {
                ["n0"] = new RouteNode("n0", "start", 0, 0.5, new[] { "n1" }, false, null, null),
                ["n1"] = new RouteNode("n1", "exit", 1, 0.5, new string[0], false, null, null),
            });
            return ExpeditionRules.Create("customs", false, new[] { PartyMember },
                1_000, ExpeditionModes.Direct, route, PythonRandom.Seed(1), "outer", locationId);
        }

        [Test]
        public void LocationIdSurvivesAWriteAndARead()
        {
            var expedition = Direct("gas-station");

            var back = ExpeditionSerializer.FromJson(ExpeditionSerializer.ToJson(expedition));

            Assert.AreEqual("gas-station", back.LocationId);
            Assert.AreEqual(Expedition.SchemaVersion, ExpeditionSerializer.ToJson(back).Value<int>("schema_version"));
        }

        [Test]
        public void ANullLocationIdRoundTripsAsNull()
        {
            var expedition = Direct(null);

            var json = ExpeditionSerializer.ToJson(expedition);

            Assert.AreEqual(Newtonsoft.Json.Linq.JTokenType.Null, json["location_id"].Type);
            Assert.IsNull(ExpeditionSerializer.FromJson(json).LocationId);
        }

        [Test]
        public void ASchemaTwoFileWithNoLocationIdKeyStillLoadsWithANullLocation()
        {
            var json = ExpeditionSerializer.ToJson(Direct("gas-station"));
            json["schema_version"] = 2;
            json.Remove("location_id"); // schema 2 files predate this key entirely
            json.Remove("node_visits");

            var back = ExpeditionSerializer.FromJson(json);

            Assert.IsNull(back.LocationId);
            Assert.AreEqual(1, back.NodesVisited, "a direct-play file still defaults to having just arrived somewhere");
            Assert.AreEqual("outer", back.ZoneId);
            Assert.AreEqual(ExpeditionModes.Direct, back.Mode);
        }

        [Test]
        public void NodesVisitedSurvivesAWriteAndARead()
        {
            var expedition = Direct("gas-station").With(nodesVisited: 17);

            var back = ExpeditionSerializer.FromJson(ExpeditionSerializer.ToJson(expedition));

            Assert.AreEqual(17, back.NodesVisited);
        }

        [Test]
        public void ASchemaThreeFileWithNoNodeVisitsKeyResumesAsIfJustArrived()
        {
            var json = ExpeditionSerializer.ToJson(Direct("gas-station").With(nodesVisited: 17));
            json["schema_version"] = 3;
            json.Remove("node_visits"); // schema 3 files predate the node cap

            var back = ExpeditionSerializer.FromJson(json);

            Assert.AreEqual("gas-station", back.LocationId);
            Assert.AreEqual(1, back.NodesVisited, "defaults as a fresh Create() would, not the count it actually had");
        }

        [Test]
        public void ASimulatedExpeditionHasNoNodesEvenWhenResumedFromAnOlderFile()
        {
            var simulated = ExpeditionRules.Create("customs", false, new[] { PartyMember }, 1_000,
                ExpeditionModes.Simulation, null, PythonRandom.Seed(1));
            var json = ExpeditionSerializer.ToJson(simulated);
            json["schema_version"] = 2;
            json.Remove("node_visits");

            Assert.AreEqual(0, ExpeditionSerializer.FromJson(json).NodesVisited);
        }

        [Test]
        public void ASchemaFiveFileIsRefused()
        {
            var json = ExpeditionSerializer.ToJson(Direct("gas-station"));
            json["schema_version"] = 5;

            Assert.Throws<ValidationException>(() => ExpeditionSerializer.FromJson(json));
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Safehouse.Core;

namespace Safehouse.Tests
{
    /// <summary>Named places within a map, and how they connect: the rules in <see cref="MapLocation"/>/<see cref="LocationGraph"/>.</summary>
    public sealed class LocationGraphTests
    {
        private static MapLocation Loc(string id, string zoneId = "outer", params string[] connectsTo) =>
            new MapLocation(id, id.ToUpperInvariant(), zoneId, connectsTo);

        [Test]
        public void AZoneIdThatIsNotOneOfTheThreeTiersIsRejected()
        {
            Assert.Throws<ValidationException>(() => new MapLocation("a", "A", "shallow", null));
            Assert.DoesNotThrow(() => new MapLocation("a", "A", "outer", null));
            Assert.DoesNotThrow(() => new MapLocation("a", "A", "center", null));
            Assert.DoesNotThrow(() => new MapLocation("a", "A", "deep", null));
        }

        [Test]
        public void AGraphNeedsAtLeastOneLocationAndAnEntryThatExists()
        {
            Assert.Throws<ValidationException>(() => new LocationGraph("a", new MapLocation[0]));
            Assert.Throws<ValidationException>(() => new LocationGraph("ghost", new[] { Loc("a") }));
            Assert.DoesNotThrow(() => new LocationGraph("a", new[] { Loc("a") }));
        }

        [Test]
        public void EveryConnectionMustLeadToARealLocationAndNotToItself()
        {
            Assert.Throws<ValidationException>(() => new LocationGraph("a", new[] { Loc("a", "outer", "ghost") }));
            Assert.Throws<ValidationException>(() => new LocationGraph("a", new[] { Loc("a", "outer", "a") }));
            Assert.DoesNotThrow(() => new LocationGraph("a", new[] { Loc("a", "outer", "b"), Loc("b") }));
        }

        [Test]
        public void DuplicateLocationIdsAreRejected()
        {
            Assert.Throws<ValidationException>(() => new LocationGraph("a", new[] { Loc("a"), Loc("a") }));
        }

        [Test]
        public void ACycleIsRejectedSoATripCanNeverLoopForever()
        {
            // a -> b -> c -> a
            var error = Assert.Throws<ValidationException>(() => new LocationGraph("a", new[]
            {
                Loc("a", "outer", "b"), Loc("b", "center", "c"), Loc("c", "deep", "a"),
            }));
            StringAssert.Contains("cycle", error.Message);

            // A location that connects back to itself two hops away, even if most of the graph is fine.
            Assert.Throws<ValidationException>(() => new LocationGraph("a", new[]
            {
                Loc("a", "outer", "b", "x"), Loc("b", "center", "c"), Loc("c", "deep", "b"), Loc("x", "deep"),
            }));

            // A plain DAG, including one with two paths converging on the same node, is fine.
            Assert.DoesNotThrow(() => new LocationGraph("a", new[]
            {
                Loc("a", "outer", "b", "c"), Loc("b", "center", "d"), Loc("c", "center", "d"), Loc("d", "deep"),
            }));
        }

        [Test]
        public void GetReturnsTheLocationOrThrowsForAnUnknownOrNullId()
        {
            var graph = new LocationGraph("a", new[] { Loc("a", "outer", "b"), Loc("b", "center") });

            Assert.AreEqual("center", graph.Get("b").ZoneId);
            Assert.Throws<ValidationException>(() => graph.Get("ghost"));
            Assert.Throws<ValidationException>(() => graph.Get(null));
        }

        [Test]
        public void ADeadEndLocationHasNoConnections()
        {
            var graph = new LocationGraph("a", new[] { Loc("a", "deep") });

            Assert.IsEmpty(graph.Get("a").ConnectsTo);
        }

        [Test]
        public void AMapStartsWithNoLocationsAndCanHaveAGraphAttached()
        {
            var zones = new[]
            {
                new Zone("outer", "Outer", "long", 0, null, null, null),
                new Zone("center", "Center", "medium", 0, null, null, null),
                new Zone("deep", "Deep", "medium", 0, null, null, null),
            };
            var map = new MapDefinition("x", "X", 30, "medium", zones);
            Assert.IsNull(map.Locations);

            var graph = new LocationGraph("a", new[] { Loc("a") });
            var withLocations = map.WithLocations(graph);

            Assert.AreSame(graph, withLocations.Locations);
            Assert.IsNull(map.Locations, "the original map is unchanged");
            Assert.AreEqual(map.MapId, withLocations.MapId);
            Assert.AreSame(map.Zones, withLocations.Zones);
        }
    }
}

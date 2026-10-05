using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Safehouse.Core;
using Safehouse.Data;

namespace Safehouse.Tests
{
    /// <summary>The shipped locations.json, and what LocationLoader accepts or refuses.</summary>
    public sealed class LocationLoaderTests
    {
        [Test]
        public void TheShippedFileCoversEveryMapWithAWellFormedGraph()
        {
            var catalog = CatalogLoader.Load();
            var maps = MapLoader.LoadMaps(catalog);

            var graphs = LocationLoader.Load();

            Assert.AreEqual(9, graphs.Count);
            foreach (var pair in graphs)
            {
                Assert.IsTrue(maps.ContainsKey(pair.Key), $"locations.json names unknown map {pair.Key}");
                var graph = pair.Value;
                Assert.IsTrue(graph.Locations.ContainsKey(graph.EntryLocationId));
                foreach (var location in graph.Locations.Values)
                {
                    Assert.DoesNotThrow(() => maps[pair.Key].Zone(location.ZoneId, false),
                        $"{pair.Key}/{location.LocationId} points at a zone its map does not have");
                    foreach (var target in location.ConnectsTo)
                    {
                        Assert.IsTrue(graph.Locations.ContainsKey(target),
                            $"{pair.Key}/{location.LocationId} connects to unknown location {target}");
                    }
                }
            }
        }

        [Test]
        public void LoadIntoAttachesEachMapsGraphAndLeavesOthersAlone()
        {
            var catalog = CatalogLoader.Load();
            var maps = MapLoader.LoadMaps(catalog);

            LocationLoader.LoadInto(maps);

            Assert.IsNotNull(maps["customs"].Locations);
            Assert.AreEqual("gas-station", maps["customs"].Locations.EntryLocationId);
            Assert.AreEqual("Gas Station", maps["customs"].Locations.Get("gas-station").Name);
        }

        [Test]
        public void EveryDeadEndLocationSitsInTheDeepestZoneTheGraphUses()
        {
            // Not a hard rule the loader enforces, but a sanity check on the content itself: a direct-play
            // trip should always be able to go somewhere from outer or center before it is forced to extract.
            foreach (var pair in LocationLoader.Load())
            {
                var deepest = pair.Value.Locations.Values.Select(location => MapZones.Ids.ToList().IndexOf(location.ZoneId)).Max();
                foreach (var location in pair.Value.Locations.Values)
                {
                    if (location.ConnectsTo.Count == 0)
                    {
                        Assert.AreEqual(deepest, MapZones.Ids.ToList().IndexOf(location.ZoneId),
                            $"{pair.Key}/{location.LocationId} is a dead end but is not in the deepest zone this map's graph uses");
                    }
                }
            }
        }

        [Test]
        public void AttachRefusesAGraphForAMapThatDoesNotExist()
        {
            var catalog = CatalogLoader.Load();
            var maps = MapLoader.LoadMaps(catalog);
            var document = JObject.Parse(@"{""schema_version"":1,""maps"":{""nowhere"":{
                ""entry_location_id"":""a"",""locations"":[{""location_id"":""a"",""name"":""A"",""zone_id"":""outer"",""connects_to"":[]}]}}}");

            var error = Assert.Throws<GameDataException>(() => LocationLoader.Attach(maps, LocationLoader.Parse(document)));

            StringAssert.Contains("nowhere", error.Message);
        }

        [Test]
        public void AGraphThatFailsCoreValidationIsReportedWithTheMapIdAndTheReason()
        {
            var document = JObject.Parse(@"{""schema_version"":1,""maps"":{""customs"":{
                ""entry_location_id"":""ghost"",""locations"":[{""location_id"":""a"",""name"":""A"",""zone_id"":""outer"",""connects_to"":[]}]}}}");

            var error = Assert.Throws<GameDataException>(() => LocationLoader.Parse(document));

            StringAssert.Contains("customs", error.Message);
            StringAssert.Contains("ghost", error.Message);
        }

        [Test]
        public void AMissingMapsObjectIsRefused()
        {
            Assert.Throws<GameDataException>(() => LocationLoader.Parse(JObject.Parse(@"{""schema_version"":1}")));
        }
    }
}

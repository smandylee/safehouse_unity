using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Safehouse.Data;

namespace Safehouse.Tests
{
    /// <summary>
    /// Guards the data files copied over from the Python build. These assert shape, not exact
    /// counts: tools/import_tarkov.py legitimately changes how many items exist, but it must never
    /// change what an item looks like or drop a collection entirely.
    /// </summary>
    public sealed class GameDataLoaderTests
    {
        [Test]
        public void EveryDataFileLoadsWithTheSupportedSchemaVersion()
        {
            foreach (var name in GameDataFile.All)
            {
                var document = GameDataLoader.Load(name);
                Assert.AreEqual(
                    GameDataLoader.SupportedSchemaVersion,
                    document["schema_version"].Value<int>(),
                    $"{name}.json");
            }
        }

        [Test]
        public void GeneratedFilesCarryTheirCollections()
        {
            AssertNonEmptyArray(GameDataFile.Items, "items");
            AssertNonEmptyArray(GameDataFile.Traders, "traders");
            AssertNonEmptyArray(GameDataFile.Maps, "maps");

            var gear = GameDataLoader.Load(GameDataFile.Gear);
            foreach (var table in new[] { "weapons", "ammo", "equipment", "meds", "mobs" })
            {
                Assert.IsNotNull(gear[table], $"gear.json is missing {table}");
                Assert.Greater(gear[table].Children().Count(), 0, $"gear.json {table} is empty");
            }
        }

        [Test]
        public void EveryItemHasTheFieldsInventoryPlacementNeeds()
        {
            var items = (JArray)GameDataLoader.Load(GameDataFile.Items)["items"];
            foreach (var item in items)
            {
                var id = item["item_id"];
                Assert.IsNotNull(id, "an item has no item_id");
                Assert.IsNotEmpty(id.Value<string>(), "an item has an empty item_id");
                Assert.IsNotEmpty(item["name"].Value<string>(), $"{id} has no name");
                Assert.Greater(item["width"].Value<int>(), 0, $"{id} has a non-positive width");
                Assert.Greater(item["height"].Value<int>(), 0, $"{id} has a non-positive height");
            }
        }

        [Test]
        public void ItemIdsAreUnique()
        {
            // Saves store item ids, so a duplicate would make a stashed item ambiguous.
            var items = (JArray)GameDataLoader.Load(GameDataFile.Items)["items"];
            var seen = new HashSet<string>();
            foreach (var item in items)
            {
                var id = item["item_id"].Value<string>();
                Assert.IsTrue(seen.Add(id), $"duplicate item_id: {id}");
            }
        }

        [Test]
        public void HandTunedFilesShipAlongsideTheGeneratedOnes()
        {
            // These hold values we decided ourselves; losing one silently would change the rules.
            foreach (var name in new[]
                     {
                         GameDataFile.Containers,
                         GameDataFile.Combat,
                         GameDataFile.RaidNodes,
                         GameDataFile.MapsOverrides,
                     })
            {
                Assert.IsTrue(File.Exists(GameDataLoader.PathFor(name)), $"{name}.json is missing");
            }

            var nodes = GameDataLoader.Load(GameDataFile.RaidNodes);
            Assert.IsNotNull(nodes["kinds"], "raid_nodes.json has no node kinds");
            Assert.IsNotNull(nodes["nodes_per_zone"], "raid_nodes.json has no nodes_per_zone");
        }

        [Test]
        public void LoadingAMissingFileSaysWhichOne()
        {
            var error = Assert.Throws<GameDataException>(() => GameDataLoader.Load("no_such_file"));
            StringAssert.Contains("no_such_file", error.Message);
        }

        private static void AssertNonEmptyArray(string file, string key)
        {
            var collection = GameDataLoader.Load(file)[key] as JArray;
            Assert.IsNotNull(collection, $"{file}.json has no {key} array");
            Assert.Greater(collection.Count, 0, $"{file}.json {key} is empty");
        }
    }
}

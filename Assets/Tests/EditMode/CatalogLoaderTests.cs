using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Safehouse.Core;
using Safehouse.Data;

namespace Safehouse.Tests
{
    /// <summary>Turning the shipped items.json into item definitions the placement rules accept.</summary>
    public sealed class CatalogLoaderTests
    {
        [Test]
        public void TheShippedCatalogLoads()
        {
            var catalog = CatalogLoader.Load();

            Assert.Greater(catalog.Count, 0);
            foreach (var pair in catalog)
            {
                Assert.AreEqual(pair.Key, pair.Value.ItemId, "catalog is keyed by item_id");
            }
        }

        [Test]
        public void EveryShippedItemFitsInAGridOfItsOwn()
        {
            // A definition the placement rules cannot place would be unreachable loot.
            var catalog = CatalogLoader.Load();
            var biggest = catalog.Values.OrderByDescending(item => item.Width * item.Height).First();
            var grid = new StashGrid(CoreLimits.MaxGrid, CoreLimits.MaxGrid);

            Assert.IsNotNull(PlacementRules.FirstFit(grid, catalog, biggest.ItemId),
                $"{biggest.ItemId} ({biggest.Width}x{biggest.Height}) does not fit anywhere");
        }

        [Test]
        public void ADuplicateItemIdIsRejected()
        {
            var document = JObject.Parse(@"{
                ""schema_version"": 1,
                ""items"": [
                    {""item_id"": ""x"", ""name"": ""X"", ""category"": ""Misc"", ""width"": 1,
                     ""height"": 1, ""base_value"": 1, ""rarity"": ""Common"", ""weight"": 0.1},
                    {""item_id"": ""x"", ""name"": ""X again"", ""category"": ""Misc"", ""width"": 1,
                     ""height"": 1, ""base_value"": 1, ""rarity"": ""Common"", ""weight"": 0.1}
                ]}");

            var error = Assert.Throws<GameDataException>(() => CatalogLoader.Parse(document));
            StringAssert.Contains("Duplicate item_id", error.Message);
        }

        [Test]
        public void AnEmptyCatalogIsRejected()
        {
            var document = JObject.Parse(@"{""schema_version"": 1, ""items"": []}");

            Assert.Throws<GameDataException>(() => CatalogLoader.Parse(document));
        }

        [Test]
        public void ABadRowNamesTheItemItCameFrom()
        {
            var document = JObject.Parse(@"{
                ""schema_version"": 1,
                ""items"": [
                    {""item_id"": ""broken-item"", ""name"": ""Broken"", ""category"": ""Misc"",
                     ""width"": 0, ""height"": 1, ""base_value"": 1, ""rarity"": ""Common"",
                     ""weight"": 0.1}
                ]}");

            var error = Assert.Throws<GameDataException>(() => CatalogLoader.Parse(document));
            StringAssert.Contains("broken-item", error.Message);
        }
    }
}

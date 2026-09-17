using System.Linq;
using NUnit.Framework;
using Safehouse.Core;
using Safehouse.Data;
using Safehouse.UI.Sample;

namespace Safehouse.Tests
{
    /// <summary>The demo stash the GEAR screen renders - real items, placed by the real rules.</summary>
    public sealed class SampleStashTests
    {
        [Test]
        public void EveryPlacedItemComesFromTheShippedCatalog()
        {
            var catalog = CatalogLoader.Load();
            var stash = SampleStash.Build(catalog);

            Assert.Greater(stash.Stash.Count, 0);
            foreach (var instance in stash.Stash)
            {
                Assert.IsTrue(catalog.ContainsKey(instance.ItemId), instance.ItemId);
            }
        }

        [Test]
        public void NoTwoPlacedItemsOverlap()
        {
            var catalog = CatalogLoader.Load();
            var stash = SampleStash.Build(catalog);

            // ValidateGrid throws on the first overlap or out-of-bounds placement it finds.
            Assert.DoesNotThrow(() => PlacementRules.ValidateGrid(stash, catalog));
        }

        [Test]
        public void BuildingTheSameStashTwiceGivesTheSamePlacements()
        {
            var catalog = CatalogLoader.Load();
            var first = SampleStash.Build(catalog);
            var second = SampleStash.Build(catalog);

            var firstPlacements = first.Stash.Select(i => (i.ItemId, i.X, i.Y, i.Rotation)).ToList();
            var secondPlacements = second.Stash.Select(i => (i.ItemId, i.X, i.Y, i.Rotation)).ToList();
            CollectionAssert.AreEqual(firstPlacements, secondPlacements);
        }
    }
}

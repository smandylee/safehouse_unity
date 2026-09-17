using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Safehouse.Core;

namespace Safehouse.Tests
{
    /// <summary>
    /// The placement rules ported from the Python inventory.py. These are the foundation the stash,
    /// the hideout, loadouts and loot delivery all sit on, so they are checked on their own here.
    /// </summary>
    public sealed class PlacementRulesTests
    {
        private static readonly Dictionary<string, ItemDefinition> Catalog = new Dictionary<string, ItemDefinition>
        {
            ["bandage"] = ItemDefinition.Create("bandage", "Bandage", "Meds", 1, 1, 100, "Common", 0.1),
            ["rifle"] = ItemDefinition.Create("rifle", "Rifle", "Weapons", 4, 1, 20000, "Rare", 3.5),
            ["case"] = ItemDefinition.Create("case", "Item case", "Containers", 2, 2, 5000, "Rare", 2.0),
        };

        private static int _nextId;

        /// <summary>Instance ids are 32 hex characters; tests only need them to be distinct.</summary>
        private static string NewInstanceId() => (++_nextId).ToString("x32");

        private static ItemInstance At(string itemId, int x, int y, int rotation = 0) =>
            ItemInstance.Create(NewInstanceId(), itemId, x, y, rotation);

        private static StashGrid Grid(int width, int height, params ItemInstance[] stash) =>
            new StashGrid(width, height, stash);

        [Test]
        public void RotatingSwapsWidthAndHeight()
        {
            PlacementRules.Footprint(Catalog["rifle"], 0, out var width, out var height);
            Assert.AreEqual((4, 1), (width, height));

            PlacementRules.Footprint(Catalog["rifle"], 90, out width, out height);
            Assert.AreEqual((1, 4), (width, height));
        }

        [Test]
        public void AnyRotationOtherThanZeroOrNinetyIsRejected()
        {
            Assert.Throws<ValidationException>(
                () => PlacementRules.Footprint(Catalog["rifle"], 45, out _, out _));
        }

        [Test]
        public void AnItemHangingOffTheEdgeIsRefused()
        {
            var grid = Grid(4, 4);
            Assert.IsNull(PlacementRules.PlacementError(grid, Catalog, "rifle", 0, 0));
            StringAssert.Contains("outside the stash",
                PlacementRules.PlacementError(grid, Catalog, "rifle", 1, 0));
        }

        [Test]
        public void RotationCanMakeAnItemFitWhereItOtherwiseWouldNot()
        {
            // A 4x1 rifle does not fit across a 2-wide grid, but stood on end it does.
            var grid = Grid(2, 6);
            StringAssert.Contains("outside the stash",
                PlacementRules.PlacementError(grid, Catalog, "rifle", 0, 0));
            Assert.IsTrue(PlacementRules.CanPlace(grid, Catalog, "rifle", 0, 0, rotation: 90));
        }

        [Test]
        public void OverlappingAnExistingItemIsRefused()
        {
            var grid = Grid(6, 6, At("case", 1, 1));

            Assert.AreEqual("That space is occupied.",
                PlacementRules.PlacementError(grid, Catalog, "bandage", 2, 2));
            // Touching along an edge is not overlapping.
            Assert.IsNull(PlacementRules.PlacementError(grid, Catalog, "bandage", 3, 1));
        }

        [Test]
        public void AnItemDoesNotCollideWithWhereItAlreadySits()
        {
            var moving = At("case", 1, 1);
            var grid = Grid(6, 6, moving);

            Assert.AreEqual("That space is occupied.",
                PlacementRules.PlacementError(grid, Catalog, "case", 2, 2));
            Assert.IsNull(PlacementRules.PlacementError(grid, Catalog, "case", 2, 2,
                ignoreInstanceId: moving.InstanceId));
        }

        [Test]
        public void PlacingAnUnknownItemSaysSoInsteadOfThrowing()
        {
            var error = PlacementRules.PlacementError(Grid(4, 4), Catalog, "no-such-item", 0, 0);
            StringAssert.Contains("Unknown item type", error);
        }

        [Test]
        public void FirstFitScansRowsLeftToRightAndPrefersTheUnrotatedShape()
        {
            var grid = Grid(6, 6, At("bandage", 0, 0));

            var spot = PlacementRules.FirstFit(grid, Catalog, "rifle");

            Assert.IsNotNull(spot);
            // Row 0 has one cell taken at x=0, leaving only 5 columns from x=1 - wide enough.
            Assert.AreEqual((1, 0, 0), (spot.Value.X, spot.Value.Y, spot.Value.Rotation));
        }

        [Test]
        public void FirstFitRotatesOnlyWhenTheOriginalOrientationNeverFits()
        {
            var spot = PlacementRules.FirstFit(Grid(2, 6), Catalog, "rifle");

            Assert.IsNotNull(spot);
            Assert.AreEqual(90, spot.Value.Rotation);
        }

        [Test]
        public void FirstFitReturnsNothingWhenTheGridIsFull()
        {
            var filled = Enumerable.Range(0, 4)
                .SelectMany(y => Enumerable.Range(0, 4).Select(x => At("bandage", x, y)))
                .ToArray();

            Assert.IsNull(PlacementRules.FirstFit(Grid(4, 4, filled), Catalog, "bandage"));
        }

        [Test]
        public void FirstFitNeverReturnsASpotThatPlacementWouldRefuse()
        {
            // The two have to agree, or loot delivery would pick a spot the stash then rejects.
            var grid = Grid(5, 5, At("case", 0, 0), At("bandage", 4, 0), At("bandage", 2, 3));

            var spot = PlacementRules.FirstFit(grid, Catalog, "rifle");

            Assert.IsNotNull(spot);
            Assert.IsNull(PlacementRules.PlacementError(grid, Catalog, "rifle",
                spot.Value.X, spot.Value.Y, spot.Value.Rotation));
            // The exact spot the Python inventory.first_fit returns for this grid. Pinned so a
            // rewrite that still "fits somewhere" cannot quietly change where loot lands.
            Assert.AreEqual((0, 2, 0), (spot.Value.X, spot.Value.Y, spot.Value.Rotation));
        }

        [Test]
        public void ItemAtFindsEveryCellAnItemCoversAndNothingElse()
        {
            var placed = At("case", 1, 1);
            var grid = Grid(6, 6, placed);

            foreach (var (x, y) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) })
            {
                Assert.AreSame(placed, PlacementRules.ItemAt(grid, Catalog, x, y), $"{x},{y}");
            }

            Assert.IsNull(PlacementRules.ItemAt(grid, Catalog, 0, 0));
            Assert.IsNull(PlacementRules.ItemAt(grid, Catalog, 3, 3));
        }

        [Test]
        public void ItemAtRespectsRotation()
        {
            var grid = Grid(6, 6, At("rifle", 0, 0, rotation: 90));

            Assert.IsNotNull(PlacementRules.ItemAt(grid, Catalog, 0, 3));
            Assert.IsNull(PlacementRules.ItemAt(grid, Catalog, 3, 0));
        }

        [Test]
        public void ValidateGridAcceptsALegitimateLayout()
        {
            var grid = Grid(6, 6, At("case", 0, 0), At("rifle", 2, 0), At("bandage", 0, 2));

            Assert.DoesNotThrow(() => PlacementRules.ValidateGrid(grid, Catalog));
        }

        [Test]
        public void ValidateGridRejectsOverlapAndNamesTheItem()
        {
            var grid = Grid(6, 6, At("case", 0, 0), At("bandage", 1, 1));

            var error = Assert.Throws<ValidationException>(
                () => PlacementRules.ValidateGrid(grid, Catalog));
            StringAssert.Contains("bandage", error.Message);
            StringAssert.Contains("occupied", error.Message);
        }

        [Test]
        public void ValidateGridRejectsADuplicateInstanceId()
        {
            var id = NewInstanceId();
            var grid = Grid(6, 6,
                ItemInstance.Create(id, "bandage", 0, 0),
                ItemInstance.Create(id, "bandage", 1, 0));

            var error = Assert.Throws<ValidationException>(
                () => PlacementRules.ValidateGrid(grid, Catalog));
            StringAssert.Contains("Duplicate instance ID", error.Message);
        }

        [Test]
        public void ValidateGridCountsAlreadyTakenIdsSuchAsWornItems()
        {
            // An item cannot be worn and lying in the stash at the same time.
            var worn = NewInstanceId();
            var grid = Grid(6, 6, ItemInstance.Create(worn, "bandage", 0, 0));

            Assert.DoesNotThrow(() => PlacementRules.ValidateGrid(grid, Catalog));
            Assert.Throws<ValidationException>(() =>
                PlacementRules.ValidateGrid(grid, Catalog, new HashSet<string> { worn }));
        }

        [Test]
        public void ValidateGridRejectsAnItemMissingFromTheCatalog()
        {
            var grid = Grid(6, 6, At("bandage", 0, 0));
            var withoutBandage = Catalog.Where(pair => pair.Key != "bandage")
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            var error = Assert.Throws<ValidationException>(
                () => PlacementRules.ValidateGrid(grid, withoutBandage));
            StringAssert.Contains("Unknown item type", error.Message);
        }

        [Test]
        public void StashTotalsAddUpCellsWeightAndValue()
        {
            var grid = Grid(8, 8, At("case", 0, 0), At("rifle", 0, 3), At("bandage", 5, 0));

            PlacementRules.StashTotals(grid, Catalog, out var cells, out var weight, out var value);

            Assert.AreEqual(2 * 2 + 4 * 1 + 1 * 1, cells);
            Assert.AreEqual(2.0 + 3.5 + 0.1, weight, 1e-9);
            Assert.AreEqual(5000 + 20000 + 100, value);
        }

        [Test]
        public void AnEmptyGridHasNothingInIt()
        {
            var grid = Grid(4, 4);

            PlacementRules.StashTotals(grid, Catalog, out var cells, out var weight, out var value);

            Assert.AreEqual(0, cells);
            Assert.AreEqual(0, weight, 1e-9);
            Assert.AreEqual(0, value);
            Assert.IsNull(PlacementRules.ItemAt(grid, Catalog, 0, 0));
            Assert.DoesNotThrow(() => PlacementRules.ValidateGrid(grid, Catalog));
        }
    }
}

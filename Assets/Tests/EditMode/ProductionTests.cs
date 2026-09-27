using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Safehouse.Core;
using Safehouse.Data;

namespace Safehouse.Tests
{
    /// <summary>Hideout production: recipes, inventory manipulation, starting/collecting jobs.</summary>
    public sealed class ProductionTests
    {
        private static Dictionary<string, ItemDefinition> _catalog;
        private static Dictionary<string, ItemDefinition> Catalog => _catalog ?? (_catalog = CatalogLoader.Load());
        private static RecipeBook _recipes;
        private static RecipeBook Recipes => _recipes ?? (_recipes = RecipeLoader.LoadForCatalog(Catalog));
        private static HideoutDefinition _definition;
        private static HideoutDefinition Definition => _definition ?? (_definition = HideoutLoader.Load());

        private static int _next;
        private static string NewId() => (++_next).ToString("x32");
        private static string NewInstanceId() => Guid.NewGuid().ToString("N");

        [Test]
        public void RecipesLoadAndReferenceValidItems()
        {
            Assert.IsNotEmpty(Recipes.Recipes);
            Assert.That(Recipes.Recipes.Any(r => r.FacilityId == "medstation"));
            Assert.That(Recipes.Recipes.Any(r => r.Category == RecipeCategory.Repeating));
            Assert.That(Recipes.Recipes.Any(r => r.Category == RecipeCategory.Random));
        }

        [Test]
        public void InventoryRulesRemoveItemsByType()
        {
            var profile = ProfileWithItems("duct-tape", 3);

            var after = InventoryRules.RemoveItems(profile.Stash, Catalog, "duct-tape", 2);

            Assert.AreEqual(1, after.Stash.Count(i => i.ItemId == "duct-tape"));
        }

        [Test]
        public void InventoryRulesRefuseToRemoveMoreThanAvailable()
        {
            var profile = ProfileWithItems("duct-tape", 1);

            Assert.Throws<ValidationException>(() =>
                InventoryRules.RemoveItems(profile.Stash, Catalog, "duct-tape", 2));
        }

        [Test]
        public void InventoryRulesAddItemsToFirstFreeSpot()
        {
            var profile = Profile.CreateNew(NewId(), "Ana");

            var after = InventoryRules.AddItems(profile.Stash, Catalog, "duct-tape", 2);

            Assert.AreEqual(2, after.Stash.Count(i => i.ItemId == "duct-tape"));
        }

        [Test]
        public void ProductionStartConsumesInputsAndCreatesJob()
        {
            var profile = ProfileWithItems("duct-tape", 1, "toilet-paper", 1);
            var account = AccountWithFacility("medstation", 1);
            var recipe = Recipes.Get("medstation-bandage");
            var now = 1000.0;

            var (paidProfile, withJob) = ProductionRules.Start(recipe, profile, account, Definition, Recipes, now, Catalog);

            Assert.AreEqual(0, paidProfile.Stash.Stash.Count(i => i.ItemId == "duct-tape"));
            Assert.AreEqual(1, withJob.ProductionJobs.Count);
            Assert.AreEqual(recipe.RecipeId, withJob.ProductionJobs[0].RecipeId);
        }

        [Test]
        public void ProductionCollectProducesOutputs()
        {
            var profile = ProfileWithItems("duct-tape", 1, "toilet-paper", 1);
            var account = AccountWithFacility("medstation", 1);
            var recipe = Recipes.Get("medstation-bandage");
            var now = 1000.0;
            var (paidProfile, withJob) = ProductionRules.Start(recipe, profile, account, Definition, Recipes, now, Catalog);
            var readyTime = now + ProductionRules.Duration(recipe, Definition, withJob.Hideout) * 3600.0 + 1.0;

            var (withOutput, collected) = ProductionRules.Collect(recipe, withJob.ProductionJobs[0],
                paidProfile, withJob, Definition, Recipes, Catalog, readyTime, new Random(1));

            Assert.AreEqual(2, withOutput.Stash.Stash.Count(i => i.ItemId == "aseptic-bandage"));
            Assert.IsEmpty(collected.ProductionJobs);
        }

        [Test]
        public void ProductionBitcoinRepeats()
        {
            var profile = ProfileWithItems("graphics-card", 2);
            var account = AccountWithFacility("bitcoin_farm", 1);
            var recipe = Recipes.Get("bitcoin-farm");
            var now = 1000.0;

            var (paidProfile, withJob) = ProductionRules.Start(recipe, profile, account, Definition, Recipes, now, Catalog, gpuCount: 2);
            var durationSeconds = ProductionRules.Duration(recipe, Definition, withJob.Hideout, 2) * 3600.0;
            var readyTime = now + durationSeconds + 1.0;

            var (withOutput, cycled) = ProductionRules.Collect(recipe, withJob.ProductionJobs[0],
                paidProfile, withJob, Definition, Recipes, Catalog, readyTime, new Random(1));

            Assert.AreEqual(1, withOutput.Stash.Stash.Count(i => i.ItemId == "physical-bitcoin"));
            Assert.AreEqual(1, cycled.ProductionJobs.Count, "repeating job starts the next cycle");
            Assert.AreEqual(readyTime, cycled.ProductionJobs[0].StartTime, 1.0,
                "next cycle starts roughly when the previous one was collected");
        }

        [Test]
        public void ProductionRandomUsesLootTable()
        {
            var profile = ProfileWithItems("crickent-lighter", 1);
            var account = AccountWithFacility("scav_case", 1);
            var recipe = Recipes.Get("scav-case");
            var now = 1000.0;
            var (paidProfile, withJob) = ProductionRules.Start(recipe, profile, account, Definition, Recipes, now, Catalog);
            var readyTime = now + ProductionRules.Duration(recipe, Definition, withJob.Hideout) * 3600.0 + 1.0;

            var (withOutput, collected) = ProductionRules.Collect(recipe, withJob.ProductionJobs[0],
                paidProfile, withJob, Definition, Recipes, Catalog, readyTime, new Random(42));

            Assert.IsNotEmpty(withOutput.Stash.Stash);
            Assert.IsEmpty(collected.ProductionJobs);
        }

        [Test]
        public void ProductionNeedsGeneratorFuel()
        {
            var profile = ProfileWithItems("duct-tape", 1, "toilet-paper", 1);
            var emptyFuel = new Account(
                Hideout.CreateNew().WithFacility(new Facility("medstation", 1)).WithFuel(0.0),
                new[] { NewId() });
            var recipe = Recipes.Get("medstation-bandage");

            var error = ProductionRules.CanStartError(recipe, profile, emptyFuel, Definition, Recipes, Catalog);

            StringAssert.Contains("generator", error.ToLowerInvariant());
        }

        [Test]
        public void AccountRoundTripKeepsProductionJobs()
        {
            var id = NewId();
            var job = new ProductionJob("medstation-bandage", 1234.0, id, 0);
            var account = new Account(Hideout.CreateNew(), new[] { id }, productionJobs: new[] { job });

            var back = AccountSerializer.FromJson(
                AccountSerializer.Parse(System.Text.Encoding.UTF8.GetString(AccountSerializer.Serialize(account))));

            Assert.AreEqual(1, back.ProductionJobs.Count);
            Assert.AreEqual(job.RecipeId, back.ProductionJobs[0].RecipeId);
            Assert.AreEqual(job.StartTime, back.ProductionJobs[0].StartTime, 0.0001);
        }

        private static Profile ProfileWithItems(params object[] pairs)
        {
            var profile = Profile.CreateNew(NewId(), "Ana");
            var stash = profile.Stash;
            for (var i = 0; i < pairs.Length; i += 2)
            {
                var itemId = (string)pairs[i];
                var count = (int)pairs[i + 1];
                stash = InventoryRules.AddItems(stash, Catalog, itemId, count);
            }

            return profile.With(stash: stash);
        }

        private static Account AccountWithFacility(string facilityId, int level)
        {
            var hideout = Hideout.CreateNew().WithFacility(new Facility(facilityId, level));
            return new Account(hideout, new[] { NewId() });
        }
    }
}

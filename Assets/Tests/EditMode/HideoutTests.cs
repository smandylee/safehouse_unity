using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Safehouse.Core;
using Safehouse.Data;

namespace Safehouse.Tests
{
    /// <summary>The hideout model, account JSON, population/stash rules, and hideout data loading.</summary>
    public sealed class HideoutTests
    {
        private static int _next;
        private static string NewId() => (++_next).ToString("x32");

        private static HideoutDefinition Definition => _definition ?? (_definition = HideoutLoader.Load());
        private static HideoutDefinition _definition;

        [Test]
        public void HideoutDataDefinesEverySharedFacilityAndAPersonalRoom()
        {
            CollectionAssert.AreEquivalent(Hideout.SharedFacilityIds,
                Definition.Facilities.Keys.Where(id => id != "personal_room"));
            Assert.IsTrue(Definition.HasFacility("personal_room"));
            Assert.AreEqual(19, Hideout.SharedFacilityIds.Count);
        }

        [Test]
        public void StatBoostingFacilitiesAreMarkedDecorative()
        {
            Assert.IsTrue(HideoutRules.IsDecorative(Definition, "air_filtering_unit"));
            Assert.IsTrue(HideoutRules.IsDecorative(Definition, "library"));
            Assert.IsTrue(HideoutRules.IsDecorative(Definition, "shooting_range"));
            Assert.IsFalse(HideoutRules.IsDecorative(Definition, "generator"));
            Assert.IsFalse(HideoutRules.IsDecorative(Definition, "medstation"));
        }

        [Test]
        public void ANewHideoutHasEveryFacilityPresentAndOnlyGeneratorBuilt()
        {
            var hideout = Hideout.CreateNew();

            CollectionAssert.AreEquivalent(Hideout.SharedFacilityIds, hideout.Facilities.Select(f => f.FacilityId));
            Assert.AreEqual(19, hideout.Facilities.Count, "every Tarkov shared facility except the personal stash");
            Assert.AreEqual(1, hideout.Facilities.Single(f => f.FacilityId == "generator").Level);
            Assert.IsTrue(hideout.Facilities.Where(f => f.FacilityId != "generator").All(f => f.Level == 0));
        }

        [Test]
        public void FacilityIdsMustBeUnique()
        {
            Assert.Throws<ValidationException>(() => new Hideout(new[]
            {
                new Facility("generator", 1),
                new Facility("generator", 2),
            }));
        }

        [Test]
        public void FacilityLevelsAreBounded()
        {
            Assert.DoesNotThrow(() => new Facility("generator", HideoutLimits.MaxFacilityLevel));
            Assert.Throws<ValidationException>(() => new Facility("generator", HideoutLimits.MaxFacilityLevel + 1));
            Assert.Throws<ValidationException>(() => new Facility("generator", -1));
        }

        [Test]
        public void ReplacingAFacilityKeepsTheOthers()
        {
            var hideout = Hideout.CreateNew().WithFacility(new Facility("generator", 3));

            Assert.AreEqual(3, hideout.LevelOf("generator"));
            Assert.AreEqual(0, hideout.LevelOf("workbench"));
        }

        [Test]
        public void ANewAccountHasAnEmptyCharacterOrder()
        {
            var account = Account.CreateNew();

            Assert.AreEqual(0, account.Population);
            Assert.AreEqual(0, account.CharacterOrder.Count);
            Assert.AreEqual(1, account.Hideout.LevelOf("generator"));
        }

        [Test]
        public void AccountCharacterOrderMustBeUniqueValidIds()
        {
            var id = NewId();
            Assert.DoesNotThrow(() => new Account(Hideout.CreateNew(), new[] { id }));
            Assert.Throws<ValidationException>(() => new Account(Hideout.CreateNew(), new[] { id, id }));
            Assert.Throws<ValidationException>(() => new Account(Hideout.CreateNew(), new[] { "not-a-valid-id" }));
        }

        [Test]
        public void AccountUnknownKeysAreIgnoredAndDroppedOnTheNextWrite()
        {
            var document = AccountSerializer.ToJson(Account.CreateNew());
            document["from_the_future"] = "hello";

            var account = AccountSerializer.FromJson(document);

            StringAssert.DoesNotContain("from_the_future", Encoding.UTF8.GetString(AccountSerializer.Serialize(account)));
        }

        [Test]
        public void GeneratorFuelScalesWithPopulationAndLevel()
        {
            var hideout = Hideout.CreateNew();

            Assert.AreEqual(0, HideoutRules.GeneratorFuelPerHour(Definition, hideout, 0));
            Assert.AreEqual(10, HideoutRules.GeneratorFuelPerHour(Definition, hideout, 1));
            Assert.AreEqual(40, HideoutRules.GeneratorFuelPerHour(Definition, hideout, 4));

            var levelTwo = hideout.WithFacility(new Facility("generator", 2));
            Assert.AreEqual(20, HideoutRules.GeneratorFuelPerHour(Definition, levelTwo, 4));
        }

        [Test]
        public void TickFuelConsumesFuelBasedOnPopulationAndGeneratorLevel()
        {
            var hideout = Hideout.CreateNew().WithFuel(100.0);

            var after = HideoutRules.TickFuel(Definition, hideout, 2, 5.0);

            Assert.AreEqual(0.0, after.Fuel, 0.0001, "2 people * 10/h * 5h = 100");
        }

        [Test]
        public void TickFuelNeverGoesBelowZero()
        {
            var hideout = Hideout.CreateNew().WithFuel(10.0);

            var after = HideoutRules.TickFuel(Definition, hideout, 1, 2.0);

            Assert.AreEqual(0.0, after.Fuel, 0.0001, "cannot consume more fuel than stored");
        }

        [Test]
        public void TickFuelDoesNothingWhenTimeDoesNotAdvance()
        {
            var hideout = Hideout.CreateNew().WithFuel(50.0);

            Assert.AreSame(hideout, HideoutRules.TickFuel(Definition, hideout, 4, 0.0));
            Assert.AreSame(hideout, HideoutRules.TickFuel(Definition, hideout, 4, -1.0));
        }

        [Test]
        public void GeneratorRunningRequiresFuelAndPopulation()
        {
            var hideout = Hideout.CreateNew().WithFuel(10.0);

            Assert.IsTrue(HideoutRules.IsGeneratorRunning(Definition, hideout, 1));
            Assert.IsFalse(HideoutRules.IsGeneratorRunning(Definition, hideout, 0));

            var empty = hideout.WithFuel(0.0);
            Assert.IsFalse(HideoutRules.IsGeneratorRunning(Definition, empty, 1));

            var noGenerator = hideout.WithFacility(new Facility("generator", 0));
            Assert.IsFalse(HideoutRules.IsGeneratorRunning(Definition, noGenerator, 1));
        }

        [Test]
        public void SolarPowerReducesGeneratorFuelConsumption()
        {
            var hideout = Hideout.CreateNew().WithFacility(new Facility("solar_power", 1));

            var fuel = HideoutRules.GeneratorFuelPerHour(Definition, hideout, 4);

            Assert.AreEqual(28, fuel); // 40 * 0.7 = 28
        }

        [Test]
        public void RoomLevelGivesStashSizeFromData()
        {
            Assert.AreEqual((10, 20), HideoutRules.RoomStashSize(Definition, 1));
            Assert.AreEqual((12, 24), HideoutRules.RoomStashSize(Definition, 2));
            Assert.AreEqual((14, 28), HideoutRules.RoomStashSize(Definition, 3));
        }

        [Test]
        public void RoomLevelIsDerivedFromStashSize()
        {
            Assert.AreEqual(1, HideoutRules.RoomLevelForStash(Definition, 10, 20));
            Assert.AreEqual(2, HideoutRules.RoomLevelForStash(Definition, 12, 24));
            Assert.AreEqual(1, HideoutRules.RoomLevelForStash(Definition, 11, 22));
        }

        [Test]
        public void UpgradeRequirementsAreChecked()
        {
            var hideout = Hideout.CreateNew();

            Assert.IsFalse(HideoutRules.CanUpgrade(Definition, hideout, "workbench", 2),
                "workbench level 2 needs generator level 2");
            Assert.IsFalse(HideoutRules.CanUpgrade(Definition, hideout, "solar_power", 1),
                "solar power needs generator level 2 and workbench level 2");

            var generatorUpgraded = hideout.WithFacility(new Facility("generator", 2));
            Assert.IsTrue(HideoutRules.CanUpgrade(Definition, generatorUpgraded, "workbench", 2));

            var fullyUpgraded = generatorUpgraded.WithFacility(new Facility("workbench", 2));
            Assert.IsTrue(HideoutRules.CanUpgrade(Definition, fullyUpgraded, "solar_power", 1));
        }

        [Test]
        public void UpgradeCostIsReadFromData()
        {
            var hideout = Hideout.CreateNew();

            var levelOneCost = HideoutRules.UpgradeCost(Definition, hideout, "workbench", 1);
            Assert.AreEqual(25000, levelOneCost.Money);

            var levelTwoCost = HideoutRules.UpgradeCost(Definition, hideout, "workbench", 2);
            Assert.AreEqual(100000, levelTwoCost.Money);

            Assert.IsNull(HideoutRules.UpgradeCost(Definition, hideout, "workbench", 0));
        }

        [Test]
        public void UpgradeErrorExplainsWhy()
        {
            var profile = Profile.CreateNew(NewId(), "Ana", money: 1000);
            var hideout = Hideout.CreateNew();

            var error = HideoutRules.UpgradeError(Definition, hideout, profile, "solar_power", 1);
            StringAssert.Contains("Requires", error);

            var missingRequirement = HideoutRules.UpgradeError(Definition, hideout, profile, "workbench", 2);
            StringAssert.Contains("Generator", missingRequirement);

            var affordable = Profile.CreateNew(NewId(), "Rich", money: 10_000_000);
            var generatorUpgraded = hideout.WithFacility(new Facility("generator", 2));
            var noRequirements = HideoutRules.UpgradeError(Definition, generatorUpgraded, affordable, "workbench", 2);
            Assert.IsNull(noRequirements);

            var missingMoney = HideoutRules.UpgradeError(Definition, generatorUpgraded, profile, "workbench", 2);
            StringAssert.Contains("Needs", missingMoney);
        }

        [Test]
        public void EffectQueriesReturnDefaultsWhenNotBuilt()
        {
            var hideout = new Hideout(Enumerable.Empty<Facility>());

            Assert.AreEqual(0, HideoutRules.IntEffect(Definition, hideout, "workbench", "crafting_slots"));
            Assert.AreEqual(0.0, HideoutRules.DoubleEffect(Definition, hideout, "rest_space", "hp_regen_per_hour"));
            Assert.IsEmpty(HideoutRules.StringListEffect(Definition, hideout, "workbench", "crafting_categories"));
        }

        [Test]
        public void StashGridCanBeResizedWhileKeepingItems()
        {
            var catalog = CatalogLoader.Load();
            var item = catalog.First().Value;
            var instance = ItemInstance.Create(Guid.NewGuid().ToString("N"), item.ItemId, 0, 0);
            var grid = new StashGrid(10, 20, new[] { instance });

            var bigger = grid.WithSize(12, 24);

            Assert.AreEqual(12, bigger.StashWidth);
            Assert.AreEqual(24, bigger.StashHeight);
            Assert.AreEqual(instance.InstanceId, bigger.Stash.Single().InstanceId);
        }

        [Test]
        public void EnsureRoomStashSizeExpandsStashToMatchRoomLevel()
        {
            var profile = Profile.CreateNew(NewId(), "Ana").With(room: new PersonalRoom(2));

            var adjusted = HideoutRules.EnsureRoomStashSize(Definition, profile);

            Assert.AreEqual(12, adjusted.Stash.StashWidth);
            Assert.AreEqual(24, adjusted.Stash.StashHeight);
            Assert.AreSame(adjusted, HideoutRules.EnsureRoomStashSize(Definition, adjusted),
                "already large enough should return the same profile");
        }

        [Test]
        public void ProfileRoomSurvivesRoundTrip()
        {
            var profile = Profile.CreateNew(NewId(), "Ana").With(room: new PersonalRoom(3));

            var back = ProfileSerializer.FromJson(ProfileMigrations.Upgrade(
                ProfileSerializer.Parse(Encoding.UTF8.GetString(ProfileSerializer.Serialize(profile)))));

            Assert.AreEqual(3, back.Room.Level);
        }

        [Test]
        public void AccountSurvivesAWriteAndARead()
        {
            var id = NewId();
            var original = new Account(
                Hideout.CreateNew().WithFacility(new Facility("generator", 2)).WithFuel(42.5),
                new[] { id },
                lastFuelTick: 1234.5);

            var bytes = AccountSerializer.Serialize(original);
            var back = AccountSerializer.FromJson(
                AccountSerializer.Parse(Encoding.UTF8.GetString(bytes)));

            Assert.AreEqual(original.CharacterOrder.Single(), back.CharacterOrder.Single());
            Assert.AreEqual(2, back.Hideout.LevelOf("generator"));
            Assert.AreEqual(0, back.Hideout.LevelOf("workbench"));
            Assert.AreEqual(42.5, back.Hideout.Fuel, 0.0001);
            Assert.AreEqual(1234.5, back.LastFuelTick, 0.0001);
            Assert.AreEqual(AccountSerializer.Serialize(original), AccountSerializer.Serialize(back));
        }

        [Test]
        public void OldSchemaSevenGetsADefaultRoom()
        {
            var document = ProfileSerializer.ToJson(Profile.CreateNew(NewId(), "Ana"));
            document["schema_version"] = 7;
            document.Remove("personal_room");

            var upgraded = ProfileMigrations.Upgrade(document);
            var profile = ProfileSerializer.FromJson(upgraded);

            Assert.AreEqual(9, upgraded["schema_version"].Value<int>());
            Assert.AreEqual(1, profile.Room.Level);
            Assert.AreEqual("", profile.Gundog.MainClass);
        }

        // ---- repository ----

        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "safehouse-account-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }

        [Test]
        public void AccountRepositoryCreatesAndLoads()
        {
            var repository = new AccountRepository(_folder);
            var account = Account.CreateNew().With(characterOrder: new[] { NewId() });
            repository.Save(account);

            var loaded = repository.Load();

            Assert.AreEqual(account.CharacterOrder.Single(), loaded.CharacterOrder.Single());
            Assert.IsTrue(repository.Exists);
        }

        [Test]
        public void AccountRepositoryRefusesToOverwriteUnreadableFile()
        {
            var repository = new AccountRepository(_folder);
            repository.Save(Account.CreateNew());
            var path = Path.Combine(_folder, "account.json");
            File.WriteAllText(path, "not json");

            var error = Assert.Throws<StorageException>(() => repository.Save(Account.CreateNew()));
            StringAssert.Contains("cannot be read", error.Message);
        }

        [Test]
        public void AccountRepositoryRecoversFromBackup()
        {
            var repository = new AccountRepository(_folder);
            var account = Account.CreateNew().With(characterOrder: new[] { NewId() });
            repository.Save(account);
            repository.Save(account); // second write creates the .bak

            var path = Path.Combine(_folder, "account.json");
            File.WriteAllText(path, "not json");

            var notices = new List<string>();
            var loaded = repository.Load(notices);

            Assert.AreEqual(account.CharacterOrder.Single(), loaded.CharacterOrder.Single());
            Assert.IsNotEmpty(notices);
        }
    }
}

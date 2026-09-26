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
    /// <summary>The hideout model, account JSON, and population/stash rules.</summary>
    public sealed class HideoutTests
    {
        private static int _next;
        private static string NewId() => (++_next).ToString("x32");

        [Test]
        public void ANewHideoutHasEveryTarkovSharedFacilityAtLevelOne()
        {
            var hideout = Hideout.CreateNew();

            CollectionAssert.AreEquivalent(Hideout.SharedFacilityIds, hideout.Facilities.Select(f => f.FacilityId));
            Assert.AreEqual(19, hideout.Facilities.Count, "every Tarkov shared facility except the personal stash");
            Assert.IsTrue(hideout.Facilities.All(f => f.Level == 1));
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
            Assert.AreEqual(1, hideout.LevelOf("workbench"));
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
        public void AccountSurvivesAWriteAndARead()
        {
            var id = NewId();
            var original = new Account(
                Hideout.CreateNew().WithFacility(new Facility("generator", 2)),
                new[] { id });

            var bytes = AccountSerializer.Serialize(original);
            var back = AccountSerializer.FromJson(
                AccountSerializer.Parse(Encoding.UTF8.GetString(bytes)));

            Assert.AreEqual(original.CharacterOrder.Single(), back.CharacterOrder.Single());
            Assert.AreEqual(2, back.Hideout.LevelOf("generator"));
            Assert.AreEqual(1, back.Hideout.LevelOf("workbench"));
            Assert.AreEqual(AccountSerializer.Serialize(original), AccountSerializer.Serialize(back));
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
            // Level 1: 10 per person.
            Assert.AreEqual(0, HideoutRules.GeneratorFuelPerHour(1, 0));
            Assert.AreEqual(10, HideoutRules.GeneratorFuelPerHour(1, 1));
            Assert.AreEqual(40, HideoutRules.GeneratorFuelPerHour(1, 4));

            // Level 2: 5 per person.
            Assert.AreEqual(20, HideoutRules.GeneratorFuelPerHour(2, 4));

            // Level 5: 2 per person.
            Assert.AreEqual(8, HideoutRules.GeneratorFuelPerHour(5, 4));
        }

        [Test]
        public void RoomLevelGivesStashSize()
        {
            Assert.AreEqual((10, 20), HideoutRules.RoomStashSize(1));
            Assert.AreEqual((12, 24), HideoutRules.RoomStashSize(2));
            Assert.AreEqual((14, 28), HideoutRules.RoomStashSize(3));
        }

        [Test]
        public void RoomLevelIsDerivedFromStashSize()
        {
            Assert.AreEqual(1, HideoutRules.RoomLevelForStash(10, 20));
            Assert.AreEqual(2, HideoutRules.RoomLevelForStash(12, 24));
            Assert.AreEqual(1, HideoutRules.RoomLevelForStash(11, 22));
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
        public void OldSchemaSevenGetsADefaultRoom()
        {
            var document = ProfileSerializer.ToJson(Profile.CreateNew(NewId(), "Ana"));
            document["schema_version"] = 7;
            document.Remove("personal_room");

            var upgraded = ProfileMigrations.Upgrade(document);
            var profile = ProfileSerializer.FromJson(upgraded);

            Assert.AreEqual(8, upgraded["schema_version"].Value<int>());
            Assert.AreEqual(1, profile.Room.Level);
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

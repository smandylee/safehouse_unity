using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Safehouse.Core;
using Safehouse.Data;

namespace Safehouse.Tests
{
    /// <summary>Saving, loading, backups, recovery and the import from the Python build, in a temp folder.</summary>
    public sealed class ProfileRepositoryTests
    {
        private static Dictionary<string, ItemDefinition> _catalog;

        private static Dictionary<string, ItemDefinition> Catalog => _catalog ?? (_catalog = CatalogLoader.Load());

        private string _folder;
        private ProfileRepository _repository;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "safehouse-saves-" + Guid.NewGuid().ToString("N"));
            _repository = new ProfileRepository(_folder, Catalog);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }

        private static int _next;
        private static string NewId() => (++_next).ToString("x32");

        private Profile SavedCharacter(string name = "Ana")
        {
            var profile = _repository.Create(name);
            var withBandage = profile.With(
                stash: profile.Stash.With(new[] { ItemInstance.Create(NewId(), "army-bandage", 1, 1) }));
            _repository.Save(withBandage);
            return withBandage;
        }

        [Test]
        public void ACharacterComesBackExactlyAsSaved()
        {
            var saved = SavedCharacter();

            var loaded = _repository.Load(saved.ProfileId);

            Assert.AreEqual(saved.DisplayName, loaded.DisplayName);
            Assert.AreEqual(saved.Stash.Stash.Single().InstanceId, loaded.Stash.Stash.Single().InstanceId);
            Assert.AreEqual(ProfileSerializer.Serialize(saved), ProfileSerializer.Serialize(loaded));
        }

        [Test]
        public void EachSaveKeepsTheOneBeforeItAsTheBackup()
        {
            var first = _repository.Create("Ana");
            var path = _repository.PathFor(first.ProfileId);
            var afterFirst = File.ReadAllBytes(path);

            _repository.Save(first.With(money: 123));

            Assert.AreEqual(afterFirst, File.ReadAllBytes(path + ".bak"));
            Assert.AreEqual(123, _repository.Load(first.ProfileId).Money);
        }

        [Test]
        public void ADamagedFileIsRestoredFromItsBackupAndTheBadOneIsKept()
        {
            var saved = SavedCharacter();
            var path = _repository.PathFor(saved.ProfileId);
            File.WriteAllText(path, "{ this is not json");
            var notices = new List<string>();

            var loaded = _repository.Load(saved.ProfileId, notices);

            Assert.AreEqual(saved.DisplayName, loaded.DisplayName);
            Assert.AreEqual(1, notices.Count);
            StringAssert.Contains("Recovered", notices[0]);
            var kept = Directory.GetFiles(Path.Combine(_folder, "recovery"));
            Assert.AreEqual(1, kept.Length);
            Assert.AreEqual("{ this is not json", File.ReadAllText(kept[0]));
            Assert.DoesNotThrow(() => _repository.Load(saved.ProfileId), "the restored file now loads on its own");
        }

        [Test]
        public void AMissingFileIsRestoredFromItsBackup()
        {
            var saved = SavedCharacter();
            var path = _repository.PathFor(saved.ProfileId);
            File.Delete(path);

            var loaded = _repository.Load(saved.ProfileId);

            Assert.AreEqual(saved.DisplayName, loaded.DisplayName);
            Assert.IsTrue(File.Exists(path));
        }

        [Test]
        public void ADamagedFileWithNoBackupIsAnErrorNotAFreshCharacter()
        {
            var created = _repository.Create("Ana"); // one save: there is no .bak yet
            var path = _repository.PathFor(created.ProfileId);
            File.WriteAllText(path, "garbage");

            var error = Assert.Throws<StorageException>(() => _repository.Load(created.ProfileId));

            StringAssert.Contains("no backup", error.Message);
            Assert.AreEqual("garbage", File.ReadAllText(path), "left as it was");
        }

        [Test]
        public void AnItemThatNoLongerExistsBlocksLoadingInsteadOfFallingBackToOlderData()
        {
            var saved = SavedCharacter();
            var path = _repository.PathFor(saved.ProfileId);
            var document = JObject.Parse(File.ReadAllText(path));
            document["stash"][0]["item_id"] = "an-item-that-was-removed";
            File.WriteAllText(path, document.ToString());
            var tampered = File.ReadAllBytes(path);
            var backupBefore = File.ReadAllBytes(path + ".bak");

            var error = Assert.Throws<StorageException>(() => _repository.Load(saved.ProfileId));

            StringAssert.Contains("Unknown item type", error.Message);
            Assert.AreEqual(tampered, File.ReadAllBytes(path), "the file is not touched");
            Assert.AreEqual(backupBefore, File.ReadAllBytes(path + ".bak"));
        }

        [Test]
        public void ASaveFromANewerVersionIsNotOverwritten()
        {
            var saved = SavedCharacter();
            var path = _repository.PathFor(saved.ProfileId);
            var document = JObject.Parse(File.ReadAllText(path));
            document["schema_version"] = 99;
            File.WriteAllText(path, document.ToString());
            var newer = File.ReadAllBytes(path);

            Assert.Throws<StorageException>(() => _repository.Load(saved.ProfileId));
            Assert.Throws<StorageException>(() => _repository.Save(saved.With(money: 1)));

            Assert.AreEqual(newer, File.ReadAllBytes(path));
        }

        [Test]
        public void AnInvalidProfileIsRefusedBeforeAnythingIsWritten()
        {
            var profile = Profile.CreateNew(NewId(), "Ana").With(
                rig: new StashGrid(6, 4, new[] { ItemInstance.Create(NewId(), "no-such-item", 0, 0) }));

            Assert.Throws<StorageException>(() => _repository.Save(profile));

            Assert.IsFalse(File.Exists(_repository.PathFor(profile.ProfileId)));
        }

        [Test]
        public void AFailedWriteLeavesTheOldFileAndNoLeftoverTempFile()
        {
            var saved = SavedCharacter();
            var path = _repository.PathFor(saved.ProfileId);
            var before = File.ReadAllBytes(path);

            // Another process holding the file open exclusively makes the write fail partway.
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<StorageException>(() => _repository.Save(saved.With(money: 999)));
            }

            Assert.AreEqual(before, File.ReadAllBytes(path));
            Assert.IsEmpty(Directory.GetFiles(Path.Combine(_folder, "saves"), "*.tmp"));
            Assert.AreEqual(saved.Money, _repository.Load(saved.ProfileId).Money);
        }

        [Test]
        public void AnIdThatWouldEscapeTheSavesFolderIsRefused()
        {
            Assert.Throws<StorageException>(() => _repository.PathFor("..\\..\\evil"));
            Assert.Throws<StorageException>(() => _repository.PathFor("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")); // upper case
            Assert.Throws<StorageException>(() => _repository.PathFor(null));
            Assert.DoesNotThrow(() => _repository.PathFor(NewId()));
        }

        [Test]
        public void DeletingRemovesTheBackupSoTheCharacterCannotComeBack()
        {
            var saved = SavedCharacter();

            _repository.Delete(saved.ProfileId);

            Assert.IsEmpty(_repository.CharacterIds());
            Assert.Throws<StorageException>(() => _repository.Load(saved.ProfileId));
        }

        [Test]
        public void ListShowsWhatOpensAndReportsWhatDoesNot()
        {
            SavedCharacter("Zed");
            SavedCharacter("ana");
            var broken = SavedCharacter("Broken");
            File.WriteAllText(_repository.PathFor(broken.ProfileId), "garbage");
            File.Delete(_repository.PathFor(broken.ProfileId) + ".bak");

            var (profiles, problems) = _repository.List();

            CollectionAssert.AreEqual(new[] { "ana", "Zed" }, profiles.Select(p => p.DisplayName).ToArray(),
                "sorted by name, ignoring case");
            Assert.AreEqual(1, problems.Count);
        }

        [Test]
        public void OnlyTenCharactersCanBeKept()
        {
            for (var i = 0; i < CharacterSheet.MaxCharacters; i++)
            {
                _repository.Create("Char " + i);
            }

            var error = Assert.Throws<StorageException>(() => _repository.Create("One too many"));

            StringAssert.Contains("At most", error.Message);
        }

        [Test]
        public void TwoCopiesOfTheGameCannotUseOneFolderAtOnce()
        {
            using (DataDirectoryLock.Acquire(_folder))
            {
                Assert.Throws<StorageException>(() => DataDirectoryLock.Acquire(_folder));
            }

            using (DataDirectoryLock.Acquire(_folder))
            {
                // released by the first one's disposal
            }
        }

        // ---- importing the Python build's characters ----

        private string WritePythonSave(string savesFolder, string id, string name, string itemId = "cpu-fan")
        {
            Directory.CreateDirectory(savesFolder);
            var document = new JObject
            {
                ["schema_version"] = 5, ["profile_id"] = id, ["display_name"] = name, ["money"] = 500000,
                ["stash_width"] = 10, ["stash_height"] = 20,
                ["stash"] = new JArray(new JObject
                {
                    ["instance_id"] = NewId(), ["item_id"] = itemId, ["x"] = 0, ["y"] = 0, ["rotation"] = 0,
                }),
                ["traders"] = new JArray(), ["status"] = "downed", ["loadout"] = new JArray(),
                ["body_parts"] = new JObject
                {
                    ["head"] = 35, ["thorax"] = 0, ["stomach"] = 70, ["left_arm"] = 60,
                    ["right_arm"] = 60, ["left_leg"] = 65, ["right_leg"] = 65,
                },
                ["conditions"] = new JObject { ["thorax"] = new JArray("heavy_bleed") },
            };
            var path = Path.Combine(savesFolder, id + ".json");
            File.WriteAllText(path, document.ToString());
            return path;
        }

        [Test]
        public void ImportingBringsAnOldCharacterOverAsACurrentOneAndLeavesTheSourceAlone()
        {
            var python = Path.Combine(_folder, "python-saves");
            var id = NewId();
            var source = WritePythonSave(python, id, "Old Hand");
            var sourceBefore = File.ReadAllBytes(source);

            var report = PythonSaveImporter.Import(python, _repository);

            CollectionAssert.AreEqual(new[] { "Old Hand" }, report.Imported);
            Assert.IsEmpty(report.Problems);
            var profile = _repository.Load(id);
            Assert.AreEqual("downed", profile.Status);
            Assert.AreEqual("cpu-fan", profile.Stash.Stash.Single().ItemId);
            Assert.AreEqual(0, profile.BodyParts["thorax"]);
            CollectionAssert.AreEqual(new[] { "heavy_bleed" }, profile.Conditions["thorax"]);
            Assert.AreEqual(sourceBefore, File.ReadAllBytes(source), "the Python save is only ever read");
            Assert.IsFalse(File.Exists(source + ".bak"));
        }

        [Test]
        public void ImportingTwiceNeverOverwritesProgressMadeHere()
        {
            var python = Path.Combine(_folder, "python-saves");
            var id = NewId();
            WritePythonSave(python, id, "Old Hand");
            PythonSaveImporter.Import(python, _repository);
            _repository.Save(_repository.Load(id).With(money: 777));

            var again = PythonSaveImporter.Import(python, _repository);

            CollectionAssert.AreEqual(new[] { "Old Hand" }, again.AlreadyPresent);
            Assert.IsEmpty(again.Imported);
            Assert.AreEqual(777, _repository.Load(id).Money);
        }

        [Test]
        public void ACharacterThatCannotBeUsedIsReportedAndLeftOut()
        {
            var python = Path.Combine(_folder, "python-saves");
            WritePythonSave(python, NewId(), "Good");
            WritePythonSave(python, NewId(), "Bad", itemId: "an-item-that-was-removed");
            File.WriteAllText(Path.Combine(python, NewId() + ".json"), "not json");

            var report = PythonSaveImporter.Import(python, _repository);

            CollectionAssert.AreEqual(new[] { "Good" }, report.Imported);
            Assert.AreEqual(2, report.Problems.Count);
            Assert.AreEqual(1, _repository.CharacterIds().Count);
        }

        [Test]
        public void AMissingPythonFolderIsReportedNotAnError()
        {
            var report = PythonSaveImporter.Import(Path.Combine(_folder, "nowhere"), _repository);

            Assert.AreEqual(1, report.Problems.Count);
            Assert.IsEmpty(report.Imported);
        }

        [Test]
        public void TheRealPythonSavesOnThisMachineImportWithoutChangingThem()
        {
            var real = PythonSaveImporter.DefaultSavesFolder;
            Assume.That(Directory.Exists(real), $"no Python saves at {real}");
            var files = Directory.GetFiles(real, "*.json");
            Assume.That(files.Length, Is.GreaterThan(0));
            var before = files.ToDictionary(file => file, File.ReadAllBytes);

            var report = PythonSaveImporter.Import(real, _repository);

            TestContext.WriteLine($"imported {report.Imported.Count}, problems {report.Problems.Count}");
            foreach (var problem in report.Problems)
            {
                TestContext.WriteLine("  " + problem);
            }

            Assert.AreEqual(files.Length, report.Imported.Count + report.AlreadyPresent.Count + report.Problems.Count,
                "every file is accounted for");
            foreach (var pair in before)
            {
                Assert.AreEqual(pair.Value, File.ReadAllBytes(pair.Key), pair.Key + " must not change");
            }

            foreach (var id in _repository.CharacterIds())
            {
                Assert.DoesNotThrow(() => _repository.Load(id), "an imported character opens");
            }
        }
    }
}

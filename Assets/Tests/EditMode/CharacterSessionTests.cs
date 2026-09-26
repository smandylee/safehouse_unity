using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Safehouse.Core;
using Safehouse.Data;
using Safehouse.UI.Sample;

namespace Safehouse.Tests
{
    /// <summary>The open character: how it is chosen, and that a change either saves or does not happen.</summary>
    public sealed class CharacterSessionTests
    {
        private static Dictionary<string, ItemDefinition> _catalog;

        private static Dictionary<string, ItemDefinition> Catalog => _catalog ?? (_catalog = CatalogLoader.Load());

        private string _folder;
        private readonly List<IDisposable> _open = new List<IDisposable>();

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "safehouse-session-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            CloseAll();
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }

        private void CloseAll()
        {
            foreach (var session in _open)
            {
                session.Dispose();
            }

            _open.Clear();
        }

        private CharacterSession Open(string preferredId = null, ICollection<string> notices = null)
        {
            var session = CharacterSession.Open(Catalog, _folder, () => SampleCharacter.Build(Catalog), preferredId, notices);
            _open.Add(session);
            return session;
        }

        private static int _next;
        private static string NewId() => (++_next).ToString("x32");

        [Test]
        public void AnEmptyFolderStartsWithTheSampleCharacterAndSavesIt()
        {
            var session = Open();

            Assert.AreEqual(SampleCharacter.Name, session.Profile.DisplayName);
            Assert.Greater(session.Profile.Stash.Stash.Count, 0);
            Assert.AreEqual((10, 20), (session.Profile.Stash.StashWidth, session.Profile.Stash.StashHeight));
            Assert.IsTrue(session.Repository.Exists(SampleCharacter.ProfileId), "it is a real, saved character");
            Assert.IsTrue(session.AccountRepository.Exists, "a default account is created with the first character");
            Assert.AreEqual(1, session.Account.CharacterOrder.Count);
            Assert.AreEqual(session.Profile.ProfileId, session.Account.CharacterOrder[0]);
        }

        [Test]
        public void TheSampleIsNotCreatedAgainOnceTheFolderHasACharacter()
        {
            Open();
            CloseAll();

            var again = Open();

            Assert.AreEqual(1, again.Repository.CharacterIds().Count);
        }

        [Test]
        public void ThePreferredCharacterOpensIfItExistsOtherwiseTheFirstByName()
        {
            var repository = new ProfileRepository(_folder, Catalog);
            var zed = repository.Create("Zed");
            repository.Create("Ana");

            Assert.AreEqual("Ana", Open().Profile.DisplayName);
            CloseAll();

            Assert.AreEqual("Zed", Open(zed.ProfileId).Profile.DisplayName);
            CloseAll();

            Assert.AreEqual("Ana", Open(NewId()).Profile.DisplayName, "an unknown id falls back");
        }

        [Test]
        public void ACommittedChangeIsOnDiskAndBecomesTheOpenCharacter()
        {
            var session = Open();
            var before = session.Profile;

            session.Commit(before.With(money: 4242));

            Assert.AreEqual(4242, session.Profile.Money);
            Assert.AreEqual(4242, session.Repository.Load(before.ProfileId).Money);
        }

        [Test]
        public void AChangeThatCannotBeSavedNeverHappens()
        {
            var session = Open();
            var before = session.Profile;
            var path = session.Repository.PathFor(before.ProfileId);

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) // held by "another process"
            {
                Assert.Throws<StorageException>(() => session.Commit(before.With(money: 1)));
            }

            Assert.AreSame(before, session.Profile, "the open character is exactly as it was");
            Assert.AreEqual(before.Money, session.Repository.Load(before.ProfileId).Money);
        }

        [Test]
        public void ACommitCannotChangeWhichCharacterIsOpen()
        {
            var session = Open();
            var other = Profile.CreateNew(NewId(), "Someone Else");

            Assert.Throws<StorageException>(() => session.Commit(other));
        }

        [Test]
        public void SwitchingOpensAnotherCharacterAndAFailedSwitchKeepsTheCurrentOne()
        {
            var session = Open();
            var other = session.Repository.Create("Other");

            session.Switch(other.ProfileId);
            Assert.AreEqual("Other", session.Profile.DisplayName);

            Assert.Throws<StorageException>(() => session.Switch(NewId()));
            Assert.AreEqual("Other", session.Profile.DisplayName, "still the one that was open");
            Assert.AreEqual(2, session.Characters().Count);
        }

        [Test]
        public void TwoCopiesOfTheGameCannotOpenTheSameFolder()
        {
            Open();

            Assert.Throws<StorageException>(() => Open());
        }

        [Test]
        public void AnUnsavedSessionKeepsChangesInMemoryOnly()
        {
            var session = CharacterSession.Unsaved(SampleCharacter.Build(Catalog));

            session.Commit(session.Profile.With(money: 5));
            session.Commit(Account.CreateNew());

            Assert.IsFalse(session.Saves);
            Assert.AreEqual(5, session.Profile.Money);
            Assert.IsFalse(Directory.Exists(_folder));
        }

        [Test]
        public void ADownedCharacterCannotHaveGearOrStashChangedAndOneAwayCanOnlyChangeGear()
        {
            var active = Profile.CreateNew(NewId(), "Ana");
            Assert.IsNull(ProfileRules.StashEditError(active));
            Assert.IsNull(ProfileRules.GearEditError(active));

            var away = active.With(status: "on_expedition");
            StringAssert.Contains("expedition", ProfileRules.StashEditError(away));
            Assert.IsNull(ProfileRules.GearEditError(away), "gear can be changed while away");

            var downed = active.With(status: "downed");
            StringAssert.Contains("downed", ProfileRules.StashEditError(downed));
            StringAssert.Contains("downed", ProfileRules.GearEditError(downed));
        }
    }
}

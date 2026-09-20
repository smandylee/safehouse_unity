using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Safehouse.Core;
using Safehouse.Data;

namespace Safehouse.Tests
{
    /// <summary>The profile model, its JSON form, and the migrations from older save schemas.</summary>
    public sealed class ProfileTests
    {
        private static Dictionary<string, ItemDefinition> _catalog;

        private static Dictionary<string, ItemDefinition> Catalog => _catalog ?? (_catalog = CatalogLoader.Load());

        private static int _next;
        private static string NewId() => (++_next).ToString("x32");
        private static string ProfileId => "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        private static Profile Filled()
        {
            var stash = new StashGrid(10, 20, new[] { ItemInstance.Create(NewId(), "army-bandage", 0, 0) });
            var rig = new StashGrid(6, 4, new[] { ItemInstance.Create(NewId(), "toolset", 2, 1, 90) });
            var backpack = new StashGrid(6, 8, new[] { ItemInstance.Create(NewId(), "army-bandage", 5, 7) });
            var loadout = new Loadout(new[] { EquippedItem.Create("meds", NewId(), "calok-b-hemostatic-applicator") });
            var traders = new[]
            {
                new TraderState("prapor", 12345, -3, new[] { new OfferPurchase("offer-a", 9876, 2) }),
            };
            var conditions = new Dictionary<string, IReadOnlyList<string>> { ["thorax"] = new[] { "heavy_bleed", "fracture" } };
            var baseProfile = Profile.CreateNew(ProfileId, "Svetlana K.", 1_000);
            return new Profile(ProfileId, "Svetlana K.", 1_000, stash, rig, backpack, traders, "active", loadout,
                baseProfile.BodyParts, conditions, baseProfile.Bio, baseProfile.Abilities);
        }

        private static Profile RoundTrip(Profile profile) =>
            ProfileSerializer.FromJson(ProfileMigrations.Upgrade(
                ProfileSerializer.Parse(Encoding.UTF8.GetString(ProfileSerializer.Serialize(profile)))));

        [Test]
        public void ANewCharacterIsHealthyAndHasNothing()
        {
            var profile = Profile.CreateNew(ProfileId, "  Ana  ");

            Assert.AreEqual("Ana", profile.DisplayName, "the name is trimmed");
            Assert.AreEqual(440, profile.Health);
            Assert.AreEqual("active", profile.Status);
            Assert.AreEqual(0, profile.Stash.Stash.Count + profile.Rig.Stash.Count + profile.Backpack.Stash.Count);
            Assert.AreEqual((10, 20), (profile.Stash.StashWidth, profile.Stash.StashHeight));
            Assert.AreEqual((6, 4), (profile.Rig.StashWidth, profile.Rig.StashHeight));
            Assert.IsTrue(profile.Abilities.Values.All(score => score == 5));
        }

        [Test]
        public void EverythingSurvivesAWriteAndARead()
        {
            var original = Filled();

            var back = RoundTrip(original);

            Assert.AreEqual(original.DisplayName, back.DisplayName);
            Assert.AreEqual(original.Money, back.Money);
            Assert.AreEqual(original.Stash.Stash.Single().InstanceId, back.Stash.Stash.Single().InstanceId);
            Assert.AreEqual(90, back.Rig.Stash.Single().Rotation);
            Assert.AreEqual((5, 7), (back.Backpack.Stash.Single().X, back.Backpack.Stash.Single().Y));
            Assert.AreEqual("calok-b-hemostatic-applicator", back.Loadout.Get("meds").ItemId);
            Assert.AreEqual(9876, back.Traders.Single().Purchases.Single().Window);
            CollectionAssert.AreEqual(new[] { "heavy_bleed", "fracture" }, back.Conditions["thorax"]);
            Assert.AreEqual(ProfileSerializer.Serialize(original), ProfileSerializer.Serialize(back),
                "writing what was read gives the same bytes");
        }

        [Test]
        public void TheFileIsTwoSpaceIndentedLfTextWithoutAByteOrderMark()
        {
            var bytes = ProfileSerializer.Serialize(Filled());
            var text = Encoding.UTF8.GetString(bytes);

            Assert.AreNotEqual(0xEF, bytes[0], "no BOM");
            StringAssert.StartsWith("{\n  \"schema_version\": 7,\n  \"profile_id\": ", text);
            StringAssert.EndsWith("}\n", text);
            StringAssert.DoesNotContain("\r", text);
        }

        [Test]
        public void ANameWithNonAsciiTextIsWrittenAsIs()
        {
            var profile = Profile.CreateNew(ProfileId, "이승무");

            var text = Encoding.UTF8.GetString(ProfileSerializer.Serialize(profile));

            StringAssert.Contains("\"display_name\": \"이승무\"", text);
            Assert.AreEqual("이승무", RoundTrip(profile).DisplayName);
        }

        [Test]
        public void ADateLikeNameIsNotTurnedIntoADate()
        {
            var profile = Profile.CreateNew(ProfileId, "2020-01-01");

            Assert.AreEqual("2020-01-01", RoundTrip(profile).DisplayName);
        }

        [Test]
        public void NumbersMustBeIntegersNotFloatsOrBooleans()
        {
            var document = ProfileSerializer.ToJson(Filled());

            document["money"] = 5.0;
            Assert.Throws<ValidationException>(() => ProfileSerializer.FromJson(document));

            document["money"] = true;
            Assert.Throws<ValidationException>(() => ProfileSerializer.FromJson(document));

            document["money"] = "5";
            Assert.Throws<ValidationException>(() => ProfileSerializer.FromJson(document));
        }

        [Test]
        public void ANewerSchemaIsRefusedNotGuessedAt()
        {
            var document = ProfileSerializer.ToJson(Filled());
            document["schema_version"] = 99;

            var upgraded = ProfileMigrations.Upgrade(document);

            Assert.AreEqual(99, upgraded["schema_version"].Value<int>(), "left as it was");
            var error = Assert.Throws<ValidationException>(() => ProfileSerializer.FromJson(upgraded));
            StringAssert.Contains("Unsupported save schema", error.Message);
        }

        [Test]
        public void UnknownKeysAreIgnoredAndDroppedOnTheNextWrite()
        {
            var document = ProfileSerializer.ToJson(Filled());
            document["from_the_future"] = "hello";

            var profile = ProfileSerializer.FromJson(document);

            StringAssert.DoesNotContain("from_the_future", Encoding.UTF8.GetString(ProfileSerializer.Serialize(profile)));
        }

        [Test]
        public void AbilityScoresMustBeTiersAndWithinThePointPool()
        {
            var baseProfile = Profile.CreateNew(ProfileId, "Ana");
            Profile With(IReadOnlyDictionary<string, int> abilities) => new Profile(ProfileId, "Ana", 0,
                baseProfile.Stash, baseProfile.Rig, baseProfile.Backpack, null, "active", null,
                baseProfile.BodyParts, baseProfile.Conditions, baseProfile.Bio, abilities);

            var notATier = baseProfile.Abilities.ToDictionary(pair => pair.Key, pair => pair.Value);
            notATier["strength"] = 6;
            Assert.Throws<ValidationException>(() => With(notATier));

            // Two tens cost 10, exactly the pool; three cost 15.
            var spent = baseProfile.Abilities.ToDictionary(pair => pair.Key, pair => pair.Value);
            spent["strength"] = 10;
            spent["agility"] = 10;
            Assert.DoesNotThrow(() => With(spent));
            spent["looks"] = 10;
            Assert.Throws<ValidationException>(() => With(spent));

            // Dropping others to 3 gives points back: 3 x -2 = -6, so a third ten is now affordable.
            spent["dexterity"] = 3;
            spent["intelligence"] = 3;
            spent["sense"] = 3;
            Assert.DoesNotThrow(() => With(spent));
        }

        [Test]
        public void ABodyPartCannotExceedItsOwnMaximum()
        {
            var profile = Profile.CreateNew(ProfileId, "Ana");
            var parts = profile.BodyParts.ToDictionary(pair => pair.Key, pair => pair.Value);
            parts["head"] = 36;

            Assert.Throws<ValidationException>(() => new Profile(ProfileId, "Ana", 0, profile.Stash, profile.Rig,
                profile.Backpack, null, "active", null, parts, profile.Conditions, profile.Bio, profile.Abilities));
        }

        [Test]
        public void OneInstanceCannotBeInTwoPlaces()
        {
            var shared = NewId();
            var profile = Profile.CreateNew(ProfileId, "Ana").With(
                stash: new StashGrid(10, 20, new[] { ItemInstance.Create(shared, "army-bandage", 0, 0) }),
                rig: new StashGrid(6, 4, new[] { ItemInstance.Create(shared, "army-bandage", 0, 0) }));

            var error = Assert.Throws<ValidationException>(() => ProfileRules.ValidateAgainstCatalog(profile, Catalog));

            StringAssert.Contains("Duplicate instance ID", error.Message);
        }

        [Test]
        public void ItemsMustExistAndFitTheirGrids()
        {
            var unknown = Profile.CreateNew(ProfileId, "Ana").With(
                backpack: new StashGrid(6, 8, new[] { ItemInstance.Create(NewId(), "no-such-item", 0, 0) }));
            Assert.Throws<ValidationException>(() => ProfileRules.ValidateAgainstCatalog(unknown, Catalog));

            var offTheEdge = Profile.CreateNew(ProfileId, "Ana").With(
                rig: new StashGrid(6, 4, new[] { ItemInstance.Create(NewId(), "toolset", 5, 3) })); // 2x2 at 5,3
            Assert.Throws<ValidationException>(() => ProfileRules.ValidateAgainstCatalog(offTheEdge, Catalog));
        }

        // ---- migrations ----

        private static JObject V1Document() => JObject.Parse(@"{
            ""schema_version"": 1, ""profile_id"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"", ""display_name"": ""Old Save"",
            ""money"": 4200, ""stash_width"": 10, ""stash_height"": 20,
            ""stash"": [ { ""instance_id"": ""11111111111111111111111111111111"", ""item_id"": ""cpu_fan"", ""x"": 3, ""y"": 4, ""rotation"": 0 } ]
        }");

        [Test]
        public void AVersionOneSaveClimbsAllTheWayToTheCurrentSchema()
        {
            var upgraded = ProfileMigrations.Upgrade(V1Document());
            var profile = ProfileSerializer.FromJson(upgraded);

            Assert.AreEqual(7, upgraded["schema_version"].Value<int>());
            Assert.AreEqual("Old Save", profile.DisplayName);
            Assert.AreEqual("cpu-fan", profile.Stash.Stash.Single().ItemId, "the old test item is renamed");
            Assert.AreEqual((3, 4), (profile.Stash.Stash.Single().X, profile.Stash.Stash.Single().Y));
            Assert.AreEqual(440, profile.Health);
            Assert.AreEqual("active", profile.Status);
            Assert.AreEqual(0, profile.Rig.Stash.Count, "the rig and backpack start empty");
        }

        [Test]
        public void UpgradingDoesNotTouchTheOriginalDocument()
        {
            var original = V1Document();

            ProfileMigrations.Upgrade(original);

            Assert.AreEqual(1, original["schema_version"].Value<int>());
            Assert.AreEqual("cpu_fan", original["stash"][0]["item_id"].Value<string>());
        }

        [Test]
        public void OldFlatHealthIsSpreadAcrossTheBodyPartsByTheirShare()
        {
            var document = V1Document();
            document["schema_version"] = 4;
            document["status"] = "active";
            document["health"] = 220;
            document["loadout"] = new JArray();
            document["traders"] = new JArray();

            var profile = ProfileSerializer.FromJson(ProfileMigrations.Upgrade(document));

            // Half of each part, rounded half-to-even like Python's round(); the last part takes the remainder.
            CollectionAssert.AreEqual(new[] { 18, 42, 35, 30, 30, 32, 33 },
                CharacterSheet.BodyParts.Select(part => profile.BodyParts[part]).ToArray());
            Assert.AreEqual(220, profile.Health);
        }

        [Test]
        public void AnUnknownOrNonNumericVersionIsReturnedUnchanged()
        {
            var weird = JObject.Parse("{\"schema_version\": \"six\"}");
            Assert.AreEqual("six", ((JObject)ProfileMigrations.Upgrade(weird))["schema_version"].Value<string>());

            var zero = JObject.Parse("{\"schema_version\": 0}");
            Assert.AreEqual(0, ProfileMigrations.Upgrade(zero)["schema_version"].Value<int>());

            var notAnObject = new JArray();
            Assert.AreSame(notAnObject, ProfileMigrations.Upgrade(notAnObject));
        }
    }
}

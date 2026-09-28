using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Safehouse.Core;
using Safehouse.Data;

namespace Safehouse.Tests
{
    public sealed class GundogTests
    {
        [Test]
        public void ClassModifiersAddTheMainRowToTheSubRow()
        {
            var sheet = Sheet("assault", "scout");

            Assert.AreEqual(40, sheet.SkillModifiers()["shooting"]);
            Assert.AreEqual(35, sheet.SkillModifiers()["transport"]);
            Assert.AreEqual(30, sheet.SkillModifiers()["perception"]);
            Assert.AreEqual(0, GundogSheet.Blank.SkillModifiers()["shooting"]);
        }

        [Test]
        public void TheSameClassCanBeBothMainAndSub()
        {
            var sheet = Sheet("assault", "assault", "assault-1");

            Assert.AreEqual(50, sheet.SkillModifiers()["shooting"]);
            Assert.AreEqual(35, sheet.SkillModifiers()["melee"]);
            Assert.Throws<ValidationException>(() => Sheet("assault", "assault", "medic-1"));
        }

        [Test]
        public void ASheetRoundTripsThroughTheSave()
        {
            var abilities = Gundog.Abilities.ToDictionary(stat => stat.StatId, stat => stat.StatId == "muscle" ? 8 : 5);
            var sheet = new GundogSheet(abilities, "sniper", "operator", new[] { "sniper-1", "operator-2" },
                new[] { "군 경력", "", "", "", "" }, 4, 9, 48, "상병", "한국어", "저격수", "");
            var profile = Profile.CreateNew("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "이승무", bio: EmptyBio(), gundog: sheet);

            var back = ProfileSerializer.FromJson(ProfileSerializer.Parse(
                System.Text.Encoding.UTF8.GetString(ProfileSerializer.Serialize(profile))));

            Assert.AreEqual(8, back.Gundog.Abilities["muscle"]);
            Assert.AreEqual("sniper", back.Gundog.MainClass);
            Assert.AreEqual("operator", back.Gundog.SubClass);
            CollectionAssert.AreEqual(new[] { "sniper-1", "operator-2" }, back.Gundog.Arts.ToArray());
            Assert.AreEqual(4, back.Gundog.RewardPoints);
            Assert.AreEqual(9, back.Gundog.Movement);
            Assert.AreEqual(48, back.Gundog.Durability);
            Assert.AreEqual("상병", back.Gundog.Rank);
            Assert.AreEqual("저격수", back.Gundog.Occupation);
        }

        private static GundogSheet Sheet(string main, string sub, params string[] arts) =>
            new GundogSheet(
                Gundog.Abilities.ToDictionary(stat => stat.StatId, stat => Gundog.AbilityDefault),
                main, sub, arts, Enumerable.Repeat("", Gundog.CareerLines).ToList(), 0, 0, 0, "", "", "", "");

        private static Dictionary<string, string> EmptyBio() =>
            CharacterSheet.BioFields.ToDictionary(field => field, field => "");
    }
}

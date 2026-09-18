using NUnit.Framework;
using Safehouse.Core;
using Safehouse.Data;

namespace Safehouse.Tests
{
    /// <summary>The shipped gear.json loads, and agrees with the shipped item catalog.</summary>
    public sealed class GearLoaderTests
    {
        [Test]
        public void TheShippedGearLoadsAgainstTheShippedCatalog()
        {
            var catalog = CatalogLoader.Load();

            var gear = GearLoader.Load(catalog);

            Assert.Greater(gear.Weapons.Count, 0);
            Assert.Greater(gear.Ammo.Count, 0);
            Assert.Greater(gear.Equipment.Count, 0);
            Assert.Greater(gear.Meds.Count, 0);
        }

        [Test]
        public void KnownItemsResolveToTheirSlots()
        {
            var gear = GearLoader.Load();

            Assert.AreEqual("primary", gear.SlotOf("kalashnikov-ak-12-545x39-assault-rifle"));
            Assert.AreEqual("ammo", gear.SlotOf("545x39mm-bp-gs"));
            Assert.AreEqual("helmet", gear.SlotOf("rys-t-bulletproof-helmet-black"));
            Assert.AreEqual("meds", gear.SlotOf("calok-b-hemostatic-applicator"));
            Assert.AreEqual(24, gear.Equipment["spiritus-systems-lv-119-plate-carrier-black-division-v1"].Capacity);
            Assert.IsTrue(gear.Fits("kalashnikov-ak-12-545x39-assault-rifle", "545x39mm-bp-gs"));
            Assert.IsFalse(gear.Fits("colt-m4a1-556x45-assault-rifle", "545x39mm-bp-gs"));
        }

        [Test]
        public void ARowMissingFromTheCatalogIsReported()
        {
            var document = Newtonsoft.Json.Linq.JObject.Parse(
                "{\"weapons\":{\"ghost\":{\"caliber\":\"x\",\"ergonomics\":1,\"recoil\":1}},\"ammo\":{},\"equipment\":{},\"meds\":{}}");
            var catalog = CatalogLoader.Load();

            var error = Assert.Throws<GameDataException>(() => GearLoader.Parse(document, catalog));

            StringAssert.Contains("ghost", error.Message);
        }
    }
}

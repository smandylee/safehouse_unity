using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Safehouse.Core;
using Safehouse.Data;

namespace Safehouse.Tests
{
    /// <summary>The shipped traders.json, and the rules for what a trader file may contain.</summary>
    public sealed class TraderLoaderTests
    {
        private static Dictionary<string, ItemDefinition> _catalog;
        private static Dictionary<string, ItemDefinition> Catalog => _catalog ?? (_catalog = CatalogLoader.Load());

        private static IReadOnlyList<TraderDefinition> _traders;
        private static IReadOnlyList<TraderDefinition> Traders => _traders ?? (_traders = TraderLoader.Load(Catalog));

        private static int _next;
        private static string NewId() => (++_next).ToString("x32");

        // A tiny trader file. "pliers" is a real catalog item whose price is worked out from its value.
        private static JObject Document(string trader) => JObject.Parse(
            "{\"schema_version\":1,\"traders\":[" + trader + "]}");

        private const string Minimal = @"{
            ""trader_id"": ""tester"", ""name"": ""Tester"", ""description"": """", ""buys_categories"": [""Tools""],
            ""sell_rate_by_level"": [0.4, 0.5],
            ""loyalty_levels"": [ {""min_spent"": 0, ""min_standing"": 0}, {""min_spent"": 1000, ""min_standing"": 10} ],
            ""offers"": [ {""offer_id"": ""pliers"", ""item_id"": ""pliers"", ""loyalty_level"": 1, ""stock"": 2, ""restock_hours"": 2, ""price"": null} ]
        }";

        // ---- the shipped data ----

        [Test]
        public void TheShippedTradersLoadAndPassTheChecks()
        {
            Assert.AreEqual(9, Traders.Count);
            CollectionAssert.AreEquivalent(
                new[] { "prapor", "therapist", "fence", "skier", "peacekeeper", "mechanic", "ragman", "jaeger", "ref" },
                Traders.Select(trader => trader.TraderId).ToArray());
            Assert.Greater(Traders.Sum(trader => trader.Offers.Count), 2000);
        }

        [Test]
        public void ATraderMatchesWhatTheFileSays()
        {
            var prapor = Traders.Single(trader => trader.TraderId == "prapor");

            Assert.AreEqual(4, prapor.MaxLevel);
            CollectionAssert.AreEqual(new[] { 0, 70, 270, 790 }, prapor.Levels.Select(level => level.MinStanding).ToArray());
            Assert.IsTrue(prapor.Levels.All(level => level.MinSpent == 0));
            Assert.IsTrue(prapor.SellBasisPoints.All(rate => rate == 4000), "a 0.40 sell rate");
            Assert.AreEqual(330, prapor.Offers.Count);
            Assert.IsTrue(prapor.Offers.All(offer => offer.RestockHours == 3));

            var fence = Traders.Single(trader => trader.TraderId == "fence");
            Assert.AreEqual(0, fence.Offers.Count, "the Fence sells nothing, only buys");
            Assert.AreEqual(2, fence.MaxLevel);
        }

        [Test]
        public void ARealOfferCanBeBoughtWithTheRealCatalog()
        {
            var prapor = Traders.Single(trader => trader.TraderId == "prapor");
            var offer = prapor.Offers.First(candidate => candidate.LoyaltyLevel == 1);
            var profile = Profile.CreateNew(NewId(), "Ana", money: 2_000_000);

            var (after, item) = TradingRules.Buy(profile, prapor, offer.OfferId, now: 1_700_000_000, Catalog);

            Assert.AreEqual(offer.ItemId, item.ItemId);
            Assert.AreEqual(2_000_000 - offer.Price, after.Money);
            Assert.AreEqual(1, after.Stash.Stash.Count);
            Assert.AreEqual(offer.Price, TradingRules.StateOf(after, "prapor").Spent);
        }

        [Test]
        public void ARealSaleIsPaidAtTheTablePrice()
        {
            var prapor = Traders.Single(trader => trader.TraderId == "prapor");
            var itemId = prapor.SellPrices.Keys.First();
            var profile = Profile.CreateNew(NewId(), "Ana").With(
                stash: new StashGrid(10, 20, new[] { ItemInstance.Create(NewId(), itemId, 0, 0) }));

            var (after, price) = TradingRules.Sell(profile, prapor, profile.Stash.Stash[0].InstanceId, Catalog);

            Assert.AreEqual(prapor.SellPrices[itemId], price);
            Assert.AreEqual(profile.Money + price, after.Money);
        }

        // ---- what a trader file may contain ----

        [Test]
        public void AMinimalTraderGetsTheDefaultMarkupAndBasisPointRates()
        {
            var trader = TraderLoader.Parse(Document(Minimal), Catalog).Single();

            var pliers = Catalog["pliers"];
            Assert.AreEqual((int)System.Math.Ceiling(pliers.BaseValue * 1.2), trader.Offers.Single().Price,
                "no price in the file: 120% of the base value, rounded up");
            CollectionAssert.AreEqual(new[] { 4000, 5000 }, trader.SellBasisPoints.ToArray());
            Assert.AreEqual(pliers.BaseValue * 4000 / 10000, TradingRules.SellPrice(trader, pliers, 1));
        }

        [Test]
        public void TheDefaultPriceRoundsUpAndIsNeverZero()
        {
            ItemDefinition WithValue(int value) => ItemDefinition.Create("x", "X", "Tools", 1, 1, value, "Common", 0.1);

            Assert.AreEqual(5400, TraderLoader.DefaultPrice(WithValue(4500)));
            Assert.AreEqual(2, TraderLoader.DefaultPrice(WithValue(1)), "1 x 1.2 rounds up to 2");
            Assert.AreEqual(1, TraderLoader.DefaultPrice(WithValue(0)), "a free item still costs 1");
        }

        [Test]
        public void ABadTraderFileIsRefusedWithAMessageThatNamesTheTraderAndOffer()
        {
            JObject With(string find, string replace) => Document(Minimal.Replace(find, replace));

            void Refuses(JObject document, string expectedFragment)
            {
                var error = Assert.Throws<GameDataException>(() => TraderLoader.Parse(document, Catalog), expectedFragment);
                StringAssert.Contains(expectedFragment, error.Message);
            }

            Refuses(With("\"item_id\": \"pliers\"", "\"item_id\": \"no-such-item\""), "unknown item_id no-such-item");
            Refuses(With("\"min_standing\": 0}, {", "\"min_standing\": 5}, {"), "level 1 is always available");
            Refuses(With("[0.4, 0.5]", "[0.5, 0.4]"), "sell rates cannot decrease");
            Refuses(With("[0.4, 0.5]", "[0.4]"), "needs one rate per loyalty level");
            Refuses(With("[0.4, 0.5]", "[0.4, 1.5]"), "between 0 and 1");
            Refuses(With("\"loyalty_level\": 1", "\"loyalty_level\": 3"), "loyalty level the trader does not have");
            Refuses(With("\"stock\": 2", "\"stock\": 0"), "stock");
            Refuses(With("\"price\": null", "\"price\": 0"), "price");
            Refuses(With("\"stock\": 2", "\"stock\": 2.5"), "must be an integer");
            Refuses(Document(Minimal + "," + Minimal), "Duplicate trader_id: tester");
            Refuses(JObject.Parse("{\"schema_version\":1}"), "traders list");
        }

        [Test]
        public void AFileWhereSomethingCanBeSoldForMoreThanItCostsIsRefused()
        {
            var sells = Minimal.Replace("\"price\": null", "\"price\": 100");
            var buys = @"{
                ""trader_id"": ""fence"", ""name"": ""Fence"", ""description"": """", ""buys_categories"": [],
                ""sell_rate_by_level"": [0.5], ""loyalty_levels"": [ {""min_spent"": 0, ""min_standing"": 0} ], ""offers"": [],
                ""sell_prices"": { ""pliers"": 250 } }";

            var error = Assert.Throws<GameDataException>(() => TraderLoader.Parse(Document(sells + "," + buys), Catalog));

            StringAssert.Contains("Raise the price or lower the sell rate", error.Message);
        }

        [Test]
        public void ASellPriceTableMayOnlyNameItemsThatExist()
        {
            var document = Document(Minimal.Replace("\"offers\": [", "\"sell_prices\": {\"ghost\": 5}, \"offers\": ["));

            var error = Assert.Throws<GameDataException>(() => TraderLoader.Parse(document, Catalog));

            StringAssert.Contains("unknown item ghost", error.Message);
        }
    }
}

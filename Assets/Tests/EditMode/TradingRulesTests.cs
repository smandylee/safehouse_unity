using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Safehouse.Core;

namespace Safehouse.Tests
{
    /// <summary>Loyalty, restock, buying and selling - the behaviours of Python test_trading.py, on a small made-up trader.</summary>
    public sealed class TradingRulesTests
    {
        private static readonly Dictionary<string, ItemDefinition> Catalog = new Dictionary<string, ItemDefinition>
        {
            ["pliers"] = ItemDefinition.Create("pliers", "Pliers", "Tools", 1, 1, 4500, "Common", 0.3),
            ["pipe-wrench"] = ItemDefinition.Create("pipe-wrench", "Pipe wrench", "Tools", 1, 2, 9500, "Common", 1.0),
            ["drill"] = ItemDefinition.Create("drill", "Drill", "Tools", 2, 2, 20000, "Uncommon", 2.0),
            ["bandage"] = ItemDefinition.Create("bandage", "Bandage", "Meds", 1, 1, 100, "Common", 0.1),
        };

        private static TraderDefinition Tester(IReadOnlyDictionary<string, int> sellPrices = null) => new TraderDefinition(
            "tester", "Tester", "", new[] { "Tools" }, new[] { 4000, 5000 },
            new[] { new LoyaltyRequirement(0, 0), new LoyaltyRequirement(1000, 10) },
            new[]
            {
                new Offer("pliers", "pliers", 1, stock: 2, restockHours: 2, price: 5400),
                new Offer("drill", "drill", 2, stock: 1, restockHours: 1, price: 30000),
            },
            sellPrices);

        private static int _next;
        private static string NewId() => (++_next).ToString("x32");

        private static Profile Ana(int money = 50_000, int stashWidth = 10, int stashHeight = 10) =>
            Profile.CreateNew(NewId(), "Ana", money, stashWidth, stashHeight);

        private static Profile Buy(Profile profile, string offerId, double now = 0) =>
            TradingRules.Buy(profile, Tester(), offerId, now, Catalog).Profile;

        private static TraderState State(Profile profile) => TradingRules.StateOf(profile, "tester");

        // ---- loyalty ----

        [Test]
        public void ALevelNeedsBothTheSpendingAndTheStanding()
        {
            var trader = Tester();

            Assert.AreEqual(1, TradingRules.LoyaltyLevel(trader, new TraderState("tester", 5000, 0, null)), "spending alone");
            Assert.AreEqual(1, TradingRules.LoyaltyLevel(trader, new TraderState("tester", 0, 50, null)), "standing alone");
            Assert.AreEqual(1, TradingRules.LoyaltyLevel(trader, new TraderState("tester", 1000, 9, null)), "one short");
            Assert.AreEqual(2, TradingRules.LoyaltyLevel(trader, new TraderState("tester", 1000, 10, null)));

            var next = TradingRules.NextRequirement(trader, new TraderState("tester", 0, 0, null));
            Assert.AreEqual((1000L, 10), (next.MinSpent, next.MinStanding));
            Assert.IsNull(TradingRules.NextRequirement(trader, new TraderState("tester", 1000, 10, null)), "nothing above the top");
        }

        [Test]
        public void ALevelIsOnlyReachedByEarningTheOnesBelowIt()
        {
            var trader = new TraderDefinition("t", "T", "", null, new[] { 0, 0, 0 },
                new[] { new LoyaltyRequirement(0, 0), new LoyaltyRequirement(100, 0), new LoyaltyRequirement(200, 0) },
                null, null);

            Assert.AreEqual(1, TradingRules.LoyaltyLevel(trader, new TraderState("t", 99, 0, null)));
            Assert.AreEqual(2, TradingRules.LoyaltyLevel(trader, new TraderState("t", 150, 0, null)));
            Assert.AreEqual(3, TradingRules.LoyaltyLevel(trader, new TraderState("t", 200, 0, null)));
        }

        // ---- time and stock ----

        [Test]
        public void TheCountdownRoundsUpToTheMinuteSoItNeverShowsZeroWhileWaiting()
        {
            Assert.AreEqual("0:00", TradingRules.FormatCountdown(0));
            Assert.AreEqual("0:01", TradingRules.FormatCountdown(1));
            Assert.AreEqual("0:01", TradingRules.FormatCountdown(60));
            Assert.AreEqual("0:02", TradingRules.FormatCountdown(61));
            Assert.AreEqual("2:00", TradingRules.FormatCountdown(7199));
            Assert.AreEqual("1:00", TradingRules.FormatCountdown(3600));
        }

        [Test]
        public void OnlyASensibleMomentInTimeIsAccepted()
        {
            Assert.AreEqual(3600, TradingRules.Timestamp(3600.9));
            Assert.Throws<ValidationException>(() => TradingRules.Timestamp(double.NaN));
            Assert.Throws<ValidationException>(() => TradingRules.Timestamp(double.PositiveInfinity));
            Assert.Throws<ValidationException>(() => TradingRules.Timestamp(-1));
            Assert.Throws<ValidationException>(() => TradingRules.Timestamp(40_000_000_000));
        }

        [Test]
        public void StockComesBackEachRestockWindow()
        {
            var pliers = Tester().GetOffer("pliers"); // 2 in stock, restocks every 2 hours
            var state = new TraderState("tester", 0, 0, null);

            state = TradingRules.RecordPurchase(state, pliers, 0);
            state = TradingRules.RecordPurchase(state, pliers, 7199);

            Assert.AreEqual(0, TradingRules.RemainingStock(pliers, state, 7199), "both bought in the first window");
            Assert.AreEqual(1, TradingRules.SecondsUntilRestock(pliers, 7199));
            Assert.AreEqual(2, TradingRules.RemainingStock(pliers, state, 7200), "a new window");
            Assert.AreEqual(7200, TradingRules.SecondsUntilRestock(pliers, 7200));

            state = TradingRules.RecordPurchase(state, pliers, 7200);
            var only = state.Purchases.Single();
            Assert.AreEqual(("pliers", 1L, 1), (only.OfferId, only.Window, only.Count), "the old window's record is dropped");
        }

        // ---- what a trader pays ----

        [Test]
        public void SellPricesRoundDownAndTheCategoryDecidesWhatIsBought()
        {
            var trader = Tester();

            Assert.AreEqual(1800, TradingRules.SellPrice(trader, Catalog["pliers"], 1)); // 4500 x 0.40
            Assert.AreEqual(4750, TradingRules.SellPrice(trader, Catalog["pipe-wrench"], 2)); // 9500 x 0.50
            Assert.IsTrue(TradingRules.Buys(trader, Catalog["pliers"]));
            Assert.IsFalse(TradingRules.Buys(trader, Catalog["bandage"]));
            Assert.AreEqual(40, TradingRules.SellPrice(trader, Catalog["bandage"], 1), "100 x 0.40");
        }

        [Test]
        public void APriceTableReplacesTheCategoryAndRateRulesAndIgnoresTheLevel()
        {
            var trader = Tester(new Dictionary<string, int> { ["bandage"] = 777 });

            Assert.IsTrue(TradingRules.Buys(trader, Catalog["bandage"]), "listed in the table");
            Assert.IsFalse(TradingRules.Buys(trader, Catalog["pliers"]), "a Tools item, but not in the table");
            Assert.AreEqual(777, TradingRules.SellPrice(trader, Catalog["bandage"], 1));
            Assert.AreEqual(777, TradingRules.SellPrice(trader, Catalog["bandage"], 2));
        }

        // ---- buying ----

        [Test]
        public void BuyingChargesPlacesCountsAndLeavesTheOriginalAlone()
        {
            var before = Ana();
            var ids = new Queue<string>(new[] { NewId() });

            var (after, item) = TradingRules.Buy(before, Tester(), "pliers", 0, Catalog, () => ids.Dequeue());

            Assert.AreEqual(44_600, after.Money);
            Assert.AreEqual(item.InstanceId, after.Stash.Stash.Single().InstanceId);
            Assert.AreEqual("pliers", item.ItemId);
            var state = State(after);
            Assert.AreEqual(5400, state.Spent);
            Assert.AreEqual(("pliers", 0L, 1), (state.Purchases.Single().OfferId, state.Purchases.Single().Window, state.Purchases.Single().Count));
            Assert.AreEqual(50_000, before.Money, "the profile that was passed in is not changed");
            Assert.AreEqual(0, before.Stash.Stash.Count);
            Assert.AreEqual(0, before.Traders.Count);
        }

        [Test]
        public void ARefusedPurchaseSaysWhyAndChangesNothing()
        {
            var profile = Ana();

            var locked = Assert.Throws<ValidationException>(() => Buy(profile, "drill"));
            StringAssert.Contains("loyalty level 2", locked.Message);

            var missing = Assert.Throws<ValidationException>(() => Buy(profile, "no-such-offer"));
            StringAssert.Contains("no offer", missing.Message);

            var broke = Assert.Throws<ValidationException>(() => Buy(Ana(money: 1000), "pliers"));
            StringAssert.Contains("Not enough money: ₽5,400 needed, ₽1,000 available.", broke.Message);

            var soldOut = Buy(Buy(profile, "pliers"), "pliers");
            var again = Assert.Throws<ValidationException>(() => Buy(soldOut, "pliers"));
            StringAssert.Contains("sold out. Restocks in 2:00", again.Message);

            Assert.AreEqual(50_000, profile.Money);
        }

        [Test]
        public void AnAbsentOrDownedCharacterCannotTrade()
        {
            var downed = Ana().With(status: "downed");
            var away = Ana().With(status: "on_expedition");

            StringAssert.Contains("downed", Assert.Throws<ValidationException>(() => Buy(downed, "pliers")).Message);
            StringAssert.Contains("expedition", Assert.Throws<ValidationException>(() => Buy(away, "pliers")).Message);
        }

        [Test]
        public void ABuyWithNoRoomInTheStashKeepsTheMoney()
        {
            var full = Ana(stashWidth: 1, stashHeight: 1).With(stash: new StashGrid(1, 1,
                new[] { ItemInstance.Create(NewId(), "bandage", 0, 0) }));

            var error = Assert.Throws<ValidationException>(() => Buy(full, "pliers"));

            StringAssert.Contains("No space in the stash", error.Message);
            Assert.AreEqual(50_000, full.Money);
        }

        [Test]
        public void TheWouldItGoThroughCheckAgreesWithBuying()
        {
            Assert.IsNull(TradingRules.BuyError(Ana(), Tester(), "pliers", 0, Catalog));
            StringAssert.Contains("loyalty level", TradingRules.BuyError(Ana(), Tester(), "drill", 0, Catalog));
        }

        [Test]
        public void SpendingAndStandingUnlockAnOfferAndTheStockThenComesBack()
        {
            var profile = Buy(Ana(money: 200_000), "pliers"); // spent 5,400: enough spending, no standing yet
            Assert.Throws<ValidationException>(() => Buy(profile, "drill"), "spending alone is not enough");

            profile = TradingRules.WithState(profile, TradingRules.WithStanding(State(profile), 10));
            var afterFirst = Buy(profile, "drill", now: 0);
            Assert.Throws<ValidationException>(() => Buy(afterFirst, "drill", now: 0), "one in stock per hour");
            Assert.DoesNotThrow(() => Buy(afterFirst, "drill", now: 3600), "an hour later it is back");

            var dropped = TradingRules.WithState(afterFirst, TradingRules.WithStanding(State(afterFirst), -5));
            Assert.Throws<ValidationException>(() => Buy(dropped, "drill", now: 3600), "losing standing locks it again");
        }

        [Test]
        public void StockIsCountedPerCharacter()
        {
            var one = Buy(Buy(Ana(), "pliers"), "pliers");
            var another = Ana();

            Assert.Throws<ValidationException>(() => Buy(one, "pliers"));
            Assert.DoesNotThrow(() => Buy(another, "pliers"));
        }

        [Test]
        public void OtherTradersStatesAreKeptWhenOneTraderChanges()
        {
            var other = new TraderState("other", 77, 3, null);
            var profile = TradingRules.WithState(Ana(), other);

            var after = Buy(profile, "pliers");

            Assert.AreEqual(77, TradingRules.StateOf(after, "other").Spent);
            Assert.AreEqual(5400, State(after).Spent);
            Assert.AreEqual(2, after.Traders.Count);
        }

        // ---- selling ----

        [Test]
        public void SellingPaysTheLevelPriceRemovesTheItemAndCountsAsSpending()
        {
            var bought = Buy(Ana(), "pliers");
            var instance = bought.Stash.Stash.Single().InstanceId;
            var beforeSpent = State(bought).Spent;

            var (after, price) = TradingRules.Sell(bought, Tester(), instance, Catalog);

            Assert.AreEqual(1800, price);
            Assert.AreEqual(bought.Money + 1800, after.Money);
            Assert.AreEqual(0, after.Stash.Stash.Count);
            Assert.AreEqual(beforeSpent + 1800, State(after).Spent, "selling counts towards loyalty too");
            Assert.AreEqual(State(bought).Standing, State(after).Standing, "but not standing");
        }

        [Test]
        public void ARefusedSaleSaysWhyAndChangesNothing()
        {
            var profile = Ana().With(stash: new StashGrid(10, 10, new[]
            {
                ItemInstance.Create(NewId(), "bandage", 0, 0),
                ItemInstance.Create(NewId(), "pliers", 1, 0),
            }));
            var bandage = profile.Stash.Stash[0].InstanceId;

            StringAssert.Contains("does not buy Meds", Assert.Throws<ValidationException>(
                () => TradingRules.Sell(profile, Tester(), bandage, Catalog)).Message);
            StringAssert.Contains("no longer exists", Assert.Throws<ValidationException>(
                () => TradingRules.Sell(profile, Tester(), NewId(), Catalog)).Message);

            var rich = Profile.CreateNew(NewId(), "Rich", CoreLimits.MaxMoney - 100).With(
                stash: new StashGrid(10, 10, new[] { ItemInstance.Create(NewId(), "pliers", 0, 0) }));
            StringAssert.Contains("maximum balance", Assert.Throws<ValidationException>(
                () => TradingRules.Sell(rich, Tester(), rich.Stash.Stash[0].InstanceId, Catalog)).Message);
            Assert.AreEqual(CoreLimits.MaxMoney - 100, rich.Money);
        }

        [Test]
        public void ASellQuoteShowsThePriceOrTheReason()
        {
            var profile = Buy(Ana(), "pliers");
            var instance = profile.Stash.Stash.Single().InstanceId;

            Assert.IsNull(TradingRules.SellQuote(profile, Tester(), instance, Catalog, out var price));
            Assert.AreEqual(1800, price);
            StringAssert.Contains("not in the stash", TradingRules.SellQuote(profile, Tester(), NewId(), Catalog, out _));
        }

        // ---- the standing ----

        [Test]
        public void StandingHasBoundsAndKeepsEverythingElse()
        {
            var state = new TraderState("tester", 500, 4, new[] { new OfferPurchase("pliers", 3, 1) });

            var low = TradingRules.WithStanding(state, -1000);
            Assert.AreEqual(-1000, low.Standing);
            Assert.AreEqual(500, low.Spent);
            Assert.AreEqual(1, low.Purchases.Count);

            Assert.Throws<ValidationException>(() => TradingRules.WithStanding(state, 1001));
            Assert.Throws<ValidationException>(() => TradingRules.WithStanding(state, -1001));
        }

        // ---- the data as a whole ----

        [Test]
        public void BuyingSomethingAndSellingItOnForAtLeastWhatItCostIsRefused()
        {
            var friendly = new TraderDefinition("fence", "Fence", "", null, new[] { 5000 },
                new[] { new LoyaltyRequirement(0, 0) }, null, new Dictionary<string, int> { ["pliers"] = 6000 });

            var error = Assert.Throws<ValidationException>(
                () => TradingRules.CheckNoArbitrage(new[] { Tester(), friendly }, Catalog));

            StringAssert.Contains("Pliers costs ₽5,400 from Tester but sells to Fence for ₽6,000", error.Message);
            Assert.DoesNotThrow(() => TradingRules.CheckNoArbitrage(new[] { Tester() }, Catalog));
        }

        // ---- a trader has to make sense ----

        [Test]
        public void ATraderThatMakesNoSenseCannotBeBuilt()
        {
            LoyaltyRequirement L(long spent, int standing) => new LoyaltyRequirement(spent, standing);
            Offer O(string id = "o", int level = 1) => new Offer(id, "pliers", level, 1, 1, 100);

            // Level 1 must cost nothing.
            Assert.Throws<ValidationException>(() => new TraderDefinition("t", "T", "", null, new[] { 0 }, new[] { L(1, 0) }, null, null));
            // Requirements cannot fall as the levels rise.
            Assert.Throws<ValidationException>(() => new TraderDefinition("t", "T", "", null, new[] { 0, 0, 0 },
                new[] { L(0, 0), L(100, 5), L(50, 5) }, null, null));
            // One sell rate per level, none falling.
            Assert.Throws<ValidationException>(() => new TraderDefinition("t", "T", "", null, new[] { 0 }, new[] { L(0, 0), L(1, 1) }, null, null));
            Assert.Throws<ValidationException>(() => new TraderDefinition("t", "T", "", null, new[] { 5000, 4000 }, new[] { L(0, 0), L(1, 1) }, null, null));
            // Offers: unique ids, and only for levels the trader has.
            Assert.Throws<ValidationException>(() => new TraderDefinition("t", "T", "", null, new[] { 0 }, new[] { L(0, 0) }, new[] { O("a"), O("a") }, null));
            Assert.Throws<ValidationException>(() => new TraderDefinition("t", "T", "", null, new[] { 0 }, new[] { L(0, 0) }, new[] { O(level: 2) }, null));
            Assert.DoesNotThrow(() => new TraderDefinition("t", "T", "", null, new[] { 0 }, new[] { L(0, 0) }, new[] { O() }, null));
        }
    }
}

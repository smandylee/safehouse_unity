using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Safehouse.Core;
using Safehouse.Data;
using Safehouse.UI;
using Safehouse.UI.Sample;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Safehouse.Tests
{
    /// <summary>
    /// The TRADERS screen, built from the real UXML next to the GEAR screen it shares a character with, in an empty
    /// temp data folder (so the character is the sample one and nothing reaches the real saves). The screen is driven
    /// through its public functions rather than by clicking list rows, for the same coverage with far less machinery.
    /// </summary>
    public sealed class TradersScreenControllerTests
    {
        private const double Now = 1_700_000_000;
        private const string Prapor = "prapor";

        private GameObject _gearHost;
        private GameObject _tradersHost;
        private string _dataRoot;
        private string _iconRoot;

        [SetUp]
        public void SetUp()
        {
            _dataRoot = Path.Combine(Path.GetTempPath(), "safehouse-trade-data-" + System.Guid.NewGuid().ToString("N"));
            _iconRoot = Path.Combine(Path.GetTempPath(), "safehouse-trade-icons-" + System.Guid.NewGuid().ToString("N"));
            GearScreenController.DataFolderOverride = _dataRoot;
            IconLibrary.DefaultFolder = _iconRoot;
        }

        [TearDown]
        public void TearDown()
        {
            if (_tradersHost != null)
            {
                Object.DestroyImmediate(_tradersHost);
            }

            if (_gearHost != null)
            {
                Object.DestroyImmediate(_gearHost);
            }

            GearScreenController.DataFolderOverride = null;
            IconLibrary.DefaultFolder = null;
            foreach (var folder in new[] { _dataRoot, _iconRoot })
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }

        private ProfileRepository Saves() => new ProfileRepository(_dataRoot, CatalogLoader.Load());

        private (GearScreenController Gear, TradersScreenController Traders) CreateScreens()
        {
#if UNITY_EDITOR
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/GearPanelSettings.asset");
            var gearUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/GearScreen.uxml");
            var tradersUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/TradersScreen.uxml");
            Assert.IsNotNull(panelSettings, "Run Safehouse > Build Gear Scene once before running this test.");
            Assert.IsNotNull(tradersUxml);

            _gearHost = new GameObject("GearScreen");
            var gearDocument = _gearHost.AddComponent<UIDocument>();
            gearDocument.panelSettings = panelSettings;
            gearDocument.visualTreeAsset = gearUxml;
            var gear = _gearHost.AddComponent<GearScreenController>();

            _tradersHost = new GameObject("TradersScreen");
            var tradersDocument = _tradersHost.AddComponent<UIDocument>();
            tradersDocument.panelSettings = panelSettings;
            tradersDocument.visualTreeAsset = tradersUxml;
            var traders = _tradersHost.AddComponent<TradersScreenController>();
            traders.Clock = () => Now;
            return (gear, traders);
#else
            Assert.Ignore("Loads assets via AssetDatabase, so this only runs as an Editor PlayMode test.");
            return (null, null);
#endif
        }

        private static VisualElement RootOf(Component controller) => controller.GetComponent<UIDocument>().rootVisualElement;

        private static TraderDefinition Trader(string id) =>
            TraderLoader.Load(CatalogLoader.Load()).First(trader => trader.TraderId == id);

        [UnityTest]
        public IEnumerator TheTradersScreenStartsHiddenAndTheTabsSwitchBetweenTheScreens()
        {
            var (gear, traders) = CreateScreens();
            yield return null;

            Assert.AreEqual(DisplayStyle.None, RootOf(traders).style.display.value, "GEAR is the screen showing at start");
            Assert.AreNotEqual(DisplayStyle.None, RootOf(gear).style.display.value);

            ScreenNavigator.Go("traders");
            Assert.AreEqual(DisplayStyle.None, RootOf(gear).style.display.value);
            Assert.AreNotEqual(DisplayStyle.None, RootOf(traders).style.display.value);

            ScreenNavigator.Go("gear");
            Assert.AreEqual(DisplayStyle.None, RootOf(traders).style.display.value);
            Assert.AreNotEqual(DisplayStyle.None, RootOf(gear).style.display.value);
        }

        [UnityTest]
        public IEnumerator TheTraderPickerHasOneButtonPerTraderAndShowsTheChosenOnesStanding()
        {
            var (gear, traders) = CreateScreens();
            yield return null;
            ScreenNavigator.Go("traders");
            var root = RootOf(traders);

            Assert.AreEqual(9, root.Q<VisualElement>("trader-picker").childCount);
            Assert.IsTrue(traders.SelectTrader("skier"));
            Assert.IsFalse(traders.SelectTrader("nobody"));
            Assert.AreEqual("skier", traders.SelectedTraderId);
            Assert.AreEqual("SKIER", root.Q<Label>("trader-name").text);
            StringAssert.Contains("LOYALTY LEVEL 1/4", root.Q<Label>("trader-loyalty").text);
            StringAssert.Contains("standing 60", root.Q<Label>("trader-next").text);
            Assert.IsTrue(root.Q("trader-skier").ClassListContains("trader-btn-active"));
            Assert.IsFalse(root.Q("trader-prapor").ClassListContains("trader-btn-active"));
            Assert.IsNotNull(gear);
        }

        [UnityTest]
        public IEnumerator TheTopBarShowsTheCharactersRealMoneyOnBothScreens()
        {
            var (gear, traders) = CreateScreens();
            yield return null;
            var money = Saves().Load(SampleCharacter.ProfileId).Money;

            Assert.AreEqual(GearScreenController.Spaced(money), RootOf(gear).Q<Label>("label-roubles").text);
            ScreenNavigator.Go("traders");
            Assert.AreEqual(GearScreenController.Spaced(money), RootOf(traders).Q<Label>("label-roubles").text);
        }

        [UnityTest]
        public IEnumerator TheOfferListSearchAndCategoryNarrowWhatIsShown()
        {
            var (gear, traders) = CreateScreens();
            yield return null;
            ScreenNavigator.Go("traders");
            traders.SelectTrader(Prapor);
            var all = traders.OfferCount;
            Assert.AreEqual(330, all);

            traders.SetSearch("buckshot");
            Assert.Greater(traders.OfferCount, 0);
            Assert.Less(traders.OfferCount, all);

            traders.SetSearch("zzzz-nothing-matches-this");
            Assert.AreEqual(0, traders.OfferCount);
            StringAssert.Contains("No offers match", RootOf(traders).Q<Label>("label-offer-problem").text);

            traders.SetSearch("");
            Assert.AreEqual(all, traders.OfferCount);
            traders.SetCategory("Ammo");
            Assert.Greater(traders.OfferCount, 0);
            Assert.Less(traders.OfferCount, all);
            Assert.IsNotNull(gear);
        }

        [UnityTest]
        public IEnumerator BuyingIsChargedPlacedSavedAndShownOnBothScreens()
        {
            var (gear, traders) = CreateScreens();
            yield return null;
            ScreenNavigator.Go("traders");
            traders.SelectTrader(Prapor);
            var offer = Trader(Prapor).Offers.First(candidate => candidate.LoyaltyLevel == 1);
            var before = Saves().Load(SampleCharacter.ProfileId);

            Assert.IsNull(traders.TryBuy(offer.OfferId));

            var saved = Saves().Load(SampleCharacter.ProfileId);
            Assert.AreEqual(before.Money - offer.Price, saved.Money, "charged, and on disk");
            Assert.AreEqual(before.Stash.Stash.Count + 1, saved.Stash.Stash.Count);
            Assert.AreEqual(offer.Price, TradingRules.StateOf(saved, Prapor).Spent);
            StringAssert.Contains("Bought", traders.StatusText);
            Assert.AreEqual(GearScreenController.Spaced(saved.Money), RootOf(traders).Q<Label>("label-roubles").text);

            ScreenNavigator.Go("gear");
            Assert.AreEqual(GearScreenController.Spaced(saved.Money), RootOf(gear).Q<Label>("label-roubles").text);
            Assert.IsNotNull(gear.GridOf(saved.Stash.Stash.Last().InstanceId), "the new item is in the GEAR stash");
        }

        [UnityTest]
        public IEnumerator ARefusedPurchaseSaysWhyAndChangesNothing()
        {
            var (gear, traders) = CreateScreens();
            yield return null;
            ScreenNavigator.Go("traders");
            traders.SelectTrader(Prapor);
            var locked = Trader(Prapor).Offers.First(candidate => candidate.LoyaltyLevel >= 2);
            var before = Saves().Load(SampleCharacter.ProfileId);

            var error = traders.TryBuy(locked.OfferId);

            StringAssert.Contains("loyalty level", error);
            Assert.AreEqual(error, traders.StatusText);
            Assert.AreEqual(before.Money, Saves().Load(SampleCharacter.ProfileId).Money);
            Assert.IsNotNull(gear);
        }

        [UnityTest]
        public IEnumerator APurchaseThatCannotBeSavedDoesNotHappen()
        {
            var (gear, traders) = CreateScreens();
            yield return null;
            ScreenNavigator.Go("traders");
            traders.SelectTrader(Prapor);
            var offer = Trader(Prapor).Offers.First(candidate => candidate.LoyaltyLevel == 1);
            var path = Saves().PathFor(SampleCharacter.ProfileId);
            var before = Saves().Load(SampleCharacter.ProfileId);

            string error;
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) // another process has the file
            {
                error = traders.TryBuy(offer.OfferId);
            }

            StringAssert.Contains("not applied", error);
            Assert.AreEqual(before.Money, gear.Session.Profile.Money, "the open character is unchanged");
            Assert.AreEqual(before.Stash.Stash.Count, gear.Session.Profile.Stash.Stash.Count);
        }

        [UnityTest]
        public IEnumerator StockRunsOutAndComesBackWhenTheRestockWindowTurnsOver()
        {
            var (gear, traders) = CreateScreens();
            yield return null;
            ScreenNavigator.Go("traders");
            traders.SelectTrader(Prapor);
            var offer = Trader(Prapor).Offers.First(candidate => candidate.LoyaltyLevel == 1 && candidate.Stock <= 5);
            Assume.That(gear.Session.Profile.Money, Is.GreaterThan(offer.Price * offer.Stock));

            for (var i = 0; i < offer.Stock; i++)
            {
                Assert.IsNull(traders.TryBuy(offer.OfferId), $"purchase {i + 1} of {offer.Stock}");
            }

            StringAssert.Contains("sold out", traders.TryBuy(offer.OfferId));

            traders.Clock = () => Now + offer.RestockHours * 3600.0; // a later window
            Assert.IsNull(traders.TryBuy(offer.OfferId), "stock is back");
        }

        [UnityTest]
        public IEnumerator StandingUnlocksALevelAndTheOffersItOpens()
        {
            var (gear, traders) = CreateScreens();
            yield return null;
            ScreenNavigator.Go("traders");
            traders.SelectTrader(Prapor);
            var locked = Trader(Prapor).Offers.First(candidate => candidate.LoyaltyLevel == 2);
            Assume.That(gear.Session.Profile.Money, Is.GreaterThan(locked.Price));
            StringAssert.Contains("loyalty level", traders.TryBuy(locked.OfferId));

            Assert.IsNull(traders.TrySetStanding(70)); // Prapor's level 2 needs standing 70

            StringAssert.Contains("LOYALTY LEVEL 2", RootOf(traders).Q<Label>("trader-loyalty").text);
            Assert.AreEqual(70, TradingRules.StateOf(Saves().Load(SampleCharacter.ProfileId), Prapor).Standing);
            Assert.IsNull(traders.TryBuy(locked.OfferId));
            Assert.IsNotNull(traders.TrySetStanding(5000), "out of range is refused");
        }

        [UnityTest]
        public IEnumerator SellingAsksTwiceThenPaysTheTablePriceAndRemovesTheItem()
        {
            var (gear, traders) = CreateScreens();
            yield return null;
            ScreenNavigator.Go("traders");
            traders.SelectTrader(Prapor);
            var prapor = Trader(Prapor);
            var profile = gear.Session.Profile;
            var sellable = profile.Stash.Stash.FirstOrDefault(item => prapor.SellPrices.ContainsKey(item.ItemId));
            Assume.That(sellable, Is.Not.Null, "the sample stash should hold something Prapor buys");
            var price = prapor.SellPrices[sellable.ItemId];

            traders.SelectStashItem(sellable.InstanceId);
            var sellInfo = RootOf(traders).Q<Label>("label-sell-info").text;
            StringAssert.Contains(TradingRules.Roubles(price), sellInfo);

            traders.PressSell();
            Assert.IsTrue(traders.SaleArmed, "the first press only asks");
            Assert.AreEqual(profile.Money, Saves().Load(SampleCharacter.ProfileId).Money, "nothing sold yet");
            StringAssert.Contains("CONFIRM", RootOf(traders).Q<Button>("button-sell").text);

            traders.PressSell();

            var saved = Saves().Load(SampleCharacter.ProfileId);
            Assert.AreEqual(profile.Money + price, saved.Money);
            Assert.IsFalse(saved.Stash.Stash.Any(item => item.InstanceId == sellable.InstanceId));
            StringAssert.Contains("Sold", traders.StatusText);
        }

        [UnityTest]
        public IEnumerator SelectingAnotherItemCancelsAPendingSale()
        {
            var (gear, traders) = CreateScreens();
            yield return null;
            ScreenNavigator.Go("traders");
            traders.SelectTrader(Prapor);
            var prapor = Trader(Prapor);
            var sellable = gear.Session.Profile.Stash.Stash.Where(item => prapor.SellPrices.ContainsKey(item.ItemId)).Take(2).ToList();
            Assume.That(sellable.Count, Is.EqualTo(2));

            traders.SelectStashItem(sellable[0].InstanceId);
            traders.PressSell();
            Assert.IsTrue(traders.SaleArmed);

            traders.SelectStashItem(sellable[1].InstanceId);

            Assert.IsFalse(traders.SaleArmed, "a confirmation only applies to the item it was asked about");
        }

        [UnityTest]
        public IEnumerator AnItemTheTraderDoesNotBuyExplainsWhyAndCannotBeSold()
        {
            var (gear, traders) = CreateScreens();
            yield return null;
            ScreenNavigator.Go("traders");
            traders.SelectTrader(Prapor);
            var prapor = Trader(Prapor);
            var unwanted = gear.Session.Profile.Stash.Stash.FirstOrDefault(item => !prapor.SellPrices.ContainsKey(item.ItemId));
            Assume.That(unwanted, Is.Not.Null);

            traders.SelectStashItem(unwanted.InstanceId);

            StringAssert.Contains("does not buy", RootOf(traders).Q<Label>("label-sell-info").text);
            Assert.IsFalse(RootOf(traders).Q<Button>("button-sell").enabledSelf);
            StringAssert.Contains("does not buy", traders.TrySell(unwanted.InstanceId));
        }

        [UnityTest]
        public IEnumerator ADownedCharacterCannotTrade()
        {
            var first = CreateScreens();
            yield return null;
            var offer = Trader(Prapor).Offers.First(candidate => candidate.LoyaltyLevel == 1);
            Assert.IsNotNull(first.Gear);
            Object.DestroyImmediate(_tradersHost);
            Object.DestroyImmediate(_gearHost);
            _tradersHost = null;
            _gearHost = null;

            var repository = Saves();
            repository.Save(repository.Load(SampleCharacter.ProfileId).With(status: "downed"));
            var second = CreateScreens();
            yield return null;
            ScreenNavigator.Go("traders");
            second.Traders.SelectTrader(Prapor);

            StringAssert.Contains("downed", second.Traders.TryBuy(offer.OfferId));
        }

        [UnityTest]
        public IEnumerator ThePortraitShowsWhenTheTraderHasOneAndIsHiddenWhenNot()
        {
            Directory.CreateDirectory(Path.Combine(_iconRoot, "traders"));
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            File.WriteAllBytes(Path.Combine(_iconRoot, "traders", "prapor.png"), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            var (gear, traders) = CreateScreens();
            yield return null;
            ScreenNavigator.Go("traders");
            var root = RootOf(traders);

            traders.SelectTrader("prapor");
            Assert.IsNotNull(root.Q<Image>("trader-portrait").image);
            Assert.AreNotEqual(DisplayStyle.None, root.Q<Image>("trader-portrait").style.display.value);
            Assert.IsNotNull(root.Q("trader-prapor").Q<Image>(className: "trader-btn-face"), "and a small face on its button");

            traders.SelectTrader("skier"); // no portrait file for this one
            Assert.AreEqual(DisplayStyle.None, root.Q<Image>("trader-portrait").style.display.value);
            Assert.IsNotNull(gear);
        }

        [UnityTest]
        public IEnumerator TheRestockCountdownFollowsTheClock()
        {
            var (gear, traders) = CreateScreens();
            yield return null;
            ScreenNavigator.Go("traders");
            traders.SelectTrader(Prapor);

            traders.Clock = () => 1_000_000 * 3 * 3600.0 + 3600; // an hour into a 3-hour window: two hours left
            traders.Tick();

            Assert.AreEqual("2:00", RootOf(traders).Q<Label>("label-restock").text);
            Assert.IsNotNull(gear);
        }
    }
}

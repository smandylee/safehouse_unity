using System;
using System.Collections.Generic;
using System.Linq;
using Safehouse.Core;
using Safehouse.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Safehouse.UI
{
    /// <summary>
    /// The TRADERS screen. Shows the open character (the one the GEAR screen holds, through its
    /// <see cref="CharacterSession"/>) and a trader: their loyalty level, their offers, and the character's stash to
    /// sell from. Every buy, sell and standing change is built by <see cref="TradingRules"/> and committed - saved -
    /// before the screen shows it, exactly as on the GEAR screen, so a trade that cannot be saved does not happen.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TradersScreenController : MonoBehaviour
    {
        private const float Pitch = 48f;
        private const string AllCategories = "All categories";

        /// <summary>The current time in seconds since 1970 (UTC). Replaced in tests, so restocks can be tested.</summary>
        public Func<double> Clock { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        private Dictionary<string, ItemDefinition> _catalog;
        private IReadOnlyList<TraderDefinition> _traders = new TraderDefinition[0];
        private string _loadProblem;
        private IconLibrary _icons;
        private VisualElement _root;

        private TraderDefinition _trader;
        private readonly List<Offer> _shown = new List<Offer>();
        private Offer _selectedOffer;
        private string _selectedStashId;
        private string _armedSaleId;
        private double _restockDue;
        private bool _visible;

        private VisualElement _picker;
        private Image _portrait;
        private Label _labelName;
        private Label _labelDescription;
        private Label _labelLoyalty;
        private Label _labelNext;
        private Label _labelMoney;
        private Label _labelRestock;
        private IntegerField _fieldStanding;
        private Button _standingButton;
        private TextField _fieldSearch;
        private DropdownField _fieldCategory;
        private ListView _offerList;
        private Label _labelOfferCount;
        private Label _labelOfferName;
        private Label _labelOfferSub;
        private Label _labelOfferProblem;
        private Button _buyButton;
        private VisualElement _stashGrid;
        private Label _labelStashCells;
        private Label _labelSellInfo;
        private Button _sellButton;
        private Label _labelStatus;

        private CharacterSession Session => GearScreenController.Current?.Session;
        private Profile Profile => Session?.Profile;

        private void OnEnable()
        {
            _catalog = CatalogLoader.Load();
            _icons = new IconLibrary();
            try
            {
                _traders = TraderLoader.Load(_catalog);
            }
            catch (GameDataException error)
            {
                _loadProblem = error.Message;
            }

            _root = GetComponent<UIDocument>().rootVisualElement;
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;

            _picker = _root.Q<VisualElement>("trader-picker");
            _portrait = _root.Q<Image>("trader-portrait");
            _labelName = _root.Q<Label>("trader-name");
            _labelDescription = _root.Q<Label>("trader-description");
            _labelLoyalty = _root.Q<Label>("trader-loyalty");
            _labelNext = _root.Q<Label>("trader-next");
            _labelMoney = _root.Q<Label>("label-roubles");
            _labelRestock = _root.Q<Label>("label-restock");
            _fieldStanding = _root.Q<IntegerField>("field-standing");
            _standingButton = _root.Q<Button>("button-standing");
            _fieldSearch = _root.Q<TextField>("field-trade-search");
            _fieldCategory = _root.Q<DropdownField>("field-category");
            _offerList = _root.Q<ListView>("offer-list");
            _labelOfferCount = _root.Q<Label>("label-offer-count");
            _labelOfferName = _root.Q<Label>("label-offer-name");
            _labelOfferSub = _root.Q<Label>("label-offer-sub");
            _labelOfferProblem = _root.Q<Label>("label-offer-problem");
            _buyButton = _root.Q<Button>("button-buy");
            _stashGrid = _root.Q<VisualElement>("trade-stash-grid");
            _labelStashCells = _root.Q<Label>("label-trade-stash-cells");
            _labelSellInfo = _root.Q<Label>("label-sell-info");
            _sellButton = _root.Q<Button>("button-sell");
            _labelStatus = _root.Q<Label>("label-trade-status");

            _offerList.itemsSource = _shown;
            _offerList.fixedItemHeight = 44;
            _offerList.selectionType = SelectionType.Single;
            _offerList.makeItem = MakeOfferRow;
            _offerList.bindItem = BindOfferRow;
            _offerList.selectionChanged += OnOfferSelected;

            _fieldSearch.RegisterValueChangedCallback(_ => ApplyFilter());
            _fieldCategory.RegisterValueChangedCallback(_ => ApplyFilter());
            _buyButton.clicked += () =>
            {
                if (_selectedOffer != null)
                {
                    TryBuy(_selectedOffer.OfferId);
                }
            };
            _sellButton.clicked += OnSellClicked;
            _standingButton.clicked += () => TrySetStanding(_fieldStanding.value);
            _root.Q<Button>("navtab-gear").clicked += () => ScreenNavigator.Go("gear");
            _root.Q<Button>("navtab-hideout").clicked += () => ScreenNavigator.Go("hideout");
            _root.Q<Button>("navtab-settings").clicked += () => ScreenNavigator.Go("settings");

            BuildTraderButtons();
            ScreenNavigator.Register("traders", Show, Hide);
            Hide(); // the GEAR screen is the one showing when the scene starts
            _root.schedule.Execute(Tick).Every(1000);
        }

        private void OnDisable()
        {
            ScreenNavigator.Unregister("traders");
        }

        // ---- showing and hiding ----

        public void Show()
        {
            _visible = true;
            _root.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Hide()
        {
            _visible = false;
            _root.style.display = DisplayStyle.None;
        }

        /// <summary>Redraws everything from the open character: money, the trader's standing and offers, the stash.</summary>
        private void Refresh()
        {
            var profile = Profile;
            _labelMoney.text = profile == null ? "—" : GearScreenController.Spaced(profile.Money);
            if (_traders.Count == 0)
            {
                SetStatus(_loadProblem ?? "Trader data is not loaded.", error: true);
                return;
            }

            if (profile == null)
            {
                SetStatus("Open a character on the GEAR screen first.", error: true);
                return;
            }

            if (_trader == null)
            {
                _trader = _traders[0];
            }

            RefreshTrader();
            BuildStash();
        }

        // ---- the traders ----

        private void BuildTraderButtons()
        {
            _picker.Clear();
            foreach (var trader in _traders)
            {
                var button = new Button { name = "trader-" + trader.TraderId };
                button.AddToClassList("trader-btn");
                var portrait = _icons.GetTrader(trader.TraderId);
                if (portrait != null)
                {
                    var face = new Image { image = portrait, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
                    face.AddToClassList("trader-btn-face");
                    button.Add(face);
                }

                var label = new Label(trader.Name.ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("trader-btn-name");
                button.Add(label);

                var id = trader.TraderId;
                button.clicked += () => SelectTrader(id);
                _picker.Add(button);
            }
        }

        /// <summary>Switches to a trader, with the search and category cleared. Returns false for one that does not exist.</summary>
        public bool SelectTrader(string traderId)
        {
            var trader = _traders.FirstOrDefault(candidate => candidate.TraderId == traderId);
            if (trader == null)
            {
                return false;
            }

            _trader = trader;
            _selectedOffer = null;
            _fieldSearch.SetValueWithoutNotify("");
            _fieldCategory.SetValueWithoutNotify(AllCategories);
            if (Profile != null)
            {
                RefreshTrader();
            }

            return true;
        }

        public string SelectedTraderId => _trader?.TraderId;

        private void RefreshTrader()
        {
            var state = TradingRules.StateOf(Profile, _trader.TraderId);
            var level = TradingRules.LoyaltyLevel(_trader, state);
            var next = TradingRules.NextRequirement(_trader, state);

            _labelName.text = _trader.Name.ToUpperInvariant();
            _labelDescription.text = _trader.Description;
            _portrait.image = _icons.GetTrader(_trader.TraderId);
            _portrait.style.display = _portrait.image == null ? DisplayStyle.None : DisplayStyle.Flex;
            _labelLoyalty.text = $"LOYALTY LEVEL {level}/{_trader.MaxLevel}   ·   Spent {TradingRules.Roubles(state.Spent)}   ·   Standing {state.Standing}";
            _labelNext.text = next == null
                ? "Maximum loyalty level"
                : $"Next level: spent {TradingRules.Roubles(next.MinSpent)} and standing {next.MinStanding}";
            _fieldStanding.SetValueWithoutNotify(state.Standing);

            foreach (var button in _picker.Children())
            {
                button.EnableInClassList("trader-btn-active", button.name == "trader-" + _trader.TraderId);
            }

            var categories = _trader.Offers.Select(offer => _catalog[offer.ItemId].Category).Distinct()
                .OrderBy(category => category, StringComparer.OrdinalIgnoreCase).ToList();
            categories.Insert(0, AllCategories);
            _fieldCategory.choices = categories;
            if (!categories.Contains(_fieldCategory.value))
            {
                _fieldCategory.SetValueWithoutNotify(AllCategories);
            }

            ApplyFilter();
            UpdateRestock();
        }

        // ---- the offers ----

        /// <summary>Filters the trader's offers by the search text and category, keeping the selection if it survives.</summary>
        private void ApplyFilter()
        {
            if (_trader == null)
            {
                return;
            }

            var text = (_fieldSearch.value ?? "").Trim();
            var category = string.IsNullOrEmpty(_fieldCategory.value) ? AllCategories : _fieldCategory.value;
            _shown.Clear();
            foreach (var offer in _trader.Offers)
            {
                var item = _catalog[offer.ItemId];
                if (category != AllCategories && item.Category != category)
                {
                    continue;
                }

                if (text.Length > 0 && !Matches(item, text))
                {
                    continue;
                }

                _shown.Add(offer);
            }

            _offerList.RefreshItems();
            _labelOfferCount.text = $"{_shown.Count} / {_trader.Offers.Count}";

            var index = _selectedOffer == null ? -1 : _shown.FindIndex(offer => offer.OfferId == _selectedOffer.OfferId);
            if (index >= 0)
            {
                _offerList.SetSelectionWithoutNotify(new[] { index });
            }
            else
            {
                _selectedOffer = null;
                _offerList.ClearSelection();
            }

            UpdateOfferDetails();
        }

        private static bool Matches(ItemDefinition item, string text) =>
            item.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
            || item.ItemId.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
            || item.Category.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
            || item.Tags.Any(tag => tag.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);

        private VisualElement MakeOfferRow()
        {
            var row = new VisualElement();
            row.AddToClassList("offer-row");

            var icon = new Image { name = "row-icon", scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            icon.AddToClassList("offer-icon");
            row.Add(icon);

            row.Add(RowLabel("row-name", "offer-col-name"));
            row.Add(RowLabel("row-size", "offer-col-size"));
            row.Add(RowLabel("row-price", "offer-col-price"));
            row.Add(RowLabel("row-level", "offer-col-level"));
            row.Add(RowLabel("row-stock", "offer-col-stock"));
            return row;
        }

        private static Label RowLabel(string name, string columnClass)
        {
            var label = new Label { name = name, pickingMode = PickingMode.Ignore };
            label.AddToClassList(columnClass);
            return label;
        }

        private void BindOfferRow(VisualElement row, int index)
        {
            var offer = _shown[index];
            var item = _catalog[offer.ItemId];
            var profile = Profile;

            row.Q<Image>("row-icon").image = _icons.GetArt(item.ItemId) ?? _icons.Get(item.ItemId);
            row.Q<Label>("row-name").text = item.Name;
            row.Q<Label>("row-size").text = $"{item.Width}×{item.Height}";
            row.Q<Label>("row-price").text = TradingRules.Roubles(offer.Price);
            row.Q<Label>("row-level").text = "LL" + offer.LoyaltyLevel;

            var remaining = offer.Stock;
            var locked = false;
            if (profile != null)
            {
                var state = TradingRules.StateOf(profile, _trader.TraderId);
                remaining = TradingRules.RemainingStock(offer, state, Clock());
                locked = TradingRules.LoyaltyLevel(_trader, state) < offer.LoyaltyLevel;
            }

            row.Q<Label>("row-stock").text = $"{remaining}/{offer.Stock}";
            row.EnableInClassList("offer-unavailable", locked || remaining == 0);
        }

        private void OnOfferSelected(IEnumerable<object> selection)
        {
            _selectedOffer = selection.OfType<Offer>().FirstOrDefault();
            UpdateOfferDetails();
        }

        /// <summary>Selects an offer by id. Public so tests can drive the screen without clicking a list row.</summary>
        public bool SelectOffer(string offerId)
        {
            var index = _shown.FindIndex(offer => offer.OfferId == offerId);
            if (index < 0)
            {
                return false;
            }

            _offerList.SetSelectionWithoutNotify(new[] { index });
            _selectedOffer = _shown[index];
            UpdateOfferDetails();
            return true;
        }

        private void UpdateOfferDetails()
        {
            if (_selectedOffer == null)
            {
                _labelOfferName.text = "";
                _labelOfferSub.text = "";
                _labelOfferProblem.text = _shown.Count == 0 ? "No offers match the search." : "No offer selected.";
                _buyButton.SetEnabled(false);
                return;
            }

            var item = _catalog[_selectedOffer.ItemId];
            _labelOfferName.text = item.Name;
            _labelOfferSub.text = $"{item.Category} · {item.Weight:0.##} kg · {item.Width}×{item.Height} cells"
                + (string.IsNullOrEmpty(item.Description) ? "" : "\n" + item.Description);

            var problem = Profile == null
                ? "Open a character on the GEAR screen first."
                : TradingRules.BuyError(Profile, _trader, _selectedOffer.OfferId, Clock(), _catalog);
            _labelOfferProblem.text = problem ?? "";
            _buyButton.SetEnabled(problem == null);
        }

        /// <summary>The number of offers matching the current search and category. For tests.</summary>
        public int OfferCount => _shown.Count;

        /// <summary>Sets the search text, as if typed.</summary>
        public void SetSearch(string text)
        {
            _fieldSearch.value = text;
            ApplyFilter(); // value-change callbacks are not raised for an unchanged value
        }

        /// <summary>Sets the category filter, as if chosen.</summary>
        public void SetCategory(string category)
        {
            _fieldCategory.value = category;
            ApplyFilter();
        }

        // ---- buying, selling, standing ----

        /// <summary>Buys one of the selected trader's offers. Returns why it could not, or null; nothing changes on a refusal.</summary>
        public string TryBuy(string offerId)
        {
            if (Session == null)
            {
                return Fail("Open a character on the GEAR screen first.");
            }

            (Profile Profile, ItemInstance Item) result;
            try
            {
                result = TradingRules.Buy(Session.Profile, _trader, offerId, Clock(), _catalog);
            }
            catch (ValidationException error)
            {
                return Fail(error.Message);
            }

            var saveError = Commit(result.Profile);
            if (saveError != null)
            {
                return saveError;
            }

            var name = _catalog[result.Item.ItemId].Name;
            var price = _trader.GetOffer(offerId).Price;
            _selectedStashId = result.Item.InstanceId;
            _armedSaleId = null;
            Refresh();
            SelectStashItem(_selectedStashId);
            SetStatus($"Bought {name} from {_trader.Name} for {TradingRules.Roubles(price)}");
            return null;
        }

        /// <summary>Sells an item from the stash to the selected trader. Returns why it could not, or null.</summary>
        public string TrySell(string instanceId)
        {
            if (Session == null)
            {
                return Fail("Open a character on the GEAR screen first.");
            }

            var itemId = Session.Profile.Stash.Stash.FirstOrDefault(item => item.InstanceId == instanceId)?.ItemId;
            (Profile Profile, int Price) result;
            try
            {
                result = TradingRules.Sell(Session.Profile, _trader, instanceId, _catalog);
            }
            catch (ValidationException error)
            {
                return Fail(error.Message);
            }

            var saveError = Commit(result.Profile);
            if (saveError != null)
            {
                return saveError;
            }

            _selectedStashId = null;
            _armedSaleId = null;
            Refresh();
            SetStatus($"Sold {(itemId == null ? "the item" : _catalog[itemId].Name)} to {_trader.Name} for {TradingRules.Roubles(result.Price)}");
            return null;
        }

        /// <summary>A game-master adjustment of the character's standing with the selected trader.</summary>
        public string TrySetStanding(int standing)
        {
            if (Session == null)
            {
                return Fail("Open a character on the GEAR screen first.");
            }

            Profile updated;
            try
            {
                var state = TradingRules.StateOf(Session.Profile, _trader.TraderId);
                updated = TradingRules.WithState(Session.Profile, TradingRules.WithStanding(state, standing));
            }
            catch (ValidationException error)
            {
                return Fail(error.Message);
            }

            var saveError = Commit(updated);
            if (saveError != null)
            {
                return saveError;
            }

            Refresh();
            SetStatus($"{_trader.Name} standing set to {standing}");
            return null;
        }

        /// <summary>Saves the profile and makes it current, or says why it could not and changes nothing.</summary>
        private string Commit(Profile candidate)
        {
            try
            {
                Session.Commit(candidate);
                return null;
            }
            catch (StorageException error)
            {
                return Fail(error.Message);
            }
        }

        private string Fail(string message)
        {
            SetStatus(message, error: true);
            return message;
        }

        // ---- the stash, to sell from ----

        private void BuildStash()
        {
            var stash = Profile.Stash;
            _stashGrid.Clear();
            _stashGrid.style.width = stash.StashWidth * Pitch;
            _stashGrid.style.height = stash.StashHeight * Pitch;
            _stashGrid.MarkDirtyRepaint();

            foreach (var instance in stash.Stash)
            {
                var item = _catalog[instance.ItemId];
                PlacementRules.Footprint(item, instance.Rotation, out var width, out var height);

                var cell = new Button { name = "cell-" + instance.InstanceId };
                cell.AddToClassList("cell");
                GearScreenController.PositionCell(cell, instance.X, instance.Y, width, height);
                cell.style.borderTopColor = cell.style.borderBottomColor =
                    cell.style.borderLeftColor = cell.style.borderRightColor = RarityPalette.For(item.Rarity);

                var icon = _icons.Get(item.ItemId);
                if (icon != null)
                {
                    cell.Add(GearScreenController.IconImage(item, icon, instance.Rotation));
                }
                else
                {
                    cell.Add(GearScreenController.MonogramLabel(item));
                }

                var id = instance.InstanceId;
                cell.clicked += () => SelectStashItem(id);
                _stashGrid.Add(cell);
            }

            PlacementRules.StashTotals(stash, _catalog, out var cells, out _, out _);
            _labelStashCells.text = $"{cells} / {stash.StashWidth * stash.StashHeight} CELLS";
            if (_selectedStashId != null && stash.Stash.All(item => item.InstanceId != _selectedStashId))
            {
                _selectedStashId = null;
            }

            UpdateSellPanel();
        }

        /// <summary>Selects an item in the stash to see what the trader pays for it.</summary>
        public void SelectStashItem(string instanceId)
        {
            if (_selectedStashId != instanceId)
            {
                _armedSaleId = null; // a confirmation only ever applies to the item it was asked about
            }

            _selectedStashId = instanceId;
            foreach (var cell in _stashGrid.Children())
            {
                cell.EnableInClassList("cell-selected", cell.name == "cell-" + instanceId);
            }

            UpdateSellPanel();
        }

        private void UpdateSellPanel()
        {
            _sellButton.text = "SELL";
            if (Profile == null || _trader == null)
            {
                _labelSellInfo.text = "";
                _sellButton.SetEnabled(false);
                return;
            }

            if (_selectedStashId == null)
            {
                _labelSellInfo.text = $"Select an item in your stash to see what {_trader.Name} pays.";
                _sellButton.SetEnabled(false);
                return;
            }

            var problem = TradingRules.SellQuote(Profile, _trader, _selectedStashId, _catalog, out var price);
            if (problem != null)
            {
                _labelSellInfo.text = problem;
                _sellButton.SetEnabled(false);
                return;
            }

            var itemId = Profile.Stash.Stash.First(item => item.InstanceId == _selectedStashId).ItemId;
            _labelSellInfo.text = $"{_trader.Name} pays {TradingRules.Roubles(price)} for {_catalog[itemId].Name}.";
            var blocked = ProfileRules.StashEditError(Profile);
            _sellButton.SetEnabled(blocked == null);
            if (blocked != null)
            {
                _labelSellInfo.text += "\n" + blocked;
            }
            else if (_armedSaleId == _selectedStashId)
            {
                _sellButton.text = $"CONFIRM SELL {TradingRules.Roubles(price)}?";
            }
        }

        /// <summary>Selling asks once more: the first click arms the button, the second sells.</summary>
        private void OnSellClicked()
        {
            if (_selectedStashId == null)
            {
                return;
            }

            if (_armedSaleId != _selectedStashId)
            {
                _armedSaleId = _selectedStashId;
                UpdateSellPanel();
                return;
            }

            TrySell(_selectedStashId);
        }

        /// <summary>Whether the sell button is waiting for its confirming click. For tests.</summary>
        public bool SaleArmed => _armedSaleId != null && _armedSaleId == _selectedStashId;

        /// <summary>Presses the sell button, as a click would.</summary>
        public void PressSell() => OnSellClicked();

        // ---- messages and time ----

        private void SetStatus(string text, bool error = false)
        {
            _labelStatus.text = text;
            _labelStatus.EnableInClassList("trade-status-error", error);
        }

        /// <summary>The status line. For tests.</summary>
        public string StatusText => _labelStatus?.text;

        /// <summary>Called once a second: keeps the restock countdown right and refreshes stock when a window turns over.</summary>
        public void Tick()
        {
            if (!_visible || _trader == null || Profile == null)
            {
                return;
            }

            UpdateRestock();
            if (Clock() >= _restockDue)
            {
                ApplyFilter(); // a new window: stock is back, which the rows and the buy button must show
                UpdateRestock();
            }
        }

        /// <summary>The countdown to the soonest restock among the trader's offers (one per distinct restock period).</summary>
        private void UpdateRestock()
        {
            var now = Clock();
            var soonest = _trader.Offers.GroupBy(offer => offer.RestockHours)
                .Select(group => TradingRules.SecondsUntilRestock(group.First(), now))
                .DefaultIfEmpty(-1).Min();
            if (soonest < 0)
            {
                _labelRestock.text = "—";
                _restockDue = double.MaxValue;
                return;
            }

            _labelRestock.text = TradingRules.FormatCountdown(soonest);
            _restockDue = TradingRules.Timestamp(now) + soonest;
        }
    }
}

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
    /// The HIDEOUT screen: shows every shared facility, its level, requirements, cost and effects, and lets the
    /// player upgrade them. The shared account and the open character are committed separately, so a save failure
    /// leaves the last good version on disk.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HideoutScreenController : MonoBehaviour
    {
        private HideoutDefinition _definition;
        private RecipeBook _recipes;
        private IReadOnlyDictionary<string, ItemDefinition> _catalog;
        private VisualElement _root;
        private VisualElement _facilityList;
        private Label _labelMoney;
        private Label _labelPopulation;
        private Label _labelFuel;
        private Label _labelGeneratorSummary;
        private Label _labelRoomLevel;
        private Label _labelRoomSize;
        private Button _buttonUpgradeRoom;
        private Label _labelFacilityCount;
        private Label _labelStatus;

        /// <summary>Seconds since Unix epoch. Replaced in tests.</summary>
        public Func<double> Clock { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private CharacterSession Session => GearScreenController.Current?.Session;
        private Profile Profile => Session?.Profile;
        private Account Account => Session?.Account;
        private double Now => Clock();

        private void OnEnable()
        {
            _definition = HideoutLoader.Load();
            _recipes = RecipeLoader.LoadForCatalog(CatalogLoader.Load());
            _catalog = CatalogLoader.Load();

            _root = GetComponent<UIDocument>().rootVisualElement;
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;

            _facilityList = _root.Q<VisualElement>("facility-list");
            _labelMoney = _root.Q<Label>("label-roubles");
            _labelPopulation = _root.Q<Label>("label-population");
            _labelFuel = _root.Q<Label>("label-fuel");
            _labelGeneratorSummary = _root.Q<Label>("label-generator-summary");
            _labelRoomLevel = _root.Q<Label>("label-room-level");
            _labelRoomSize = _root.Q<Label>("label-room-size");
            _buttonUpgradeRoom = _root.Q<Button>("button-upgrade-room");
            _labelFacilityCount = _root.Q<Label>("label-facility-count");
            _labelStatus = _root.Q<Label>("label-status");

            _root.Q<Button>("navtab-gear").clicked += () => ScreenNavigator.Go("gear");
            _root.Q<Button>("navtab-traders").clicked += () => ScreenNavigator.Go("traders");
            _root.Q<Button>("navtab-settings").clicked += () => ScreenNavigator.Go("settings");
            _buttonUpgradeRoom.clicked += TryUpgradeRoom;

            ScreenNavigator.Register("hideout", Show, Hide);
            Hide();
        }

        private void OnDisable()
        {
            ScreenNavigator.Unregister("hideout");
        }

        public void Show()
        {
            _root.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
        }

        private void Refresh()
        {
            var profile = Profile;
            var account = Account;

            _labelMoney.text = profile == null ? "—" : GearScreenController.Spaced(profile.Money);
            _labelPopulation.text = account == null ? "—" : account.Population.ToString();
            _labelFuel.text = account == null || profile == null
                ? "—"
                : HideoutRules.GeneratorFuelPerHour(_definition, account.Hideout, account.Population).ToString();

            if (profile == null || account == null)
            {
                _labelStatus.text = "Open a character on the GEAR screen first.";
                _facilityList.Clear();
                return;
            }

            _labelStatus.text = "";
            RefreshGeneratorSummary(account.Hideout);
            RefreshRoomSummary(profile.Room);
            BuildFacilityCards(account.Hideout, profile);
        }

        private void RefreshGeneratorSummary(Hideout hideout)
        {
            var generatorLevel = hideout.LevelOf("generator");
            var perPerson = HideoutRules.IntEffect(_definition, hideout, "generator", "fuel_per_character");
            var running = Account != null && HideoutRules.IsGeneratorRunning(_definition, hideout, Account.Population);
            _labelGeneratorSummary.text = $"LEVEL {generatorLevel} · {perPerson}/person/h · FUEL {Math.Floor(hideout.Fuel)} · {(running ? "RUNNING" : "OFF")}";
        }

        private void RefreshRoomSummary(PersonalRoom room)
        {
            var (width, height) = HideoutRules.RoomStashSize(_definition, room.Level);
            _labelRoomLevel.text = $"LEVEL {room.Level}";
            _labelRoomSize.text = $"{width}×{height} cells";

            var next = room.Level + 1;
            var canUpgrade = next <= _definition.PersonalRoom.MaxLevel
                && Profile.Money >= _definition.PersonalRoom.Level(next).Cost.Money;
            _buttonUpgradeRoom.SetEnabled(canUpgrade);
        }

        private void BuildFacilityCards(Hideout hideout, Profile profile)
        {
            _facilityList.Clear();
            var builtCount = hideout.Facilities.Count(f => f.Level > 0);
            _labelFacilityCount.text = $"{builtCount} / {Hideout.SharedFacilityIds.Count} BUILT";

            foreach (var facilityId in Hideout.SharedFacilityIds)
            {
                var definition = _definition.Facility(facilityId);
                var currentLevel = hideout.LevelOf(facilityId);
                var card = MakeFacilityCard(definition, currentLevel, profile, hideout);
                _facilityList.Add(card);
            }
        }

        private VisualElement MakeFacilityCard(FacilityDefinition definition, int currentLevel, Profile profile, Hideout hideout)
        {
            var card = new VisualElement();
            card.AddToClassList("facility-card");

            var header = new VisualElement();
            header.AddToClassList("facility-card-header");

            var name = new Label(definition.Name.ToUpperInvariant());
            name.AddToClassList("facility-card-name");
            header.Add(name);

            var level = new Label($"LV{currentLevel}/{definition.MaxLevel}");
            level.AddToClassList("facility-card-level");
            header.Add(level);

            var nextLevel = currentLevel + 1;
            var canUpgrade = nextLevel <= definition.MaxLevel;
            var button = new Button { text = canUpgrade ? "UPGRADE" : "MAX" };
            button.AddToClassList("btn");
            button.AddToClassList(canUpgrade ? "btn-primary" : "btn-outline");
            button.AddToClassList("facility-card-btn");
            button.SetEnabled(canUpgrade && HideoutRules.UpgradeError(_definition, hideout, profile, definition.FacilityId, nextLevel) == null);

            if (canUpgrade)
            {
                var id = definition.FacilityId;
                var toLevel = nextLevel;
                button.clicked += () => TryUpgradeFacility(id, toLevel);
            }

            header.Add(button);
            card.Add(header);

            if (!string.IsNullOrEmpty(definition.Description))
            {
                var descLabel = new Label(definition.Description);
                descLabel.AddToClassList("facility-card-desc");
                card.Add(descLabel);
            }

            if (definition.Decorative)
            {
                var decorativeLabel = new Label("Decorative — does not affect character stats.");
                decorativeLabel.AddToClassList("facility-card-decorative");
                card.Add(decorativeLabel);
            }

            if (canUpgrade)
            {
                var levelDef = definition.Level(nextLevel);
                if (levelDef.Requirements.Count > 0)
                {
                    var reqText = "Requires: " + string.Join(", ", levelDef.Requirements.Select(r =>
                        $"{_definition.Facility(r.FacilityId).Name} L{r.Level}"));
                    var reqLabel = new Label(reqText);
                    reqLabel.AddToClassList("facility-card-requirements");
                    card.Add(reqLabel);
                }

                var costText = levelDef.Cost.Items.Count > 0
                    ? $"Cost: {TradingRules.Roubles(levelDef.Cost.Money)} + items"
                    : $"Cost: {TradingRules.Roubles(levelDef.Cost.Money)}";
                var costLabel = new Label(costText);
                costLabel.AddToClassList("facility-card-cost");
                card.Add(costLabel);
            }

            if (currentLevel > 0)
            {
                var effects = FormatEffects(definition.Level(currentLevel).Effects);
                if (!string.IsNullOrEmpty(effects))
                {
                    var effectsLabel = new Label(effects);
                    effectsLabel.AddToClassList("facility-card-effects");
                    card.Add(effectsLabel);
                }
            }

            AddProductionSection(card, definition.FacilityId, currentLevel);

            return card;
        }

        private void AddProductionSection(VisualElement card, string facilityId, int facilityLevel)
        {
            var recipes = _recipes.ForFacility(facilityId).Where(r => r.FacilityLevel <= facilityLevel).ToList();
            if (recipes.Count == 0)
            {
                return;
            }

            var section = new VisualElement();
            section.AddToClassList("facility-card-production");

            foreach (var recipe in recipes)
            {
                var row = new VisualElement();
                row.AddToClassList("facility-recipe-row");

                var name = new Label(recipe.Name);
                name.AddToClassList("facility-recipe-name");
                row.Add(name);

                var running = Account?.ProductionJobs.FirstOrDefault(j => j.RecipeId == recipe.RecipeId);
                if (running != null)
                {
                    var progress = ProductionRules.Progress(recipe, running, _definition, Account.Hideout, Now);
                    var ready = ProductionRules.IsReady(recipe, running, _definition, Account.Hideout, Now);

                    var track = new VisualElement();
                    track.AddToClassList("facility-recipe-progress");
                    var bar = new VisualElement();
                    bar.AddToClassList("facility-recipe-progress-bar");
                    bar.style.width = new Length((float)(progress * 100.0), LengthUnit.Percent);
                    track.Add(bar);
                    row.Add(track);

                    var button = new Button { text = ready ? "COLLECT" : $"{progress * 100f:0}%" };
                    button.AddToClassList("btn");
                    button.AddToClassList(ready ? "btn-primary" : "btn-outline");
                    button.AddToClassList("facility-recipe-btn");
                    button.SetEnabled(ready);
                    var job = running;
                    button.clicked += () => TryCollectRecipe(recipe, job);
                    row.Add(button);
                }
                else
                {
                    var error = Profile != null && Account != null
                        ? ProductionRules.CanStartError(recipe, Profile, Account, _definition, _recipes, _catalog,
                            GpuCountFor(recipe))
                        : "Open a character first.";

                    var button = new Button
                    {
                        text = recipe.Category == RecipeCategory.Repeating ? "INSERT GPUs" : "START"
                    };
                    button.AddToClassList("btn");
                    button.AddToClassList("btn-primary");
                    button.AddToClassList("facility-recipe-btn");
                    button.SetEnabled(error == null);
                    if (error != null)
                    {
                        button.tooltip = error;
                    }

                    var r = recipe;
                    button.clicked += () => TryStartRecipe(r);
                    row.Add(button);
                }

                section.Add(row);
            }

            card.Add(section);
        }

        private int GpuCountFor(RecipeDefinition recipe)
        {
            if (recipe.Category != RecipeCategory.Repeating || Profile == null || Account == null)
            {
                return 0;
            }

            var maxSlots = HideoutRules.IntEffect(_definition, Account.Hideout, "bitcoin_farm", "bitcoin_slots");
            var available = Profile.Stash.Stash.Count(i => i.ItemId == "graphics-card");
            return Math.Min(maxSlots, available);
        }

        private void TryStartRecipe(RecipeDefinition recipe)
        {
            var profile = Profile;
            var account = Account;
            if (profile == null || account == null)
            {
                SetStatus("Open a character on the GEAR screen first.", error: true);
                return;
            }

            var gpuCount = recipe.Category == RecipeCategory.Repeating ? GpuCountFor(recipe) : 0;
            try
            {
                var (paidProfile, withJob) = ProductionRules.Start(recipe, profile, account, _definition, _recipes,
                    Now, _catalog, gpuCount);
                if (!CommitAll(paidProfile, withJob))
                {
                    return;
                }

                SetStatus($"Started {recipe.Name}.");
                Refresh();
            }
            catch (ValidationException error)
            {
                SetStatus(error.Message, error: true);
            }
        }

        private void TryCollectRecipe(RecipeDefinition recipe, ProductionJob job)
        {
            var profile = Profile;
            var account = Account;
            if (profile == null || account == null)
            {
                SetStatus("Open a character on the GEAR screen first.", error: true);
                return;
            }

            try
            {
                var (withOutput, collected) = ProductionRules.Collect(recipe, job, profile, account, _definition,
                    _recipes, _catalog, Now, new System.Random());
                if (!CommitAll(withOutput, collected))
                {
                    return;
                }

                SetStatus($"Collected {recipe.Name}.");
                Refresh();
            }
            catch (ValidationException error)
            {
                SetStatus(error.Message, error: true);
            }
        }

        private static string FormatEffects(IReadOnlyDictionary<string, object> effects)
        {
            if (effects == null || effects.Count == 0)
            {
                return null;
            }

            var parts = new List<string>();
            foreach (var pair in effects)
            {
                if (pair.Value is List<object> list)
                {
                    parts.Add($"{pair.Key}: {string.Join(", ", list.OfType<string>())}");
                }
                else
                {
                    parts.Add($"{pair.Key}: {pair.Value}");
                }
            }

            return string.Join(" · ", parts);
        }

        private void TryUpgradeFacility(string facilityId, int toLevel)
        {
            var profile = Profile;
            var account = Account;
            if (profile == null || account == null)
            {
                SetStatus("Open a character on the GEAR screen first.", error: true);
                return;
            }

            var error = HideoutRules.UpgradeError(_definition, account.Hideout, profile, facilityId, toLevel);
            if (error != null)
            {
                SetStatus(error, error: true);
                return;
            }

            var cost = HideoutRules.UpgradeCost(_definition, account.Hideout, facilityId, toLevel);
            var upgradedHideout = account.Hideout.WithFacility(new Facility(facilityId, toLevel));
            var paidProfile = profile.With(money: profile.Money - cost.Money);

            if (!CommitAll(paidProfile, account.With(hideout: upgradedHideout)))
            {
                return;
            }

            SetStatus($"Upgraded {_definition.Facility(facilityId).Name} to level {toLevel}.");
            Refresh();
        }

        private void TryUpgradeRoom()
        {
            var profile = Profile;
            var account = Account;
            if (profile == null || account == null)
            {
                SetStatus("Open a character on the GEAR screen first.", error: true);
                return;
            }

            var nextLevel = profile.Room.Level + 1;
            if (nextLevel > _definition.PersonalRoom.MaxLevel)
            {
                SetStatus("Personal room is already at maximum level.", error: true);
                return;
            }

            var cost = _definition.PersonalRoom.Level(nextLevel).Cost;
            if (profile.Money < cost.Money)
            {
                SetStatus($"Not enough money: {TradingRules.Roubles(cost.Money)} needed.", error: true);
                return;
            }

            var paidProfile = profile.With(money: profile.Money - cost.Money, room: new PersonalRoom(nextLevel));
            paidProfile = HideoutRules.EnsureRoomStashSize(_definition, paidProfile);
            if (!CommitAll(paidProfile, account))
            {
                return;
            }

            SetStatus($"Upgraded personal room to level {nextLevel}. Stash expanded to {paidProfile.Stash.StashWidth}×{paidProfile.Stash.StashHeight}.");
            Refresh();
        }

        /// <summary>
        /// Saves the profile and account. Returns false if either save fails; the first successful save is not rolled
        /// back, so this is best-effort for now.
        /// </summary>
        private bool CommitAll(Profile profile, Account account)
        {
            try
            {
                Session.Commit(profile);
                Session.Commit(account);
                return true;
            }
            catch (StorageException error)
            {
                SetStatus(error.Message, error: true);
                return false;
            }
        }

        private void SetStatus(string text, bool error = false)
        {
            _labelStatus.text = text;
            _labelStatus.EnableInClassList("text-danger", error);
            _labelStatus.EnableInClassList("text-muted", !error);
        }
    }
}

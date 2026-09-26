using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// Buying, selling, loyalty and restock, as pure functions of a profile and a moment in time. The C# side of
    /// the Python traders.py rules and InventorySession.buy / sell. Nothing here reads a clock, a file or a random
    /// generator: the caller passes <c>now</c> (seconds since 1970, UTC), and gets back a new profile to save. A
    /// refused trade throws and changes nothing, so a purchase is always money, item and trader state together or
    /// not at all.
    /// </summary>
    public static class TradingRules
    {
        private const double MaxTimestamp = 32_503_680_000; // the year 3000

        /// <summary>The moment as whole seconds since 1970, or an error for something that is not a plausible time.</summary>
        public static long Timestamp(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || now > MaxTimestamp)
            {
                throw new ValidationException("The current time must be a number of seconds since 1970.");
            }

            return (long)Math.Floor(now);
        }

        /// <summary>"h:mm", rounded up to the minute so it never reads 0:00 while a wait remains.</summary>
        public static string FormatCountdown(long seconds)
        {
            var minutes = Math.Max(0, (seconds + 59) / 60);
            return $"{minutes / 60}:{minutes % 60:00}";
        }

        // ---- loyalty ----

        /// <summary>
        /// The highest level whose requirements are met - both the spending and the standing, and every level
        /// below it too: the climb stops at the first level not yet earned.
        /// </summary>
        public static int LoyaltyLevel(TraderDefinition trader, TraderState state)
        {
            var level = 1;
            for (var n = 2; n <= trader.Levels.Count; n++)
            {
                var requirement = trader.Levels[n - 1];
                if (state.Spent < requirement.MinSpent || state.Standing < requirement.MinStanding)
                {
                    break;
                }

                level = n;
            }

            return level;
        }

        /// <summary>What the next level needs, or null at the top level.</summary>
        public static LoyaltyRequirement NextRequirement(TraderDefinition trader, TraderState state)
        {
            var level = LoyaltyLevel(trader, state);
            return level >= trader.Levels.Count ? null : trader.Levels[level];
        }

        // ---- stock and restock ----

        private static long Period(Offer offer) => offer.RestockHours * 3600L;

        /// <summary>
        /// Which restock window <paramref name="now"/> is in: fixed blocks counted from 1970, so they are the same for
        /// every character and keep going while the game is closed.
        /// </summary>
        public static long RestockWindow(Offer offer, double now) => Timestamp(now) / Period(offer);

        public static long SecondsUntilRestock(Offer offer, double now) => Period(offer) - Timestamp(now) % Period(offer);

        /// <summary>How many this character can still buy in the current window.</summary>
        public static int RemainingStock(Offer offer, TraderState state, double now)
        {
            var window = RestockWindow(offer, now);
            var bought = state.Purchases.FirstOrDefault(p => p.OfferId == offer.OfferId && p.Window == window)?.Count ?? 0;
            return Math.Max(0, offer.Stock - bought);
        }

        /// <summary>Counts one more purchase of the offer in the current window, dropping the old windows' records.</summary>
        public static TraderState RecordPurchase(TraderState state, Offer offer, double now)
        {
            var bought = offer.Stock - RemainingStock(offer, state, now);
            var purchases = state.Purchases.Where(p => p.OfferId != offer.OfferId).ToList();
            purchases.Add(new OfferPurchase(offer.OfferId, RestockWindow(offer, now), bought + 1));
            return new TraderState(state.TraderId, state.Spent, state.Standing, purchases);
        }

        /// <summary>Adds to the total the character has spent with the trader (which is what unlocks loyalty levels).</summary>
        public static TraderState AddSpent(TraderState state, long amount)
        {
            if (amount < 0 || amount > CoreLimits.MaxMoney)
            {
                throw new ValidationException($"Trade amount must be an integer between 0 and {CoreLimits.MaxMoney}.");
            }

            return new TraderState(state.TraderId, Math.Min(CharacterSheet.MaxSpent, state.Spent + amount),
                state.Standing, state.Purchases);
        }

        /// <summary>A game-master adjustment of standing (quests will raise the same value in a later version).</summary>
        public static TraderState WithStanding(TraderState state, int standing)
        {
            Validate.Integer(standing, "Standing", -CharacterSheet.MaxStanding, CharacterSheet.MaxStanding);
            return new TraderState(state.TraderId, state.Spent, standing, state.Purchases);
        }

        // ---- a character's state with a trader ----

        public static TraderState StateOf(Profile profile, string traderId) =>
            profile.Traders.FirstOrDefault(state => state.TraderId == traderId)
            ?? new TraderState(traderId, 0, 0, null);

        /// <summary>
        /// <paramref name="profile"/> with this trader state replacing (or added to) the one in
        /// <paramref name="basis"/>, which is the profile whose trader list is the starting point - usually the one
        /// before the trade. Other traders' states are kept.
        /// </summary>
        public static Profile WithState(Profile profile, TraderState state, Profile basis = null)
        {
            var traders = (basis ?? profile).Traders.ToList();
            var index = traders.FindIndex(existing => existing.TraderId == state.TraderId);
            if (index >= 0)
            {
                traders[index] = state;
            }
            else
            {
                traders.Add(state);
            }

            return profile.With(traders: traders);
        }

        // ---- what a trader pays ----

        /// <summary>Whether the trader buys this item. A price table, when the trader has one, is the whole list.</summary>
        public static bool Buys(TraderDefinition trader, ItemDefinition item) =>
            trader.SellPrices.Count > 0
                ? trader.SellPrices.ContainsKey(item.ItemId)
                : trader.BuysCategories.Contains(item.Category);

        /// <summary>
        /// What the trader pays for the item at this loyalty level: the table's flat price if there is one (whatever the
        /// level), otherwise the item's base value times the level's rate, rounded down.
        /// </summary>
        public static int SellPrice(TraderDefinition trader, ItemDefinition item, int level)
        {
            if (trader.SellPrices.Count > 0)
            {
                return trader.SellPrices[item.ItemId];
            }

            return (int)((long)item.BaseValue * trader.SellBasisPoints[level - 1] / TradeLimits.BasisPoints);
        }

        // ---- buying ----

        /// <summary>
        /// Buys one of an offer. Money, the item in the stash, the trader's stock and the character's spending with
        /// them all change in the one profile that comes back; if anything is wrong this throws and there is nothing to
        /// save. The item goes in the first place it fits, turned if that helps.
        /// </summary>
        public static (Profile Profile, ItemInstance Item) Buy(Profile profile, TraderDefinition trader, string offerId,
            double now, IReadOnlyDictionary<string, ItemDefinition> catalog, Func<string> newInstanceId = null)
        {
            var blocked = ProfileRules.StashEditError(profile);
            if (blocked != null)
            {
                throw new ValidationException(blocked);
            }

            var offer = trader.GetOffer(offerId);
            var state = StateOf(profile, trader.TraderId);
            if (LoyaltyLevel(trader, state) < offer.LoyaltyLevel)
            {
                throw new ValidationException($"{trader.Name} sells this from loyalty level {offer.LoyaltyLevel}.");
            }

            if (RemainingStock(offer, state, now) == 0)
            {
                throw new ValidationException(
                    $"{trader.Name} is sold out. Restocks in {FormatCountdown(SecondsUntilRestock(offer, now))}.");
            }

            if (profile.Money < offer.Price)
            {
                throw new ValidationException(
                    $"Not enough money: {Roubles(offer.Price)} needed, {Roubles(profile.Money)} available.");
            }

            var spot = PlacementRules.FirstFit(profile.Stash, catalog, offer.ItemId);
            if (spot == null)
            {
                throw new ValidationException(
                    "No space in the stash for this item, even after rotation. Nothing was bought.");
            }

            var item = ItemInstance.Create((newInstanceId ?? NewInstanceId)(), offer.ItemId,
                spot.Value.X, spot.Value.Y, spot.Value.Rotation);
            var stash = profile.Stash.With(profile.Stash.Stash.Concat(new[] { item }));
            var updated = AddSpent(RecordPurchase(state, offer, now), offer.Price);
            return (WithState(profile.With(stash: stash, money: profile.Money - offer.Price), updated, profile), item);
        }

        /// <summary>Why <see cref="Buy"/> would refuse, or null when it would go through. For enabling a button.</summary>
        public static string BuyError(Profile profile, TraderDefinition trader, string offerId, double now,
            IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            try
            {
                Buy(profile, trader, offerId, now, catalog);
                return null;
            }
            catch (ValidationException error)
            {
                return error.Message;
            }
        }

        // ---- selling ----

        /// <summary>
        /// Sells an item from the stash. The price is paid, the item is gone, and the price counts as spending with the
        /// trader (so selling raises loyalty as buying does). Standing is not changed.
        /// </summary>
        public static (Profile Profile, int Price) Sell(Profile profile, TraderDefinition trader, string instanceId,
            IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            var blocked = ProfileRules.StashEditError(profile);
            if (blocked != null)
            {
                throw new ValidationException(blocked);
            }

            var item = profile.Stash.Stash.FirstOrDefault(candidate => candidate.InstanceId == instanceId)
                ?? throw new ValidationException("That item instance no longer exists in this character's stash.");
            var definition = catalog.TryGetValue(item.ItemId, out var found)
                ? found
                : throw new ValidationException($"Unknown item type: {item.ItemId}.");

            if (!Buys(trader, definition))
            {
                throw new ValidationException($"{trader.Name} does not buy {definition.Category}.");
            }

            var state = StateOf(profile, trader.TraderId);
            var price = SellPrice(trader, definition, LoyaltyLevel(trader, state));
            if ((long)profile.Money + price > CoreLimits.MaxMoney)
            {
                throw new ValidationException(
                    $"That sale would exceed the maximum balance of {Roubles(CoreLimits.MaxMoney)}.");
            }

            var stash = profile.Stash.With(profile.Stash.Stash.Where(candidate => candidate.InstanceId != instanceId));
            return (WithState(profile.With(stash: stash, money: profile.Money + price), AddSpent(state, price), profile), price);
        }

        /// <summary>What the trader would pay for an item in the stash, or a reason they will not; for showing before selling.</summary>
        public static string SellQuote(Profile profile, TraderDefinition trader, string instanceId,
            IReadOnlyDictionary<string, ItemDefinition> catalog, out int price)
        {
            price = 0;
            var item = profile.Stash.Stash.FirstOrDefault(candidate => candidate.InstanceId == instanceId);
            if (item == null || !catalog.TryGetValue(item.ItemId, out var definition))
            {
                return "That item is not in the stash.";
            }

            if (!Buys(trader, definition))
            {
                return $"{trader.Name} does not buy {definition.Category}.";
            }

            price = SellPrice(trader, definition, LoyaltyLevel(trader, StateOf(profile, trader.TraderId)));
            return null;
        }

        // ---- data-wide check ----

        /// <summary>
        /// Refuses trader data where something could be bought from one trader and sold to another (or the same one) for
        /// as much or more - free money. The cheapest offer for an item is compared with what every trader would pay
        /// at their top level.
        /// </summary>
        public static void CheckNoArbitrage(IReadOnlyList<TraderDefinition> traders,
            IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            var cheapest = new Dictionary<string, (TraderDefinition Seller, int Price)>();
            foreach (var trader in traders)
            {
                foreach (var offer in trader.Offers)
                {
                    if (!cheapest.TryGetValue(offer.ItemId, out var best) || offer.Price < best.Price)
                    {
                        cheapest[offer.ItemId] = (trader, offer.Price);
                    }
                }
            }

            foreach (var pair in cheapest)
            {
                if (!catalog.TryGetValue(pair.Key, out var item))
                {
                    continue;
                }

                foreach (var buyer in traders)
                {
                    if (!Buys(buyer, item))
                    {
                        continue;
                    }

                    var pays = SellPrice(buyer, item, buyer.MaxLevel);
                    if (pays >= pair.Value.Price)
                    {
                        throw new ValidationException(
                            $"{item.Name} costs {Roubles(pair.Value.Price)} from {pair.Value.Seller.Name} but sells to " +
                            $"{buyer.Name} for {Roubles(pays)}. Raise the price or lower the sell rate.");
                    }
                }
            }
        }

        /// <summary>An amount as the game writes it: "₽5,400".</summary>
        public static string Roubles(long amount) => "₽" + amount.ToString("N0", CultureInfo.InvariantCulture);

        private static string NewInstanceId() => Guid.NewGuid().ToString("N");
    }
}

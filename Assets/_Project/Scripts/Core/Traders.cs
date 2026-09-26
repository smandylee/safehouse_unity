using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>Bounds on trader data, the C# side of the Python traders.py constants.</summary>
    public static class TradeLimits
    {
        public const int MaxLoyaltyLevels = 10;
        public const int MaxOffers = 5000;
        public const int MaxSellPrices = 20000;
        public const int MaxStock = 10000;
        public const int MaxRestockHours = 168;
        public const int MaxBuysCategories = 50;
        public const int MaxTraderNameLength = 40;
        public const int MaxTraderDescriptionLength = 500;

        /// <summary>A trader with no price of its own for an item sells it at this percent of its base value.</summary>
        public const int DefaultMarkupPercent = 120;

        /// <summary>Sell rates are kept as hundredths of a percent, so a price is integer arithmetic, never float rounding.</summary>
        public const int BasisPoints = 10_000;
    }

    /// <summary>What a character needs before a trader treats them as the next loyalty level.</summary>
    public sealed class LoyaltyRequirement
    {
        public LoyaltyRequirement(long minSpent, int minStanding)
        {
            if (minSpent < 0 || minSpent > CharacterSheet.MaxSpent)
            {
                throw new ValidationException("A loyalty level's min_spent is out of range.");
            }

            MinSpent = minSpent;
            MinStanding = Validate.Integer(minStanding, "A loyalty level's min_standing",
                -CharacterSheet.MaxStanding, CharacterSheet.MaxStanding);
        }

        public long MinSpent { get; }
        public int MinStanding { get; }
    }

    /// <summary>One thing a trader sells: what, from which loyalty level, how many per restock, at what price.</summary>
    public sealed class Offer
    {
        public Offer(string offerId, string itemId, int loyaltyLevel, int stock, int restockHours, int price)
        {
            OfferId = Validate.Identifier(offerId, "offer_id");
            ItemId = Validate.Identifier(itemId, "item_id");
            LoyaltyLevel = Validate.Integer(loyaltyLevel, "Offer loyalty_level", 1, TradeLimits.MaxLoyaltyLevels);
            Stock = Validate.Integer(stock, "Offer stock", 1, TradeLimits.MaxStock);
            RestockHours = Validate.Integer(restockHours, "Offer restock_hours", 1, TradeLimits.MaxRestockHours);
            Price = Validate.Integer(price, "Offer price", 1);
        }

        public string OfferId { get; }
        public string ItemId { get; }
        public int LoyaltyLevel { get; }

        /// <summary>How many can be bought per restock window, per character.</summary>
        public int Stock { get; }

        public int RestockHours { get; }
        public int Price { get; }
    }

    /// <summary>
    /// A trader: who they are, how their loyalty levels are earned, what they sell, and what they pay for what
    /// the player sells. The C# side of the Python TraderDefinition. Everything is checked when it is built, so a
    /// trader that exists is one the rules can use.
    /// </summary>
    public sealed class TraderDefinition
    {
        private readonly Dictionary<string, Offer> _offersById;

        public TraderDefinition(string traderId, string name, string description, IEnumerable<string> buysCategories,
            IEnumerable<int> sellBasisPoints, IEnumerable<LoyaltyRequirement> levels, IEnumerable<Offer> offers,
            IReadOnlyDictionary<string, int> sellPrices)
        {
            TraderId = Validate.Identifier(traderId, "trader_id");
            Name = Validate.Text(name, "Trader name", TradeLimits.MaxTraderNameLength);
            Description = Validate.Text(description ?? "", "Trader description",
                TradeLimits.MaxTraderDescriptionLength, allowEmpty: true);

            BuysCategories = (buysCategories ?? Enumerable.Empty<string>()).ToList();
            if (BuysCategories.Count > TradeLimits.MaxBuysCategories)
            {
                throw new ValidationException($"{Name} may buy at most {TradeLimits.MaxBuysCategories} categories.");
            }

            foreach (var category in BuysCategories)
            {
                Validate.Text(category, "A bought category", CoreLimits.MaxCategoryLength);
            }

            Levels = (levels ?? Enumerable.Empty<LoyaltyRequirement>()).ToList();
            if (Levels.Count < 1 || Levels.Count > TradeLimits.MaxLoyaltyLevels)
            {
                throw new ValidationException($"{Name} needs 1 to {TradeLimits.MaxLoyaltyLevels} loyalty levels.");
            }

            if (Levels[0].MinSpent != 0 || Levels[0].MinStanding != 0)
            {
                throw new ValidationException(
                    $"{Name}: level 1 is always available; set its min_spent and min_standing to 0.");
            }

            for (var i = 1; i < Levels.Count; i++)
            {
                if (Levels[i].MinSpent < Levels[i - 1].MinSpent || Levels[i].MinStanding < Levels[i - 1].MinStanding)
                {
                    throw new ValidationException($"{Name}: level {i + 1} requirements cannot be lower than level {i}.");
                }
            }

            SellBasisPoints = (sellBasisPoints ?? Enumerable.Empty<int>()).ToList();
            if (SellBasisPoints.Count != Levels.Count)
            {
                throw new ValidationException($"{Name}: sell_rate_by_level needs one rate per loyalty level ({Levels.Count}).");
            }

            for (var i = 0; i < SellBasisPoints.Count; i++)
            {
                Validate.Integer(SellBasisPoints[i], "A sell rate", 0, TradeLimits.BasisPoints);
                if (i > 0 && SellBasisPoints[i] < SellBasisPoints[i - 1])
                {
                    throw new ValidationException($"{Name}: sell rates cannot decrease at higher loyalty levels.");
                }
            }

            Offers = (offers ?? Enumerable.Empty<Offer>()).ToList();
            if (Offers.Count > TradeLimits.MaxOffers)
            {
                throw new ValidationException($"{Name} may have at most {TradeLimits.MaxOffers} offers.");
            }

            if (Offers.Select(offer => offer.OfferId).Distinct().Count() != Offers.Count)
            {
                throw new ValidationException($"{Name}: duplicate offer_id.");
            }

            if (Offers.Any(offer => offer.LoyaltyLevel > Levels.Count))
            {
                throw new ValidationException($"{Name}: an offer needs a loyalty level the trader does not have.");
            }

            SellPrices = new Dictionary<string, int>(sellPrices ?? new Dictionary<string, int>());
            if (SellPrices.Count > TradeLimits.MaxSellPrices)
            {
                throw new ValidationException($"{Name} may have at most {TradeLimits.MaxSellPrices} sell prices.");
            }

            foreach (var pair in SellPrices)
            {
                Validate.Identifier(pair.Key, "sell_prices item_id");
                Validate.Integer(pair.Value, "A sell price", 1);
            }

            _offersById = Offers.ToDictionary(offer => offer.OfferId);
        }

        public string TraderId { get; }
        public string Name { get; }
        public string Description { get; }
        public IReadOnlyList<string> BuysCategories { get; }
        public IReadOnlyList<int> SellBasisPoints { get; }
        public IReadOnlyList<LoyaltyRequirement> Levels { get; }
        public IReadOnlyList<Offer> Offers { get; }

        /// <summary>A flat price per item the trader buys. When present it replaces the category and rate rules.</summary>
        public IReadOnlyDictionary<string, int> SellPrices { get; }

        public int MaxLevel => Levels.Count;

        public Offer GetOffer(string offerId) =>
            offerId != null && _offersById.TryGetValue(offerId, out var offer)
                ? offer
                : throw new ValidationException($"{Name} has no offer {offerId}.");
    }
}

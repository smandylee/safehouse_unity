using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// The character-sheet vocabulary a profile is made of: body parts, conditions, bio fields, abilities.
    /// The C# side of the Python config.py constants, in the same order (the order is the JSON key order).
    /// </summary>
    public static class CharacterSheet
    {
        public const int MaxHealth = 440;
        public const int MaxNameLength = 40;
        public const int MaxBioLength = 40;
        public const int MaxCharacters = 10;
        public const int MaxTraderStates = 200;
        public const int MaxOfferPurchases = 1000;
        public const int MaxPurchaseCount = 1_000_000;
        public const long MaxSpent = 1_000_000_000_000;
        public const int MaxStanding = 1000;

        public const string Active = "active";
        public const string OnExpedition = "on_expedition";
        public const string Downed = "downed";

        public static readonly IReadOnlyList<string> Statuses = new[] { Active, OnExpedition, Downed };

        /// <summary>The game's own split of <see cref="MaxHealth"/>.</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, int>> BodyPartMaxHealth = new[]
        {
            new KeyValuePair<string, int>("head", 35),
            new KeyValuePair<string, int>("thorax", 85),
            new KeyValuePair<string, int>("stomach", 70),
            new KeyValuePair<string, int>("left_arm", 60),
            new KeyValuePair<string, int>("right_arm", 60),
            new KeyValuePair<string, int>("left_leg", 65),
            new KeyValuePair<string, int>("right_leg", 65),
        };

        public static IEnumerable<string> BodyParts => BodyPartMaxHealth.Select(pair => pair.Key);

        public static readonly IReadOnlyList<string> ConditionNames = new[] { "light_bleed", "heavy_bleed", "fracture" };

        public static readonly IReadOnlyList<string> BioFields = new[]
        {
            "nationality", "affiliation", "gender", "age", "blood_type",
            "height", "weight", "hair_color", "eye_color", "skin_color",
        };

        public static readonly IReadOnlyList<string> Abilities = new[]
        {
            "strength", "dexterity", "agility", "intelligence", "sense", "charisma", "constitution", "looks",
        };

        public const int AbilityBase = 5;
        public const int AbilityBonusPool = 10;
        public static readonly IReadOnlyList<int> AbilityTiers = new[] { 3, 5, 7, 10 };

        /// <summary>Bonus points a tier costs (negative: it gives points back).</summary>
        public static int AbilityCost(int tier)
        {
            switch (tier)
            {
                case 3: return -2;
                case 5: return 0;
                case 7: return 2;
                case 10: return 5;
                default: throw new ValidationException($"Ability scores must be one of {string.Join(", ", AbilityTiers)}.");
            }
        }

        public static int MaxHealthOf(string bodyPart) =>
            BodyPartMaxHealth.First(pair => pair.Key == bodyPart).Value;
    }

    /// <summary>How many of one trader offer were bought in one restock window.</summary>
    public sealed class OfferPurchase
    {
        public OfferPurchase(string offerId, long window, int count)
        {
            OfferId = Validate.Identifier(offerId, "offer_id");
            if (window < 0 || window > CharacterSheet.MaxSpent)
            {
                throw new ValidationException("Purchase window is out of range.");
            }

            Window = window;
            Count = Validate.Integer(count, "Purchase count", 0, CharacterSheet.MaxPurchaseCount);
        }

        public string OfferId { get; }
        public long Window { get; }
        public int Count { get; }
    }

    /// <summary>
    /// A character's standing with one trader. Kept and written back exactly even though trading is not
    /// ported yet, so loading and saving a profile never loses it.
    /// </summary>
    public sealed class TraderState
    {
        public TraderState(string traderId, long spent, int standing, IEnumerable<OfferPurchase> purchases)
        {
            TraderId = Validate.Identifier(traderId, "trader_id");
            if (spent < 0 || spent > CharacterSheet.MaxSpent)
            {
                throw new ValidationException("Trader spending is out of range.");
            }

            Spent = spent;
            Standing = Validate.Integer(standing, "Trader standing", -CharacterSheet.MaxStanding, CharacterSheet.MaxStanding);
            Purchases = (purchases ?? Enumerable.Empty<OfferPurchase>()).ToList();
            if (Purchases.Count > CharacterSheet.MaxOfferPurchases)
            {
                throw new ValidationException($"A trader may record at most {CharacterSheet.MaxOfferPurchases} purchases.");
            }

            if (Purchases.Select(purchase => purchase.OfferId).Distinct().Count() != Purchases.Count)
            {
                throw new ValidationException("A trader lists an offer more than once.");
            }
        }

        public string TraderId { get; }
        public long Spent { get; }
        public int Standing { get; }
        public IReadOnlyList<OfferPurchase> Purchases { get; }
    }
}

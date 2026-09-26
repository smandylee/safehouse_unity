using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Safehouse.Core;

namespace Safehouse.Data
{
    /// <summary>
    /// Turns traders.json into trader definitions. The C# side of the Python traders.parse_traders: it owns the JSON
    /// shape and the checks against the item catalog; Safehouse.Core owns what a valid trader is. An offer with no
    /// price is priced at the default markup over the item's base value. A problem is reported with the trader and
    /// offer it is in, since the file is tens of thousands of lines.
    /// </summary>
    public static class TraderLoader
    {
        public static IReadOnlyList<TraderDefinition> Parse(JObject document, IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            if (!(document["traders"] is JArray rows))
            {
                throw new GameDataException("Trader database must contain a traders list.");
            }

            var traders = new List<TraderDefinition>();
            try
            {
                foreach (var row in rows)
                {
                    var trader = ReadTrader(row, catalog);
                    if (traders.Any(existing => existing.TraderId == trader.TraderId))
                    {
                        throw new ValidationException($"Duplicate trader_id: {trader.TraderId}.");
                    }

                    traders.Add(trader);
                }

                TradingRules.CheckNoArbitrage(traders, catalog);
            }
            catch (ValidationException error)
            {
                throw new GameDataException($"Cannot load the trader database:\n{error.Message}", error);
            }

            return traders;
        }

        public static IReadOnlyList<TraderDefinition> Load(IReadOnlyDictionary<string, ItemDefinition> catalog) =>
            Parse(GameDataLoader.Load(GameDataFile.Traders), catalog);

        private static TraderDefinition ReadTrader(JToken token, IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            var trader = Obj(token, "Each trader");
            var id = Str(trader["trader_id"], "trader_id");
            try
            {
                var levels = Arr(trader["loyalty_levels"], "loyalty_levels").Select(level =>
                {
                    var row = Obj(level, "Each loyalty level");
                    return new LoyaltyRequirement(Long(row["min_spent"], "min_spent"), Int(row["min_standing"], "min_standing"));
                }).ToList();

                var basisPoints = Arr(trader["sell_rate_by_level"], "sell_rate_by_level").Select(rate =>
                {
                    var value = Number(rate, "A sell rate");
                    if (value < 0 || value > 1)
                    {
                        throw new ValidationException("Sell rates must be between 0 and 1.");
                    }

                    // Math.Round is round-half-to-even, like Python's round().
                    return (int)Math.Round(value * TradeLimits.BasisPoints);
                }).ToList();

                var offers = Arr(trader["offers"], "offers").Select(offer => ReadOffer(offer, catalog)).ToList();

                var sellPrices = new Dictionary<string, int>();
                if (trader["sell_prices"] != null && trader["sell_prices"].Type != JTokenType.Null)
                {
                    foreach (var property in Obj(trader["sell_prices"], "sell_prices").Properties())
                    {
                        if (!catalog.ContainsKey(property.Name))
                        {
                            throw new ValidationException($"sell_prices names unknown item {property.Name}.");
                        }

                        sellPrices[property.Name] = Int(property.Value, "A sell price");
                    }
                }

                return new TraderDefinition(id, Str(trader["name"], "name"),
                    trader["description"] == null ? "" : Str(trader["description"], "description"),
                    trader["buys_categories"] == null
                        ? new List<string>()
                        : Arr(trader["buys_categories"], "buys_categories").Select(c => Str(c, "A bought category")).ToList(),
                    basisPoints, levels, offers, sellPrices);
            }
            catch (ValidationException error)
            {
                throw new ValidationException($"{id}: {error.Message}");
            }
        }

        private static Offer ReadOffer(JToken token, IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            var offer = Obj(token, "Each offer");
            var offerId = Str(offer["offer_id"], "offer_id");
            var itemId = Str(offer["item_id"], "item_id");
            try
            {
                if (!catalog.TryGetValue(itemId, out var item))
                {
                    throw new ValidationException($"unknown item_id {itemId}. Add it to items.json first.");
                }

                var price = offer["price"] == null || offer["price"].Type == JTokenType.Null
                    ? DefaultPrice(item)
                    : Int(offer["price"], "price");
                return new Offer(offerId, itemId, Int(offer["loyalty_level"], "loyalty_level"), Int(offer["stock"], "stock"),
                    Int(offer["restock_hours"], "restock_hours"), price);
            }
            catch (ValidationException error)
            {
                throw new ValidationException($"offer {offerId}: {error.Message}");
            }
        }

        /// <summary>A trader with no price of its own sells at the default markup over the base value, rounded up.</summary>
        public static int DefaultPrice(ItemDefinition item) =>
            (int)Math.Max(1, ((long)item.BaseValue * TradeLimits.DefaultMarkupPercent + 99) / 100);

        // ---- strict readers ----

        private static JObject Obj(JToken token, string label) =>
            token as JObject ?? throw new ValidationException($"{label} must be an object.");

        private static JArray Arr(JToken token, string label) =>
            token as JArray ?? throw new ValidationException($"{label} must be a list.");

        private static string Str(JToken token, string label) =>
            token != null && token.Type == JTokenType.String
                ? token.Value<string>()
                : throw new ValidationException($"{label} must be text.");

        private static long Long(JToken token, string label)
        {
            if (token == null || token.Type != JTokenType.Integer)
            {
                throw new ValidationException($"{label} must be an integer.");
            }

            try
            {
                return token.Value<long>();
            }
            catch (OverflowException)
            {
                throw new ValidationException($"{label} is out of range.");
            }
        }

        private static int Int(JToken token, string label)
        {
            var value = Long(token, label);
            if (value < int.MinValue || value > int.MaxValue)
            {
                throw new ValidationException($"{label} is out of range.");
            }

            return (int)value;
        }

        private static double Number(JToken token, string label)
        {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            {
                throw new ValidationException($"{label} must be a number.");
            }

            var value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ValidationException($"{label} must be a finite number.");
            }

            return value;
        }
    }
}

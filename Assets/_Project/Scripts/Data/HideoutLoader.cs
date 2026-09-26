using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Safehouse.Core;

namespace Safehouse.Data
{
    /// <summary>
    /// Turns <c>StreamingAssets/data/hideout.json</c> into typed <see cref="HideoutDefinition"/> rules.
    /// The C# side of the Python catalog.py pattern for hideout data.
    /// </summary>
    public static class HideoutLoader
    {
        public static HideoutDefinition Load()
        {
            var document = GameDataLoader.Load(GameDataFile.Hideout);
            return FromJson(document);
        }

        public static HideoutDefinition FromJson(JObject document)
        {
            var facilities = Obj(document["facilities"], "facilities").Properties()
                .ToDictionary(property => property.Name, property => ReadFacility(property.Name, Obj(property.Value, $"facility {property.Name}")));
            return new HideoutDefinition(facilities);
        }

        private static FacilityDefinition ReadFacility(string facilityId, JObject facility)
        {
            var maxLevel = Int(facility["max_level"], "max_level");
            var levels = Obj(facility["levels"], "levels").Properties()
                .ToDictionary(
                    property => ParseLevelKey(property.Name),
                    property => ReadLevel(facilityId, ParseLevelKey(property.Name), Obj(property.Value, $"level {property.Name}")));

            return new FacilityDefinition(
                facilityId,
                Str(facility["name"], "name"),
                Str(facility["description"], "description", allowEmpty: true),
                maxLevel,
                levels);
        }

        private static int ParseLevelKey(string key)
        {
            if (!int.TryParse(key, out var level) || level < 1)
            {
                throw new ValidationException($"Level key '{key}' must be a positive integer.");
            }

            return level;
        }

        private static FacilityLevelDefinition ReadLevel(string facilityId, int level, JObject levelDocument)
        {
            var requirements = Arr(levelDocument["requirements"], "requirements").Select(ReadRequirement).ToList();
            var cost = ReadCost(Obj(levelDocument["cost"], "cost"));
            var effects = ReadEffects(Obj(levelDocument["effects"], "effects"));

            return new FacilityLevelDefinition(level, requirements, cost, effects);
        }

        private static FacilityRequirement ReadRequirement(JToken token)
        {
            var requirement = Obj(token, "Each requirement");
            return new FacilityRequirement(
                Str(requirement["facility_id"], "facility_id"),
                Int(requirement["level"], "level"));
        }

        private static FacilityCost ReadCost(JObject cost)
        {
            var money = Int(cost["money"], "money");
            var items = cost.ContainsKey("items")
                ? Arr(cost["items"], "items").Select(ReadItemCost).ToList()
                : new List<FacilityItemCost>();
            return new FacilityCost(money, items);
        }

        private static FacilityItemCost ReadItemCost(JToken token)
        {
            var item = Obj(token, "Each item cost");
            return new FacilityItemCost(
                Str(item["item_id"], "item_id"),
                Int(item["count"], "count"));
        }

        private static IReadOnlyDictionary<string, object> ReadEffects(JObject effects)
        {
            var result = new Dictionary<string, object>();
            foreach (var property in effects.Properties())
            {
                result[property.Name] = ReadEffectValue(property.Value);
            }

            return result;
        }

        private static object ReadEffectValue(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Integer:
                    return token.Value<int>();
                case JTokenType.Float:
                    return token.Value<double>();
                case JTokenType.String:
                    return token.Value<string>();
                case JTokenType.Boolean:
                    return token.Value<bool>();
                case JTokenType.Array:
                    return Arr(token, "effect array").Select(ReadEffectValue).ToList();
                default:
                    throw new ValidationException($"Unsupported effect value type: {token.Type}.");
            }
        }

        // ---- strict readers ----

        private static JObject Obj(JToken token, string label) =>
            token as JObject ?? throw new ValidationException($"{label} must be an object.");

        private static JArray Arr(JToken token, string label) =>
            token as JArray ?? throw new ValidationException($"{label} must be a list.");

        private static string Str(JToken token, string label, bool allowEmpty = false) =>
            token != null && token.Type == JTokenType.String
                ? token.Value<string>()
                : throw new ValidationException($"{label} must be text.");

        private static int Int(JToken token, string label)
        {
            if (token == null || token.Type != JTokenType.Integer)
            {
                throw new ValidationException($"{label} must be an integer.");
            }

            try
            {
                return token.Value<int>();
            }
            catch (System.OverflowException)
            {
                throw new ValidationException($"{label} is out of range.");
            }
        }
    }
}

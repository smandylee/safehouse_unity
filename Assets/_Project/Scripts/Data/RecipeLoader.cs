using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Safehouse.Core;
using UnityEngine;

namespace Safehouse.Data
{
    /// <summary>Turns <c>StreamingAssets/data/recipes.json</c> into typed <see cref="RecipeBook"/> rules.</summary>
    public static class RecipeLoader
    {
        public const int SupportedSchemaVersion = 1;

        public static RecipeBook Load()
        {
            var path = Path.Combine(Application.streamingAssetsPath, "data", "recipes.json");
            var text = File.ReadAllText(path);
            var document = JObject.Parse(text);

            var schema = document["schema_version"]?.Value<int>() ?? 0;
            if (schema != SupportedSchemaVersion)
            {
                throw new GameDataException($"Unsupported recipes schema: {schema}.");
            }

            var lootTables = ReadLootTables(document["loot_tables"]);
            var recipes = ReadRecipes(document["recipes"], lootTables);

            return new RecipeBook(recipes, lootTables);
        }

        public static RecipeBook LoadForCatalog(IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            var book = Load();
            foreach (var recipe in book.Recipes)
            {
                foreach (var stack in recipe.Inputs.Concat(recipe.Outputs))
                {
                    if (!catalog.ContainsKey(stack.ItemId))
                    {
                        throw new GameDataException($"Recipe '{recipe.RecipeId}' references unknown item '{stack.ItemId}'.");
                    }
                }
            }

            foreach (var pair in book.LootTables)
            {
                foreach (var entry in pair.Value)
                {
                    if (!catalog.ContainsKey(entry.ItemId))
                    {
                        throw new GameDataException($"Loot table '{pair.Key}' references unknown item '{entry.ItemId}'.");
                    }
                }
            }

            return book;
        }

        private static IReadOnlyList<RecipeDefinition> ReadRecipes(JToken token,
            IReadOnlyDictionary<string, IReadOnlyList<LootEntry>> lootTables)
        {
            if (token == null || token.Type != JTokenType.Array)
            {
                throw new GameDataException("recipes must be a list.");
            }

            return token.Select(ReadRecipe).ToList();
        }

        private static RecipeDefinition ReadRecipe(JToken token)
        {
            var obj = token as JObject ?? throw new GameDataException("Each recipe must be an object.");
            var category = ReadCategory(Str(obj["category"], "category"));

            var durationHours = category == RecipeCategory.Repeating ? 0.0 : Double(obj["duration_hours"], "duration_hours");

            return new RecipeDefinition(
                Str(obj["recipe_id"], "recipe_id"),
                Str(obj["facility_id"], "facility_id"),
                Int(obj["facility_level"], "facility_level"),
                Str(obj["name"], "name"),
                category,
                durationHours,
                ReadStacks(obj["inputs"], "inputs"),
                ReadStacks(obj["outputs"], "outputs"),
                category == RecipeCategory.Random ? Str(obj["output_table"], "output_table") : null);
        }

        private static RecipeCategory ReadCategory(string value)
        {
            switch (value)
            {
                case "crafting": return RecipeCategory.Crafting;
                case "repeating": return RecipeCategory.Repeating;
                case "random": return RecipeCategory.Random;
                default: throw new GameDataException($"Unknown recipe category: '{value}'.");
            }
        }

        private static IReadOnlyList<RecipeStack> ReadStacks(JToken token, string label)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return new RecipeStack[0];
            }

            if (token.Type != JTokenType.Array)
            {
                throw new GameDataException($"{label} must be a list.");
            }

            return token.Select(item => new RecipeStack(
                Str(item["item_id"], $"{label} item_id"),
                Int(item["count"], $"{label} count"))).ToList();
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<LootEntry>> ReadLootTables(JToken token)
        {
            if (token == null || token.Type != JTokenType.Object)
            {
                return new Dictionary<string, IReadOnlyList<LootEntry>>();
            }

            return token.ToObject<JObject>().Properties().ToDictionary(
                p => p.Name,
                p => (IReadOnlyList<LootEntry>)p.Value.Select(entry => new LootEntry(
                    Str(entry["item_id"], "loot item_id"),
                    Int(entry["count"], "loot count"),
                    Int(entry["weight"], "loot weight"))).ToList());
        }

        private static string Str(JToken token, string label) =>
            token != null && token.Type == JTokenType.String
                ? token.Value<string>()
                : throw new GameDataException($"{label} must be text.");

        private static int Int(JToken token, string label)
        {
            if (token == null || token.Type != JTokenType.Integer)
            {
                throw new GameDataException($"{label} must be an integer.");
            }

            return token.Value<int>();
        }

        private static double Double(JToken token, string label)
        {
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
            {
                throw new GameDataException($"{label} must be a number.");
            }

            return token.Value<double>();
        }
    }
}

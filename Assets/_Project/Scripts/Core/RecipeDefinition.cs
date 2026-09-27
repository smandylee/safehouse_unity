using System;
using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>One item stack required to start a recipe or produced by it.</summary>
    public sealed class RecipeStack
    {
        public RecipeStack(string itemId, int count)
        {
            ItemId = Validate.Identifier(itemId, "item_id");
            Count = Validate.Integer(count, "count", 1);
        }

        public string ItemId { get; }
        public int Count { get; }
    }

    /// <summary>
    /// A production recipe for a hideout facility. Can be a one-off craft, a repeating job (Bitcoin farm),
    /// or a random-output job (Scav case).
    /// </summary>
    public sealed class RecipeDefinition
    {
        public RecipeDefinition(string recipeId, string facilityId, int facilityLevel, string name,
            RecipeCategory category, double durationHours, IReadOnlyList<RecipeStack> inputs,
            IReadOnlyList<RecipeStack> outputs = null, string outputTableId = null)
        {
            RecipeId = Validate.Identifier(recipeId, "recipe_id");
            FacilityId = Validate.Identifier(facilityId, "facility_id");
            FacilityLevel = Validate.Integer(facilityLevel, "facility_level", 1, HideoutLimits.MaxFacilityLevel);
            Name = Validate.Text(name, "name");
            Category = category;
            DurationHours = Validate.Double(durationHours, "duration_hours", 0.0, 10000.0);
            Inputs = inputs ?? new RecipeStack[0];
            Outputs = outputs ?? new RecipeStack[0];
            OutputTableId = outputTableId;

            if (category == RecipeCategory.Random && string.IsNullOrEmpty(outputTableId))
            {
                throw new ValidationException("Random recipes need an output_table.");
            }
        }

        public string RecipeId { get; }
        public string FacilityId { get; }
        public int FacilityLevel { get; }
        public string Name { get; }
        public RecipeCategory Category { get; }
        public double DurationHours { get; }
        public IReadOnlyList<RecipeStack> Inputs { get; }
        public IReadOnlyList<RecipeStack> Outputs { get; }
        public string OutputTableId { get; }

        public bool IsRepeating => Category == RecipeCategory.Repeating;
    }

    public enum RecipeCategory
    {
        Crafting,
        Repeating,
        Random,
    }

    /// <summary>A weighted entry in a loot table used by random-output recipes.</summary>
    public sealed class LootEntry
    {
        public LootEntry(string itemId, int count, int weight)
        {
            ItemId = Validate.Identifier(itemId, "item_id");
            Count = Validate.Integer(count, "count", 1);
            Weight = Validate.Integer(weight, "weight", 1);
        }

        public string ItemId { get; }
        public int Count { get; }
        public int Weight { get; }
    }

    /// <summary>All recipes and loot tables loaded from <c>recipes.json</c>.</summary>
    public sealed class RecipeBook
    {
        public RecipeBook(IReadOnlyList<RecipeDefinition> recipes,
            IReadOnlyDictionary<string, IReadOnlyList<LootEntry>> lootTables)
        {
            Recipes = recipes ?? new RecipeDefinition[0];
            LootTables = lootTables ?? new Dictionary<string, IReadOnlyList<LootEntry>>();

            var ids = Recipes.Select(r => r.RecipeId).ToList();
            if (ids.Distinct().Count() != ids.Count)
            {
                throw new ValidationException("Duplicate recipe_id in recipes.");
            }

            foreach (var recipe in Recipes.Where(r => r.Category == RecipeCategory.Random))
            {
                if (!LootTables.ContainsKey(recipe.OutputTableId))
                {
                    throw new ValidationException($"Missing loot table '{recipe.OutputTableId}'.");
                }
            }
        }

        public IReadOnlyList<RecipeDefinition> Recipes { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<LootEntry>> LootTables { get; }

        public RecipeDefinition Get(string recipeId) =>
            Recipes.FirstOrDefault(r => r.RecipeId == recipeId)
            ?? throw new ValidationException($"Recipe '{recipeId}' does not exist.");

        public IReadOnlyList<RecipeDefinition> ForFacility(string facilityId) =>
            Recipes.Where(r => r.FacilityId == facilityId).ToList();
    }
}

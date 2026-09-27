using System;
using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// Pure functions for hideout production: starting jobs, advancing them, collecting outputs, and applying
    /// facility effects to durations and output tables.
    /// </summary>
    public static class ProductionRules
    {
        /// <summary>Why this recipe cannot be started, or null when it can.</summary>
        public static string CanStartError(RecipeDefinition recipe, Profile profile, Account account,
            HideoutDefinition definition, RecipeBook recipes, IReadOnlyDictionary<string, ItemDefinition> catalog,
            int gpuCount = 0)
        {
            if (HideoutRules.LevelOf(account.Hideout, recipe.FacilityId) < recipe.FacilityLevel)
            {
                return $"Needs {recipe.FacilityId} level {recipe.FacilityLevel}.";
            }

            if (!HideoutRules.IsGeneratorRunning(definition, account.Hideout, account.Population))
            {
                return "The generator is off or out of fuel.";
            }

            if (account.ProductionJobs.Any(j => j.RecipeId == recipe.RecipeId))
            {
                return "A job for this recipe is already running.";
            }

            if (recipe.Category == RecipeCategory.Repeating)
            {
                var maxSlots = HideoutRules.IntEffect(definition, account.Hideout, "bitcoin_farm", "bitcoin_slots");
                if (gpuCount <= 0 || gpuCount > maxSlots)
                {
                    return $"Insert 1 to {maxSlots} graphics cards.";
                }

                var availableGpus = profile.Stash.Stash.Count(i => i.ItemId == "graphics-card");
                if (availableGpus < gpuCount)
                {
                    return $"Need {gpuCount} graphics cards, have {availableGpus}.";
                }
            }
            else
            {
                foreach (var input in recipe.Inputs)
                {
                    var available = profile.Stash.Stash.Count(i => i.ItemId == input.ItemId);
                    if (available < input.Count)
                    {
                        var name = catalog.TryGetValue(input.ItemId, out var item) ? item.Name : input.ItemId;
                        return $"Need {input.Count} {name}, have {available}.";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Starts a job. Returns the updated profile and account. Throws if the recipe cannot be started.
        /// </summary>
        public static (Profile profile, Account account) Start(RecipeDefinition recipe, Profile profile, Account account,
            HideoutDefinition definition, RecipeBook recipes, double now,
            IReadOnlyDictionary<string, ItemDefinition> catalog, int gpuCount = 0)
        {
            var error = CanStartError(recipe, profile, account, definition, recipes, catalog, gpuCount);
            if (error != null)
            {
                throw new ValidationException(error);
            }

            Profile paidProfile;
            if (recipe.Category == RecipeCategory.Repeating)
            {
                paidProfile = profile.With(stash: InventoryRules.RemoveItems(profile.Stash, catalog, "graphics-card", gpuCount));
            }
            else
            {
                paidProfile = recipe.Inputs.Aggregate(profile, (current, input) =>
                    current.With(stash: InventoryRules.RemoveItems(current.Stash, catalog, input.ItemId, input.Count)));
            }

            var job = new ProductionJob(recipe.RecipeId, now, profile.ProfileId, gpuCount);
            var jobs = new List<ProductionJob>(account.ProductionJobs) { job };
            return (paidProfile, account.With(productionJobs: jobs));
        }

        /// <summary>The duration of this job in hours, after applying facility effects.</summary>
        public static double Duration(RecipeDefinition recipe, HideoutDefinition definition, Hideout hideout,
            int gpuCount = 0)
        {
            if (recipe.Category == RecipeCategory.Repeating)
            {
                var rate = HideoutRules.DoubleEffect(definition, hideout, "bitcoin_farm", "bitcoin_per_hour_per_gpu");
                if (rate <= 0.0 || gpuCount <= 0)
                {
                    throw new ValidationException("Bitcoin farm cannot produce with no GPUs or no rate.");
                }

                return 1.0 / (gpuCount * rate);
            }

            var speed = HideoutRules.DoubleEffect(definition, hideout, "illumination", "crafting_speed_multiplier");
            speed = speed <= 0.0 ? 1.0 : speed;
            return recipe.DurationHours * speed;
        }

        /// <summary>True when the job is complete and can be collected.</summary>
        public static bool IsReady(RecipeDefinition recipe, ProductionJob job, HideoutDefinition definition,
            Hideout hideout, double now)
        {
            return now >= job.StartTime + Duration(recipe, definition, hideout, job.GpuCount) * 3600.0;
        }

        /// <summary>Progress from 0 to 1.</summary>
        public static double Progress(RecipeDefinition recipe, ProductionJob job, HideoutDefinition definition,
            Hideout hideout, double now)
        {
            var total = Duration(recipe, definition, hideout, job.GpuCount) * 3600.0;
            if (total <= 0.0)
            {
                return 1.0;
            }

            return Math.Max(0.0, Math.Min(1.0, (now - job.StartTime) / total));
        }

        /// <summary>
        /// Collects a ready job. Outputs go into the current profile's stash. For repeating jobs, a new cycle starts
        /// immediately. Throws if the job is not ready or if the outputs do not fit.
        /// </summary>
        public static (Profile profile, Account account) Collect(RecipeDefinition recipe, ProductionJob job,
            Profile profile, Account account, HideoutDefinition definition, RecipeBook recipes,
            IReadOnlyDictionary<string, ItemDefinition> catalog, double now, Random random)
        {
            if (!IsReady(recipe, job, definition, account.Hideout, now))
            {
                throw new ValidationException("This job is not ready yet.");
            }

            var outputs = recipe.Category == RecipeCategory.Random
                ? PickLoot(recipes, definition, account.Hideout, recipe.OutputTableId, random)
                : recipe.Outputs.ToList();

            var withOutputs = outputs.Aggregate(profile, (current, stack) =>
                current.With(stash: InventoryRules.AddItems(current.Stash, catalog, stack.ItemId, stack.Count)));

            var remaining = account.ProductionJobs.Where(j => j != job).ToList();
            if (recipe.IsRepeating)
            {
                var cycleSeconds = Duration(recipe, definition, account.Hideout, job.GpuCount) * 3600.0;
                var nextStart = job.StartTime + cycleSeconds;
                remaining.Add(new ProductionJob(job.RecipeId, nextStart, job.CharacterId, job.GpuCount));
            }

            return (withOutputs, account.With(productionJobs: remaining));
        }

        private static IReadOnlyList<RecipeStack> PickLoot(RecipeBook recipes, HideoutDefinition definition,
            Hideout hideout, string tableId, Random random)
        {
            if (!recipes.LootTables.TryGetValue(tableId, out var table) || table.Count == 0)
            {
                throw new ValidationException($"Loot table '{tableId}' is empty or missing.");
            }

            var totalWeight = table.Sum(e => e.Weight);
            var roll = random.Next(totalWeight);
            var current = 0;
            foreach (var entry in table)
            {
                current += entry.Weight;
                if (roll < current)
                {
                    return new[] { new RecipeStack(entry.ItemId, entry.Count) };
                }
            }

            var last = table[table.Count - 1];
            return new[] { new RecipeStack(last.ItemId, last.Count) };
        }
    }
}

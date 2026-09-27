using System;

namespace Safehouse.Core
{
    /// <summary>
    /// One running hideout production job. Immutable: starting and collecting always build a new job list.
    /// </summary>
    public sealed class ProductionJob
    {
        public ProductionJob(string recipeId, double startTime, string characterId, int gpuCount = 0)
        {
            RecipeId = Validate.Identifier(recipeId, "recipe_id");
            StartTime = Validate.Double(startTime, "start_time", 0.0);
            CharacterId = Validate.Identifier(characterId, "character_id", instance: true);
            GpuCount = Validate.Integer(gpuCount, "gpu_count", 0);
        }

        public string RecipeId { get; }
        public double StartTime { get; }
        public string CharacterId { get; }
        public int GpuCount { get; }

        public ProductionJob WithStartTime(double startTime) =>
            new ProductionJob(RecipeId, startTime, CharacterId, GpuCount);
    }
}

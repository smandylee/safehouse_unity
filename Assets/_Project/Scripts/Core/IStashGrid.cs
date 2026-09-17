using System.Collections.Generic;

namespace Safehouse.Core
{
    /// <summary>
    /// Anything the placement rules can work on: a character's stash, the shared hideout, or a
    /// container on an expedition. They only differ in size and owner, never in how placement works,
    /// so every rule below takes this instead of one concrete owner type.
    /// </summary>
    public interface IStashGrid
    {
        int StashWidth { get; }
        int StashHeight { get; }
        IReadOnlyList<ItemInstance> Stash { get; }
    }

    /// <summary>A plain grid, for callers that need one without a profile or a save behind it.</summary>
    public sealed class StashGrid : IStashGrid
    {
        public StashGrid(int width, int height, IEnumerable<ItemInstance> stash = null)
        {
            StashWidth = Validate.Integer(width, "stash_width", 1, CoreLimits.MaxGrid);
            StashHeight = Validate.Integer(height, "stash_height", 1, CoreLimits.MaxGrid);
            Stash = stash == null ? new List<ItemInstance>() : new List<ItemInstance>(stash);
        }

        public int StashWidth { get; }
        public int StashHeight { get; }
        public IReadOnlyList<ItemInstance> Stash { get; }

        public StashGrid With(IEnumerable<ItemInstance> stash) =>
            new StashGrid(StashWidth, StashHeight, stash);
    }
}

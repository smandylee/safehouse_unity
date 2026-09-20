using System.Collections.Generic;

namespace Safehouse.Core
{
    /// <summary>The checks on a profile that need the item catalog. The C# side of Python validate_profile.</summary>
    public static class ProfileRules
    {
        /// <summary>
        /// Every item must exist in the catalog, fit inside its grid without overlapping, and every instance id
        /// must be unique across the stash, the rig, the backpack and everything worn - one item, one place.
        /// Throws <see cref="ValidationException"/> naming the first problem.
        /// </summary>
        public static void ValidateAgainstCatalog(Profile profile, IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            var taken = new HashSet<string>();
            foreach (var worn in profile.Loadout.Items)
            {
                if (!catalog.ContainsKey(worn.ItemId))
                {
                    throw new ValidationException(
                        $"{worn.Slot}: Unknown item type: {worn.ItemId}. Restore its item database entry.");
                }

                if (!taken.Add(worn.InstanceId))
                {
                    throw new ValidationException($"Duplicate instance ID: {worn.InstanceId}.");
                }
            }

            PlacementRules.ValidateGrid(profile.Stash, catalog, taken);
            PlacementRules.ValidateGrid(profile.Rig, catalog, taken);
            PlacementRules.ValidateGrid(profile.Backpack, catalog, taken);
        }
    }
}

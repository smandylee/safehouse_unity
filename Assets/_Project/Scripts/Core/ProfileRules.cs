using System.Collections.Generic;

namespace Safehouse.Core
{
    /// <summary>The checks on a profile that need the item catalog. The C# side of Python validate_profile.</summary>
    public static class ProfileRules
    {
        /// <summary>
        /// Why this character's stash and carried grids cannot be rearranged right now, or null when they can.
        /// Someone away on an expedition or downed is not there to have their things moved (Python require_active).
        /// </summary>
        public static string StashEditError(Profile profile)
        {
            switch (profile.Status)
            {
                case CharacterSheet.OnExpedition:
                    return $"{profile.DisplayName} is away on an expedition.";
                case CharacterSheet.Downed:
                    return $"{profile.DisplayName} is downed.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Why this character's gear cannot be changed right now, or null when it can. Unlike the stash, gear can be
        /// changed while a character is away - it is read fresh when their next zone starts - but not while downed
        /// (Python require_not_downed).
        /// </summary>
        public static string GearEditError(Profile profile) =>
            profile.Status == CharacterSheet.Downed ? $"{profile.DisplayName} is downed and cannot change gear." : null;

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

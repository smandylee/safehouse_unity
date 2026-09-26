using System;
using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// Pure functions for hideout effects and upgrades. Uses <see cref="HideoutDefinition"/> from
    /// <c>hideout.json</c> so every cost, requirement and effect lives in data, not code.
    /// </summary>
    public static class HideoutRules
    {
        /// <summary>The population used for shared facility calculations.</summary>
        public static int Population(Account account) => account?.Population ?? 0;

        /// <summary>
        /// Generator fuel consumed per in-game hour, reduced by Solar Power. Returns 0 when no population.
        /// </summary>
        public static int GeneratorFuelPerHour(HideoutDefinition definition, Hideout hideout, int population)
        {
            var people = Validate.Integer(population, "Population", 0, HideoutLimits.MaxPopulation);
            if (people == 0)
            {
                return 0;
            }

            var generator = definition.Facility("generator");
            var generatorLevel = LevelOf(hideout, "generator");
            if (!generator.Levels[generatorLevel].Effects.TryGetValue("fuel_per_character", out var raw)
                || !(raw is int perPerson))
            {
                throw new ValidationException("Generator level has no fuel_per_character effect.");
            }

            var total = perPerson * people;
            var solarLevel = LevelOf(hideout, "solar_power");
            if (solarLevel > 0 && definition.Facility("solar_power").Levels[solarLevel].Effects.TryGetValue("fuel_consumption_multiplier", out var solarRaw)
                && solarRaw is double multiplier)
            {
                total = (int)Math.Ceiling(total * multiplier);
            }

            return total;
        }

        /// <summary>The stash size a room level provides.</summary>
        public static (int width, int height) RoomStashSize(HideoutDefinition definition, int roomLevel)
        {
            var room = definition.PersonalRoom;
            var level = room.Level(Validate.Integer(roomLevel, "Room level", 1, HideoutLimits.MaxRoomLevel));
            if (!level.Effects.TryGetValue("stash_width", out var widthRaw) || !(widthRaw is int width)
                || !level.Effects.TryGetValue("stash_height", out var heightRaw) || !(heightRaw is int height))
            {
                throw new ValidationException("Personal room level has no stash size effects.");
            }

            return (width, height);
        }

        /// <summary>The room level that matches a stash size, or the closest lower level.</summary>
        public static int RoomLevelForStash(HideoutDefinition definition, int width, int height)
        {
            var room = definition.PersonalRoom;
            var level = Enumerable.Range(1, room.MaxLevel)
                .LastOrDefault(l =>
                {
                    var (w, h) = RoomStashSize(definition, l);
                    return w <= width && h <= height;
                });
            return level == 0 ? 1 : level;
        }

        /// <summary>Whether the hideout has this facility built at least to the given level.</summary>
        public static bool HasLevel(Hideout hideout, string facilityId, int level) =>
            LevelOf(hideout, facilityId) >= level;

        /// <summary>The current level of a facility in the hideout, or 0 if it is not built.</summary>
        public static int LevelOf(Hideout hideout, string facilityId)
        {
            if (hideout == null)
            {
                return 0;
            }

            var facility = hideout.Facilities.FirstOrDefault(f => f.FacilityId == facilityId);
            return facility?.Level ?? 0;
        }

        /// <summary>True when every requirement for upgrading to <paramref name="toLevel"/> is met.</summary>
        public static bool CanUpgrade(HideoutDefinition definition, Hideout hideout, string facilityId, int toLevel)
        {
            var level = definition.Facility(facilityId).Level(toLevel);
            return level.Requirements.All(r => HasLevel(hideout, r.FacilityId, r.Level));
        }

        /// <summary>The cost to upgrade a facility to a level, or null if already at or above it.</summary>
        public static FacilityCost UpgradeCost(HideoutDefinition definition, Hideout hideout, string facilityId, int toLevel)
        {
            var current = LevelOf(hideout, facilityId);
            if (current >= toLevel)
            {
                return null;
            }

            return definition.Facility(facilityId).Level(toLevel).Cost;
        }

        /// <summary>Why an upgrade cannot happen, or null when it can.</summary>
        public static string UpgradeError(HideoutDefinition definition, Hideout hideout, Profile profile,
            string facilityId, int toLevel)
        {
            var facility = definition.Facility(facilityId);
            var current = LevelOf(hideout, facilityId);
            if (current >= toLevel)
            {
                return $"{facility.Name} is already level {current}.";
            }

            if (toLevel > facility.MaxLevel)
            {
                return $"{facility.Name} cannot exceed level {facility.MaxLevel}.";
            }

            var level = facility.Level(toLevel);
            foreach (var requirement in level.Requirements)
            {
                var required = definition.Facility(requirement.FacilityId);
                if (!HasLevel(hideout, requirement.FacilityId, requirement.Level))
                {
                    return $"Requires {required.Name} level {requirement.Level}.";
                }
            }

            if (profile.Money < level.Cost.Money)
            {
                return $"Needs {TradingRules.Roubles(level.Cost.Money)}, you have {TradingRules.Roubles(profile.Money)}.";
            }

            return null;
        }

        /// <summary>
        /// The merged effect value for an effect key across all facilities, using the highest level built.
        /// For numeric effects that stack, call this once per facility and sum yourself.
        /// </summary>
        public static bool TryEffect(HideoutDefinition definition, Hideout hideout, string facilityId,
            string effectKey, out object value)
        {
            value = null;
            var level = LevelOf(hideout, facilityId);
            if (level == 0)
            {
                return false;
            }

            return definition.Facility(facilityId).Level(level).Effects.TryGetValue(effectKey, out value);
        }

        /// <summary>Numeric effect value at a facility's current level, or the default if missing/not built.</summary>
        public static int IntEffect(HideoutDefinition definition, Hideout hideout, string facilityId,
            string effectKey, int defaultValue = 0)
        {
            return TryEffect(definition, hideout, facilityId, effectKey, out var raw) && raw is int value
                ? value
                : defaultValue;
        }

        /// <summary>Fractional effect value at a facility's current level, or the default if missing/not built.</summary>
        public static double DoubleEffect(HideoutDefinition definition, Hideout hideout, string facilityId,
            string effectKey, double defaultValue = 0.0)
        {
            return TryEffect(definition, hideout, facilityId, effectKey, out var raw) && raw is double value
                ? value
                : defaultValue;
        }

        /// <summary>String list effect at a facility's current level, or an empty list if missing/not built.</summary>
        public static IReadOnlyList<string> StringListEffect(HideoutDefinition definition, Hideout hideout,
            string facilityId, string effectKey)
        {
            if (!TryEffect(definition, hideout, facilityId, effectKey, out var raw) || !(raw is List<object> list))
            {
                return new string[0];
            }

            return list.OfType<string>().ToList();
        }
    }
}

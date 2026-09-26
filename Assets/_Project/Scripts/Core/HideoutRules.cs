using System;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// Pure functions for hideout effects: how population changes shared facility costs, and what level gives
    /// which benefit. The C# side of the Python hideout.py rules.
    /// </summary>
    public static class HideoutRules
    {
        /// <summary>
        /// Generator fuel consumed per in-game hour. Higher population costs more, but higher generator level
        /// is more efficient per person.
        /// </summary>
        public static int GeneratorFuelPerHour(int generatorLevel, int population)
        {
            var level = Validate.Integer(generatorLevel, "Generator level", 1, HideoutLimits.MaxFacilityLevel);
            var people = Validate.Integer(population, "Population", 0, HideoutLimits.MaxPopulation);
            if (people == 0)
            {
                return 0;
            }

            // Level 1: 10 per person, level 2: 5, level 3: 4, level 4: 3, level 5+: 2.
            var perPerson = level == 1 ? 10
                : level == 2 ? 5
                : level == 3 ? 4
                : level == 4 ? 3
                : 2;

            return perPerson * people;
        }

        /// <summary>
        /// The stash size a room level provides. Level 1 is the default 10 x 20 stash.</summary>
        public static (int width, int height) RoomStashSize(int roomLevel)
        {
            var level = Validate.Integer(roomLevel, "Room level", 1, HideoutLimits.MaxRoomLevel);
            return (8 + level * 2, 16 + level * 4);
        }

        /// <summary>
        /// The room level that matches a stash size, or the closest lower level if the size is between tiers.</summary>
        public static int RoomLevelForStash(int width, int height)
        {
            var level = Enumerable.Range(1, HideoutLimits.MaxRoomLevel)
                .LastOrDefault(l =>
                {
                    var (w, h) = RoomStashSize(l);
                    return w <= width && h <= height;
                });
            return level == 0 ? 1 : level;
        }

        /// <summary>
        /// The population used for shared facility calculations: characters in the account order.</summary>
        public static int Population(Account account) => account?.Population ?? 0;
    }
}

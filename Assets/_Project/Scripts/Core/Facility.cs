using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// One shared hideout module and its current level. Immutable: a change builds a new facility.
    /// </summary>
    public sealed class Facility
    {
        public Facility(string facilityId, int level)
        {
            FacilityId = Validate.Identifier(facilityId, "facility_id");
            Level = Validate.Integer(level, "Facility level", 0, HideoutLimits.MaxFacilityLevel);
        }

        public string FacilityId { get; }
        public int Level { get; }

        public Facility WithLevel(int level) => new Facility(FacilityId, level);
    }

    /// <summary>
    /// The shared part of the hideout: facilities every character benefits from. The C# side of the Python
    /// hideout.Hideout model.
    /// </summary>
    public sealed class Hideout
    {
        /// <summary>
        /// Every shared hideout module from Escape from Tarkov. The Stash is excluded because this project
        /// models stash size through each character's private <see cref="PersonalRoom"/> instead.
        /// </summary>
        public static readonly IReadOnlyList<string> SharedFacilityIds = new[]
        {
            "air_filtering_unit",
            "bitcoin_farm",
            "booze_generator",
            "generator",
            "heating",
            "illumination",
            "intelligence_center",
            "lavatory",
            "library",
            "medstation",
            "nutrition_unit",
            "rest_space",
            "scav_case",
            "security",
            "shooting_range",
            "solar_power",
            "vents",
            "water_collector",
            "workbench",
        };

        public Hideout(IEnumerable<Facility> facilities)
        {
            var list = (facilities ?? Enumerable.Empty<Facility>()).ToList();
            var ids = list.Select(f => f.FacilityId).ToList();
            if (ids.Distinct().Count() != ids.Count)
            {
                throw new ValidationException("A facility appears more than once in the hideout.");
            }

            Facilities = list;
        }

        public IReadOnlyList<Facility> Facilities { get; }

        public Facility Facility(string facilityId) =>
            Facilities.FirstOrDefault(f => f.FacilityId == facilityId)
            ?? throw new ValidationException($"Facility '{facilityId}' does not exist.");

        public int LevelOf(string facilityId) =>
            Facilities.FirstOrDefault(f => f.FacilityId == facilityId)?.Level ?? 0;

        /// <summary>
        /// A brand-new hideout: only the generator is built at level 1. Every other shared facility starts at
        /// level 0 and must be upgraded by the player.
        /// </summary>
        public static Hideout CreateNew() => new Hideout(SharedFacilityIds.Select(id =>
            new Facility(id, id == "generator" ? 1 : 0)));

        public Hideout WithFacility(Facility facility)
        {
            var list = Facilities.Where(f => f.FacilityId != facility.FacilityId).ToList();
            list.Add(facility);
            return new Hideout(list);
        }
    }

    /// <summary>
    /// A character's private room level. The C# side of the Python hideout.PersonalRoom model.
    /// </summary>
    public sealed class PersonalRoom
    {
        public const int DefaultLevel = 1;

        public PersonalRoom(int level)
        {
            Level = Validate.Integer(level, "Room level", 1, HideoutLimits.MaxRoomLevel);
        }

        public int Level { get; }

        public static PersonalRoom Default => new PersonalRoom(DefaultLevel);

        public PersonalRoom WithLevel(int level) => new PersonalRoom(level);
    }
}
